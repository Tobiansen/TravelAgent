#pragma once
#include <string_view>
namespace travelble {
constexpr const char* identityError(std::string_view operation,std::string_view target,std::string_view boot,
                                   std::string_view deviceId,std::string_view bootId){
    const bool discovery=operation=="getSnapshot"&&target.empty()&&boot.empty();
    if(!discovery&&target!=deviceId)return "wrong_device";
    if(!discovery&&boot!=bootId)return "wrong_boot";
    return nullptr;
}
constexpr const char* guardRequestError(bool owner,bool stringField,std::string_view desired,
                                      bool configurationBusy,bool configurationHealthy){
    if(!owner)return "owner_authentication_required";
    if(!stringField||(desired!="armed"&&desired!="disarmed"))return "invalid_desired_state";
    if(desired=="armed"&&configurationBusy)return "configuration_busy";
    if(desired=="armed"&&!configurationHealthy)return "configuration_unhealthy";
    return nullptr; // Disarm is independent of sensor/configuration health.
}
}
