#pragma once
#include <cstddef>
#include <string_view>
namespace travelble {
inline constexpr size_t strictRequestCapacity=64;
inline constexpr int overflowDisarmIndex=64,overflowAuthIndex=65,overflowSnapshotIndex=66;
inline constexpr size_t totalRequestCapacity=67;
enum class RequestAdmission { Strict, Overflow, Full, Busy };
constexpr int overflowRequestIndex(std::string_view operation,std::string_view desired,bool owner){
    if(operation=="setGuardState"&&desired=="disarmed"&&owner)return overflowDisarmIndex;
    if(operation=="authenticate")return overflowAuthIndex;
    if(operation=="getSnapshot")return overflowSnapshotIndex;
    return -1;
}
// Called only for previously unseen IDs, after checking every retained ID for
// duplicates/conflicts. Completed overflow records are replaceable; pending ones
// are never replaced. The 64 strict records are never evicted within the boot.
constexpr RequestAdmission requestAdmission(bool strictFree,std::string_view operation,
                                            std::string_view desired,bool owner,bool lanePending){
    if(strictFree)return RequestAdmission::Strict;
    if(overflowRequestIndex(operation,desired,owner)<0)return RequestAdmission::Full;
    return lanePending?RequestAdmission::Busy:RequestAdmission::Overflow;
}
}
