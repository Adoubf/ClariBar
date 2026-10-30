#include <windows.h>
#include <shellapi.h>
#include <dwmapi.h>
#include <string>
#include <thread>
#include <mutex>
#include <atomic>
#include <vector>
#include <algorithm>
#include <set>
#include <cmath>
#include "explorer_bridge.h"
#include "text_ink.h"
LRESULT ClariPickerDispatch(HWND,UINT,LPARAM,bool&);
void ClariPickerShutdown();

namespace {
constexpr UINT trayMessage = WM_APP + 1, updateMessage = WM_APP + 2, languageMessage = WM_APP + 3;
constexpr UINT toolHotkeyMessage = WM_APP + 20;
struct ToolHotkeyUpdate { int slot; UINT modifiers, key; };
constexpr UINT iconId = 1;
using Callback = void(__stdcall*)(int);
std::thread worker;
HWND host{}, monitor{};
HANDLE ready{};
Callback callback{};
UINT taskbarCreated{};
UINT showRequested{};
UINT quitRequested{};
UINT desktopCommandRequested{};
HICON icon{};
HFONT font{};
int fontPixels{};
std::wstring fontFamily;
bool fontBold{};
std::vector<int> cellWidths;
std::wstring monitorText = L"ClariBar · Waiting for data";
COLORREF foreground = RGB(255,255,255), background = RGB(32,32,32);
BYTE alpha = 235;
bool shown{}, positioned{};
std::atomic<bool> running{};
std::atomic<bool> chinese{};
const wchar_t* Tr(const wchar_t* english,const wchar_t* simplified){return chinese.load()?simplified:english;}
int transparencyMode{};
BYTE taskbarAlpha = 128;
std::vector<HWND> modified;

struct AccentPolicy { int state; int flags; DWORD gradient; int animation; };
struct CompositionData { int attribute; void* data; size_t size; };
using SetComposition = BOOL(WINAPI*)(HWND, CompositionData*);
struct MonitorUpdate { const wchar_t* text; COLORREF fg,bg; BYTE opacity; bool visible; };
struct TaskbarUpdate { int mode; BYTE opacity; };

void Notify(int value) { if (callback) callback(value); }
void AddTray() {
    NOTIFYICONDATAW data{sizeof(data)};
    data.hWnd=host; data.uID=iconId;
    data.uFlags=NIF_ICON|NIF_MESSAGE|NIF_TIP;
    data.hIcon=icon; data.uCallbackMessage=trayMessage;
    wcscpy_s(data.szTip,Tr(L"ClariBar · Double-click to open settings",L"澄栏 ClariBar · 双击打开设置"));
    Shell_NotifyIconW(NIM_ADD,&data);
    data.uVersion=NOTIFYICON_VERSION_4;
    Shell_NotifyIconW(NIM_SETVERSION,&data);
}

HICON MakeIcon() {
    wchar_t executable[32768]{};GetModuleFileNameW(nullptr,executable,32768);
    std::wstring path=executable;auto slash=path.find_last_of(L"\\/");
    path=path.substr(0,slash+1)+L"ClariBar.ico";
    UINT dpi=GetDpiForSystem();int size=GetSystemMetricsForDpi(SM_CXSMICON,dpi);
    auto result=static_cast<HICON>(LoadImageW(nullptr,path.c_str(),IMAGE_ICON,size,size,LR_LOADFROMFILE));
    return result?result:CopyIcon(LoadIconW(nullptr,IDI_APPLICATION));
}

bool ApplyComposition(HWND window, int mode, BYTE opacity) {
    auto proc=reinterpret_cast<SetComposition>(GetProcAddress(GetModuleHandleW(L"user32.dll"),"SetWindowCompositionAttribute"));
    if (!proc) return false;
    // Legacy composition request only; modern XAML taskbars may ignore this.
    AccentPolicy policy{mode==0?0:2,0, mode==0?0u:(static_cast<DWORD>(opacity)<<24)|0x202020u,0};
    CompositionData data{19,&policy,sizeof(policy)};
    return proc(window,&data)!=FALSE;
}
int ApplyTaskbars() {
    int count=0;
    HWND primary=FindWindowW(L"Shell_TrayWnd",nullptr);
    std::vector<HWND> bars;
    if(primary) bars.push_back(primary);
    HWND next=nullptr;
    while((next=FindWindowExW(nullptr,next,L"Shell_SecondaryTrayWnd",nullptr))) bars.push_back(next);
    for(auto window:bars) {
        if(ApplyComposition(window,transparencyMode,taskbarAlpha)) {
            count++;
            if(transparencyMode && std::find(modified.begin(),modified.end(),window)==modified.end()) modified.push_back(window);
        }
    }
    return count;
}
void RestoreTaskbars() {
    transparencyMode=0;
    for(auto window:modified) if(IsWindow(window)) ApplyComposition(window,0,0);
    modified.clear();
}

void EnsureFont(const claribar::ExplorerConfig& config,int dpi) {
    int pixels=MulDiv(static_cast<int>(config.fontSize),dpi,96);
    std::wstring family=config.fontFamily[0]?config.fontFamily:L"Microsoft YaHei UI";
    bool bold=config.fontWeight>=600;
    if(pixels==fontPixels && family==fontFamily && bold==fontBold && font)return;
    if(font)DeleteObject(font);
    font=CreateFontW(-pixels,0,0,0,bold?FW_BOLD:FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,
      OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,DEFAULT_PITCH,family.c_str());
    fontPixels=pixels;fontFamily=family;fontBold=bold;
}
int MonitorWidth(const claribar::ExplorerConfig& config,int dpi,int available) {
    EnsureFont(config,dpi);
    unsigned count=std::max(1u,std::min(config.metricCount,claribar::maxMetrics));
    unsigned cols=config.layout==1?2u:config.layout==2?(count+1)/2:count;cols=std::max(cols,1u);
    int padding=MulDiv(14,dpi,96),gap=MulDiv(12,dpi,96);
    cellWidths.assign(cols,gap);
    HDC dc=GetDC(monitor);auto previous=SelectObject(dc,font);
    for(unsigned i=0;i<count;i++) {
        const wchar_t* text=config.metricCount?config.metrics[i].text:monitorText.c_str();
        SIZE size{};GetTextExtentPoint32W(dc,text,static_cast<int>(wcslen(text)),&size);
        cellWidths[i%cols]=std::max(cellWidths[i%cols],static_cast<int>(size.cx)+gap);
    }
    SelectObject(dc,previous);ReleaseDC(monitor,dc);
    int natural=2*padding;for(int width:cellWidths)natural+=width;
    int width=std::max(64,std::min(available,config.autoWidth?natural:MulDiv(static_cast<int>(config.width),dpi,96)));
    int used=0;
    for(unsigned i=0;i<cols;i++){
        cellWidths[i]=config.autoWidth?MulDiv(cellWidths[i],width-2*padding,std::max(1,natural-2*padding)):(width-2*padding)/static_cast<int>(cols);
        used+=cellWidths[i];
    }
    cellWidths.back()+=width-2*padding-used;
    return width;
}
int CALLBACK CollectFont(const LOGFONTW* entry,const TEXTMETRICW*,DWORD,LPARAM context) {
    if(entry->lfFaceName[0] && entry->lfFaceName[0]!=L'@')reinterpret_cast<std::set<std::wstring>*>(context)->insert(entry->lfFaceName);
    return 1;
}

LRESULT CALLBACK MonitorProc(HWND window,UINT message,WPARAM wparam,LPARAM lparam) {
    if(message==WM_ERASEBKGND) return 1;
    if(message==WM_PAINT) {
        PAINTSTRUCT paint{};HDC dc=BeginPaint(window,&paint);RECT rect{};GetClientRect(window,&rect);
        HBRUSH brush=CreateSolidBrush(background);FillRect(dc,&rect,brush);DeleteObject(brush);
        auto config=ClariLocalConfig();int dpi=static_cast<int>(GetDpiForWindow(window));
        EnsureFont(config,dpi);
        SetBkMode(dc,TRANSPARENT);SetTextColor(dc,foreground);
        auto old=SelectObject(dc,font);int padding=MulDiv(14,dpi,96);rect.left+=padding;rect.right-=padding;
        if(config.metricCount) {
            unsigned count=std::min(config.metricCount,claribar::maxMetrics);
            unsigned columns=config.layout==1?2u:config.layout==2?(count+1)/2:count;
            columns=std::max(columns,1u);unsigned rows=(count+columns-1)/columns;
            int height=(rect.bottom-rect.top)/rows;
            for(unsigned i=0;i<count;i++){
                int left=rect.left;
                for(unsigned j=0;j<i%columns && j<cellWidths.size();j++)left+=cellWidths[j];
                int width=i%columns<cellWidths.size()?cellWidths[i%columns]:(rect.right-rect.left)/columns;
                RECT cell{left,rect.top+static_cast<int>(i/columns)*height,left+width-MulDiv(6,dpi,96),rect.top+static_cast<int>(i/columns+1)*height};
                auto rgb=config.metrics[i].color;SetTextColor(dc,RGB((rgb>>16)&255,(rgb>>8)&255,rgb&255));
                DrawTextW(dc,config.metrics[i].text,-1,&cell,DT_SINGLELINE|DT_VCENTER|DT_END_ELLIPSIS|DT_NOPREFIX);
            }
        }else DrawTextW(dc,monitorText.c_str(),-1,&rect,DT_SINGLELINE|DT_VCENTER|DT_END_ELLIPSIS|DT_NOPREFIX);
        SelectObject(dc,old);EndPaint(window,&paint);return 0;
    }
    if(message==WM_NCHITTEST && ClariLocalConfig().clickThrough)return HTTRANSPARENT;
    if(message==WM_LBUTTONDBLCLK) {Notify(1);return 0;}
    if(message==WM_LBUTTONDOWN) {
        ReleaseCapture();SendMessageW(window,WM_NCLBUTTONDOWN,HTCAPTION,lparam);return 0;
    }
    if(message==WM_RBUTTONUP) {Notify(1);return 0;}
    return DefWindowProcW(window,message,wparam,lparam);
}

void PositionMonitor() {
    if(!monitor) return;
    RECT work{};SystemParametersInfoW(SPI_GETWORKAREA,0,&work,0);
    auto config=ClariLocalConfig();int dpi=static_cast<int>(GetDpiForWindow(monitor));
    unsigned count=std::max(1u,std::min(config.metricCount,claribar::maxMetrics));
    unsigned columns=config.layout==1?2u:config.layout==2?(count+1)/2:count;columns=std::max(columns,1u);
    int rows=static_cast<int>((count+columns-1)/columns);
    int width=MonitorWidth(config,dpi,static_cast<int>((work.right-work.left)*.60));
    int height=std::max(28,MulDiv((static_cast<int>(config.fontSize)+8)*rows+8,dpi,96));
    SetWindowPos(monitor,HWND_TOPMOST,work.right-width-16,work.bottom-height-12,width,height,SWP_NOACTIVATE);
    positioned=true;
}

LRESULT CALLBACK HostProc(HWND window,UINT message,WPARAM wparam,LPARAM lparam) {
    bool pickerHandled{};auto pickerResult=ClariPickerDispatch(window,message,lparam,pickerHandled);if(pickerHandled)return pickerResult;
    if(message==toolHotkeyMessage) {
        const auto* data=reinterpret_cast<const ToolHotkeyUpdate*>(lparam);
        if(data->slot<0 || data->slot>7)return ERROR_INVALID_PARAMETER;
        const int id=10+data->slot;UnregisterHotKey(window,id);
        if(!data->key)return 0;
        if(RegisterHotKey(window,id,data->modifiers|MOD_NOREPEAT,data->key))return 0;
        return GetLastError();
    }
    if(message==WM_HOTKEY && wparam>=10 && wparam<=17) {Notify(wparam==17?22:static_cast<int>(wparam));return 0;}
    if(message==languageMessage) {
        NOTIFYICONDATAW data{sizeof(data)};data.hWnd=host;data.uID=iconId;data.uFlags=NIF_TIP;
        wcscpy_s(data.szTip,Tr(L"ClariBar · Double-click to open settings",L"澄栏 ClariBar · 双击打开设置"));
        Shell_NotifyIconW(NIM_MODIFY,&data);return 0;
    }
    if(message==showRequested) {Notify(1);return 0;}
    if(message==quitRequested) {Notify(2);return 0;}
    if(message==desktopCommandRequested) {if(wparam>=10 && wparam<=22)Notify(static_cast<int>(wparam));return 0;}
    if(message==WM_TIMER){ClariBridgeHeartbeat();return 0;}
    if(message==taskbarCreated) {AddTray();if(transparencyMode) ApplyTaskbars();return 0;}
    if(message==trayMessage) {
        auto event=LOWORD(lparam);
        if(event==WM_LBUTTONDBLCLK || event==NIN_SELECT || event==NIN_KEYSELECT) Notify(1);
        if(event==WM_CONTEXTMENU || event==WM_RBUTTONUP) {
            POINT point{};GetCursorPos(&point);HMENU menu=CreatePopupMenu();
            AppendMenuW(menu,MF_STRING,1,Tr(L"Open ClariBar settings",L"打开澄栏设置"));
            AppendMenuW(menu,MF_SEPARATOR,0,nullptr);
            AppendMenuW(menu,MF_STRING,10,Tr(L"Screenshot",L"截图"));
            AppendMenuW(menu,MF_STRING,11,Tr(L"Pick a screen color",L"屏幕取色"));
            AppendMenuW(menu,MF_STRING,12,Tr(L"Pin clipboard image",L"贴图（剪贴板）"));
            AppendMenuW(menu,MF_STRING,13,Tr(L"Extract text (OCR)",L"提取文字（OCR）"));
            AppendMenuW(menu,MF_STRING,22,Tr(L"Screenshot translation",L"截图翻译"));
            AppendMenuW(menu,MF_STRING,14,Tr(L"Show / hide all images",L"显示 / 隐藏所有贴图"));
            AppendMenuW(menu,MF_STRING,15,Tr(L"Toggle image click-through",L"切换贴图鼠标穿透"));
            AppendMenuW(menu,MF_STRING,16,Tr(L"Delayed capture",L"延时截图"));
            AppendMenuW(menu,MF_STRING,17,Tr(L"Capture current screen",L"截取当前屏幕"));
            AppendMenuW(menu,MF_STRING,18,Tr(L"Capture entire desktop",L"截取整个桌面"));
            AppendMenuW(menu,MF_STRING,19,Tr(L"Repeat last region",L"重复上次选区"));
            AppendMenuW(menu,MF_STRING,20,Tr(L"Whiteboard",L"白板"));
            AppendMenuW(menu,MF_STRING,21,Tr(L"Pinned images and groups",L"贴图与分组"));
            AppendMenuW(menu,MF_SEPARATOR,0,nullptr);
            AppendMenuW(menu,MF_STRING,3,shown?Tr(L"Hide floating monitor",L"隐藏监控窗"):Tr(L"Show floating monitor",L"显示监控窗"));
            AppendMenuW(menu,MF_STRING,4,ClariLocalConfig().embedded?Tr(L"Hide taskbar monitor",L"隐藏任务栏监控"):Tr(L"Show taskbar monitor",L"显示任务栏监控"));
            AppendMenuW(menu,MF_SEPARATOR,0,nullptr);AppendMenuW(menu,MF_STRING,2,Tr(L"Quit ClariBar",L"退出澄栏"));
            SetForegroundWindow(window);
            auto result=TrackPopupMenu(menu,TPM_RETURNCMD|TPM_NONOTIFY,point.x,point.y,0,window,nullptr);
            DestroyMenu(menu);if(result) Notify(result);PostMessageW(window,WM_NULL,0,0);
        }
        return 0;
    }
    if(message==updateMessage) {
        const auto* data=reinterpret_cast<const MonitorUpdate*>(lparam);
        monitorText=data->text?data->text:L"";foreground=data->fg;background=data->bg;alpha=data->opacity;
        if(!positioned) PositionMonitor();
        auto config=ClariLocalConfig();LONG_PTR style=GetWindowLongPtrW(monitor,GWL_EXSTYLE);
        SetWindowLongPtrW(monitor,GWL_EXSTYLE,config.clickThrough?(style|WS_EX_TRANSPARENT):(style&~WS_EX_TRANSPARENT));
        RECT original{};GetWindowRect(monitor,&original);PositionMonitor();
        SetWindowPos(monitor,nullptr,original.left,original.top,0,0,SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE);
        SetLayeredWindowAttributes(monitor,0,alpha,LWA_ALPHA);
        shown=data->visible;ShowWindow(monitor,shown?SW_SHOWNOACTIVATE:SW_HIDE);InvalidateRect(monitor,nullptr,FALSE);
        return 0;
    }
    if(message==updateMessage+1) {
        const auto* data=reinterpret_cast<const TaskbarUpdate*>(lparam);
        if(data->mode==0) {RestoreTaskbars();return 0;}
        transparencyMode=data->mode;taskbarAlpha=data->opacity;return ApplyTaskbars();
    }
    if(message==WM_DISPLAYCHANGE || message==WM_SETTINGCHANGE) {
        PositionMonitor();if(transparencyMode) ApplyTaskbars();return 0;
    }
    if(message==WM_CLOSE) {DestroyWindow(window);return 0;}
    if(message==WM_DESTROY) {
        ClariPickerShutdown();
        for(int id=10;id<=17;id++)UnregisterHotKey(window,id);
        RestoreTaskbars();NOTIFYICONDATAW data{sizeof(data)};data.hWnd=window;data.uID=iconId;Shell_NotifyIconW(NIM_DELETE,&data);
        if(monitor) DestroyWindow(monitor);monitor=nullptr;PostQuitMessage(0);return 0;
    }
    return DefWindowProcW(window,message,wparam,lparam);
}
}

HWND ClariHostWindow(){return host;}

extern "C" __declspec(dllexport) int __stdcall CB_SetToolHotkey(int slot,unsigned modifiers,unsigned key) {
    if(!host)return ERROR_INVALID_WINDOW_HANDLE;
    ToolHotkeyUpdate data{slot,modifiers,key};
    return static_cast<int>(SendMessageW(host,toolHotkeyMessage,0,reinterpret_cast<LPARAM>(&data)));
}

extern "C" __declspec(dllexport) int __stdcall CB_Start(Callback handler) {
    if(running.exchange(true)) return 1;
    callback=handler;ready=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    worker=std::thread([] {
        HINSTANCE instance=GetModuleHandleW(nullptr);
        WNDCLASSW hostClass{};hostClass.lpfnWndProc=HostProc;hostClass.hInstance=instance;hostClass.lpszClassName=L"ClariBar.Host";
        RegisterClassW(&hostClass);
        WNDCLASSW monitorClass{};monitorClass.lpfnWndProc=MonitorProc;monitorClass.hInstance=instance;
        monitorClass.lpszClassName=L"ClariBar.Monitor";monitorClass.hCursor=LoadCursorW(nullptr,IDC_ARROW);monitorClass.style=CS_DBLCLKS;
        RegisterClassW(&monitorClass);
        taskbarCreated=RegisterWindowMessageW(L"TaskbarCreated");
        showRequested=RegisterWindowMessageW(L"ClariBar.ShowSettings");
        quitRequested=RegisterWindowMessageW(L"ClariBar.Exit");
        desktopCommandRequested=RegisterWindowMessageW(L"ClariBar.DesktopCommand");
        host=CreateWindowExW(WS_EX_TOOLWINDOW,L"ClariBar.Host",L"ClariBar",WS_POPUP,0,0,0,0,nullptr,nullptr,instance,nullptr);
        monitor=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_LAYERED|WS_EX_TOPMOST|WS_EX_TRANSPARENT,L"ClariBar.Monitor",L"澄栏实时监控",WS_POPUP,0,0,680,42,nullptr,nullptr,instance,nullptr);
        int dpi=monitor?static_cast<int>(GetDpiForWindow(monitor)):96;
        font=CreateFontW(-MulDiv(12,dpi,72),0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");
        icon=MakeIcon();if(host){AddTray();SetTimer(host,1,1000,nullptr);}SetEvent(ready);
        if(host && monitor) {MSG msg{};while(GetMessageW(&msg,nullptr,0,0)>0) {TranslateMessage(&msg);DispatchMessageW(&msg);}}
        if(font) DeleteObject(font);if(icon) DestroyIcon(icon);host=nullptr;running=false;
    });
    if(WaitForSingleObject(ready,10000)!=WAIT_OBJECT_0) return 0;
    CloseHandle(ready);ready=nullptr;return host && monitor?1:0;
}
extern "C" __declspec(dllexport) void __stdcall CB_SetMonitor(const wchar_t* text,unsigned fg,unsigned bg,int opacity,int visible) {
    if(!host) return;
    MonitorUpdate data{text,fg,bg,static_cast<BYTE>(std::clamp(opacity,30,100)*255/100),visible!=0};
    SendMessageW(host,updateMessage,0,reinterpret_cast<LPARAM>(&data));
}
extern "C" __declspec(dllexport) int __stdcall CB_SetTaskbar(int mode,int opacity) {
    if(!host) return -1;
    TaskbarUpdate data{mode,static_cast<BYTE>(std::clamp(opacity,0,255))};
    return static_cast<int>(SendMessageW(host,updateMessage+1,0,reinterpret_cast<LPARAM>(&data)));
}
extern "C" __declspec(dllexport) void __stdcall CB_Stop() {
    ClariBridgeStop();
    if(host) SendMessageW(host,WM_CLOSE,0,0);
    if(worker.joinable()) worker.join();callback=nullptr;
}

extern "C" __declspec(dllexport) int __stdcall CB_EnumFonts(wchar_t* output,int capacity) {
    if(!output || capacity<2)return 0;
    HDC dc=GetDC(nullptr);LOGFONTW query{};query.lfCharSet=DEFAULT_CHARSET;
    std::set<std::wstring> names;EnumFontFamiliesExW(dc,&query,CollectFont,reinterpret_cast<LPARAM>(&names),0);ReleaseDC(nullptr,dc);
    std::wstring all;for(auto& name:names)all+=name+L"\n";
    wcsncpy_s(output,capacity,all.c_str(),_TRUNCATE);return static_cast<int>(wcslen(output));
}

extern "C" __declspec(dllexport) int __stdcall CB_MeasureTextInk(const wchar_t* text,const wchar_t* family,double size,unsigned weight,claribar::TextInk* ink) {
    if(!ink || !std::isfinite(size) || size<1 || size>256)return 0;
    return claribar::MeasureTextInk(text,family,size,weight,*ink)?1:0;
}
extern "C" __declspec(dllexport) double __stdcall CB_TaskbarHeight() {
    auto previous=SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    HWND taskbar=FindWindowW(L"Shell_TrayWnd",nullptr);RECT rect{};double height=48;
    if(taskbar && GetWindowRect(taskbar,&rect))height=(rect.bottom-rect.top)*96./std::max(GetDpiForWindow(taskbar),1u);
    if(previous)SetThreadDpiAwarenessContext(previous);
    return height;
}

extern "C" __declspec(dllexport) void __stdcall CB_SetLanguage(int useChinese) {
    chinese=useChinese!=0;if(host)PostMessageW(host,languageMessage,0,0);
}
