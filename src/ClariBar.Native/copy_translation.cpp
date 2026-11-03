#include <windows.h>

HWND ClariHostWindow();
void ClariNotifyCopy();

namespace {
constexpr UINT configureMessage=WM_APP+40,readMessage=WM_APP+41,beginReadMessage=WM_APP+42,endReadMessage=WM_APP+43;
constexpr UINT watchPopupMessage=WM_APP+44,pollPopupMessage=WM_APP+45,unwatchPopupMessage=WM_APP+46;
constexpr UINT_PTR copyTimer=0x43424350;
struct CopyTrigger { LONG x,y; DWORD sequence,serial; };
HWND listener{};
DWORD observedSequence{},queuedSequence{},readingSerial{};
POINT anchor{};
CopyTrigger latest{};
HWND popup{};
HHOOK popupMouseHook{};
int outsideClick{};

LRESULT CALLBACK PopupMouseProc(int code,WPARAM message,LPARAM data) {
    if(code==HC_ACTION && popup && !outsideClick && (message==WM_LBUTTONDOWN || message==WM_RBUTTONDOWN)) {
        const auto* input=reinterpret_cast<const MSLLHOOKSTRUCT*>(data);RECT bounds{};
        if(IsWindowVisible(popup) && GetWindowRect(popup,&bounds) && !PtInRect(&bounds,input->pt))
            outsideClick=message==WM_LBUTTONDOWN?1:2;
    }
    // Observe only: the application under the pointer still receives the click.
    return CallNextHookEx(popupMouseHook,code,message,data);
}
void StopPopup() {
    if(popupMouseHook)UnhookWindowsHookEx(popupMouseHook);
    popupMouseHook=nullptr;popup=nullptr;outsideClick=0;
}

bool OwnWindow(HWND window) {
    DWORD process{};GetWindowThreadProcessId(window,&process);
    return process==GetCurrentProcessId();
}
void Stop() {
    StopPopup();
    if(listener){RemoveClipboardFormatListener(listener);KillTimer(listener,copyTimer);}
    listener=nullptr;observedSequence=queuedSequence=readingSerial=0;latest={};
}
int Start(HWND window) {
    if(listener)return 0;
    if(!AddClipboardFormatListener(window))return static_cast<int>(GetLastError());
    listener=window;observedSequence=GetClipboardSequenceNumber();
    return 0;
}
}

void ClariCopyShutdown(){Stop();}
LRESULT ClariCopyDispatch(HWND window,UINT message,WPARAM wparam,LPARAM lparam,bool& handled) {
    handled=true;
    if(message==watchPopupMessage) {
        auto target=reinterpret_cast<HWND>(lparam);
        if(!IsWindow(target) || !OwnWindow(target))return ERROR_INVALID_WINDOW_HANDLE;
        popup=target;outsideClick=0;
        if(!popupMouseHook) {
            HMODULE instance{};GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(&PopupMouseProc),&instance);
            popupMouseHook=SetWindowsHookExW(WH_MOUSE_LL,PopupMouseProc,instance,0);
            if(!popupMouseHook){DWORD error=GetLastError();StopPopup();return error?error:ERROR_GEN_FAILURE;}
        }
        return 0;
    }
    if(message==pollPopupMessage)return popup==reinterpret_cast<HWND>(lparam)?outsideClick:0;
    if(message==unwatchPopupMessage){if(popup==reinterpret_cast<HWND>(lparam))StopPopup();return 0;}
    if(message==configureMessage){if(!wparam){Stop();return 0;}return Start(window);}
    if(message==readMessage) {
        if(!latest.serial || !lparam)return 0;
        *reinterpret_cast<CopyTrigger*>(lparam)=latest;return 1;
    }
    // The managed reader calls these while holding the clipboard lock. Delayed
    // rendering may advance its sequence; that is still the same copy operation.
    if(message==beginReadMessage) {
        if(!listener || !wparam || latest.serial!=static_cast<DWORD>(wparam))return 0;
        readingSerial=static_cast<DWORD>(wparam);return 1;
    }
    if(message==endReadMessage) {
        if(readingSerial && readingSerial==static_cast<DWORD>(wparam)) {
            observedSequence=static_cast<DWORD>(lparam);queuedSequence=readingSerial=0;KillTimer(window,copyTimer);
        }
        return 0;
    }
    if(message==WM_CLIPBOARDUPDATE) {
        if(!listener)return 0;
        DWORD sequence=GetClipboardSequenceNumber();
        if(sequence!=observedSequence && sequence!=queuedSequence) {
            queuedSequence=sequence;GetCursorPos(&anchor);SetTimer(window,copyTimer,75,nullptr);
        }
        return 0;
    }
    if(message==WM_TIMER && wparam==copyTimer) {
        KillTimer(window,copyTimer);
        if(!listener)return 0;
        if(readingSerial){SetTimer(window,copyTimer,75,nullptr);return 0;}
        DWORD sequence=GetClipboardSequenceNumber();
        if(sequence==observedSequence)return 0;
        observedSequence=sequence;queuedSequence=0;
        // Ignore file copies, private clipboard formats and copies made by ClariBar itself.
        if(OwnWindow(GetClipboardOwner()) ||
            !IsClipboardFormatAvailable(CF_UNICODETEXT) || IsClipboardFormatAvailable(CF_HDROP) ||
            IsClipboardFormatAvailable(RegisterClipboardFormatW(L"ExcludeClipboardContentFromMonitorProcessing")))return 0;
        DWORD serial=latest.serial+1;if(!serial)serial=1;
        latest={anchor.x,anchor.y,sequence,serial};ClariNotifyCopy();return 0;
    }
    handled=false;return 0;
}
extern "C" __declspec(dllexport) int __stdcall CB_SetCopyTranslation(int enabled) {
    auto host=ClariHostWindow();if(!host)return ERROR_INVALID_WINDOW_HANDLE;
    return static_cast<int>(SendMessageW(host,configureMessage,enabled,0));
}
extern "C" __declspec(dllexport) int __stdcall CB_GetCopyTranslationTrigger(CopyTrigger* trigger) {
    auto host=ClariHostWindow();
    return host && trigger?static_cast<int>(SendMessageW(host,readMessage,0,reinterpret_cast<LPARAM>(trigger))):0;
}
extern "C" __declspec(dllexport) int __stdcall CB_BeginCopyTranslationRead(unsigned serial) {
    auto host=ClariHostWindow();return host?static_cast<int>(SendMessageW(host,beginReadMessage,serial,0)):0;
}
extern "C" __declspec(dllexport) void __stdcall CB_EndCopyTranslationRead(unsigned serial,unsigned sequence) {
    if(auto host=ClariHostWindow())SendMessageW(host,endReadMessage,serial,sequence);
}
extern "C" __declspec(dllexport) int __stdcall CB_WatchCopyPopup(HWND window) {
    auto host=ClariHostWindow();return host?static_cast<int>(SendMessageW(host,watchPopupMessage,0,reinterpret_cast<LPARAM>(window))):ERROR_INVALID_WINDOW_HANDLE;
}
extern "C" __declspec(dllexport) int __stdcall CB_PollCopyPopup(HWND window) {
    auto host=ClariHostWindow();return host?static_cast<int>(SendMessageW(host,pollPopupMessage,0,reinterpret_cast<LPARAM>(window))):0;
}
extern "C" __declspec(dllexport) void __stdcall CB_UnwatchCopyPopup(HWND window) {
    if(auto host=ClariHostWindow())SendMessageW(host,unwatchPopupMessage,0,reinterpret_cast<LPARAM>(window));
}
