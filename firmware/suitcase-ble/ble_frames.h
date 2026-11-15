#pragma once
#include <array>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <string>

namespace travelble {
constexpr size_t maxMessage=8192, maxFragments=1024, headerSize=8, maxFrame=512;
constexpr uint32_t frameTtlMs=10000;
inline uint16_t read16(const uint8_t* p){return uint16_t(p[0])|(uint16_t(p[1])<<8);}
inline void write16(uint8_t* p,uint16_t n){p[0]=uint8_t(n);p[1]=uint8_t(n>>8);}
inline size_t payloadSize(uint16_t mtu){return mtu<23?0:std::min<size_t>(mtu-3,512)-headerSize;}
enum class FrameResult { Incomplete, Complete, Invalid, Full };

// Fixed memory even for four malicious 1024-fragment assemblies. Payloads are
// appended into bounded arenas; offsets reconstruct out-of-order fragments.
class Reassembler {
    struct Slot {
        bool active=false;
        uint16_t id=0,count=0,received=0,total=0;
        uint32_t started=0;
        std::array<uint16_t,maxFragments> offsets{}, lengths{};
        std::array<uint8_t,maxMessage> bytes{};
    };
    std::array<Slot,4> slots{};
public:
    void clear(){for(auto& s:slots)s.active=false;}
    void expire(uint32_t now){for(auto& s:slots)if(s.active&&uint32_t(now-s.started)>frameTtlMs)s.active=false;}
    FrameResult accept(const uint8_t* frame,size_t length,uint32_t now,std::string& output){
        expire(now);output.clear();
        if(length<=headerSize||length>maxFrame||frame[0]!=0x54||frame[1]!=1)return FrameResult::Invalid;
        const auto id=read16(frame+2), index=read16(frame+4),count=read16(frame+6);
        if(!count||count>maxFragments||index>=count)return FrameResult::Invalid;
        Slot* slot=nullptr;
        for(auto& s:slots)if(s.active&&s.id==id){slot=&s;break;}
        if(!slot){
            for(auto& s:slots)if(!s.active){slot=&s;break;}
            if(!slot)return FrameResult::Full;
            slot->active=true;slot->id=id;slot->count=count;slot->started=now;
            slot->received=slot->total=0;slot->lengths.fill(0);
        }
        auto& s=*slot;
        if(s.count!=count){s.active=false;return FrameResult::Invalid;}
        const size_t size=length-headerSize;
        if(s.lengths[index]){
            if(s.lengths[index]!=size||memcmp(s.bytes.data()+s.offsets[index],frame+headerSize,size)){
                s.active=false;return FrameResult::Invalid;
            }
        }else{
            if(size>maxMessage-s.total){s.active=false;return FrameResult::Invalid;}
            s.offsets[index]=s.total;s.lengths[index]=uint16_t(size);
            memcpy(s.bytes.data()+s.total,frame+headerSize,size);
            s.total+=uint16_t(size);++s.received;
        }
        if(s.received!=s.count)return FrameResult::Incomplete;
        output.reserve(s.total);
        for(uint16_t i=0;i<s.count;++i)output.append(reinterpret_cast<const char*>(s.bytes.data()+s.offsets[i]),s.lengths[i]);
        s.active=false;return FrameResult::Complete;
    }
};
inline size_t makeFrame(const std::string& message,uint16_t mtu,uint16_t id,uint16_t index,uint8_t* frame){
    const size_t payload=payloadSize(mtu);
    if(!payload||message.empty()||message.size()>maxMessage)return 0;
    const size_t count=(message.size()+payload-1)/payload;
    if(index>=count||count>maxFragments)return 0;
    const size_t offset=index*payload, size=std::min(payload,message.size()-offset);
    frame[0]=0x54;frame[1]=1;write16(frame+2,id);write16(frame+4,index);write16(frame+6,uint16_t(count));
    memcpy(frame+headerSize,message.data()+offset,size);return size+headerSize;
}
}
