#pragma once
#include <stdint.h>

namespace crumbguard {
enum class DispensePhase : uint8_t { Idle, Boost, Run, Gap, Finished, Inhibited };
struct Dispenser {
    inline static constexpr uint32_t boostMs=150, burstMs=800, gapMs=1200, maximumEpisodeMs=20000;
    inline static constexpr uint8_t runPwm=192, maximumBursts=8;
    DispensePhase phase=DispensePhase::Idle;
    uint8_t pwm=0, bursts=0;
    uint32_t episodeAt=0, phaseAt=0;
    bool handled=false;
    constexpr void beginBurst(uint32_t now) { ++bursts;phaseAt=now;phase=DispensePhase::Boost;pwm=255; }
    constexpr void tick(uint32_t now,bool alarm,bool motionReason,bool ready,bool healthy,bool stop,bool enabled) {
        if(!alarm) {phase=DispensePhase::Idle;pwm=bursts=0;handled=false;return;}
        if(!handled) {
            handled=true;episodeAt=now;
            if(!motionReason || !ready || !healthy || stop || !enabled) {
                phase=DispensePhase::Inhibited;pwm=0;return;
            }
            beginBurst(now);
        }
        if(!ready || !healthy || stop || !enabled) {phase=DispensePhase::Inhibited;pwm=0;return;}
        if(phase==DispensePhase::Inhibited || phase==DispensePhase::Finished) {pwm=0;return;}
        if(uint32_t(now-episodeAt)>=maximumEpisodeMs) {phase=DispensePhase::Finished;pwm=0;return;}
        if(phase==DispensePhase::Boost || phase==DispensePhase::Run) {
            if(uint32_t(now-phaseAt)>=burstMs) {
                pwm=0;phase=bursts>=maximumBursts?DispensePhase::Finished:DispensePhase::Gap;phaseAt=now;
            } else if(uint32_t(now-phaseAt)>=boostMs) {phase=DispensePhase::Run;pwm=runPwm;}
        } else if(phase==DispensePhase::Gap && uint32_t(now-phaseAt)>=gapMs) {
            if(uint32_t(now-episodeAt)+burstMs<=maximumEpisodeMs) beginBurst(now);
            else {phase=DispensePhase::Finished;pwm=0;}
        }
    }
};
constexpr const char* name(DispensePhase p) {
    switch(p) {
        case DispensePhase::Idle:return "idle";case DispensePhase::Boost:return "boost";
        case DispensePhase::Run:return "run";case DispensePhase::Gap:return "gap";
        case DispensePhase::Finished:return "finished";case DispensePhase::Inhibited:return "inhibited";
    }
    return "unknown";
}
}
