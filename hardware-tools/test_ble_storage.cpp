#include <cassert>
#include <iostream>
#include <map>
#include <vector>
#include "../firmware/suitcase-ble/ble_storage.h"
// Fault-inject the NVS boundary, exercising the actual firmware Storage class.
std::map<std::string,std::vector<uint8_t>> committed,staged;
bool failCommit=false,failRead=false,failOpen=false;
esp_err_t nvs_open(const char*,int,nvs_handle_t* h){if(failOpen)return ESP_FAIL;*h=1;staged=committed;return ESP_OK;}
esp_err_t nvs_set_blob(nvs_handle_t,const char* k,const void* p,size_t n){auto* b=static_cast<const uint8_t*>(p);staged[k]={b,b+n};return ESP_OK;}
esp_err_t nvs_get_blob(nvs_handle_t,const char* k,void* p,size_t* n){
    if(failRead){failRead=false;return ESP_FAIL;}
    auto it=committed.find(k);if(it==committed.end())return ESP_ERR_NVS_NOT_FOUND;
    if(!p){*n=it->second.size();return ESP_OK;}
    if(*n<it->second.size())return ESP_FAIL;*n=it->second.size();memcpy(p,it->second.data(),*n);return ESP_OK;
}
esp_err_t nvs_commit(nvs_handle_t){if(failCommit){failCommit=false;return ESP_FAIL;}committed=staged;return ESP_OK;}
esp_err_t nvs_erase_key(nvs_handle_t,const char* k){return staged.erase(k)?ESP_OK:ESP_ERR_NVS_NOT_FOUND;}
void nvs_close(nvs_handle_t){}
int main(){
    using namespace travelble;
    Storage initial;assert(initial.load()&&!initial.hasOwner&&initial.config.revision==0);
    ConfigRecord next;assert(nextConfig(initial.config,"ABAB","AABB",0,next));
    assert(Storage::saveConfig(next,initial.config));Storage reboot;assert(reboot.load());
    assert(reboot.config.revision==1&&!strcmp(reboot.config.arm,"ABAB"));
    ConfigRecord second;assert(nextConfig(reboot.config,"ABBA","BAAB",1,second));
    failCommit=true;assert(!Storage::saveConfig(second,reboot.config));
    Storage afterCommitFailure;assert(afterCommitFailure.load()&&afterCommitFailure.config.revision==1);
    failRead=true;assert(!Storage::saveConfig(second,reboot.config));
    Storage afterReadFailure;assert(afterReadFailure.load()&&afterReadFailure.config.revision==1);
    OwnerRecord owner;owner.hash[0]=0x55;owner.identity[1]=0xab;
    assert(Storage::saveOwner(owner));Storage owned;assert(owned.load()&&owned.hasOwner&&owned.owner.hash[0]==0x55);
    assert(Storage::resetOwner());Storage reset;assert(reset.load()&&!reset.hasOwner&&reset.config.revision==1);
    committed["owner"]={1,2};Storage brokenOwner;assert(!brokenOwner.load()&&brokenOwner.hasOwner&&!brokenOwner.ownerHealthy);
    committed.erase("owner");committed["config"]={1,2};Storage brokenConfig;assert(!brokenConfig.load()&&!brokenConfig.configHealthy);
    failOpen=true;Storage unavailable;assert(!unavailable.load()&&unavailable.hasOwner);
    std::cout<<"9 persistence scenarios passed (NVS fault-injection boundary; not a physical flash test)\n";
}
