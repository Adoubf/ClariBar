#include <windows.h>
#include <ole2.h>
#include <UIAutomation.h>
#include <wrl/client.h>
#include <algorithm>

using Microsoft::WRL::ComPtr;

// Runs on a bounded background caller, never on the settings window's UI thread.
extern "C" __declspec(dllexport) int __stdcall CB_ElementRegions(HWND window,RECT* output,int capacity) {
    if(!window || !output || capacity<=0)return 0;
    const HRESULT initialized=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
    struct Apartment {HRESULT result;~Apartment(){if(SUCCEEDED(result))CoUninitialize();}} apartment{initialized};
    ComPtr<IUIAutomation> automation;
    HRESULT result=CoCreateInstance(__uuidof(CUIAutomation8),nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&automation));
    if(FAILED(result))result=CoCreateInstance(__uuidof(CUIAutomation),nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&automation));
    if(FAILED(result))return 0;
    ComPtr<IUIAutomationElement> root;
    if(FAILED(automation->ElementFromHandle(window,&root)))return 0;
    ComPtr<IUIAutomationCacheRequest> cache;
    ComPtr<IUIAutomationCondition> condition;
    if(FAILED(automation->CreateCacheRequest(&cache)) || FAILED(automation->CreateTrueCondition(&condition)))return 0;
    cache->AddProperty(UIA_BoundingRectanglePropertyId);
    cache->AddProperty(UIA_IsOffscreenPropertyId);
    cache->put_AutomationElementMode(AutomationElementMode_None);
    ComPtr<IUIAutomationElementArray> elements;
    if(FAILED(root->FindAllBuildCache(TreeScope_Descendants,condition.Get(),cache.Get(),&elements)))return 0;
    int length=0,count=0;elements->get_Length(&length);
    for(int index=0;index<std::min(length,12000) && count<capacity;++index) {
        ComPtr<IUIAutomationElement> element;RECT box{};BOOL offscreen=FALSE;
        if(FAILED(elements->GetElement(index,&element)) || FAILED(element->get_CachedBoundingRectangle(&box)))continue;
        element->get_CachedIsOffscreen(&offscreen);
        if(!offscreen && box.right-box.left>2 && box.bottom-box.top>2)output[count++]=box;
    }
    return count;
}
