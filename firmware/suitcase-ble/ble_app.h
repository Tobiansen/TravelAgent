#pragma once
#include <ArduinoJson.h>
#include "full_app.h"
#include "ble_requests.h"
// Host protocol tests inject hardware boundaries while executing this same App.
#ifdef TRAVELBLE_TEST_PLATFORM
#include TRAVELBLE_TEST_PLATFORM
#else
#include <esp_random.h>
#include <mbedtls/base64.h>
#include <mbedtls/sha256.h>
#include "ble_transport.h"
#include "ble_storage.h"
#endif

namespace travelble {
inline void digest(const std::string& value,uint8_t out[32]){
    mbedtls_sha256(reinterpret_cast<const uint8_t*>(value.data()),value.size(),out,0);
}
inline bool equalSecret(const uint8_t* a,const uint8_t* b,size_t size){uint8_t diff=0;for(size_t i=0;i<size;++i)diff|=a[i]^b[i];return diff==0;}
inline bool safeId(const std::string& s){
    if(s.empty()||s.size()>64)return false;
    for(auto c:s)if(!(c>='a'&&c<='z')&&!(c>='A'&&c<='Z')&&!(c>='0'&&c<='9')&&c!='-'&&c!='_'&&c!='.')return false;
    return true;
}
inline bool validUtf8(const std::string& s){
    size_t i=0;while(i<s.size()){
        uint32_t c=uint8_t(s[i++]);if(c<0x80){if(!c)return false;continue;}
        unsigned extra;uint32_t minimum;
        if(c>=0xc2&&c<=0xdf){extra=1;minimum=0x80;c&=31;}
        else if(c>=0xe0&&c<=0xef){extra=2;minimum=0x800;c&=15;}
        else if(c>=0xf0&&c<=0xf4){extra=3;minimum=0x10000;c&=7;}
        else return false;
        if(i+extra>s.size())return false;
        while(extra--){auto next=uint8_t(s[i++]);if((next&0xc0)!=0x80)return false;c=(c<<6)|(next&63);}
        if(c<minimum||c>0x10ffff||(c>=0xd800&&c<=0xdfff))return false;
    }return true;
}

class App {
    enum class Work { Config, Claim, ResetOwner };
    struct Job {Work work;int cache=-1;bool ok=false;ConfigRecord next{},previous{};OwnerRecord owner{};};
    // Strict configuration/Arm IDs are never evicted. At capacity, independent
    // one-entry Disarm/auth/snapshot lanes preserve bounded remote recovery.
    Transport transport;
    Storage storage;
    Reassembler frames;
    RequestCache cache;
    int guardRequest=-1;
    QueueHandle_t jobs=nullptr,done=nullptr;
    bool started=false,authorized=false,wasReady=false,jobPending=false;
    bool bothTracking=false,bothReleasing=false,aTracking=false,bTracking=false,resetConfirm=false;
    uint32_t releaseAt=0;
    uint32_t bothAt=0,aAt=0,bAt=0,confirmAt=0,windowAt=0,seenEpoch=0,lastSnapshot=0;
    uint32_t maxTickUs=0,invalidFrames=0,indicationAt=0;
    bool window=false;
    LiveMotionDetector liveDetector;
    bool liveSession=false,liveSampleSeen=false;
    uint32_t lastLiveSample=0,lastMotionAt=0,motionSequence=0,eventSequence=0;
    std::string deviceId,bootId,lastSignature;
    std::string status="Disarmed; hold both buttons 3 seconds and release to enroll";
    static void workerEntry(void* self){static_cast<App*>(self)->storageWorker();}
    void storageWorker(){
        Job* job=nullptr;
        for(;;){
            if(xQueueReceive(jobs,&job,portMAX_DELAY)!=pdTRUE)continue;
            if(job->work==Work::Config)job->ok=Storage::saveConfig(job->next,job->previous);
            else if(job->work==Work::Claim)job->ok=Storage::saveOwner(job->owner);
            else job->ok=Storage::resetOwner();
            xQueueSend(done,&job,portMAX_DELAY); // This worker alone can wait on NVS.
        }
    }
    bool ownerIdentity() const{
        uint8_t identity[7];return storage.hasOwner&&storage.ownerHealthy&&transport.identity(identity)&&
            equalSecret(identity,storage.owner.identity,7);
    }
    bool ownerAuthenticated() const{
        const uint32_t authenticatedEpoch=transport.epoch();
        return authorized&&seenEpoch==authenticatedEpoch&&ownerIdentity()&&authenticatedEpoch==transport.epoch();
    }
    void snapshot(JsonObject out,crumbguard::FullApp& alarm){
        const bool authenticated=ownerAuthenticated();
        out["protocolVersion"]=1;out["deviceId"]=deviceId;out["bootId"]=bootId;
        out["connected"]=transport.ready();out["authenticated"]=authenticated;
        out["guardState"]=suitcase::name(alarm.guardState());out["reason"]=suitcase::name(alarm.guardReason());
        out["recordingState"]="idle";
        out["armSequence"]=authenticated?storage.config.arm:"";
        out["disarmSequence"]=authenticated?storage.config.disarm:"";
        out["configRevision"]=storage.config.revision;out["motorState"]=alarm.motorPhase();
        out["codeProgress"]=alarm.codeProgress();out["status"]=status;
        auto capabilities=out["capabilities"].to<JsonArray>();
        capabilities.add("getSnapshot");capabilities.add("claimOwner");capabilities.add("authenticate");
        capabilities.add("setButtonSequences");capabilities.add("setGuardState");
        capabilities.add("liveMotion");capabilities.add("liveEvents");
        out["ownerClaimed"]=storage.hasOwner;out["enrollmentOpen"]=window;
        out["configurationHealthy"]=storage.configHealthy;out["configurationBusy"]=jobPending;
        out["strictRequestCapacity"]=strictRequestCapacity;out["strictRequestUsed"]=cache.strictUsed();
        out["overflowDisarmIdRetention"]="latest";
        out["uptimeMs"]=millis();
        out["motionSampleIntervalMs"]=200;
        out["motionSamplesDropped"]=transport.motionDropped.load();
        out["liveEventsDropped"]=transport.eventDropped.load();
    }
    void publishLive(crumbguard::FullApp& alarm){
        const bool authenticated=ownerAuthenticated();
        transport.allowLive(authenticated,seenEpoch);
        if(!authenticated){
            liveSession=false;liveSampleSeen=false;liveDetector.reset();return;
        }
        const uint32_t now=millis();
        if(!liveSession){
            liveSession=true;liveSampleSeen=false;liveDetector.reset();lastMotionAt=now-200;
        }
        const auto reading=alarm.motionReading(now);
        bool disturbance=false;
        if(!reading.sensorFresh)liveDetector.update(now,false,false);
        else if(!liveSampleSeen||reading.elapsedMs!=lastLiveSample){
            liveSampleSeen=true;lastLiveSample=reading.elapsedMs;
            disturbance=liveDetector.update(reading.elapsedMs,reading.deltaG>0.12f||reading.gyroDps>20.0f,true);
        }
        if(disturbance){
            JsonDocument document;document["type"]="event";document["protocolVersion"]=1;
            auto event=document["bagEvent"].to<JsonObject>();
            event["protocolVersion"]=1;event["deviceId"]=deviceId;event["bootId"]=bootId;
            if(++eventSequence==0)++eventSequence;
            event["sequence"]=eventSequence;event["elapsedMs"]=reading.elapsedMs;
            event["type"]="movement";event["recordedAtUtc"]="";event["peakG"]=0;
            event["origin"]="Live device";
            event["quality"]="device_elapsed_only;live_connected;no_offline_replay";
            event["summary"]="Live movement threshold exceeded. Inspect bag and contents; motion does not prove damage.";
            std::string bytes;serializeJson(document,bytes);transport.sendEvent(std::move(bytes));
        }
        if(uint32_t(now-lastMotionAt)<200)return;
        lastMotionAt=now;
        JsonDocument document;document["type"]="motion";document["protocolVersion"]=1;
        auto sample=document["sample"].to<JsonObject>();
        sample["protocolVersion"]=1;sample["deviceId"]=deviceId;sample["bootId"]=bootId;
        if(++motionSequence==0)++motionSequence;
        sample["sequence"]=motionSequence;sample["elapsedMs"]=reading.elapsedMs;
        sample["deltaG"]=reading.deltaG;sample["gyroDps"]=reading.gyroDps;
        sample["sensorFresh"]=reading.sensorFresh;sample["sampleAgeMs"]=reading.sampleAgeMs;
        sample["droppedSamples"]=transport.motionDropped.load();
        std::string bytes;serializeJson(document,bytes);transport.sendMotion(std::move(bytes));
    }
    void sendSnapshot(crumbguard::FullApp& alarm){
        JsonDocument document;document["type"]="snapshot";document["protocolVersion"]=1;
        snapshot(document["snapshot"].to<JsonObject>(),alarm);
        std::string bytes;serializeJson(document,bytes);transport.send(std::move(bytes));lastSnapshot=millis();
    }
    void ack(const std::string& id,const std::string& operation,bool ok,const std::string& error,
             crumbguard::FullApp& alarm,const std::string& token=""){
        JsonDocument document;document["type"]="ack";document["protocolVersion"]=1;
        document["id"]=id;document["operation"]=operation;document["deviceId"]=deviceId;document["bootId"]=bootId;
        document["ok"]=ok;if(!error.empty())document["error"]=error;
        snapshot(document["snapshot"].to<JsonObject>(),alarm);
        if(!token.empty())document["ownerToken"]=token;
        std::string bytes;serializeJson(document,bytes);transport.send(std::move(bytes));
    }
    void finish(int index,bool ok,const char* error,crumbguard::FullApp& alarm){
        auto& entry=cache[index];entry.pending=false;entry.ok=ok;entry.error=error?error:"";
        if(entry.epoch==transport.epoch()){
            const auto& token=ownerAuthenticated()?entry.token:std::string{};
            ack(entry.id,entry.operation,ok,entry.error,alarm,token);
        }
    }
    void clearTokens(){for(auto& entry:cache){std::fill(entry.token.begin(),entry.token.end(),'\0');entry.token.clear();}}
    bool startJob(Job* job,crumbguard::FullApp& alarm){
        if(!job)return false;
        if(xQueueSend(jobs,&job,0)!=pdTRUE){delete job;return false;}
        jobPending=true;alarm.lockConfiguration(true);return true;
    }
    void process(const std::string& bytes,crumbguard::FullApp& alarm){
        if(!validUtf8(bytes)){++invalidFrames;return;}
        JsonDocument command;
        if(deserializeJson(command,bytes,DeserializationOption::NestingLimit(8))||!command.is<JsonObject>()){++invalidFrames;return;}
        const std::string id=command["id"].as<std::string>(),operation=command["operation"].as<std::string>();
        if(!command["id"].is<const char*>()||!command["operation"].is<const char*>()||!safeId(id)||!safeId(operation)){
            ack("","",false,"invalid_envelope",alarm);return;
        }
        // Validate identities before any admission/overflow replacement. Invalid
        // envelopes never consume a strict slot or evict a retained Disarm ID.
        auto earlyReject=[&](const char* error){ack(id,operation,false,error,alarm);};
        if(!command["type"].is<const char*>()||command["type"]!="command"||!command["protocolVersion"].is<int>()||command["protocolVersion"]!=1){earlyReject("unsupported_envelope_version");return;}
        if(!command["deviceId"].is<const char*>()||!command["bootId"].is<const char*>()){earlyReject("missing_identity");return;}
        const std::string target=command["deviceId"].as<std::string>(),boot=command["bootId"].as<std::string>();
        if(const auto* error=identityError(operation,target,boot,deviceId,bootId)){earlyReject(error);return;}
        const std::string desired=command["desiredState"].as<std::string>();
        if(operation=="setGuardState"){
            if(const auto* error=guardRequestError(ownerAuthenticated(),command["desiredState"].is<const char*>(),desired,false,true)){
                earlyReject(error);return;
            }
        }
        std::string canonical;serializeJson(command,canonical);uint8_t fingerprint[32];digest(canonical,fingerprint);
        // canonical may contain a credential; never print it and clear its buffer.
        std::fill(canonical.begin(),canonical.end(),'\0');
        const auto lookup=cache.reserve(id,operation,fingerprint,transport.epoch(),desired,ownerAuthenticated());
        if(lookup.state==RequestState::Conflict){ack(id,operation,false,"request_id_conflict",alarm);return;}
        if(lookup.state==RequestState::Full){ack(id,operation,false,"request_cache_full_reboot_when_disarmed",alarm);return;}
        if(lookup.state==RequestState::Busy){ack(id,operation,false,"request_in_progress",alarm);return;}
        if(lookup.state==RequestState::Pending)return;
        const int index=lookup.index;auto& entry=cache[index];
        if(lookup.state==RequestState::Complete&&operation!="authenticate"){
            if(operation=="setGuardState"&&!ownerAuthenticated()){
                ack(id,operation,false,"owner_authentication_required",alarm);return;
            }
            const bool tokenAllowed=entry.epoch==transport.epoch()&&ownerAuthenticated();
            ack(id,operation,entry.ok,entry.error,alarm,tokenAllowed?entry.token:std::string{});return;
        }
        // Authentication is connection state, not a persistent mutation. Always
        // verify the actual token/bond again, even for a retained duplicate ID.
        if(lookup.state==RequestState::Complete){entry.pending=true;entry.epoch=transport.epoch();}
        auto reject=[&](const char* error){finish(index,false,error,alarm);};
        if(operation=="getSnapshot"){finish(index,true,nullptr,alarm);return;}
        if(operation!="claimOwner"&&operation!="authenticate"&&operation!="setButtonSequences"&&operation!="setGuardState"){
            reject("unsupported_operation");return;
        }
        if(operation=="authenticate"){
            authorized=false;
            if(!ownerIdentity()){reject("wrong_owner_bond");return;}
            if(!command["ownerToken"].is<const char*>()){reject("invalid_owner_token");return;}
            std::string token=command["ownerToken"].as<std::string>();
            uint8_t decoded[33]={};size_t length=0;
            const bool encoded=token.size()==44&&mbedtls_base64_decode(decoded,sizeof(decoded),&length,
                reinterpret_cast<const uint8_t*>(token.data()),token.size())==0&&length==32;
            uint8_t hash[32]={};if(encoded)mbedtls_sha256(decoded,length,hash,0);
            authorized=encoded&&equalSecret(hash,storage.owner.hash,32);
            memset(decoded,0,sizeof(decoded));memset(hash,0,sizeof(hash));std::fill(token.begin(),token.end(),'\0');
            if(!authorized){reject("invalid_owner_token");return;}
            status="Owner phone authorized";finish(index,true,nullptr,alarm);return;
        }
        if(operation=="setGuardState"){
            if(const auto* error=guardRequestError(ownerAuthenticated(),command["desiredState"].is<const char*>(),
                                                   desired,jobPending,storage.configHealthy)){
                reject(error);return;
            }
            const bool armed=desired=="armed";
            if(guardRequest>=0||!alarm.queueGuard(armed)){reject("guard_busy");return;}
            guardRequest=index;return;
        }
        if(operation=="claimOwner"){
            if(jobPending){reject("configuration_busy");return;}
            if(alarm.guardState()!=suitcase::State::Disarmed){reject("disarm_first");return;}
            if(storage.hasOwner){reject("owner_already_claimed");return;}
            if(!window){reject("enrollment_window_closed");return;}
            auto* job=new(std::nothrow) Job{};if(!job){reject("storage_unavailable");return;}
            job->work=Work::Claim;job->cache=index;
            if(!transport.identity(job->owner.identity)){delete job;reject("encrypted_bond_required");return;}
            uint8_t random[32];esp_fill_random(random,sizeof(random));mbedtls_sha256(random,sizeof(random),job->owner.hash,0);
            uint8_t encoded[48]={};size_t count=0;
            mbedtls_base64_encode(encoded,sizeof(encoded),&count,random,sizeof(random));
            entry.token.assign(reinterpret_cast<const char*>(encoded),count);
            memset(random,0,sizeof(random));memset(encoded,0,sizeof(encoded));
            if(!startJob(job,alarm)){std::fill(entry.token.begin(),entry.token.end(),'\0');entry.token.clear();reject("storage_busy");}
            return;
        }
        if(!ownerAuthenticated()){reject("owner_authentication_required");return;}
        if(jobPending){reject("configuration_busy");return;}
        if(alarm.guardState()!=suitcase::State::Disarmed){reject("disarm_first");return;}
        if(!command["armSequence"].is<const char*>()||!command["disarmSequence"].is<const char*>()){
            reject("invalid_button_sequences");return;
        }
        const std::string a=command["armSequence"].as<std::string>(),b=command["disarmSequence"].as<std::string>();
        if(a.size()!=4||b.size()!=4||!validCode(a.c_str())||!validCode(b.c_str())||a==b){reject("invalid_button_sequences");return;}
        if(!command["expectedConfigRevision"].is<uint32_t>()||command["expectedConfigRevision"].as<uint32_t>()!=storage.config.revision){reject("stale_config_revision");return;}
        auto* job=new(std::nothrow) Job{};if(!job){reject("storage_unavailable");return;}
        job->work=Work::Config;job->cache=index;job->previous=storage.config;
        if(!nextConfig(storage.config,a.c_str(),b.c_str(),storage.config.revision,job->next)){delete job;reject("config_revision_exhausted");return;}
        if(!startJob(job,alarm))reject("storage_busy");
    }
    void completed(crumbguard::FullApp& alarm){
        Job* job=nullptr;if(xQueueReceive(done,&job,0)!=pdTRUE)return;
        jobPending=false;
        if(job->work==Work::Config){
            if(job->ok){storage.config=job->next;storage.configHealthy=true;alarm.setCodes(storage.config.arm,storage.config.disarm);status="Configuration saved";}
            else {storage.configHealthy=false;status="Configuration storage failed; previous active codes retained; arming locked";}
        }else if(job->work==Work::Claim){
            if(job->ok){
                storage.owner=job->owner;storage.hasOwner=storage.ownerHealthy=true;
                authorized=job->cache>=0&&cache[job->cache].epoch==transport.epoch()&&ownerIdentity();
                window=false;status="Owner enrolled";
            }else{storage.hasOwner=true;storage.ownerHealthy=false;clearTokens();status="Owner storage failed; physical ownership reset required";}
        }else{
            if(job->ok){
                authorized=false;storage.hasOwner=false;storage.ownerHealthy=true;
                memset(&storage.owner,0,sizeof(storage.owner));clearTokens();
                cache.clear();
                transport.disconnect();NimBLEDevice::deleteAllBonds();
                status="Ownership reset; release buttons then open enrollment window";
            }else status="Ownership reset storage failed";
        }
        alarm.lockConfiguration(!storage.configHealthy);
        if(job->cache>=0)finish(job->cache,job->ok,job->ok?nullptr:"storage_commit_or_readback_failed",alarm);
        memset(&job->owner,0,sizeof(job->owner));delete job;
    }
    void openWindow(){window=true;windowAt=millis();status="Enrollment open for 60 seconds; authorize phone";}
    void gestures(crumbguard::FullApp& alarm,bool a,bool b){
        const auto now=millis();
        if(window&&uint32_t(now-windowAt)>=60000){window=false;status="Enrollment window closed";}
        if(resetConfirm&&uint32_t(now-confirmAt)>15000){resetConfirm=false;status="Ownership reset cancelled";}
        if(alarm.guardState()!=suitcase::State::Disarmed||jobPending){
            bothTracking=bothReleasing=aTracking=bTracking=false;resetConfirm=false;window=false;return;
        }
        if(bothReleasing){
            alarm.resetInput();aTracking=bTracking=false;
            if(uint32_t(now-releaseAt)>2000)bothReleasing=false;
            else if(!a&&!b){bothReleasing=false;openWindow();}
            return;
        }
        if(a&&b){
            aTracking=bTracking=false;
            if(!bothTracking){bothTracking=true;bothAt=now;}
            if(uint32_t(now-bothAt)>=3000)alarm.resetInput();
        }else if(bothTracking){
            const auto held=uint32_t(now-bothAt);bothTracking=false;
            if(held>=3000&&held<8000){
                alarm.resetInput();aTracking=bTracking=false;
                if(!a&&!b)openWindow();else{bothReleasing=true;releaseAt=now;}
                return;
            }
        }
        if(a&&!b&&!resetConfirm){
            if(!aTracking){aTracking=true;aAt=now;}
            if(uint32_t(now-aAt)>=8000)alarm.resetInput();
        }else if(aTracking){
            const auto held=uint32_t(now-aAt);aTracking=false;
            if(!a&&!b&&held>=8000){resetConfirm=true;confirmAt=now;alarm.resetInput();status="Reset ownership? Hold B 3 seconds and release within 15 seconds";}
        }
        if(resetConfirm&&b&&!a){
            if(!bTracking){bTracking=true;bAt=now;}
            if(uint32_t(now-bAt)>=3000)alarm.resetInput();
        }else if(bTracking){
            const auto held=uint32_t(now-bAt);bTracking=false;
            if(resetConfirm&&!a&&!b&&held>=3000){
                resetConfirm=false;window=false;authorized=false;alarm.resetInput();
                auto* job=new(std::nothrow) Job{};
                if(job){job->work=Work::ResetOwner;if(!startJob(job,alarm))status="Ownership reset could not start";}
            }
        }
    }
public:
    bool begin(crumbguard::FullApp& alarm){
        storage.load();alarm.setCodes(storage.config.arm,storage.config.disarm);alarm.lockConfiguration(!storage.configHealthy);
        char name[24],identity[32],boot[33];const uint64_t mac=ESP.getEfuseMac();
        snprintf(identity,sizeof(identity),"cg-%012llx",static_cast<unsigned long long>(mac));deviceId=identity;
        uint8_t random[16];esp_fill_random(random,sizeof(random));
        for(unsigned i=0;i<16;++i)snprintf(boot+2*i,3,"%02x",random[i]);bootId=boot;
        snprintf(name,sizeof(name),"CrumbGuard-%04x",unsigned(mac&0xffff));
        jobs=xQueueCreate(1,sizeof(Job*));done=xQueueCreate(1,sizeof(Job*));
        if(!jobs||!done||xTaskCreate(workerEntry,"ble-store",6144,this,1,nullptr)!=pdPASS)return false;
        started=transport.begin(name);
        if(!storage.configHealthy)status="Stored configuration invalid; physical arming locked until configuration repair";
        Serial.printf("{\"type\":\"event\",\"payload\":{\"event_type\":\"state\",\"key\":\"ble_started\",\"value\":%s}}\n",started?"true":"false");
        return started;
    }
    template<class Button> void tick(crumbguard::FullApp& alarm,Button& a,Button& b){
        const auto began=micros();if(!started)return;
        if(seenEpoch!=transport.epoch()){
            seenEpoch=transport.epoch();authorized=false;wasReady=false;frames.clear();clearTokens();lastSignature.clear();
        }
        completed(alarm);gestures(alarm,a.isPressed(),b.isPressed());transport.allowPairing(window);
        a.setColor(resetConfirm?100:0,window?60:0,window||resetConfirm?100:0);
        b.setColor(resetConfirm?100:0,window?60:0,window||resetConfirm?100:0);
        Transport::Packet packet;
        // Bound callback work per pass. Hardware controls get a turn between passes.
        for(unsigned i=0;i<4&&transport.receive(packet);++i){
            if(packet.epoch!=transport.epoch()||packet.connection!=transport.handle()||!transport.ready())continue;
            std::string message;
            const auto result=frames.accept(packet.bytes,packet.length,millis(),message);
            if(result==FrameResult::Complete){process(message,alarm);std::fill(message.begin(),message.end(),'\0');break;}
            if(result==FrameResult::Invalid||result==FrameResult::Full)++invalidFrames;
        }
        frames.expire(millis());
        maxTickUs=std::max<uint32_t>(maxTickUs,uint32_t(micros()-began));
    }
    // Must follow FullApp::tick: controller, siren/grids and motor PWM have now
    // applied the queued command. Never acknowledge a disarm before PWM is zero.
    void afterTick(crumbguard::FullApp& alarm){
        if(!started)return;
        suitcase::GuardResult result;
        if(guardRequest>=0&&alarm.takeGuardResult(result)){
            const int index=guardRequest;guardRequest=-1;
            const char* error=result==suitcase::GuardResult::SensorUnavailable?"sensor_unavailable":
                result==suitcase::GuardResult::ConfigurationLocked?"configuration_unhealthy":nullptr;
            if(!error)status=std::string("Guard state applied: ")+suitcase::name(alarm.guardState());
            finish(index,!error,error,alarm);
        }
        const bool ready=transport.ready();
        std::string signature=std::string(suitcase::name(alarm.guardState()))+":"+suitcase::name(alarm.guardReason())+":"+
            alarm.motorPhase()+":"+std::to_string(alarm.codeProgress())+":"+std::to_string(ownerAuthenticated())+":"+std::to_string(window);
        if(ready&&(!wasReady||uint32_t(millis()-lastSnapshot)>=2000||signature!=lastSignature)){
            sendSnapshot(alarm);lastSignature=signature;
        }
        publishLive(alarm);
        wasReady=ready;
    }
    void report(){
        Serial.printf("{\"type\":\"event\",\"payload\":{\"event_type\":\"state\",\"key\":\"ble_status\",\"value\":{\"started\":%s,\"connected\":%s,\"authenticated\":%s,\"owner_claimed\":%s,\"enrollment_open\":%s,\"config_revision\":%lu,\"configuration_healthy\":%s,\"configuration_busy\":%s,\"max_tick_us\":%lu,\"invalid_frames\":%lu,\"rx_dropped\":%lu,\"tx_dropped\":%lu,\"free_heap\":%u}}}\n",
            started?"true":"false",transport.ready()?"true":"false",ownerAuthenticated()?"true":"false",storage.hasOwner?"true":"false",window?"true":"false",
            (unsigned long)storage.config.revision,storage.configHealthy?"true":"false",jobPending?"true":"false",(unsigned long)maxTickUs,
            (unsigned long)invalidFrames,(unsigned long)transport.rxDropped.load(),(unsigned long)transport.txDropped.load(),ESP.getFreeHeap());
    }
};
}
