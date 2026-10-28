#pragma once
#include <windows.h>
#include <cstdint>

// Private ABI shared by the host and its Explorer diagnostics extension.
// Both endpoints are x64; no pointers or STL objects cross the boundary.
namespace claribar {
inline constexpr uint32_t protocolMagic=0x43425832, protocolVersion=3;
inline constexpr CLSID tapClsid{0x7499a0ec,0x102a,0x43fb,{0x85,0x21,0xe9,0xd2,0x7a,0xd3,0x41,0x80}};
inline constexpr unsigned maxMetrics=8, dumpCapacity=60000;
enum ColorFlags : uint32_t { TimeColor=1,DateColor=2,TrayColor=4,NetworkColor=8,VolumeColor=16,InputColor=32,BatteryColor=64 };
struct Metric { uint32_t color=0xffffffff; wchar_t text[192]{}; };
struct ExplorerConfig {
    uint32_t enabled=1, backgroundMode=0, backgroundArgb=0x00202020;
    uint32_t embedded=0, layout=1, clickThrough=1, fontSize=11, width=300;
    uint32_t colorFlags=0,timeArgb=0xffffffff,dateArgb=0xffffffff,trayArgb=0xffffffff;
    uint32_t networkArgb=0xffffffff,volumeArgb=0xffffffff,inputArgb=0xffffffff,batteryArgb=0xffffffff;
    uint32_t metricCount=0;
    Metric metrics[maxMetrics]{};
    uint32_t autoWidth=1,fontWeight=400;
    wchar_t fontFamily[96]=L"Microsoft YaHei UI";
};
struct ExplorerStatus {
    uint32_t connected=0, explorerPid=0, frames=0, backgrounds=0, times=0, dates=0;
    uint32_t systemIcons=0, networks=0,volumes=0,inputs=0,batteries=0,bitmapIcons=0;
    uint32_t panels=0,metricCount=0,patchedProperties=0,appliedRevision=0,restored=0;
    int32_t lastError=0;
    uint64_t tick=0;
};
struct SharedPage {
    uint32_t magic=protocolMagic,version=protocolVersion,bytes=sizeof(SharedPage),hostPid=0;
    volatile LONG configSequence=0;
    volatile LONG64 heartbeat=0;
    ExplorerConfig config{};
    volatile LONG statusSequence=0;
    ExplorerStatus status{};
    volatile LONG dumpSequence=0;
    wchar_t dump[dumpCapacity]{};
};

template<class T> inline bool ReadStable(volatile LONG& sequence,const T& source,T& result) {
    for(int retry=0;retry<12;retry++) {
        LONG before=InterlockedCompareExchange(&sequence,0,0);
        if(before&1) {YieldProcessor();continue;}
        MemoryBarrier();memcpy(&result,&source,sizeof(T));MemoryBarrier();
        if(before==InterlockedCompareExchange(&sequence,0,0)) return true;
    }
    return false;
}
template<class T> inline void WriteStable(volatile LONG& sequence,T& target,const T& source) {
    InterlockedIncrement(&sequence);MemoryBarrier();memcpy(&target,&source,sizeof(T));
    MemoryBarrier();InterlockedIncrement(&sequence);
}
}

static_assert(sizeof(claribar::ExplorerConfig)==3372);
static_assert(sizeof(claribar::SharedPage)==123496);
