#include <windows.h>
#include <cstring>

HWND ClariHostWindow();
namespace {
constexpr UINT beginPicker=WM_APP+60,updatePicker=WM_APP+61,endPicker=WM_APP+62;
using PickerCallback=void(__stdcall*)(int,int,int,int);
struct Begin {PickerCallback callback;bool observe;};
struct Preview {int x,y,width,height;const void* pixels;};
HWND preview{};
HHOOK mouseHook{},keyboardHook{};
PickerCallback callback{};
bool observing{};
unsigned pressed{};
LRESULT CALLBACK PreviewProc(HWND window,UINT message,WPARAM wparam,LPARAM lparam) {
    if(message==WM_NCHITTEST)return HTTRANSPARENT;
    if(message==WM_MOUSEACTIVATE)return MA_NOACTIVATE;
    return DefWindowProcW(window,message,wparam,lparam);
}
void Notify(int action,POINT point,int data=0){if(callback)callback(action,point.x,point.y,data);}
LRESULT CALLBACK MouseProc(int code,WPARAM message,LPARAM data) {
    if(code>=0 && callback && !observing) {
        auto* input=reinterpret_cast<MSLLHOOKSTRUCT*>(data);
        unsigned bit=message==WM_LBUTTONDOWN || message==WM_LBUTTONUP?1u:message==WM_MBUTTONDOWN || message==WM_MBUTTONUP?2u:message==WM_RBUTTONDOWN || message==WM_RBUTTONUP?4u:0u;
        if(message==WM_LBUTTONDOWN || message==WM_MBUTTONDOWN || message==WM_RBUTTONDOWN){pressed|=bit;return 1;}
        if(message==WM_LBUTTONUP || message==WM_MBUTTONUP || message==WM_RBUTTONUP){if(pressed&bit){pressed&=~bit;Notify(bit==1?1:bit==2?2:3,input->pt);return 1;}}
        if(message==WM_MOUSEWHEEL){Notify(4,input->pt,static_cast<short>(HIWORD(input->mouseData)));return 1;}
    }
    return CallNextHookEx(mouseHook,code,message,data);
}
LRESULT CALLBACK KeyboardProc(int code,WPARAM message,LPARAM data) {
    if(code>=0 && callback && !observing && reinterpret_cast<KBDLLHOOKSTRUCT*>(data)->vkCode==VK_ESCAPE) {
        if(message==WM_KEYUP || message==WM_SYSKEYUP){POINT point{};GetCursorPos(&point);Notify(0,point);}return 1;
    }
    return CallNextHookEx(keyboardHook,code,message,data);
}
}
void ClariPickerShutdown() {
    callback=nullptr;pressed=0;
    if(mouseHook)UnhookWindowsHookEx(mouseHook);mouseHook=nullptr;
    if(keyboardHook)UnhookWindowsHookEx(keyboardHook);keyboardHook=nullptr;
    if(preview)DestroyWindow(preview);preview=nullptr;
}
LRESULT ClariPickerDispatch(HWND host,UINT message,LPARAM data,bool& handled) {
    handled=message==beginPicker || message==updatePicker || message==endPicker;if(!handled)return 0;
    if(message==endPicker){ClariPickerShutdown();return 1;}
    if(message==beginPicker) {
        ClariPickerShutdown();auto* request=reinterpret_cast<Begin*>(data);observing=request->observe;
        HMODULE instance{};GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(&MouseProc),&instance);
        WNDCLASSW type{};type.lpfnWndProc=PreviewProc;type.hInstance=instance;type.lpszClassName=L"ClariBar.LiveColorPreview";RegisterClassW(&type);
        preview=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_TOPMOST|WS_EX_LAYERED|WS_EX_TRANSPARENT|WS_EX_NOACTIVATE,type.lpszClassName,L"ClariBar color preview",WS_POPUP,0,0,1,1,host,nullptr,instance,nullptr);
        if(!preview)return GetLastError();
        SetWindowDisplayAffinity(preview,0x11);
        mouseHook=SetWindowsHookExW(WH_MOUSE_LL,MouseProc,instance,0);keyboardHook=SetWindowsHookExW(WH_KEYBOARD_LL,KeyboardProc,instance,0);
        if(!mouseHook || !keyboardHook){DWORD error=GetLastError();ClariPickerShutdown();return error?error:ERROR_GEN_FAILURE;}
        callback=request->callback;return 0;
    }
    auto* frame=reinterpret_cast<Preview*>(data);
    if(!preview)return -ERROR_INVALID_WINDOW_HANDLE;
    if(!frame || !frame->pixels || frame->width<1 || frame->height<1 || frame->width>2048 || frame->height>2048)return -ERROR_INVALID_PARAMETER;
    HDC screen=GetDC(nullptr);if(!screen)return -ERROR_GEN_FAILURE;
    HDC memory=CreateCompatibleDC(screen);if(!memory){DWORD error=GetLastError();ReleaseDC(nullptr,screen);return -static_cast<int>(error?error:ERROR_GEN_FAILURE);}BITMAPINFO info{};
    info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=frame->width;info.bmiHeader.biHeight=-frame->height;info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
    void* pixels{};HBITMAP bitmap=CreateDIBSection(screen,&info,DIB_RGB_COLORS,&pixels,nullptr,0);
    if(!bitmap || !pixels){DWORD error=GetLastError();if(bitmap)DeleteObject(bitmap);DeleteDC(memory);ReleaseDC(nullptr,screen);return -static_cast<int>(error?error:ERROR_GEN_FAILURE);}
    auto old=SelectObject(memory,bitmap);std::memcpy(pixels,frame->pixels,static_cast<size_t>(frame->width)*frame->height*4);
    POINT destination{frame->x,frame->y},source{};SIZE size{frame->width,frame->height};BLENDFUNCTION blend{AC_SRC_OVER,0,255,AC_SRC_ALPHA};
    BOOL result=UpdateLayeredWindow(preview,screen,&destination,&size,memory,&source,0,&blend,ULW_ALPHA);
    DWORD error=result?ERROR_SUCCESS:GetLastError();
    if(result)ShowWindow(preview,SW_SHOWNOACTIVATE);
    SelectObject(memory,old);DeleteObject(bitmap);DeleteDC(memory);ReleaseDC(nullptr,screen);return result?1:-static_cast<int>(error?error:ERROR_GEN_FAILURE);
}
extern "C" __declspec(dllexport) int __stdcall CB_BeginLivePicker(PickerCallback handler,int observeOnly) {
    HWND host=ClariHostWindow();if(!host || !handler)return ERROR_INVALID_WINDOW_HANDLE;
    Begin request{handler,observeOnly!=0};return static_cast<int>(SendMessageW(host,beginPicker,0,reinterpret_cast<LPARAM>(&request)));
}
extern "C" __declspec(dllexport) int __stdcall CB_UpdateLivePicker(int x,int y,int width,int height,const void* pixels) {
    HWND host=ClariHostWindow();if(!host)return -ERROR_INVALID_WINDOW_HANDLE;Preview frame{x,y,width,height,pixels};return static_cast<int>(SendMessageW(host,updatePicker,0,reinterpret_cast<LPARAM>(&frame)));
}
extern "C" __declspec(dllexport) void __stdcall CB_EndLivePicker(){if(HWND host=ClariHostWindow())SendMessageW(host,endPicker,0,0);}
extern "C" __declspec(dllexport) HWND __stdcall CB_LivePickerWindow(){return preview;}
