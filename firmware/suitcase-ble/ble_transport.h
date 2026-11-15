#pragma once
#include <Arduino.h>
#include <NimBLEDevice.h>
#include <atomic>
#include <new>
#include <string>
#include "ble_frames.h"

namespace travelble {
inline constexpr char serviceUuid[]="70a7e000-9a55-4b4e-8b7c-27c478e9d751";
inline constexpr char commandUuid[]="70a7e001-9a55-4b4e-8b7c-27c478e9d751";
inline constexpr char responseUuid[]="70a7e002-9a55-4b4e-8b7c-27c478e9d751";

class Transport : public NimBLEServerCallbacks,public NimBLECharacteristicCallbacks {
public:
    struct Packet {uint32_t epoch;uint16_t connection,length;uint8_t bytes[maxFrame];};
private:
    enum class OutKind { Control, Event, Motion };
    struct Out {
        uint32_t epoch;uint16_t connection;std::string bytes;
        OutKind kind=OutKind::Control;
        bool initialized=false;uint16_t id=0,index=0,mtu=23;size_t offset=0;uint32_t began=0;
    };
    QueueHandle_t input=nullptr,output=nullptr,liveEvents=nullptr,latestMotion=nullptr;
    NimBLEServer* server=nullptr;
    NimBLECharacteristic* response=nullptr;
    std::atomic<uint16_t> connection{BLE_HS_CONN_HANDLE_NONE};
    std::atomic<uint32_t> generation{0},liveGeneration{0};
    std::atomic<bool> subscribed{false},pairing{false},liveAllowed{false};
    uint16_t nextId=0;
    static void txEntry(void* self){static_cast<Transport*>(self)->transmit();}
    // A low-priority document advances only one fragment before rechecking the
    // control queue. At most one partial event and one partial motion message
    // coexist with a control message, within the receiver's four-assembly bound.
    bool fragment(Out* out){
        if(out->epoch!=epoch()||out->connection!=handle()||!ready()||
            (out->kind!=OutKind::Control&&(!liveAllowed||out->epoch!=liveGeneration.load())))return true;
        if(!out->initialized){
            out->initialized=true;out->id=++nextId;out->began=millis();
            out->mtu=server->getPeerInfoByHandle(out->connection).getMTU();
        }
        if(uint32_t(millis()-out->began)>8000||!payloadSize(out->mtu))return true;
        uint8_t frame[maxFrame];
        const auto length=makeFrame(out->bytes,out->mtu,out->id,out->index,frame);
        if(!length)return true;
        if(response->notify(frame,length,out->connection)){out->offset+=length-headerSize;++out->index;}
        return out->offset==out->bytes.size();
    }
    void dispose(Out*& out){
        if(!out)return;
        if(out->offset!=out->bytes.size()){
            ++txDropped;
            if(out->kind==OutKind::Motion)++motionDropped;
            if(out->kind==OutKind::Event)++eventDropped;
        }
        std::fill(out->bytes.begin(),out->bytes.end(),'\0');delete out;out=nullptr;
    }
    void transmit(){
        Out* event=nullptr;Out* motion=nullptr;
        for(;;){
            Out* control=nullptr;
            if(xQueueReceive(output,&control,0)==pdTRUE){
                while(!fragment(control))vTaskDelay(pdMS_TO_TICKS(1));
                dispose(control);
            }else{
                if(!event)xQueueReceive(liveEvents,&event,0);
                if(event){if(fragment(event))dispose(event);}
                else{
                    if(!motion)xQueueReceive(latestMotion,&motion,0);
                    if(motion&&fragment(motion))dispose(motion);
                }
            }
            vTaskDelay(pdMS_TO_TICKS(1)); // Worker only; hardware loop never waits.
        }
    }
    bool sendLive(std::string bytes,OutKind kind){
        auto& dropped=kind==OutKind::Motion?motionDropped:eventDropped;
        const uint32_t authenticatedEpoch=liveGeneration.load();
        if(!ready()||!liveAllowed||authenticatedEpoch!=epoch()||bytes.empty()||bytes.size()>(kind==OutKind::Motion?512:1024)){++dropped;return false;}
        // Bind data to the authenticated generation, not a possibly new peer
        // observed between an authorization check and queue admission.
        auto* out=new(std::nothrow) Out{authenticatedEpoch,handle(),std::move(bytes),kind};
        if(!out){++dropped;return false;}
        auto queue=kind==OutKind::Motion?latestMotion:liveEvents;
        if(xQueueSend(queue,&out,0)==pdTRUE)return true;
        if(kind==OutKind::Motion){
            Out* superseded=nullptr;
            if(xQueueReceive(queue,&superseded,0)==pdTRUE)dispose(superseded);
            if(xQueueSend(queue,&out,0)==pdTRUE)return true;
        }
        dispose(out);return false;
    }
public:
    std::atomic<uint32_t> rxDropped{0},txDropped{0},motionDropped{0},eventDropped{0};
    uint16_t handle() const{return connection.load();}
    uint32_t epoch() const{return generation.load();}
    void allowPairing(bool value){pairing=value;}
    void allowLive(bool value,uint32_t authenticatedEpoch){
        liveGeneration=authenticatedEpoch;
        liveAllowed=value&&authenticatedEpoch==epoch();
    }
    bool ready() const{
        const auto h=handle();
        if(!server||h==BLE_HS_CONN_HANDLE_NONE||!subscribed)return false;
        const auto peer=server->getPeerInfoByHandle(h);
        return peer.isEncrypted()&&peer.isBonded()&&peer.getConnHandle()==h;
    }
    bool identity(uint8_t out[7]) const{
        if(!ready())return false;
        const auto address=server->getPeerInfoByHandle(handle()).getIdAddress();
        const auto* base=address.getBase();out[0]=base->type&1;memcpy(out+1,base->val,6);return true;
    }
    bool begin(const char* name){
        input=xQueueCreate(16,sizeof(Packet));output=xQueueCreate(4,sizeof(Out*));
        liveEvents=xQueueCreate(4,sizeof(Out*));latestMotion=xQueueCreate(1,sizeof(Out*));
        if(!input||!output||!liveEvents||!latestMotion)return false;
        if(!NimBLEDevice::init(name))return false;
        NimBLEDevice::setSecurityAuth(true,false,true); // Bonded LE Secure Connections, Just Works.
        NimBLEDevice::setSecurityIOCap(BLE_HS_IO_NO_INPUT_OUTPUT);
        NimBLEDevice::setMTU(185);
        server=NimBLEDevice::createServer();server->setCallbacks(this,false);server->advertiseOnDisconnect(true);
        auto* service=server->createService(serviceUuid);
        auto* command=service->createCharacteristic(commandUuid,NIMBLE_PROPERTY::WRITE|NIMBLE_PROPERTY::WRITE_ENC,512);
        command->setCallbacks(this);
        response=service->createCharacteristic(responseUuid,NIMBLE_PROPERTY::NOTIFY|NIMBLE_PROPERTY::READ_ENC,512);
        response->setCallbacks(this); // NimBLE creates the standard CCCD automatically.
        service->start();
        auto* advertising=NimBLEDevice::getAdvertising();
        // Put a useful short name in the primary 31-byte advertisement too:
        // some service-filtered scanners never surface the scan-response name.
        char shortName[8];snprintf(shortName,sizeof(shortName),"CG-%s",name+strlen(name)-4);
        advertising->enableScanResponse(false);
        if(!advertising->addServiceUUID(serviceUuid)||!advertising->setName(shortName))return false;
        advertising->enableScanResponse(true);
        if(!advertising->setName(name))return false;
        if(xTaskCreate(txEntry,"ble-tx",4096,this,1,nullptr)!=pdPASS)return false;
        return NimBLEDevice::startAdvertising();
    }
    bool receive(Packet& packet){return input&&xQueueReceive(input,&packet,0)==pdTRUE;}
    bool send(std::string bytes){
        if(!ready()||bytes.empty()||bytes.size()>maxMessage)return false;
        auto* out=new(std::nothrow) Out{epoch(),handle(),std::move(bytes)};
        if(!out)return false;
        if(xQueueSend(output,&out,0)!=pdTRUE){std::fill(out->bytes.begin(),out->bytes.end(),'\0');delete out;++txDropped;return false;}
        return true;
    }
    bool sendMotion(std::string bytes){return sendLive(std::move(bytes),OutKind::Motion);}
    bool sendEvent(std::string bytes){return sendLive(std::move(bytes),OutKind::Event);}
    void disconnect(){if(server&&handle()!=BLE_HS_CONN_HANDLE_NONE)server->disconnect(handle());}
    void onConnect(NimBLEServer* s,NimBLEConnInfo& info) override{
        if(handle()!=BLE_HS_CONN_HANDLE_NONE||(!pairing&&!NimBLEDevice::isBonded(info.getIdAddress()))){
            s->disconnect(info.getConnHandle());return;
        }
        liveAllowed=false;connection=info.getConnHandle();++generation;subscribed=false;
        s->updateConnParams(info.getConnHandle(),12,24,0,400);
        NimBLEDevice::startSecurity(info.getConnHandle());
    }
    void onDisconnect(NimBLEServer*,NimBLEConnInfo& info,int) override{
        if(info.getConnHandle()==handle()){liveAllowed=false;connection=BLE_HS_CONN_HANDLE_NONE;subscribed=false;++generation;}
    }
    void onAuthenticationComplete(NimBLEConnInfo& info) override{
        if(!info.isEncrypted()||!info.isBonded())server->disconnect(info.getConnHandle());
    }
    void onSubscribe(NimBLECharacteristic*,NimBLEConnInfo& info,uint16_t value) override{
        if(info.getConnHandle()==handle())subscribed=(value&1)!=0;
    }
    void onWrite(NimBLECharacteristic* characteristic,NimBLEConnInfo& info) override{
        if(info.getConnHandle()!=handle()||!info.isEncrypted()||!info.isBonded())return;
        auto value=characteristic->getValue();
        if(value.size()<=headerSize||value.size()>std::min<size_t>(info.getMTU()-3,maxFrame)){++rxDropped;return;}
        Packet packet{};packet.epoch=epoch();packet.connection=handle();packet.length=value.size();
        memcpy(packet.bytes,value.data(),value.size());
        if(xQueueSend(input,&packet,0)!=pdTRUE){++rxDropped;server->disconnect(info.getConnHandle());}
    }
};
}
