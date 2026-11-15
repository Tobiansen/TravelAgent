#pragma once
#include "alarm_logic.h"

// Compile-time regression tests use the same controller/keypad as the device.
// No Arduino timing, input emulation framework or host compiler is required.
namespace suitcase::tests {
constexpr Controller arming() {
    Controller c;
    c.tap(Key::A, 100, true); c.tap(Key::A, 200, true);
    c.tap(Key::B, 300, true); c.tap(Key::B, 400, true);
    return c;
}
constexpr Controller armed() {
    Controller c = arming();
    c.tick(500, true, true, false);
    c.tick(5400, true, true, false);
    return c;
}
constexpr bool countdown() {
    Controller c = arming();
    c.tick(500, true, true, false);
    c.tick(5399, true, true, false);
    if (c.state != State::Arming) return false;
    c.tick(5400, true, true, false);
    return c.state == State::Armed;
}
constexpr bool mustSettle() {
    Controller c = arming();
    c.tick(5400, true, false, false);
    c.tick(6000, true, true, false);
    c.tick(6999, true, true, false);
    if (c.state != State::Arming) return false;
    c.tick(7000, true, true, false);
    return c.state == State::Armed;
}
constexpr bool incorrectAndExpired() {
    Controller c;
    for (Time t = 100; t <= 400; t += 100) c.tap(Key::A, t, true);
    if (c.state != State::Disarmed || c.entered() != 0) return false;
    c.tap(Key::A, 500, true); c.tap(Key::A, 600, true);
    c.tap(Key::B, 2600, true); c.tap(Key::B, 2700, true);
    return c.state == State::Disarmed && c.entered() == 2;
}
constexpr bool motionAndDisarm() {
    Controller c = armed();
    c.tick(5500, true, false, true);
    c.tick(5649, true, false, true);
    if (c.state != State::Armed) return false;
    c.tick(5650, true, false, true);
    if (c.state != State::Warning) return false;
    c.tick(7649, true, true, false);
    if (c.state != State::Warning) return false;
    c.tick(7650, true, true, false);
    if (c.state != State::Alarm) return false;
    c.tick(9000, true, true, false);
    if (c.state != State::Alarm) return false;
    c.tap(Key::B, 9100, true); c.tap(Key::A, 9200, true);
    c.tap(Key::B, 9300, true); c.tap(Key::A, 9400, true);
    return c.state == State::Disarmed;
}
constexpr bool disarmDuringWarningAndArming() {
    Controller c = armed();
    c.tick(5500, false, false, false);
    if (c.reason != Reason::SensorFault || c.state != State::Warning) return false;
    c.tap(Key::B, 5600, false); c.tap(Key::A, 5700, false);
    c.tap(Key::B, 5800, false); c.tap(Key::A, 5900, false);
    if (c.state != State::Disarmed) return false;
    c = arming();
    c.tap(Key::B, 600, true); c.tap(Key::A, 700, true);
    c.tap(Key::B, 800, true); c.tap(Key::A, 900, true);
    return c.state == State::Disarmed;
}
constexpr bool wrongCodeDoesNotDisarm() {
    Controller c = armed();
    const Time original = c.since;
    for (Time t = 5500; t <= 5800; t += 100) c.tap(Key::A, t, true);
    return c.state == State::Armed && c.since == original;
}
constexpr bool noSensorNoArm() {
    Controller c;
    c.tap(Key::A, 100, false); c.tap(Key::A, 200, false);
    c.tap(Key::B, 300, false);
    const auto result = c.tap(Key::B, 400, false);
    return result == CodeResult::SensorUnavailable && c.state == State::Disarmed;
}
constexpr bool rollover() {
    Controller c;
    const Time base = UINT32_MAX - 1000;
    c.tap(Key::A, base, true); c.tap(Key::A, base + 100U, true);
    c.tap(Key::B, base + 200U, true); c.tap(Key::B, base + 300U, true);
    c.tick(base + 400U, true, true, false);
    c.tick(base + 5300U, true, true, false);
    return c.state == State::Armed;
}
constexpr bool keyBounceAndHold() {
    Keypad p;
    p.sample(0, 0); p.sample(1, 10); p.sample(0, 20); p.sample(1, 25);
    if (p.sample(1, 60) != Key::None || p.sample(1, 3000) != Key::None) return false;
    p.sample(0, 3010);
    return p.sample(0, 3045) == Key::A && p.sample(0, 3050) == Key::None;
}
constexpr bool overlapAndHeldBoot() {
    Keypad p;
    p.sample(1, 0); p.sample(0, 10);
    if (p.sample(0, 45) != Key::None) return false;
    p.sample(1, 100); p.sample(1, 140);
    p.sample(3, 150);
    if (p.sample(3, 190) != Key::Cancel) return false;
    p.sample(0, 200);
    return p.sample(0, 240) == Key::None;
}
static_assert(arming().state == State::Arming, "Correct four-tap arm sequence");
static_assert(countdown(), "Five-second countdown");
static_assert(mustSettle(), "Require final quiet second");
static_assert(incorrectAndExpired(), "Wrong and timed-out entries");
static_assert(motionAndDisarm(), "Sustained motion, warning, latch and disarm");
static_assert(disarmDuringWarningAndArming(), "Disarm cancels pending states/fault warning");
static_assert(wrongCodeDoesNotDisarm(), "Wrong code preserves armed state");
static_assert(noSensorNoArm(), "Unavailable sensor cannot arm");
static_assert(rollover(), "Timers survive millis rollover");
static_assert(keyBounceAndHold(), "Bounce rejected and hold produces one release tap");
static_assert(overlapAndHeldBoot(), "Overlapping and boot-held keys do not become taps");
}  // namespace suitcase::tests
