#pragma once
#include <cstdint>
#include <cstring>
#include <limits>

namespace travelble {
constexpr uint32_t configMagic=0x31474343, ownerMagic=0x314F4743;
struct ConfigRecord {
    uint32_t magic=configMagic,revision=0;
    char arm[5]={'A','A','B','B',0},disarm[5]={'B','A','B','A',0};
    uint8_t reserved[2]={};
};
struct OwnerRecord {
    uint32_t magic=ownerMagic;
    uint8_t hash[32]={},identity[7]={}; // ble_addr_t type followed by six address octets
    uint8_t reserved=0;
};
inline bool validCode(const char* s){
    if(!s)return false;
    for(unsigned i=0;i<4;++i)if(s[i]!='A'&&s[i]!='B')return false;
    return s[4]==0;
}
inline bool validConfig(const ConfigRecord& c){return c.magic==configMagic&&validCode(c.arm)&&validCode(c.disarm)&&strcmp(c.arm,c.disarm);}
inline bool nextConfig(const ConfigRecord& current,const char* a,const char* b,uint32_t expected,ConfigRecord& next){
    if(!validCode(a)||!validCode(b)||!strcmp(a,b)||expected!=current.revision||current.revision==UINT32_MAX)return false;
    next=current;memcpy(next.arm,a,5);memcpy(next.disarm,b,5);next.revision++;return true;
}
}
