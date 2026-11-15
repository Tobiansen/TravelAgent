#pragma once
#include "alarm_logic.h"
#include "dispenser_logic.h"
namespace crumbguard {
constexpr void follow(Dispenser& d,const suitcase::Controller& c,uint32_t now) {
    d.tick(now,c.state==suitcase::State::Alarm,c.reason==suitcase::Reason::Motion,true,true,false,true);
}
constexpr bool codeMotionAndMotorTest() {
    using namespace suitcase;
    Controller c;Dispenser d;
    c.tap(Key::A,0,true);c.tap(Key::A,1,true);c.tap(Key::B,2,true);c.tap(Key::B,3,true);
    c.tick(4,true,true,false);c.tick(5003,true,true,false);
    if(c.state!=State::Armed)return false;
    follow(d,c,5003);if(d.pwm)return false;
    c.tick(6000,true,false,true);c.tick(6150,true,false,true);follow(d,c,6150);
    if(c.state!=State::Warning || d.pwm)return false;
    c.tick(8150,true,false,true);follow(d,c,8150);
    if(c.state!=State::Alarm || d.pwm!=255)return false;
    c.tap(Key::A,8200,true);c.tap(Key::A,8201,true);c.tap(Key::A,8202,true);c.tap(Key::A,8203,true);
    follow(d,c,8300);
    if(c.state!=State::Alarm || d.bursts!=1 || d.pwm!=192)return false;
    c.tap(Key::B,8400,true);c.tap(Key::A,8401,true);c.tap(Key::B,8402,true);c.tap(Key::A,8403,true);
    follow(d,c,8403);
    return c.state==State::Disarmed && d.pwm==0;
}
constexpr bool faultAlarmNoMotorTest() {
    using namespace suitcase;
    Controller c;Dispenser d;c.state=State::Armed;
    c.tick(1,false,false,false);c.tick(2001,false,false,false);follow(d,c,2001);
    return c.state==State::Alarm && c.reason==Reason::SensorFault && d.pwm==0;
}
static_assert(codeMotionAndMotorTest(),"Full interaction: codes, warning, motor, wrong code, disarm");
static_assert(faultAlarmNoMotorTest(),"Fault alarm lights/sound path does not release crumbs");
}
