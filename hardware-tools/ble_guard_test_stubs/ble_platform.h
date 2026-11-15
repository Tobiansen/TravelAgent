#pragma once
// Only hardware boundaries are replaced. Tests execute real BLE JSON dispatch,
// framing/cache, Storage, alarm application/controller and dispenser application.
// The digest is deterministic test data, NOT a cryptographic implementation.
#include <deque>
#include <map>
#include <memory>
#include "../../firmware/suitcase-ble/ble_frames.h"
#include "../../firmware/suitcase-ble/ble_storage.h"

struct TestQueue {size_t capacity,size;std::deque<std::vector<uint8_t>> items;};
using QueueHandle_t=TestQueue*;
inline std::vector<std::unique_ptr<TestQueue>> testQueues;
constexpr int pdTRUE=1,pdPASS=1,portMAX_DELAY=-1;
inline QueueHandle_t xQueueCreate(size_t capacity,size_t size){
    testQueues.emplace_back(new TestQueue{capacity,size,{}});return testQueues.back().get();
}
inline int xQueueSend(QueueHandle_t q,const void* p,int){
    if(q->items.size()==q->capacity)return 0;
    const auto* bytes=static_cast<const uint8_t*>(p);q->items.emplace_back(bytes,bytes+q->size);return pdTRUE;
}
inline int xQueueReceive(QueueHandle_t q,void* out,int){
    if(q->items.empty())return 0;
    memcpy(out,q->items.front().data(),q->size);q->items.pop_front();return pdTRUE;
}
inline int xTaskCreate(void(*)(void*),const char*,int,void*,int,void*){return pdPASS;}
struct TestESP {uint64_t getEfuseMac(){return 0x441bf67640f4;}unsigned getFreeHeap(){return 140000;}};
inline TestESP ESP;
inline unsigned testRandomCounter=1,testBondDeletes=0;
inline void esp_fill_random(void* data,size_t size){memset(data,testRandomCounter++,size);}
struct NimBLEDevice {static void deleteAllBonds(){++testBondDeletes;}};
inline int mbedtls_sha256(const uint8_t* bytes,size_t size,uint8_t out[32],int){
    uint64_t hash=14695981039346656037ULL;
    for(size_t i=0;i<size;++i){hash^=bytes[i];hash*=1099511628211ULL;}
    for(unsigned i=0;i<32;++i){hash^=hash>>29;hash*=0x9e3779b97f4a7c15ULL;out[i]=uint8_t(hash>>32);}
    return 0;
}
inline int mbedtls_base64_decode(uint8_t* out,size_t capacity,size_t* count,const uint8_t* in,size_t size){
    // Known all-zero 32-byte owner credential fixture. Invalid tokens fail.
    if(capacity<32||std::string(reinterpret_cast<const char*>(in),size)!=std::string(43,'A')+'=')return -1;
    memset(out,0,32);*count=32;return 0;
}
inline int mbedtls_base64_encode(uint8_t*,size_t,size_t*,const uint8_t*,size_t){return -1;}
inline std::map<std::string,std::vector<uint8_t>> testNvs;
inline unsigned testNvsWrites=0,testNvsCommits=0;
inline esp_err_t nvs_open(const char*,int,nvs_handle_t* h){*h=1;return ESP_OK;}
inline esp_err_t nvs_get_blob(nvs_handle_t,const char* key,void* out,size_t* size){
    auto it=testNvs.find(key);if(it==testNvs.end())return ESP_ERR_NVS_NOT_FOUND;
    if(!out){*size=it->second.size();return ESP_OK;}
    if(*size<it->second.size())return ESP_FAIL;
    *size=it->second.size();memcpy(out,it->second.data(),*size);return ESP_OK;
}
inline esp_err_t nvs_set_blob(nvs_handle_t,const char* key,const void* data,size_t size){
    ++testNvsWrites;auto* p=static_cast<const uint8_t*>(data);testNvs[key]={p,p+size};return ESP_OK;
}
inline esp_err_t nvs_commit(nvs_handle_t){++testNvsCommits;return ESP_OK;}
inline esp_err_t nvs_erase_key(nvs_handle_t,const char* key){++testNvsWrites;return testNvs.erase(key)?ESP_OK:ESP_ERR_NVS_NOT_FOUND;}
inline void nvs_close(nvs_handle_t){}

namespace travelble {
class Transport {
public:
    struct Packet {uint32_t epoch;uint16_t connection,length;uint8_t bytes[maxFrame];};
    inline static Transport* active=nullptr;
    bool connected=true,ownerBond=true;
    uint32_t generation=1;
    std::deque<Packet> incoming;
    std::vector<std::string> sent;
    std::atomic<uint32_t> rxDropped{0},txDropped{0},motionDropped{0},eventDropped{0};
    bool liveAllowed=false;
    bool begin(const char*){active=this;return true;}
    uint32_t epoch()const{return generation;}
    uint16_t handle()const{return connected?1:0xffff;}
    bool ready()const{return connected;}
    bool identity(uint8_t out[7])const{memset(out,0,7);out[1]=ownerBond?0xab:0xcd;return ready();}
    void allowPairing(bool){}
    void allowLive(bool value,uint32_t epoch){liveAllowed=value&&epoch==generation;}
    bool receive(Packet& out){if(incoming.empty())return false;out=incoming.front();incoming.pop_front();return true;}
    bool send(std::string bytes){if(!ready())return false;sent.push_back(std::move(bytes));return true;}
    bool sendMotion(std::string bytes){if(!liveAllowed)return false;return send(std::move(bytes));}
    bool sendEvent(std::string bytes){if(!liveAllowed)return false;return send(std::move(bytes));}
    void disconnect(){connected=false;++generation;}
    void inject(const std::string& json,uint16_t id){
        for(uint16_t i=0;;++i){Packet packet{generation,handle(),0,{}};
            packet.length=uint16_t(makeFrame(json,185,id,i,packet.bytes));
            if(!packet.length)break;incoming.push_back(packet);
        }
    }
};
}
