#include <cassert>
#include <iostream>
#include <vector>
#include "../firmware/suitcase-ble/ble_frames.h"
#include "../firmware/suitcase-ble/ble_config.h"
#include "../firmware/suitcase-ble/ble_requests.h"
#include "../firmware/suitcase-ble/alarm_logic_tests.h"
#include "../firmware/suitcase-ble/dispenser_tests.h"
#include "../firmware/suitcase-ble/integration_tests.h"
#include "../firmware/suitcase-ble/guard_tests.h"

using namespace travelble;
static std::vector<std::vector<uint8_t>> split(const std::string& s,uint16_t mtu,uint16_t id){
    std::vector<std::vector<uint8_t>> result;uint8_t frame[maxFrame];
    for(uint16_t i=0;;++i){auto n=makeFrame(s,mtu,id,i,frame);if(!n)break;result.emplace_back(frame,frame+n);}
    return result;
}
int main(){
    unsigned checks=0;Reassembler rx;std::string out;
    for(uint16_t mtu:{23,185,517}){
        for(size_t n:{size_t(1),size_t(13),size_t(175),maxMessage}){
            std::string bytes(n,'x');auto parts=split(bytes,mtu,12);
            assert(!parts.empty());rx.clear();
            for(size_t k=0;k<parts.size();++k){
                const auto& p=parts[parts.size()-1-k]; // deliberately reversed
                auto r=rx.accept(p.data(),p.size(),uint32_t(k),out);
                assert(r==(k+1==parts.size()?FrameResult::Complete:FrameResult::Incomplete));
            }
            assert(out==bytes);++checks;
        }
    }
    const std::string unicode=u8"{\"name\":\"Laukku 🧳 ä\"}";
    auto parts=split(unicode,23,33);rx.clear();
    for(size_t i=0;i<parts.size();++i)rx.accept(parts[i].data(),parts[i].size(),0,out);
    assert(out==unicode);++checks;
    parts=split(std::string(40,'a'),23,1);rx.clear();
    assert(rx.accept(parts[0].data(),parts[0].size(),0,out)==FrameResult::Incomplete);
    assert(rx.accept(parts[0].data(),parts[0].size(),1,out)==FrameResult::Incomplete);
    auto bad=parts[0];bad.back()='b';assert(rx.accept(bad.data(),bad.size(),2,out)==FrameResult::Invalid);++checks;
    rx.clear();rx.accept(parts[0].data(),parts[0].size(),0,out);
    bad=parts[1];write16(bad.data()+6,99);assert(rx.accept(bad.data(),bad.size(),1,out)==FrameResult::Invalid);++checks;
    rx.clear();for(unsigned id=0;id<4;++id){auto p=split(std::string(24,'a'),23,id)[0];assert(rx.accept(p.data(),p.size(),0,out)==FrameResult::Incomplete);}
    auto fifth=split(std::string(24,'b'),23,5)[0];assert(rx.accept(fifth.data(),fifth.size(),1,out)==FrameResult::Full);
    assert(rx.accept(fifth.data(),fifth.size(),10001,out)==FrameResult::Incomplete);++checks;
    rx.clear();parts=split(std::string(24,'a'),23,1);
    rx.accept(parts[0].data(),parts[0].size(),UINT32_MAX-100,out);
    assert(rx.accept(parts[1].data(),parts[1].size(),100,out)==FrameResult::Complete);++checks;
    rx.clear();uint8_t large[512]={0x54,1};write16(large+2,8);write16(large+6,20);
    for(unsigned i=0;i<16;++i){write16(large+4,i);assert(rx.accept(large,sizeof(large),i,out)==FrameResult::Incomplete);}
    write16(large+4,16);assert(rx.accept(large,sizeof(large),17,out)==FrameResult::Invalid);++checks;
    rx.clear();auto invalid=parts[0];invalid[0]=0;assert(rx.accept(invalid.data(),invalid.size(),0,out)==FrameResult::Invalid);
    invalid=parts[0];write16(invalid.data()+6,1025);assert(rx.accept(invalid.data(),invalid.size(),0,out)==FrameResult::Invalid);++checks;
    ConfigRecord current,next;
    assert(validConfig(current));assert(nextConfig(current,"ABAB","AABB",0,next)&&next.revision==1);++checks;
    assert(!nextConfig(current,"AAAA","AAAA",0,next));assert(!nextConfig(current,"ABAC","AABB",0,next));
    assert(!nextConfig(current,"ABA","AABB",0,next));assert(!nextConfig(current,"ABAB","AABB",1,next));++checks;
    RequestCache ledger;uint8_t hash[32]={1},otherHash[32]={2};
    auto reservation=ledger.reserve("request-1","setButtonSequences",hash,1);
    assert(reservation.state==RequestState::Fresh);
    assert(ledger.reserve("request-1","setButtonSequences",hash,1).state==RequestState::Pending);
    assert(ledger.reserve("request-1","setButtonSequences",otherHash,1).state==RequestState::Conflict);
    ledger[reservation.index].pending=false;ledger[reservation.index].ok=true;
    assert(ledger.reserve("request-1","setButtonSequences",hash,2).state==RequestState::Complete);++checks;
    for(unsigned i=1;i<64;++i)assert(ledger.reserve("id-"+std::to_string(i),"getSnapshot",hash,1).state==RequestState::Fresh);
    assert(ledger.reserve("overflow","setButtonSequences",hash,1).state==RequestState::Full);
    assert(ledger.reserve("request-1","setButtonSequences",hash,2).state==RequestState::Complete);
    assert(ledger.reserve("full-arm","setGuardState",hash,1,"armed",true).state==RequestState::Full);
    auto stop=ledger.reserve("stop-one","setGuardState",hash,1,"disarmed",true);
    assert(stop.state==RequestState::Fresh&&stop.index==overflowDisarmIndex&&ledger.strictUsed()==64);++checks;
    assert(ledger.reserve("stop-one","setGuardState",hash,1,"disarmed",true).state==RequestState::Pending);
    assert(ledger.reserve("stop-two","setGuardState",hash,1,"disarmed",true).state==RequestState::Busy);
    assert(ledger.reserve("stop-one","setGuardState",otherHash,1,"disarmed",true).state==RequestState::Conflict);
    ledger[stop.index].pending=false;ledger[stop.index].ok=true;
    assert(ledger.reserve("stop-one","setGuardState",hash,1,"disarmed",true).state==RequestState::Complete);++checks;
    auto auth=ledger.reserve("overflow-auth","authenticate",hash,2);
    auto snapshot=ledger.reserve("overflow-snapshot","getSnapshot",hash,2);
    assert(auth.state==RequestState::Fresh&&auth.index==overflowAuthIndex);
    assert(snapshot.state==RequestState::Fresh&&snapshot.index==overflowSnapshotIndex);
    assert(ledger.reserve("stop-one","setGuardState",hash,2,"disarmed",true).state==RequestState::Complete);++checks;
    auto newerStop=ledger.reserve("stop-two","setGuardState",hash,2,"disarmed",true);
    assert(newerStop.state==RequestState::Fresh&&newerStop.index==stop.index);
    assert(ledger.reserve("request-1","setButtonSequences",hash,2).state==RequestState::Complete);
    assert(ledger.reserve("request-1","setButtonSequences",otherHash,2).state==RequestState::Conflict);
    assert(ledger.reserve("another-config","setButtonSequences",hash,2,"disarmed",true).state==RequestState::Full);++checks;
    ledger[newerStop.index].pending=false;
    assert(ledger.reserve("stop-one","setGuardState",hash,2,"disarmed",true).state==RequestState::Fresh); // retired overflow ID may reassert Disarm
    assert(ledger.reserve("unauthorized-stop","setGuardState",hash,2,"disarmed",false).state==RequestState::Full);++checks;
    ledger.clear();assert(ledger.reserve("after-reset","getSnapshot",hash,3).state==RequestState::Fresh);++checks;
    suitcase::Controller controller;
    assert(controller.setCodes("ABAB","AABB"));
    controller.tap(suitcase::Key::A,0,true);controller.tap(suitcase::Key::B,100,true);
    controller.tap(suitcase::Key::A,200,true);assert(controller.tap(suitcase::Key::B,300,true)==suitcase::CodeResult::Arm);
    assert(!controller.setCodes("AAAA","BBBB"));
    controller.tap(suitcase::Key::A,400,true);controller.tap(suitcase::Key::A,500,true);
    controller.tap(suitcase::Key::B,600,true);assert(controller.tap(suitcase::Key::B,700,true)==suitcase::CodeResult::Disarm);++checks;
    suitcase::Controller other;assert(!other.setCodes("AAAA","AAAA"));assert(other.armCode[1]==1);++checks;
    std::cout<<checks<<" runtime checks passed; existing alarm/motor compile-time regressions passed\n";
}
