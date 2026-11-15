#pragma once
#include <Arduino.h>
#include <nvs.h>
#include "ble_config.h"

namespace travelble {
// Configuration and owner each occupy a single atomic NVS blob. nvs_commit is
// mandatory; readback must match before the application publishes success.
class Storage {
    static bool writeRead(const char* key,const void* bytes,size_t size){
        nvs_handle_t handle;
        if(nvs_open("crumbguard",NVS_READWRITE,&handle)!=ESP_OK)return false;
        bool ok=nvs_set_blob(handle,key,bytes,size)==ESP_OK&&nvs_commit(handle)==ESP_OK;
        uint8_t actual[sizeof(OwnerRecord)];size_t count=size;
        ok=ok&&nvs_get_blob(handle,key,actual,&count)==ESP_OK&&count==size&&!memcmp(actual,bytes,size);
        nvs_close(handle);memset(actual,0,sizeof(actual));return ok;
    }
public:
    bool configHealthy=true,ownerHealthy=true,hasOwner=false;
    ConfigRecord config{};OwnerRecord owner{};
    bool load(){
        nvs_handle_t handle;
        if(nvs_open("crumbguard",NVS_READWRITE,&handle)!=ESP_OK){configHealthy=ownerHealthy=false;hasOwner=true;return false;}
        size_t length=sizeof(config);
        auto result=nvs_get_blob(handle,"config",&config,&length);
        if(result!=ESP_ERR_NVS_NOT_FOUND&&(result!=ESP_OK||length!=sizeof(config)||!validConfig(config))){config=ConfigRecord{};configHealthy=false;}
        length=sizeof(owner);result=nvs_get_blob(handle,"owner",&owner,&length);
        hasOwner=result!=ESP_ERR_NVS_NOT_FOUND;
        if(hasOwner&&(result!=ESP_OK||length!=sizeof(owner)||owner.magic!=ownerMagic))ownerHealthy=false;
        nvs_close(handle);return configHealthy&&ownerHealthy;
    }
    static bool saveConfig(const ConfigRecord& next,const ConfigRecord& previous){
        if(writeRead("config",&next,sizeof(next)))return true;
        // A failed verification must not deliberately leave a new confirmed record.
        writeRead("config",&previous,sizeof(previous));return false;
    }
    static bool saveOwner(const OwnerRecord& next){return writeRead("owner",&next,sizeof(next));}
    static bool resetOwner(){
        nvs_handle_t handle;if(nvs_open("crumbguard",NVS_READWRITE,&handle)!=ESP_OK)return false;
        const auto result=nvs_erase_key(handle,"owner");
        bool ok=(result==ESP_OK||result==ESP_ERR_NVS_NOT_FOUND)&&nvs_commit(handle)==ESP_OK;
        size_t size=0;ok=ok&&nvs_get_blob(handle,"owner",nullptr,&size)==ESP_ERR_NVS_NOT_FOUND;
        nvs_close(handle);return ok;
    }
};
}
