#include "explorer_bridge.h"
#include <xamlOM.h>
#include <sddl.h>
#include <string>
#include <mutex>
#include <atomic>
#include <thread>
#include <memory>
#include <cstdio>
#include <algorithm>
#include <objbase.h>

namespace {
std::mutex gate;
HANDLE mapping{};
claribar::SharedPage* page{};
claribar::ExplorerConfig latest{};
std::wstring mappingName,adapterPath;
std::atomic<bool> attaching{};
std::atomic<DWORD> attachedPid{};
std::atomic<HRESULT> attachResult{S_FALSE};
std::atomic<ULONGLONG> lastAttachTick{};
bool recoverySuppressed{};
std::atomic<bool> heartbeatPaused{};

DWORD ExplorerPid() {
    DWORD pid{};GetWindowThreadProcessId(FindWindowW(L"Shell_TrayWnd",nullptr),&pid);return pid;
}
HRESULT EnsureMapping() {
    if(page) return S_OK;
    GUID nonce{};CoCreateGuid(&nonce);wchar_t identifier[64]{};StringFromGUID2(nonce,identifier,64);
    mappingName=L"Local\\ClariBar.Xaml."+std::to_wstring(GetCurrentProcessId())+L"."+identifier;
    // Default process DACL: same interactive user; never grant Everyone access.
    mapping=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(claribar::SharedPage),mappingName.c_str());
    if(!mapping) return HRESULT_FROM_WIN32(GetLastError());
    page=static_cast<claribar::SharedPage*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,0));
    if(!page) {auto hr=HRESULT_FROM_WIN32(GetLastError());CloseHandle(mapping);mapping=nullptr;return hr;}
    new(page) claribar::SharedPage{};page->hostPid=GetCurrentProcessId();
    claribar::WriteStable(page->configSequence,page->config,latest);
    InterlockedExchange64(&page->heartbeat,static_cast<LONG64>(GetTickCount64()));
    return S_OK;
}
HRESULT Attach(DWORD pid,const std::wstring& path,const std::wstring& name) {
    HRESULT com=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
    HMODULE xaml=LoadLibraryExW(L"Windows.UI.Xaml.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    HRESULT result=HRESULT_FROM_WIN32(GetLastError());
    if(xaml) {
        auto initialize=reinterpret_cast<decltype(&InitializeXamlDiagnosticsEx)>(GetProcAddress(xaml,"InitializeXamlDiagnosticsEx"));
        result=initialize?HRESULT_FROM_WIN32(ERROR_NOT_FOUND):HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND);
        // The system numbers diagnostics endpoints per XAML runtime instance.
        for(unsigned i=1;initialize && i<=256;i++) {
            std::wstring endpoint=L"VisualDiagConnection"+std::to_wstring(i);
            result=initialize(endpoint.c_str(),pid,L"",path.c_str(),claribar::tapClsid,name.c_str());
            if(result!=HRESULT_FROM_WIN32(ERROR_NOT_FOUND)) break;
        }
        FreeLibrary(xaml);
    }
    if(SUCCEEDED(com)) CoUninitialize();
    return result;
}
}

extern "C" __declspec(dllexport) HRESULT __stdcall CB_XamlAttach(const wchar_t* path) {
    if(!path || !*path || GetFileAttributesW(path)==INVALID_FILE_ATTRIBUTES) return E_INVALIDARG;
    DWORD pid=ExplorerPid();if(!pid) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    std::wstring name;
    {std::lock_guard lock(gate);HRESULT hr=EnsureMapping();if(FAILED(hr))return hr;adapterPath=path;name=mappingName;recoverySuppressed=false;}
    if(attaching.exchange(true))return HRESULT_FROM_WIN32(ERROR_BUSY);
    lastAttachTick=GetTickCount64();HRESULT result=Attach(pid,path,name);attachResult=result;attachedPid=SUCCEEDED(result)?pid:0;attaching=false;
    return result;
}
extern "C" __declspec(dllexport) void __stdcall CB_XamlConfigure(const claribar::ExplorerConfig* config) {
    if(!config)return;
    std::lock_guard lock(gate);latest=*config;latest.metricCount=std::min(latest.metricCount,claribar::maxMetrics);
    if(page)claribar::WriteStable(page->configSequence,page->config,latest);
}
claribar::ExplorerConfig ClariLocalConfig() {std::lock_guard lock(gate);return latest;}
void ClariBridgeHeartbeat() {
    if(heartbeatPaused)return;
    std::wstring path,name;DWORD pid{};
    {
        std::lock_guard lock(gate);if(!page)return;
        InterlockedExchange64(&page->heartbeat,static_cast<LONG64>(GetTickCount64()));
        pid=ExplorerPid();if(recoverySuppressed || pid==0 || pid==attachedPid || attaching)return;
        // A fast Explorer restart can indicate an adapter failure. Do not create
        // a restart loop; require an explicit reconnect after this condition.
        if(GetTickCount64()-lastAttachTick<30000) {
            recoverySuppressed=true;attachResult=HRESULT_FROM_WIN32(ERROR_RETRY);latest.enabled=0;
            claribar::WriteStable(page->configSequence,page->config,latest);return;
        }
        if(attaching.exchange(true))return;
        path=adapterPath;name=mappingName;
    }
    lastAttachTick=GetTickCount64();std::thread([pid,path,name] {HRESULT hr=Attach(pid,path,name);attachResult=hr;attachedPid=SUCCEEDED(hr)?pid:0;attaching=false;}).detach();
}
// Diagnostic control used only by the integration probe to exercise loss of
// heartbeat without terminating Explorer or changing any system configuration.
extern "C" __declspec(dllexport) void __stdcall CB_XamlPauseHeartbeat(int paused){heartbeatPaused=paused!=0;}
extern "C" __declspec(dllexport) int __stdcall CB_XamlStatus(wchar_t* buffer,int capacity) {
    if(!buffer || capacity<4)return 0;
    claribar::ExplorerStatus status{};HRESULT result=attachResult.load();
    {std::lock_guard lock(gate);if(page)claribar::ReadStable(page->statusSequence,page->status,status);}
    int written=swprintf_s(buffer,capacity,
      L"{\"connected\":%u,\"explorerPid\":%u,\"frames\":%u,\"backgrounds\":%u,\"times\":%u,\"dates\":%u,\"systemIcons\":%u,\"networks\":%u,\"volumes\":%u,\"inputs\":%u,\"batteries\":%u,\"bitmapIcons\":%u,\"panels\":%u,\"metricCount\":%u,\"patchedProperties\":%u,\"appliedRevision\":%u,\"restored\":%u,\"lastError\":%d,\"attachResult\":%d,\"tick\":%llu}",
      status.connected,status.explorerPid,status.frames,status.backgrounds,status.times,status.dates,status.systemIcons,status.networks,status.volumes,status.inputs,status.batteries,status.bitmapIcons,status.panels,status.metricCount,status.patchedProperties,status.appliedRevision,status.restored,status.lastError,result,status.tick);
    return written>0?written:0;
}
extern "C" __declspec(dllexport) int __stdcall CB_XamlDump(wchar_t* buffer,int capacity) {
    if(!buffer || capacity<2)return 0;std::lock_guard lock(gate);if(!page){buffer[0]=0;return 0;}
    auto copy=std::make_unique<wchar_t[]>(claribar::dumpCapacity);
    for(int i=0;i<8;i++) {
        LONG before=InterlockedCompareExchange(&page->dumpSequence,0,0);if(before&1)continue;
        memcpy(copy.get(),page->dump,sizeof(page->dump));MemoryBarrier();
        if(before!=InterlockedCompareExchange(&page->dumpSequence,0,0))continue;
        copy[claribar::dumpCapacity-1]=0;wcsncpy_s(buffer,capacity,copy.get(),_TRUNCATE);return static_cast<int>(wcslen(buffer));
    }
    buffer[0]=0;return 0;
}
void ClariBridgeStop() {
    {std::lock_guard lock(gate);latest.enabled=0;if(page)claribar::WriteStable(page->configSequence,page->config,latest);}
    // The Explorer UI timer performs restoration. Do not block its UI thread.
    for(int i=0;i<25;i++) {
        bool restored;
        {std::lock_guard lock(gate);claribar::ExplorerStatus state{};restored=!page ||
          (claribar::ReadStable(page->statusSequence,page->status,state) && state.panels==0 && state.patchedProperties==0);}
        if(restored)break;Sleep(80);
    }
    std::lock_guard lock(gate);
    if(page){InterlockedExchange64(&page->heartbeat,0);UnmapViewOfFile(page);page=nullptr;}
    if(mapping){CloseHandle(mapping);mapping=nullptr;}
    attachedPid=0;adapterPath.clear();
}
