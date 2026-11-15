#pragma once
#include <math.h>
#include <stdint.h>
#include "motion_model.h"
namespace crumbguard {
struct Features {float value[6]={};};
class MotionWindow {
    float samples[20][3]={};
    uint8_t next=0,count=0;
public:
    constexpr void clear(){next=count=0;}
    constexpr void push(float absoluteG,float stepG,float gyro){
        samples[next][0]=absoluteG;samples[next][1]=stepG;samples[next][2]=gyro;
        next=(next+1)%20;if(count<20)++count;
    }
    constexpr bool ready()const{return count==20;}
    constexpr Features features()const{
        Features f;
        if(!count)return f;
        for(uint8_t channel=0;channel<3;++channel){
            float sum=0,maximum=0;
            for(uint8_t row=0;row<count;++row){sum+=samples[row][channel];if(samples[row][channel]>maximum)maximum=samples[row][channel];}
            f.value[channel*2]=sum/count;f.value[channel*2+1]=maximum;
        }
        return f;
    }
    float probability()const{
        if(!MotionModel::trained || !ready())return -1;
        const auto f=features();float score=MotionModel::bias;
        for(uint8_t i=0;i<6;++i)score+=MotionModel::weights[i]*(f.value[i]-MotionModel::mean[i])/MotionModel::scale[i];
        if(score>20)score=20;if(score< -20)score=-20;
        return 1.0f/(1.0f+expf(-score));
    }
};
constexpr bool close(float a,float b){return a>b?a-b<0.0001f:b-a<0.0001f;}
constexpr bool featureWindowTest(){
    MotionWindow w;
    for(uint8_t i=0;i<19;++i)w.push(0.1f,0.2f,3.0f);
    if(w.ready())return false;
    w.push(0.1f,0.2f,3.0f);const auto f=w.features();
    return w.ready() && close(f.value[0],.1f) && close(f.value[1],.1f) && close(f.value[2],.2f) && close(f.value[3],.2f) && close(f.value[4],3) && close(f.value[5],3);
}
constexpr bool featureExpiryTest(){
    MotionWindow w;w.push(9,9,99);
    for(uint8_t i=0;i<20;++i)w.push(0,0,0);
    const auto f=w.features();w.clear();
    return f.value[1]==0 && f.value[5]==0 && !w.ready();
}
static_assert(featureWindowTest(),"Features match a full 20-frame window");
static_assert(featureExpiryTest(),"Old impulse leaves the rolling window; reset discards history");
}
