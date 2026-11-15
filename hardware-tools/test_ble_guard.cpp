#define TRAVELBLE_TEST_PLATFORM "ble_platform.h"
#include <cassert>
#include <cstdlib>
#include <iostream>
#include "../firmware/suitcase-ble/ble_app.h"

static unsigned checks=0,scenarios=0;
#define CHECK(condition) do {++checks;if(!(condition)){std::cerr<<"FAILED line "<<__LINE__<<": " #condition "\n";std::abort();}} while(false)
using suitcase::State;
using suitcase::Reason;
using suitcase::GuardResult;

struct Imu {
    bool healthy=true,moving=false;
    bool isConnected(){return healthy;}
    bool hasFreshSample(unsigned){return healthy;}
    uint32_t getSampleCount(){return testNow/20+1;}
    unsigned getSampleAgeMs(){return healthy?0:999;}
    unsigned getReadErrors(){return healthy?0:1;}
    void readAll(float* ax,float* ay,float* az,float* gx,float* gy,float* gz,void*){
        *ax=*ay=*gy=*gz=0;*az=1;*gx=moving?30:0;
    }
};
struct Button {bool pressed=false;bool isPressed(){return pressed;}void setColor(int,int,int){}};
struct Led {
    int pixels[9]={},shown[9]={};
    void clear(){memset(pixels,0,sizeof(pixels));}
    void setAll(int r,int g,int b){for(auto& p:pixels)p=(r<<16)|(g<<8)|b;}
    void setPixel(unsigned i,int r,int g,int b){if(i<9)pixels[i]=(r<<16)|(g<<8)|b;}
    void show(){memcpy(shown,pixels,sizeof(pixels));}
    bool off()const{for(auto p:shown)if(p)return false;return true;}
};
struct Audio {bool sounding=false;void stop(){sounding=false;}void playTone(int,int){sounding=true;}};
struct Motor {int speed=0;bool isReady(){return true;}void stop(){speed=0;}void setSpeed(int value){speed=value;}};

template<class T> static void persistFixture(const char* key,const T& value){
    auto* p=reinterpret_cast<const uint8_t*>(&value);testNvs[key]={p,p+sizeof(value)};
}
struct Bench {
    crumbguard::FullApp alarm;
    travelble::App wireless;
    Imu imu;Led left,right;Button a,b;Audio audio;Motor motor;
    std::string device,boot;unsigned sequence=0;uint16_t frameId=0;
    travelble::Transport* radio=nullptr;
    explicit Bench(bool corruptConfig=false){
        testNow=100;Serial.lines.clear();testNvs.clear();testNvsWrites=testNvsCommits=testBondDeletes=0;
        travelble::ConfigRecord config;config.revision=1;memcpy(config.arm,"ABAB",5);memcpy(config.disarm,"ABBA",5);
        persistFixture("config",config);if(corruptConfig)testNvs["config"]={1,2};
        travelble::OwnerRecord owner;owner.identity[1]=0xab;uint8_t raw[32]={};mbedtls_sha256(raw,32,owner.hash,0);
        persistFixture("owner",owner);
        CHECK(wireless.begin(alarm));radio=travelble::Transport::active;cycle();
        auto s=snapshot();device=s["deviceId"].as<std::string>();boot=s["bootId"].as<std::string>();
    }
    void cycle(){wireless.tick(alarm,a,b);alarm.tick(imu,left,right,a,b,audio,motor);wireless.afterTick(alarm);}
    void advance(unsigned milliseconds){
        while(milliseconds){unsigned step=std::min(20u,milliseconds);testNow+=step;milliseconds-=step;cycle();}
    }
    JsonDocument command(const char* op,const char* desired=nullptr,const std::string& id=""){
        JsonDocument d;d["type"]="command";d["protocolVersion"]=1;d["id"]=id.empty()?"guard-test-"+std::to_string(++sequence):id;
        d["deviceId"]=device;d["bootId"]=boot;d["operation"]=op;if(desired)d["desiredState"]=desired;return d;
    }
    void deliver(const JsonDocument& d){
        std::string bytes;serializeJson(d,bytes);radio->inject(bytes,++frameId);
        while(!radio->incoming.empty())wireless.tick(alarm,a,b);
    }
    bool hasAck(const std::string& id){
        for(auto& bytes:radio->sent){JsonDocument d;CHECK(!deserializeJson(d,bytes));if(d["type"]=="ack"&&d["id"]==id)return true;}
        return false;
    }
    JsonDocument ack(const std::string& id){
        for(auto i=radio->sent.rbegin();i!=radio->sent.rend();++i){JsonDocument d;CHECK(!deserializeJson(d,*i));if(d["type"]=="ack"&&d["id"]==id)return d;}
        std::cerr<<"Missing ack "<<id<<"\n";std::abort();
    }
    JsonDocument send(JsonDocument d){auto id=d["id"].as<std::string>();deliver(d);alarm.tick(imu,left,right,a,b,audio,motor);wireless.afterTick(alarm);return ack(id);}
    JsonDocument snapshot(){
        JsonDocument result;
        for(auto i=radio->sent.rbegin();i!=radio->sent.rend();++i){JsonDocument d;CHECK(!deserializeJson(d,*i));if(d["snapshot"].is<JsonObject>()){result.set(d["snapshot"]);return result;}}
        std::abort();
    }
    void authenticate(){auto d=command("authenticate");d["ownerToken"]=std::string(43,'A')+'=';auto reply=send(std::move(d));CHECK(reply["ok"]==true);CHECK(reply["snapshot"]["authenticated"]==true);}
    JsonDocument guard(const char* desired){return send(command("setGuardState",desired));}
    void arm(){auto reply=guard("armed");CHECK(reply["ok"]==true);CHECK(reply["snapshot"]["guardState"]=="arming");}
    void armed(){arm();advance(5000);CHECK(alarm.guardState()==State::Armed);}
    void warning(bool sensorFault=false){armed();if(sensorFault)imu.healthy=false;else imu.moving=true;advance(200);CHECK(alarm.guardState()==State::Warning);}
    void sounding(bool sensorFault=false){warning(sensorFault);advance(2000);CHECK(alarm.guardState()==State::Alarm);CHECK(audio.sounding);}
    void tap(char key){(key=='A'?a:b).pressed=true;advance(60);(key=='A'?a:b).pressed=false;advance(60);}
    void code(const char* keys){for(unsigned i=0;i<4;++i)tap(keys[i]);}
    void unchangedStorage(){CHECK(testNvsWrites==0&&testNvsCommits==0&&testBondDeletes==0);}
    void fillStrict(unsigned target=64){
        while(snapshot()["strictRequestUsed"].as<unsigned>()<target){
            auto r=send(command("getSnapshot"));CHECK(r["ok"]==true);
        }
        CHECK(snapshot()["strictRequestUsed"]==target);
    }
};

static std::vector<JsonDocument> messageType(Bench& t,const char* type){
    std::vector<JsonDocument> result;
    for(const auto& bytes:t.radio->sent){JsonDocument value;CHECK(!deserializeJson(value,bytes));if(value["type"]==type)result.push_back(std::move(value));}
    return result;
}

int main(){
    {
        suitcase::Controller c;CHECK(c.state==State::Disarmed);CHECK(c.setCodes("ABAB","ABBA"));
        CHECK(c.setGuard(true,10,false)==GuardResult::SensorUnavailable);CHECK(c.state==State::Disarmed);
        for(auto state:{State::Arming,State::Armed,State::Warning,State::Alarm}){
            c.state=state;c.since=10;c.reason=Reason::SensorFault;c.tap(suitcase::Key::A,20,false);
            CHECK(c.setGuard(true,30,false)==GuardResult::Unchanged);CHECK(c.state==state&&c.since==10&&c.reason==Reason::SensorFault&&c.entered()==1);
            CHECK(c.setGuard(false,31,false)==GuardResult::Applied);CHECK(c.state==State::Disarmed&&c.reason==Reason::None&&c.entered()==0);
        }
        ++scenarios;
    }
    {
        Bench t;auto s=t.snapshot();CHECK(s["armSequence"]==""&&s["recordingState"]=="idle");
        bool guard=false,recording=false;for(auto cap:s["capabilities"].as<JsonArray>()){guard|=cap=="setGuardState";recording|=cap=="setRecordingMode";}
        CHECK(guard&&!recording);CHECK(t.alarm.guardState()==State::Disarmed&&t.motor.speed==0);
        for(auto desired:{"armed","disarmed"}){auto r=t.guard(desired);CHECK(r["ok"]==false&&r["error"]=="owner_authentication_required");}
        auto bad=t.command("authenticate");bad["ownerToken"]="wrong";CHECK(t.send(std::move(bad))["ok"]==false);
        t.radio->ownerBond=false;auto other=t.command("authenticate");other["ownerToken"]=std::string(43,'A')+'=';
        CHECK(t.send(std::move(other))["error"]=="wrong_owner_bond");t.radio->ownerBond=true;t.authenticate();
        s=t.snapshot();CHECK(s["armSequence"]=="ABAB"&&s["disarmSequence"]=="ABBA"&&s["configRevision"]==1);
        t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();
        for(auto value:{"Armed","arming","warning","alarm","","toggle"}){auto r=t.guard(value);CHECK(r["ok"]==false&&r["error"]=="invalid_desired_state");}
        for(int value=0;value<4;++value){auto d=t.command("setGuardState");if(value==1)d["desiredState"]=true;else if(value==2)d["desiredState"]=1;else if(value==3)d["desiredState"]=nullptr;
            auto r=t.send(std::move(d));CHECK(r["ok"]==false&&r["error"]=="invalid_desired_state");}
        auto embedded=t.command("setGuardState");embedded["desiredState"]=std::string("armed\0x",7);CHECK(t.send(std::move(embedded))["error"]=="invalid_desired_state");
        for(auto field:{"deviceId","bootId"}){auto d=t.command("setGuardState","armed");d[field]="stale";auto r=t.send(std::move(d));CHECK(r["ok"]==false);CHECK(r["error"]==(std::string(field)=="deviceId"?"wrong_device":"wrong_boot"));}
        auto d=t.command("setGuardState","armed");d["protocolVersion"]=2;CHECK(t.send(std::move(d))["error"]=="unsupported_envelope_version");
        d=t.command("setGuardState","armed");d.remove("bootId");CHECK(t.send(std::move(d))["error"]=="missing_identity");
        CHECK(t.alarm.guardState()==State::Disarmed&&t.motor.speed==0);t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();auto d=t.command("setGuardState","armed","same-arm");t.deliver(d);
        CHECK(t.alarm.guardState()==State::Disarmed&&!t.hasAck("same-arm"));
        t.deliver(d);CHECK(!t.hasAck("same-arm")); // duplicate pending does not queue twice
        t.alarm.tick(t.imu,t.left,t.right,t.a,t.b,t.audio,t.motor);
        CHECK(t.alarm.guardState()==State::Arming&&!t.hasAck("same-arm"));
        t.wireless.afterTick(t.alarm);auto reply=t.ack("same-arm");CHECK(reply["ok"]==true&&reply["snapshot"]["guardState"]=="arming");
        t.advance(4900);CHECK(t.alarm.guardState()==State::Arming);
        CHECK(t.send(d)["ok"]==true);CHECK(t.guard("armed")["ok"]==true);
        auto conflict=t.command("setGuardState","disarmed","same-arm");CHECK(t.send(std::move(conflict))["error"]=="request_id_conflict");
        t.advance(100);CHECK(t.alarm.guardState()==State::Armed); // repeat never restarted settling
        CHECK(t.guard("disarmed")["ok"]==true);CHECK(t.send(d)["ok"]==true);CHECK(t.alarm.guardState()==State::Disarmed); // cached old arm never re-executes
        t.radio->disconnect();t.cycle();t.radio->connected=true;t.cycle();
        CHECK(t.send(d)["error"]=="owner_authentication_required");CHECK(t.alarm.guardState()==State::Disarmed);
        t.authenticate();CHECK(t.send(d)["ok"]==true);CHECK(t.alarm.guardState()==State::Disarmed);
        t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();t.imu.moving=true;t.arm();t.advance(4500);t.imu.moving=false;t.advance(500);
        CHECK(t.alarm.guardState()==State::Arming);
        // Twenty quiet calibration samples precede the controller's quiet timer.
        t.advance(899);CHECK(t.alarm.guardState()==State::Arming);t.advance(1);CHECK(t.alarm.guardState()==State::Armed);
        t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();t.imu.moving=true;t.arm();t.advance(19980);CHECK(t.alarm.guardState()==State::Arming);
        CHECK(t.guard("armed")["ok"]==true);t.advance(20);CHECK(t.alarm.guardState()==State::Disarmed); // original 20-second cancellation
        t.unchangedStorage();++scenarios;
    }
    for(auto state:{State::Arming,State::Armed,State::Warning,State::Alarm}){
        Bench t;t.authenticate();
        if(state==State::Arming)t.arm();else if(state==State::Armed)t.armed();else if(state==State::Warning)t.warning();else t.sounding();
        if(state==State::Alarm)CHECK(t.motor.speed>0);
        t.tap('A');t.tap('B');CHECK(t.alarm.codeProgress()==2);
        t.imu.healthy=false;auto d=t.command("setGuardState","disarmed","stop-now");t.radio->sent.clear();t.deliver(d);
        CHECK(!t.hasAck("stop-now"));
        ++testNow;t.alarm.tick(t.imu,t.left,t.right,t.a,t.b,t.audio,t.motor);
        CHECK(t.alarm.guardState()==State::Disarmed&&t.motor.speed==0&&!t.audio.sounding&&t.left.off()&&t.right.off());
        CHECK(!t.hasAck("stop-now"));t.wireless.afterTick(t.alarm);auto reply=t.ack("stop-now");
        CHECK(reply["ok"]==true&&reply["snapshot"]["guardState"]=="disarmed"&&reply["snapshot"]["motorState"]=="idle");
        CHECK(reply["snapshot"]["codeProgress"]==0&&reply["snapshot"]["configRevision"]==1&&reply["snapshot"]["recordingState"]=="idle");
        t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();t.sounding(true);CHECK(t.motor.speed==0);CHECK(t.alarm.guardReason()==Reason::SensorFault);
        CHECK(t.guard("armed")["ok"]==true);CHECK(t.alarm.guardState()==State::Alarm&&t.alarm.guardReason()==Reason::SensorFault);
        auto reply=t.guard("disarmed");CHECK(reply["ok"]==true&&t.alarm.guardState()==State::Disarmed&&!t.audio.sounding);
        auto failed=t.command("setGuardState","armed","failed-arm");CHECK(t.send(failed)["error"]=="sensor_unavailable");
        t.imu.healthy=true;t.advance(40);CHECK(t.send(failed)["error"]=="sensor_unavailable");CHECK(t.alarm.guardState()==State::Disarmed);
        t.arm();t.imu.healthy=false;t.advance(20);CHECK(t.alarm.guardState()==State::Disarmed); // settling health cancellation
        t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();t.sounding();t.advance(200);CHECK(t.motor.speed==192);
        CHECK(t.guard("armed")["ok"]==true);CHECK(t.alarm.guardState()==State::Alarm&&t.motor.speed==192);
        t.advance(16000);CHECK(std::string(t.alarm.motorPhase())=="finished"&&t.motor.speed==0);
        CHECK(t.guard("armed")["ok"]==true);t.advance(2000);CHECK(std::string(t.alarm.motorPhase())=="finished"&&t.motor.speed==0);
        t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();t.sounding();t.a.pressed=t.b.pressed=true;t.advance(520);
        CHECK(t.motor.speed==0&&t.alarm.guardState()==State::Alarm&&std::string(t.alarm.motorPhase())=="inhibited");
        t.a.pressed=t.b.pressed=false;t.advance(60);t.code("ABBA");CHECK(t.alarm.guardState()==State::Disarmed&&t.motor.speed==0);
        t.imu.moving=false;t.code("ABAB");CHECK(t.alarm.guardState()==State::Arming);t.code("ABBA");CHECK(t.alarm.guardState()==State::Disarmed);
        auto r=t.send(t.command("getSnapshot"));CHECK(r["snapshot"]["configRevision"]==1&&r["snapshot"]["armSequence"]=="ABAB"&&r["snapshot"]["disarmSequence"]=="ABBA");
        t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();CHECK(t.alarm.captureIfDisarmed(true));t.arm();t.guard("disarmed");Serial.lines.clear();t.advance(40);
        bool capture=false;for(auto& line:Serial.lines)capture|=line.find("motion_frame")!=std::string::npos;
        CHECK(capture);CHECK(t.snapshot()["recordingState"]=="idle"); // diagnostic capture preserved; never claimed as durable recording
        t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();auto config=t.command("setButtonSequences");config["armSequence"]="AAAA";config["disarmSequence"]="BBBB";config["expectedConfigRevision"]=1;
        t.deliver(config);CHECK(t.snapshot()["configRevision"]==1); // fake worker intentionally never runs
        CHECK(t.guard("armed")["error"]=="configuration_busy");CHECK(t.guard("disarmed")["ok"]==true);
        CHECK(t.alarm.guardState()==State::Disarmed);t.unchangedStorage();++scenarios;
    }
    {
        Bench t(true);t.authenticate();CHECK(t.guard("armed")["error"]=="configuration_unhealthy");CHECK(t.guard("disarmed")["ok"]==true);
        CHECK(t.alarm.guardState()==State::Disarmed&&t.motor.speed==0);t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();t.fillStrict(63);
        auto arm=t.command("setGuardState","armed","last-slot-arm");CHECK(t.send(arm)["ok"]==true);
        CHECK(t.alarm.guardState()==State::Arming&&t.snapshot()["strictRequestUsed"]==64);
        auto first=t.command("setGuardState","disarmed","overflow-stop-first");CHECK(t.send(first)["ok"]==true);
        CHECK(t.alarm.guardState()==State::Disarmed&&t.snapshot()["configRevision"]==1);
        CHECK(t.guard("armed")["error"]=="request_cache_full_reboot_when_disarmed");
        t.code("ABAB");CHECK(t.alarm.guardState()==State::Arming);
        CHECK(t.send(first)["ok"]==true);CHECK(t.alarm.guardState()==State::Arming); // retained ID returns result; no re-execution
        auto conflict=first;conflict["desiredState"]="armed";CHECK(t.send(std::move(conflict))["error"]=="request_id_conflict");
        CHECK(t.guard("disarmed")["ok"]==true); // replaces only the completed overflow Disarm record
        CHECK(t.send(arm)["ok"]==true&&t.alarm.guardState()==State::Disarmed); // strict Arm ID never forgotten
        t.code("ABAB");CHECK(t.alarm.guardState()==State::Arming);
        CHECK(t.send(first)["ok"]==true&&t.alarm.guardState()==State::Disarmed); // retired overflow ID reasserts Disarm
        auto config=t.command("setButtonSequences");config["armSequence"]="AAAA";config["disarmSequence"]="BBBB";config["expectedConfigRevision"]=1;
        CHECK(t.send(std::move(config))["error"]=="request_cache_full_reboot_when_disarmed");
        CHECK(t.snapshot()["configRevision"]==1);t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();t.sounding();t.fillStrict();CHECK(t.motor.speed>0);
        auto stop=t.command("setGuardState","disarmed","full-alarm-stop");t.radio->sent.clear();t.deliver(stop);
        CHECK(!t.hasAck("full-alarm-stop")&&t.motor.speed>0);
        auto busy=t.command("setGuardState","disarmed");t.deliver(busy);CHECK(t.ack(busy["id"].as<std::string>())["error"]=="request_in_progress");
        t.alarm.tick(t.imu,t.left,t.right,t.a,t.b,t.audio,t.motor);t.wireless.afterTick(t.alarm);
        CHECK(t.ack("full-alarm-stop")["ok"]==true&&t.motor.speed==0&&!t.audio.sounding&&t.alarm.guardState()==State::Disarmed);
        auto stale=t.command("setGuardState","disarmed");stale["bootId"]="old";CHECK(t.send(std::move(stale))["error"]=="wrong_boot");
        t.radio->ownerBond=false;CHECK(t.guard("disarmed")["error"]=="owner_authentication_required");t.radio->ownerBond=true;
        t.code("ABAB");CHECK(t.alarm.guardState()==State::Arming);CHECK(t.send(stop)["ok"]==true&&t.alarm.guardState()==State::Arming); // rejected requests never evicted last Disarm
        CHECK(t.guard("disarmed")["ok"]==true);t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();t.fillStrict();auto stop=t.command("setGuardState","disarmed","retained-stop");CHECK(t.send(stop)["ok"]==true);
        t.radio->disconnect();t.cycle();t.radio->connected=true;t.cycle();
        auto publicSnapshot=t.send(t.command("getSnapshot"));CHECK(publicSnapshot["ok"]==true&&publicSnapshot["snapshot"]["authenticated"]==false);
        CHECK(t.guard("disarmed")["error"]=="owner_authentication_required");
        auto bad=t.command("authenticate");bad["ownerToken"]="wrong";CHECK(t.send(std::move(bad))["error"]=="invalid_owner_token");
        CHECK(t.guard("disarmed")["error"]=="owner_authentication_required");
        auto auth=t.command("authenticate");auth["ownerToken"]=std::string(43,'A')+'=';CHECK(t.send(auth)["ok"]==true);
        CHECK(t.snapshot()["authenticated"]==true);t.code("ABAB");CHECK(t.alarm.guardState()==State::Arming);
        CHECK(t.send(stop)["ok"]==true&&t.alarm.guardState()==State::Arming); // auth/read overflow never evicts Disarm slot
        CHECK(t.guard("disarmed")["ok"]==true);
        t.radio->disconnect();t.cycle();t.radio->connected=true;t.cycle();
        auto again=t.send(auth);CHECK(again["ok"]==true&&again["snapshot"]["authenticated"]==true); // duplicate auth rechecks token on new link
        CHECK(t.guard("disarmed")["ok"]==true);CHECK(t.snapshot()["configRevision"]==1);t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.advance(1000);CHECK(messageType(t,"motion").empty()&&messageType(t,"event").empty());
        t.authenticate();t.radio->sent.clear();t.advance(1000);
        auto samples=messageType(t,"motion");CHECK(samples.size()==5);
        uint32_t previous=0;
        for(auto& value:samples){auto s=value["sample"];CHECK(s["deviceId"]==t.device&&s["bootId"]==t.boot);
            CHECK(s["sensorFresh"]==true&&s["deltaG"].as<float>()==0&&s["gyroDps"].as<float>()==0);
            CHECK(s["sequence"].as<uint32_t>()>previous&&s["sampleAgeMs"].as<unsigned>()<=20);previous=s["sequence"].as<uint32_t>();}
        t.imu.moving=true;t.advance(180);auto events=messageType(t,"event");CHECK(events.size()==1);
        auto e=events.back()["bagEvent"];CHECK(e["type"]=="movement"&&e["recordedAtUtc"]==""&&e["peakG"].as<float>()==0);
        CHECK(e["deviceId"]==t.device&&e["bootId"]==t.boot&&e["sequence"].as<unsigned>()>0);
        CHECK(t.alarm.guardState()==State::Disarmed&&t.motor.speed==0&&!t.audio.sounding);
        t.advance(3000);CHECK(messageType(t,"event").size()==1);
        t.imu.moving=false;t.advance(600);t.imu.moving=true;t.advance(180);CHECK(messageType(t,"event").size()==2);
        t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();t.advance(400);auto before=messageType(t,"motion").back()["sample"]["sequence"].as<uint32_t>();
        t.imu.healthy=false;t.advance(400);auto staleMessages=messageType(t,"motion");auto stale=staleMessages.back()["sample"];
        CHECK(stale["sensorFresh"]==false&&stale["deltaG"].as<float>()==0&&stale["gyroDps"].as<float>()==0);
        t.radio->disconnect();t.cycle();t.radio->sent.clear();t.advance(400);CHECK(messageType(t,"motion").empty());
        t.radio->connected=true;t.cycle();t.advance(400);CHECK(messageType(t,"motion").empty());
        t.imu.healthy=true;t.authenticate();t.advance(400);auto afterMessages=messageType(t,"motion");auto after=afterMessages.back()["sample"];
        CHECK(after["sensorFresh"]==true&&after["sequence"].as<uint32_t>()>before);
        CHECK(t.alarm.guardState()==State::Disarmed&&t.motor.speed==0&&!t.audio.sounding);t.unchangedStorage();++scenarios;
    }
    {
        Bench t;t.authenticate();t.radio->sent.clear();
        // A radio callback can replace the generation after App::tick but before
        // afterTick. Prior connection authorization must not survive that gap.
        t.radio->disconnect();t.radio->connected=true;t.wireless.afterTick(t.alarm);
        CHECK(messageType(t,"motion").empty()&&messageType(t,"event").empty());
        CHECK(t.snapshot()["authenticated"]==false&&!t.radio->liveAllowed);
        t.cycle();t.authenticate();t.advance(200);CHECK(!messageType(t,"motion").empty());
        t.unchangedStorage();++scenarios;
    }
    std::cout<<scenarios<<" guard/controller/protocol scenarios passed ("<<checks<<" assertions); hardware/radio/crypto boundaries mocked, no device actions\n";
}
