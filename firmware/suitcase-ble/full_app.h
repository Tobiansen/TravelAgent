#pragma once
#include "alarm_logic_tests.h"
#include "alarm_app.h"
#include "dispenser_tests.h"
#include "integration_tests.h"
#include "guard_tests.h"
#include "live_motion_tests.h"

namespace crumbguard {
class FullApp {
    suitcase::App alarm;
    Dispenser dispenser;
    bool enabled=true, bothTracking=false;
    uint32_t bothAt=0, reportedAt=0;
    int applied=-1;
    DispensePhase previous=DispensePhase::Idle;
public:
    suitcase::State guardState() const {return alarm.currentState();}
    suitcase::Reason guardReason() const {return alarm.alarmReason();}
    unsigned codeProgress() const {return alarm.codeProgress();}
    const char* motorPhase() const {return name(dispenser.phase);}
    suitcase::MotionReading motionReading(uint32_t now) const {return alarm.motionReading(now);}
    void resetInput(){alarm.resetInput();}
    void lockConfiguration(bool value){alarm.lockConfiguration(value);}
    bool setCodes(const char* a,const char* b){return alarm.setCodes(a,b);}
    bool queueGuard(bool armed){return alarm.queueGuard(armed);}
    bool takeGuardResult(suitcase::GuardResult& result){return alarm.takeGuardResult(result);}
    // Commanded direction only; actual rotation and loaded drum speed are unmeasured.
    // A perforated drum can use either direction. Change to -1 if the assembly needs it.
    inline static constexpr int motorDirection=1;
    template<class Motor> void disable(Motor& motor) {enabled=false;motor.stop();applied=0;}
    template<class Motor> bool enableIfDisarmed(Motor& motor) {
        if(alarm.currentState()!=suitcase::State::Disarmed || !motor.isReady()) return false;
        enabled=true;return true;
    }
    bool captureIfDisarmed(bool value) {
        if(value && alarm.currentState()!=suitcase::State::Disarmed)return false;
        alarm.capture(value);return true;
    }
    template<class Imu,class Motor> void report(Imu& imu,Motor& motor) {
        Serial.printf("{\"type\":\"event\",\"payload\":{\"event_type\":\"state\",\"key\":\"alarm_status\",\"value\":{"
            "\"state\":\"%s\",\"reason\":\"%s\",\"dispenser\":\"%s\",\"pwm\":%u,\"bursts\":%u,"
            "\"enabled\":%s,\"pwm_ready\":%s,\"sensor_fresh\":%s,\"model_trained\":%s,\"uptime_ms\":%lu}}}\n",
            suitcase::name(alarm.currentState()),suitcase::name(alarm.alarmReason()),name(dispenser.phase),
            dispenser.pwm,dispenser.bursts,enabled?"true":"false",motor.isReady()?"true":"false",
            alarm.sensorHealthy()?"true":"false",alarm.modelTrained()?"true":"false",(unsigned long)millis());
    }
    template<class Imu,class Led,class Button,class Audio,class Motor>
    void tick(Imu& imu,Led& left,Led& right,Button& a,Button& b,Audio& audio,Motor& motor) {
        const uint32_t now=millis();
        const bool both=a.isPressed() && b.isPressed();
        if(both && !bothTracking){bothTracking=true;bothAt=now;}
        if(!both)bothTracking=false;
        const bool inhibit=bothTracking && uint32_t(now-bothAt)>=500;
        alarm.tick(imu,left,right,a,b,audio);
        dispenser.tick(now,alarm.currentState()==suitcase::State::Alarm,
            alarm.alarmReason()==suitcase::Reason::Motion,motor.isReady(),alarm.sensorHealthy(),inhibit,enabled);
        if(applied!=dispenser.pwm){motor.setSpeed(motorDirection*dispenser.pwm);applied=dispenser.pwm;}
        if(previous!=dispenser.phase) {
            previous=dispenser.phase;
            Serial.printf("{\"type\":\"event\",\"payload\":{\"event_type\":\"state\",\"key\":\"dispenser_state\",\"value\":{"
                "\"phase\":\"%s\",\"pwm\":%u,\"bursts\":%u,\"uptime_ms\":%lu}}}\n",name(dispenser.phase),
                dispenser.pwm,dispenser.bursts,(unsigned long)now);
        }
        if(uint32_t(now-reportedAt)>=1000){reportedAt=now;report(imu,motor);}
    }
};
}
