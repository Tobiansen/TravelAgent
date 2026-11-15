#pragma once
#include <stdint.h>

namespace travelble {
// This detector only creates a descriptive connected-session marker. It never
// changes guard state, calibration, siren, motor, stored configuration or owner.
class LiveMotionDetector {
    bool tracking=false,latched=false,quietTracking=false,hasEvent=false,hasSample=false;
    uint32_t movingAt=0,quietAt=0,eventAt=0,lastSampleAt=0;
public:
    constexpr void reset(){*this=LiveMotionDetector{};}
    constexpr bool update(uint32_t at,bool aboveThreshold,bool healthy){
        // Missing/invalid IMU data cannot establish sustained movement or quiet.
        if(!healthy){tracking=false;quietTracking=false;hasSample=false;return false;}
        if(hasSample&&uint32_t(at-lastSampleAt)>300){tracking=false;quietTracking=false;}
        lastSampleAt=at;hasSample=true;
        if(!aboveThreshold){
            tracking=false;
            if(!quietTracking){quietTracking=true;quietAt=at;}
            if(uint32_t(at-quietAt)>=500)latched=false;
            return false;
        }
        quietTracking=false;
        if(latched)return false;
        if(!tracking){tracking=true;movingAt=at;}
        if(uint32_t(at-movingAt)<150||(hasEvent&&uint32_t(at-eventAt)<2000))return false;
        tracking=false;latched=true;hasEvent=true;eventAt=at;return true;
    }
};
}
