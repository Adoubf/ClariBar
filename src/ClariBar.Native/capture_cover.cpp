#include <windows.h>
#include <dwmapi.h>
#include <algorithm>
#include <cstring>

HWND ClariHostWindow();
namespace {
constexpr UINT showCover=WM_APP+80,raiseCover=WM_APP+81,closeCover=WM_APP+82;
struct Request {const void* pixels;int width,height,x,y,left,top,right,bottom;};
HWND cover{};HDC imageDc{},shadeDc{};HBITMAP image{},oldImage{},shade{},oldShade{};
RECT selection{};int width{},height{};
void Close() {
    if(cover)DestroyWindow(cover);cover=nullptr;
    if(imageDc){if(oldImage)SelectObject(imageDc,oldImage);if(image)DeleteObject(image);DeleteDC(imageDc);}
    if(shadeDc){if(oldShade)SelectObject(shadeDc,oldShade);if(shade)DeleteObject(shade);DeleteDC(shadeDc);}
    imageDc=shadeDc=nullptr;image=oldImage=shade=oldShade=nullptr;
}
LRESULT CALLBACK CoverProc(HWND window,UINT message,WPARAM wparam,LPARAM lparam) {
    if(message==WM_ERASEBKGND)return 1;
    if(message==WM_MOUSEACTIVATE)return MA_NOACTIVATEANDEAT;
    if(message==WM_PAINT) {
        PAINTSTRUCT paint{};auto dc=BeginPaint(window,&paint);
        if(imageDc) {
            BitBlt(dc,0,0,width,height,imageDc,0,0,SRCCOPY);
            BLENDFUNCTION blend{AC_SRC_OVER,0,175,0};
            auto Dim=[&](int x,int y,int w,int h){if(w>0 && h>0)AlphaBlend(dc,x,y,w,h,shadeDc,0,0,1,1,blend);};
            if(IsRectEmpty(&selection))Dim(0,0,width,height);
            else {Dim(0,0,width,selection.top);Dim(0,selection.bottom,width,height-selection.bottom);Dim(0,selection.top,selection.left,selection.bottom-selection.top);Dim(selection.right,selection.top,width-selection.right,selection.bottom-selection.top);}
        }
        EndPaint(window,&paint);GdiFlush();return 0;
    }
    return DefWindowProcW(window,message,wparam,lparam);
}
int Show(const Request& request) {
    Close();if(!request.pixels || request.width<=0 || request.height<=0 || static_cast<long long>(request.width)*request.height>64000000)return ERROR_INVALID_PARAMETER;
    width=request.width;height=request.height;selection={request.left,request.top,request.right,request.bottom};
    imageDc=CreateCompatibleDC(nullptr);shadeDc=CreateCompatibleDC(nullptr);
    BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=width;info.bmiHeader.biHeight=-height;info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;
    void* bits{};image=CreateDIBSection(imageDc,&info,DIB_RGB_COLORS,&bits,nullptr,0);
    shade=CreateBitmap(1,1,1,32,nullptr);
    if(!imageDc || !shadeDc || !image || !bits || !shade){Close();return ERROR_NOT_ENOUGH_MEMORY;}
    oldImage=static_cast<HBITMAP>(SelectObject(imageDc,image));oldShade=static_cast<HBITMAP>(SelectObject(shadeDc,shade));SetPixel(shadeDc,0,0,RGB(0,0,0));
    std::memcpy(bits,request.pixels,static_cast<size_t>(width)*height*4);
    auto instance=GetModuleHandleW(nullptr);WNDCLASSW type{};type.hInstance=instance;type.lpfnWndProc=CoverProc;type.lpszClassName=L"ClariBar.CaptureCover";type.hCursor=LoadCursorW(nullptr,IDC_CROSS);RegisterClassW(&type);
    cover=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_TOPMOST|WS_EX_NOACTIVATE|WS_EX_LAYERED,type.lpszClassName,L"ClariBar capture",WS_POPUP,request.x,request.y,width,height,nullptr,nullptr,instance,nullptr);
    if(!cover){DWORD error=GetLastError();Close();return static_cast<int>(error);}
    BOOL disabled=TRUE;DwmSetWindowAttribute(cover,DWMWA_TRANSITIONS_FORCEDISABLED,&disabled,sizeof(disabled));
    // A nearly opaque cover keeps the XAML window below it eligible for rendering.
    SetLayeredWindowAttributes(cover,0,254,LWA_ALPHA);
    SetWindowPos(cover,HWND_TOPMOST,request.x,request.y,width,height,SWP_NOACTIVATE|SWP_SHOWWINDOW);UpdateWindow(cover);GdiFlush();return 0;
}
}
void ClariCaptureCoverShutdown(){Close();}
LRESULT ClariCaptureCoverDispatch(UINT message,LPARAM parameter,bool& handled) {
    handled=true;
    if(message==showCover)return Show(*reinterpret_cast<const Request*>(parameter));
    if(message==raiseCover){if(cover)SetWindowPos(cover,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE);return 0;}
    if(message==closeCover){Close();return 0;}
    handled=false;return 0;
}
extern "C" __declspec(dllexport) int __stdcall CB_ShowCaptureCover(const void* pixels,int width,int height,int x,int y,int left,int top,int right,int bottom) {
    auto host=ClariHostWindow();if(!host)return ERROR_INVALID_WINDOW_HANDLE;
    Request request{pixels,width,height,x,y,left,top,right,bottom};return static_cast<int>(SendMessageW(host,showCover,0,reinterpret_cast<LPARAM>(&request)));
}
extern "C" __declspec(dllexport) void __stdcall CB_RaiseCaptureCover(){if(auto host=ClariHostWindow())SendMessageW(host,raiseCover,0,0);}
extern "C" __declspec(dllexport) void __stdcall CB_CloseCaptureCover(){if(auto host=ClariHostWindow())SendMessageW(host,closeCover,0,0);}
