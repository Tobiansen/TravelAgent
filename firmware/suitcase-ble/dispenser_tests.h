#pragma once
#include "dispenser_logic.h"
namespace crumbguard {
constexpr void run(Dispenser& d,uint32_t t,bool alarm=true,bool reason=true,bool ready=true,bool healthy=true,bool stop=false,bool enabled=true) {
    d.tick(t,alarm,reason,ready,healthy,stop,enabled);
}
constexpr bool pulseTimingTest() {
    Dispenser d;run(d,0);
    if(d.pwm!=255 || d.bursts!=1)return false;
    run(d,149);if(d.pwm!=255)return false;
    run(d,150);if(d.pwm!=192)return false;
    run(d,799);if(!d.pwm)return false;
    run(d,800);if(d.pwm || d.phase!=DispensePhase::Gap)return false;
    run(d,1999);if(d.pwm)return false;
    run(d,2000);return d.pwm==255 && d.bursts==2;
}
constexpr bool budgetTest() {
    Dispenser d;
    for(uint32_t i=0;i<8;++i) {run(d,i*2000);if(d.bursts!=i+1 || !d.pwm)return false;run(d,i*2000+800);if(d.pwm)return false;}
    for(uint32_t i=16000;i<24000;i+=100) {run(d,i);if(d.pwm || d.bursts!=8)return false;}
    return d.phase==DispensePhase::Finished;
}
constexpr bool stopAndNoRestartTest() {
    Dispenser d;run(d,0);run(d,1,true,true,true,true,true);
    if(d.pwm || d.phase!=DispensePhase::Inhibited)return false;
    run(d,3000);if(d.pwm)return false;
    run(d,3001,false);run(d,4000);return d.bursts==1 && d.pwm==255;
}
constexpr bool faultNoFeedTest() {
    Dispenser d;run(d,0,true,false);run(d,1000);
    return !d.pwm && !d.bursts && d.phase==DispensePhase::Inhibited;
}
constexpr bool healthLossTest() {
    Dispenser d;run(d,0);run(d,1,true,true,true,false);run(d,3000);
    return !d.pwm && d.phase==DispensePhase::Inhibited;
}
constexpr bool setupAndDisableTest() {
    Dispenser d;run(d,0,true,true,false);if(d.pwm)return false;
    run(d,1,false);run(d,2);if(!d.pwm)return false;
    run(d,3,true,true,true,true,false,false);return !d.pwm && d.phase==DispensePhase::Inhibited;
}
constexpr bool rolloverTest() {
    Dispenser d;run(d,0xffffff00U);run(d,0xffffff96U);if(d.pwm!=192)return false;
    run(d,544);return !d.pwm && d.phase==DispensePhase::Gap;
}
constexpr bool deadlineTest() {
    Dispenser d;run(d,0);run(d,20000);return !d.pwm && d.phase==DispensePhase::Finished;
}
static_assert(pulseTimingTest(),"Boost, run, cutoff and rest interval");
static_assert(budgetTest(),"At most eight bursts per alarm, no automatic restart");
static_assert(stopAndNoRestartTest(),"Manual motor stop inhibits until a new alarm");
static_assert(faultNoFeedTest(),"Sensor-fault alarm never dispenses");
static_assert(healthLossTest(),"Loss of fresh input stops dispensing");
static_assert(setupAndDisableTest(),"No motor without ready driver; disable takes priority");
static_assert(rolloverTest(),"Dispenser timing survives millis rollover");
static_assert(deadlineTest(),"Episode wall-clock limit");
}
