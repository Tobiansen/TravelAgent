#pragma once
#include "alarm_logic.h"
#include "dispenser_logic.h"
#include "ble_guard_validation.h"
#include "ble_request_admission.h"
namespace travelble::guard_tests {
using suitcase::Controller;using suitcase::State;using suitcase::Reason;using suitcase::GuardResult;
constexpr bool disarm(State state){
    Controller c;c.setCodes("ABAB","ABBA");c.state=state;c.reason=Reason::SensorFault;
    c.tap(suitcase::Key::A,10,false);
    c.setGuard(false,20,false);
    return c.state==State::Disarmed&&c.reason==Reason::None&&c.entered()==0&&
        c.armCode[0]==1&&c.armCode[1]==2&&c.disarmCode[1]==2&&c.disarmCode[3]==1;
}
constexpr bool repeat(State state){
    Controller c;c.state=state;c.reason=Reason::Motion;c.since=1;c.tap(suitcase::Key::B,2,true);
    return c.setGuard(true,999,false)==GuardResult::Unchanged&&c.state==state&&
        c.since==1&&c.reason==Reason::Motion&&c.entered()==1;
}
constexpr bool settling(){
    Controller c;if(c.setGuard(true,100,true)!=GuardResult::Applied||c.state!=State::Arming)return false;
    c.tick(101,true,true,false);c.tick(5099,true,true,false);if(c.state!=State::Arming)return false;
    c.setGuard(true,5099,true);c.tick(5100,true,true,false);return c.state==State::Armed;
}
constexpr bool quietSecond(){
    Controller c;c.setGuard(true,100,true);c.tick(4999,true,false,false);c.tick(5000,true,true,false);
    c.tick(5999,true,true,false);if(c.state!=State::Arming)return false;
    c.tick(6000,true,true,false);return c.state==State::Armed;
}
constexpr bool cancel(){
    Controller c;c.setGuard(true,100,true);c.tick(20099,true,false,false);
    if(c.state!=State::Arming)return false;
    c.setGuard(true,20099,true);c.tick(20100,true,false,false);
    return c.state==State::Disarmed;
}
constexpr bool health(){
    Controller c;if(c.setGuard(true,1,false)!=GuardResult::SensorUnavailable||c.state!=State::Disarmed)return false;
    c.setGuard(true,2,true);c.tick(3,false,false,false);return c.state==State::Disarmed;
}
constexpr bool motor(){
    Controller c;c.state=State::Alarm;c.reason=Reason::Motion;
    crumbguard::Dispenser d;d.tick(100,true,true,true,true,false,true);
    c.setGuard(true,300,true);d.tick(300,c.state==State::Alarm,true,true,true,false,true);
    if(d.pwm!=192||d.episodeAt!=100||d.bursts!=1)return false;
    c.setGuard(false,301,false);d.tick(301,c.state==State::Alarm,true,true,false,false,true);
    return d.pwm==0&&d.phase==crumbguard::DispensePhase::Idle;
}
constexpr bool noBudgetRestart(){
    Controller c;c.state=State::Alarm;c.reason=Reason::Motion;crumbguard::Dispenser d;
    d.tick(0,true,true,true,true,false,true);d.tick(20000,true,true,true,true,false,true);
    c.setGuard(true,21000,true);d.tick(21000,c.state==State::Alarm,true,true,true,false,true);
    return d.pwm==0&&d.phase==crumbguard::DispensePhase::Finished&&d.episodeAt==0;
}
constexpr bool lastSlotArmDisarm(){
    Controller c;
    if(requestAdmission(true,"setGuardState","armed",true,false)!=RequestAdmission::Strict)return false;
    c.setGuard(true,1,true);
    if(requestAdmission(false,"setGuardState","disarmed",true,false)!=RequestAdmission::Overflow)return false;
    c.setGuard(false,2,false);
    return c.state==State::Disarmed;
}
static_assert(disarm(State::Disarmed),"Repeated disarm clears input without sensor health or code changes");
static_assert(disarm(State::Arming),"Remote disarm cancels Arming");
static_assert(disarm(State::Armed),"Remote disarm leaves Armed");
static_assert(disarm(State::Warning),"Remote disarm leaves fault Warning");
static_assert(disarm(State::Alarm),"Remote disarm clears latched fault Alarm");
static_assert(repeat(State::Arming),"Repeated arm preserves settling time and partial code");
static_assert(repeat(State::Armed),"Repeated arm preserves Armed");
static_assert(repeat(State::Warning),"Repeated arm preserves Warning and reason");
static_assert(repeat(State::Alarm),"Repeated arm preserves Alarm and reason");
static_assert(settling(),"Arm initially applies Arming and waits at least five seconds");
static_assert(quietSecond(),"Settling needs a final full quiet second");
static_assert(cancel(),"Repeated arm cannot postpone original twenty-second cancellation");
static_assert(health(),"Unhealthy arm rejected; sensor loss cancels settling");
static_assert(motor(),"Repeated protection preserves PWM budget; disarm clears it on next output pass");
static_assert(noBudgetRestart(),"Repeated arm never restarts finished dispensing");
static_assert(guardRequestError(true,true,"disarmed",true,false)==nullptr,"Disarm bypasses configuration busy/unhealthy gates");
static_assert(std::string_view(guardRequestError(false,true,"disarmed",false,true))=="owner_authentication_required","Disarm requires owner");
static_assert(std::string_view(guardRequestError(true,true,"Armed",false,true))=="invalid_desired_state"&&
              std::string_view(guardRequestError(true,true,std::string_view("armed\0x",7),false,true))=="invalid_desired_state"&&
              std::string_view(guardRequestError(true,false,"armed",false,true))=="invalid_desired_state","Exact string desired state required");
static_assert(std::string_view(guardRequestError(true,true,"armed",true,true))=="configuration_busy"&&
              std::string_view(guardRequestError(true,true,"armed",false,false))=="configuration_unhealthy","Configuration failures block arming only");
static_assert(identityError("getSnapshot","","","device","boot")==nullptr&&
              std::string_view(identityError("setGuardState","","","device","boot"))=="wrong_device","Only discovery can omit identity");
static_assert(std::string_view(identityError("setGuardState","other","boot","device","boot"))=="wrong_device"&&
              std::string_view(identityError("setGuardState","device","stale","device","boot"))=="wrong_boot","Reject stale target and boot");
static_assert(requestAdmission(false,"setGuardState","armed",true,false)==RequestAdmission::Full,"Full strict ledger rejects new Arm");
static_assert(requestAdmission(false,"setGuardState","disarmed",true,false)==RequestAdmission::Overflow,"Full strict ledger admits owner Disarm");
static_assert(requestAdmission(false,"setGuardState","disarmed",false,false)==RequestAdmission::Full,"Overflow cannot bypass owner authentication");
static_assert(requestAdmission(false,"setGuardState","Disarmed",true,false)==RequestAdmission::Full,"Overflow requires exact desired Disarm");
static_assert(requestAdmission(false,"setButtonSequences","disarmed",true,false)==RequestAdmission::Full,"Configuration writes never enter replaceable overflow");
static_assert(requestAdmission(false,"setGuardState","disarmed",true,true)==RequestAdmission::Busy,"Never replace pending Disarm");
static_assert(requestAdmission(true,"setGuardState","armed",true,false)==RequestAdmission::Strict&&
              requestAdmission(true,"setGuardState","disarmed",true,false)==RequestAdmission::Strict,"Free strict slots retain both guard IDs");
static_assert(requestAdmission(false,"authenticate","",false,false)==RequestAdmission::Overflow&&
              requestAdmission(false,"getSnapshot","",false,false)==RequestAdmission::Overflow,"Full ledger still permits discovery/authentication");
static_assert(overflowDisarmIndex>=int(strictRequestCapacity)&&overflowDisarmIndex!=overflowAuthIndex&&
              overflowDisarmIndex!=overflowSnapshotIndex&&overflowSnapshotIndex<int(totalRequestCapacity),"Separate bounded overflow lanes preserve Disarm retention");
static_assert(lastSlotArmDisarm(),"Arm consuming last strict slot cannot block subsequent Disarm");
}
