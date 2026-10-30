#include <windows.h>
#include <commctrl.h>
#include <dwmapi.h>

// DWM setup follows the MIT-licensed WinUIEx TransparentTintBackdrop pattern.
// See licenses/WinUIEx-LICENSE.txt. The subclass only belongs to our tool HWND.
namespace {
constexpr UINT_PTR alphaSubclass=0x4342414c;
constexpr UINT_PTR frameSubclass=0x43424652;
LRESULT CALLBACK FrameProc(HWND window,UINT message,WPARAM wparam,LPARAM lparam,UINT_PTR,DWORD_PTR) {
    // These image/tool windows draw their entire frame in XAML. Keep Windows from
    // reserving or painting an extra non-client border around that content.
    if(message==WM_NCCALCSIZE || message==WM_NCPAINT)return 0;
    if(message==WM_NCACTIVATE)return TRUE;
    if(message==WM_NCDESTROY)RemoveWindowSubclass(window,FrameProc,frameSubclass);
    return DefSubclassProc(window,message,wparam,lparam);
}
void Configure(HWND window) {
    MARGINS margins{};
    DwmExtendFrameIntoClientArea(window,&margins);
    HRGN region=CreateRectRgn(-2,-2,-1,-1);
    DWM_BLURBEHIND blur{};
    blur.dwFlags=DWM_BB_ENABLE|DWM_BB_BLURREGION;
    blur.fEnable=TRUE;blur.hRgnBlur=region;
    DwmEnableBlurBehindWindow(window,&blur);
    DeleteObject(region);
}
void Clear(HWND window,HDC dc) {
    RECT client{};
    if(GetClientRect(window,&client))FillRect(dc,&client,static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)));
}
LRESULT CALLBACK AlphaProc(HWND window,UINT message,WPARAM wparam,LPARAM lparam,UINT_PTR,DWORD_PTR) {
    if(message==WM_ERASEBKGND){Clear(window,reinterpret_cast<HDC>(wparam));return 1;}
    if(message==WM_DWMCOMPOSITIONCHANGED){Configure(window);return 0;}
    if(message==WM_NCDESTROY)RemoveWindowSubclass(window,AlphaProc,alphaSubclass);
    return DefSubclassProc(window,message,wparam,lparam);
}
}
extern "C" __declspec(dllexport) int __stdcall CB_MakeToolWindowBorderless(HWND window) {
    if(!IsWindow(window))return ERROR_INVALID_WINDOW_HANDLE;
    if(!SetWindowSubclass(window,FrameProc,frameSubclass,0)){DWORD error=GetLastError();return static_cast<int>(error?error:ERROR_GEN_FAILURE);}
    auto style=GetWindowLongPtrW(window,GWL_STYLE);
    SetWindowLongPtrW(window,GWL_STYLE,(style&~static_cast<LONG_PTR>(WS_OVERLAPPEDWINDOW))|WS_POPUP);
    auto extended=GetWindowLongPtrW(window,GWL_EXSTYLE);
    SetWindowLongPtrW(window,GWL_EXSTYLE,(extended&~static_cast<LONG_PTR>(WS_EX_WINDOWEDGE|WS_EX_CLIENTEDGE|WS_EX_DLGMODALFRAME|WS_EX_STATICEDGE))|WS_EX_TOOLWINDOW);
    DWORD none=DWMWA_COLOR_NONE;DwmSetWindowAttribute(window,DWMWA_BORDER_COLOR,&none,sizeof(none));
    DWM_WINDOW_CORNER_PREFERENCE corners=DWMWCP_DONOTROUND;DwmSetWindowAttribute(window,DWMWA_WINDOW_CORNER_PREFERENCE,&corners,sizeof(corners));
    SetWindowPos(window,nullptr,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED);
    return 0;
}
extern "C" __declspec(dllexport) int __stdcall CB_EnableWindowAlpha(HWND window) {
    if(!IsWindow(window) || !SetWindowSubclass(window,AlphaProc,alphaSubclass,0))return 0;
    Configure(window);HDC dc=GetDC(window);Clear(window,dc);ReleaseDC(window,dc);
    return 1;
}
extern "C" __declspec(dllexport) void __stdcall CB_DisableWindowAlpha(HWND window) {
    if(IsWindow(window))RemoveWindowSubclass(window,AlphaProc,alphaSubclass);
}
