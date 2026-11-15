#pragma once
// No model has been trained on this suitcase yet. The collector/trainer can replace
// this header after evaluation on completely separate physical movement trials.
namespace crumbguard::MotionModel {
inline constexpr bool trained=false;
inline constexpr float mean[6]={0,0,0,0,0,0};
inline constexpr float scale[6]={1,1,1,1,1,1};
inline constexpr float weights[6]={0,0,0,0,0,0};
inline constexpr float bias=0;
inline constexpr float threshold=0.8f;
}
