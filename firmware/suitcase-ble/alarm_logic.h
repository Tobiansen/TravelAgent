#pragma once
#include <stdint.h>

namespace suitcase {
using Time = uint32_t;
constexpr Time elapsed(Time now, Time then) { return now - then; }
enum class Key : uint8_t { None, A, B, Cancel };
enum class State : uint8_t { Disarmed, Arming, Armed, Warning, Alarm };
enum class Reason : uint8_t { None, Motion, SensorFault };
enum class CodeResult : uint8_t { Pending, Arm, Disarm, Wrong, SensorUnavailable };
enum class GuardResult : uint8_t { Applied, Unchanged, SensorUnavailable, ConfigurationLocked };

// Two debounced keys. A tap completes on release; overlapping keys cancel entry.
class Keypad {
    struct Contact {
        bool stable = false, candidate = false;
        Time changed = 0;
        constexpr void update(bool raw, Time now) {
            if (raw != candidate) { candidate = raw; changed = now; }
            if (stable != candidate && elapsed(now, changed) >= 30) stable = candidate;
        }
    };
    Contact a{}, b{};
    bool initialized = false, blocked = true;
    uint8_t pending = 0;
public:
    constexpr Key sample(uint8_t raw, Time now) {
        if (!initialized) {
            a.stable = a.candidate = (raw & 1) != 0;
            b.stable = b.candidate = (raw & 2) != 0;
            a.changed = b.changed = now;
            initialized = true;
        }
        const uint8_t before = (a.stable ? 1 : 0) | (b.stable ? 2 : 0);
        a.update((raw & 1) != 0, now);
        b.update((raw & 2) != 0, now);
        const uint8_t after = (a.stable ? 1 : 0) | (b.stable ? 2 : 0);
        if (blocked) {
            if (after == 0) blocked = false;
            return Key::None;
        }
        if (after == 3 || (before != 0 && after != 0 && before != after)) {
            blocked = true;
            pending = 0;
            return Key::Cancel;
        }
        if (before == 0 && after != 0) pending = after;
        if (before != 0 && after == 0) {
            const uint8_t completed = pending;
            pending = 0;
            return completed == 1 ? Key::A : completed == 2 ? Key::B : Key::None;
        }
        return Key::None;
    }
};

class Controller {
    uint8_t input[4] = {};
    uint8_t count = 0;
    Time lastTap = 0, quietSince = 0, motionSince = 0;
    bool quietTracking = false, motionTracking = false;
    constexpr void change(State next, Time now) {
        state = next;
        since = now;
        quietTracking = motionTracking = false;
    }
public:
    // Per-controller defaults; the BLE application restores the NVS configuration.
    uint8_t armCode[4] = {1, 1, 2, 2};
    uint8_t disarmCode[4] = {2, 1, 2, 1};
    State state = State::Disarmed;
    Reason reason = Reason::None;
    Time since = 0;
    constexpr uint8_t entered() const { return count; }
    constexpr void clearEntry() { count = 0; }
    // Shared desired-state transition for keypad and authorized remote commands.
    // Reasserting protection must not restart settling, Warning or Alarm.
    constexpr GuardResult setGuard(bool armed, Time now, bool sensorHealthy) {
        if (armed && state != State::Disarmed) return GuardResult::Unchanged;
        if (armed && !sensorHealthy) return GuardResult::SensorUnavailable;
        clearEntry();
        reason = Reason::None;
        if (!armed && state == State::Disarmed) return GuardResult::Unchanged;
        change(armed ? State::Arming : State::Disarmed, now);
        return GuardResult::Applied;
    }
    constexpr bool setCodes(const char* arm,const char* disarm) {
        if(state!=State::Disarmed)return false;
        for(unsigned i=0;i<4;++i)if((arm[i]!='A'&&arm[i]!='B')||(disarm[i]!='A'&&disarm[i]!='B'))return false;
        if(arm[4]||disarm[4])return false;
        bool differ=false;for(unsigned i=0;i<4;++i)if(arm[i]!=disarm[i])differ=true;
        if(!differ)return false;
        for(unsigned i=0;i<4;++i){armCode[i]=arm[i]=='A'?1:2;disarmCode[i]=disarm[i]=='A'?1:2;}
        clearEntry();return true;
    }
    constexpr void expireEntry(Time now) {
        if (count && elapsed(now, lastTap) >= 2000) clearEntry();
    }
    constexpr CodeResult tap(Key key, Time now, bool sensorHealthy) {
        expireEntry(now);
        if (key == Key::Cancel) { clearEntry(); return CodeResult::Wrong; }
        if (key != Key::A && key != Key::B) return CodeResult::Pending;
        input[count++] = key == Key::A ? 1 : 2;
        lastTap = now;
        if (count < 4) return CodeResult::Pending;
        const bool arming = state == State::Disarmed;
        bool matches = true;
        for (uint8_t i = 0; i < 4; ++i) {
            if (input[i] != (arming ? armCode[i] : disarmCode[i])) matches = false;
        }
        clearEntry();
        if (!matches) return CodeResult::Wrong;
        if (arming) {
            if (setGuard(true, now, sensorHealthy) == GuardResult::SensorUnavailable)
                return CodeResult::SensorUnavailable;
            return CodeResult::Arm;
        }
        setGuard(false, now, sensorHealthy);
        return CodeResult::Disarm;
    }
    constexpr void tick(Time now, bool healthy, bool quiet, bool moving) {
        expireEntry(now);
        if (state == State::Arming) {
            if (!healthy || elapsed(now, since) >= 20000) {
                change(State::Disarmed, now);
                return;
            }
            if (quiet) {
                if (!quietTracking) { quietTracking = true; quietSince = now; }
            } else quietTracking = false;
            if (elapsed(now, since) >= 5000 && quietTracking && elapsed(now, quietSince) >= 1000) {
                change(State::Armed, now);
            }
        } else if (state == State::Armed) {
            if (!healthy) {
                reason = Reason::SensorFault;
                change(State::Warning, now);
            } else if (moving) {
                if (!motionTracking) { motionTracking = true; motionSince = now; }
                if (elapsed(now, motionSince) >= 150) {
                    reason = Reason::Motion;
                    change(State::Warning, now);
                }
            } else motionTracking = false;
        } else if (state == State::Warning && elapsed(now, since) >= 2000) {
            change(State::Alarm, now);
        }
        // Alarm latches until a complete valid disarm code, even after motion stops.
    }
};

constexpr const char* name(State s) {
    switch (s) {
        case State::Disarmed: return "disarmed";
        case State::Arming: return "arming";
        case State::Armed: return "armed";
        case State::Warning: return "warning";
        case State::Alarm: return "alarm";
    }
    return "unknown";
}
constexpr const char* name(Reason r) {
    return r == Reason::Motion ? "motion" : r == Reason::SensorFault ? "sensor_fault" : "none";
}
}  // namespace suitcase
