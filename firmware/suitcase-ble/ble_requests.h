#pragma once
#include <array>
#include <cstdint>
#include <cstring>
#include <string>
#include <algorithm>
#include "ble_request_admission.h"

namespace travelble {
struct RequestResult {
    bool used=false,pending=false,ok=false;
    uint32_t epoch=0;
    std::string id,operation,error,token;
    uint8_t fingerprint[32]={};
};
enum class RequestState { Fresh, Pending, Complete, Conflict, Full, Busy };
class RequestCache {
    std::array<RequestResult,totalRequestCapacity> entries{};
public:
    struct Lookup {RequestState state;int index;};
    auto begin(){return entries.begin();}auto end(){return entries.end();}
    RequestResult& operator[](size_t i){return entries[i];}
    size_t strictUsed()const{size_t count=0;for(size_t i=0;i<strictRequestCapacity;++i)count+=entries[i].used;return count;}
    void clear(){for(auto& entry:entries){std::fill(entry.token.begin(),entry.token.end(),'\0');entry=RequestResult{};}}
    Lookup reserve(const std::string& id,const std::string& operation,const uint8_t fingerprint[32],uint32_t epoch,
                   std::string_view desired={},bool owner=false){
        int free=-1;
        for(size_t i=0;i<entries.size();++i){
            auto& entry=entries[i];
            if(!entry.used){if(i<strictRequestCapacity&&free<0)free=int(i);continue;}
            if(entry.id!=id)continue;
            if(memcmp(entry.fingerprint,fingerprint,32))return {RequestState::Conflict,int(i)};
            return {entry.pending?RequestState::Pending:RequestState::Complete,int(i)};
        }
        const int overflow=overflowRequestIndex(operation,desired,owner);
        const auto admission=requestAdmission(free>=0,operation,desired,owner,overflow>=0&&entries[overflow].pending);
        if(admission==RequestAdmission::Full)return {RequestState::Full,-1};
        if(admission==RequestAdmission::Busy)return {RequestState::Busy,-1};
        const int index=admission==RequestAdmission::Strict?free:overflow;
        auto& entry=entries[index];std::fill(entry.token.begin(),entry.token.end(),'\0');entry=RequestResult{};
        entry.used=entry.pending=true;entry.id=id;entry.operation=operation;
        entry.epoch=epoch;memcpy(entry.fingerprint,fingerprint,32);return {RequestState::Fresh,index};
    }
};
}
