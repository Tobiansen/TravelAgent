#pragma once
#include "live_motion.h"
namespace travelble::livemotiontests {
constexpr bool sustained(){
    LiveMotionDetector d;
    return !d.update(0,true,true)&&!d.update(149,true,true)&&d.update(150,true,true)&&!d.update(200,true,true);
}
constexpr bool transient(){
    LiveMotionDetector d;
    return !d.update(0,true,true)&&!d.update(100,false,true)&&!d.update(200,true,true)&&!d.update(300,false,true);
}
constexpr bool invalidBreaksSustain(){
    LiveMotionDetector d;
    return !d.update(0,true,true)&&!d.update(140,true,false)&&!d.update(160,true,true)&&!d.update(300,true,true)&&d.update(310,true,true);
}
constexpr bool gapsBreakSustain(){
    LiveMotionDetector d;
    return !d.update(0,true,true)&&!d.update(500,true,true)&&!d.update(649,true,true)&&d.update(650,true,true);
}
constexpr bool quietAndCooldown(){
    LiveMotionDetector d;
    if(d.update(0,true,true)||!d.update(150,true,true))return false;
    if(d.update(200,false,true)||d.update(400,false,true)||d.update(699,false,true)||d.update(700,false,true))return false;
    if(d.update(800,true,true)||d.update(1000,true,true)||d.update(1200,true,true)||d.update(1400,true,true)||d.update(1600,true,true)||d.update(1800,true,true)||d.update(2000,true,true))return false;
    return d.update(2150,true,true)&&!d.update(2200,true,true);
}
constexpr bool noRearmWhileMoving(){
    LiveMotionDetector d;
    if(d.update(0,true,true)||!d.update(150,true,true))return false;
    for(uint32_t at=200;at<10000;at+=200)if(d.update(at,true,true))return false;
    return true;
}
constexpr bool clockWrap(){
    LiveMotionDetector d;
    return !d.update(0xffffffb0U,true,true)&&!d.update(0x40U,true,true)&&d.update(0x46U,true,true);
}
constexpr bool resetSession(){
    LiveMotionDetector d;
    if(d.update(0,true,true)||!d.update(150,true,true))return false;
    d.reset();return !d.update(200,true,true)&&d.update(350,true,true);
}
static_assert(sustained(),"Live marker requires150ms and latches once");
static_assert(transient(),"Transient bump does not establish sustained movement");
static_assert(invalidBreaksSustain(),"Invalid samples break sustain");
static_assert(gapsBreakSustain(),"Sampling gaps break sustain");
static_assert(quietAndCooldown(),"Quiet rearm and2s spacing both required");
static_assert(noRearmWhileMoving(),"Continuous movement cannot flood markers");
static_assert(clockWrap(),"Elapsed detection survives millis wrap");
static_assert(resetSession(),"A new authenticated session starts a new detector window");
}
