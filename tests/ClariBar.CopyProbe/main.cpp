#include <windows.h>
#include <shlobj.h>
#include <algorithm>
#include <cstring>
#include <string>
#include <vector>

namespace {
HWND editor{};
WNDPROC editProc{};
bool delayed{};
std::wstring delayedText;
constexpr UINT copyMenuCommand=40001,copyButtonCommand=40002;
constexpr UINT_PTR autoMenuTimer=0x43424D;
bool autoMenu{};unsigned menuCopies{},menusOpened{},renderDelay{};
constexpr ULONG_PTR dismissInputTag=0x43425050;
HHOOK dismissGuard{};unsigned guardedInputs{};
LRESULT CALLBACK DismissGuard(int code,WPARAM message,LPARAM data) {
    if(code==HC_ACTION && reinterpret_cast<const MSLLHOOKSTRUCT*>(data)->dwExtraInfo==dismissInputTag) {
        ++guardedInputs;return 1;
    }
    return CallNextHookEx(dismissGuard,code,message,data);
}
bool Put(UINT format,const void* data,SIZE_T bytes) {
    auto memory=GlobalAlloc(GMEM_MOVEABLE,bytes);if(!memory)return false;
    auto address=GlobalLock(memory);if(!address){GlobalFree(memory);return false;}
    std::memcpy(address,data,bytes);GlobalUnlock(memory);
    if(SetClipboardData(format,memory))return true;GlobalFree(memory);return false;
}
std::wstring Selection(HWND window) {
    int length=GetWindowTextLengthW(window);std::wstring text(length+1,L'\0');GetWindowTextW(window,text.data(),length+1);text.resize(length);
    DWORD start{},end{};SendMessageW(window,EM_GETSEL,reinterpret_cast<WPARAM>(&start),reinterpret_cast<LPARAM>(&end));
    start=std::min(start,static_cast<DWORD>(length));end=std::clamp(end,start,static_cast<DWORD>(length));return text.substr(start,end-start);
}
LRESULT CALLBACK EditorProc(HWND window,UINT message,WPARAM wparam,LPARAM lparam) {
    if(message==WM_CONTEXTMENU) {
        RECT bounds{};GetWindowRect(window,&bounds);auto menu=CreatePopupMenu();AppendMenuW(menu,MF_STRING,copyMenuCommand,L"Copy");
        if(autoMenu)SetTimer(GetParent(window),autoMenuTimer,40,nullptr);
        TrackPopupMenu(menu,TPM_LEFTALIGN|TPM_RIGHTBUTTON,bounds.left+30,bounds.top+30,0,GetParent(window),nullptr);
        DestroyMenu(menu);autoMenu=false;return 0;
    }
    if(delayed && (message==WM_COPY || message==WM_KEYDOWN && wparam=='C' && (GetKeyState(VK_CONTROL)&0x8000))) {
        delayedText=Selection(window);
        if(OpenClipboard(GetParent(window))){EmptyClipboard();SetClipboardData(CF_UNICODETEXT,nullptr);CloseClipboard();}
        return 0;
    }
    return CallWindowProcW(editProc,window,message,wparam,lparam);
}
LRESULT CALLBACK WindowProc(HWND window,UINT message,WPARAM wparam,LPARAM lparam) {
    if(message==WM_CREATE) {
        editor=CreateWindowExW(WS_EX_CLIENTEDGE,L"EDIT",L"Copy translation integration test.",
            WS_CHILD|WS_VISIBLE|ES_MULTILINE|ES_READONLY,12,12,450,210,window,nullptr,GetModuleHandleW(nullptr),nullptr);
        editProc=reinterpret_cast<WNDPROC>(SetWindowLongPtrW(editor,GWLP_WNDPROC,reinterpret_cast<LONG_PTR>(EditorProc)));
        CreateWindowExW(0,L"BUTTON",L"Copy",WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON,12,232,110,28,window,reinterpret_cast<HMENU>(static_cast<INT_PTR>(copyButtonCommand)),GetModuleHandleW(nullptr),nullptr);
        return 0;
    }
    if(message==WM_APP+2){delayed=wparam!=0;return 0;}
    if(message==WM_APP+3){autoMenu=true;PostMessageW(editor,WM_CONTEXTMENU,reinterpret_cast<WPARAM>(editor),-1);return 0;}
    if(message==WM_APP+4)return menuCopies;
    if(message==WM_APP+7)return menusOpened;
    // Install before the app's observer: tagged test clicks reach that observer
    // and this guard, then stop here without clicking another desktop window.
    if(message==WM_APP+8) {
        if(dismissGuard)UnhookWindowsHookEx(dismissGuard);dismissGuard=nullptr;guardedInputs=0;
        if(wparam)dismissGuard=SetWindowsHookExW(WH_MOUSE_LL,DismissGuard,GetModuleHandleW(nullptr),0);
        return !wparam || dismissGuard?1:0;
    }
    if(message==WM_APP+9) {
        if(!dismissGuard || (wparam!=1 && wparam!=2))return 0;
        INPUT inputs[2]{};for(auto& input:inputs){input.type=INPUT_MOUSE;input.mi.dwExtraInfo=dismissInputTag;}
        inputs[0].mi.dwFlags=wparam==1?MOUSEEVENTF_LEFTDOWN:MOUSEEVENTF_RIGHTDOWN;
        inputs[1].mi.dwFlags=wparam==1?MOUSEEVENTF_LEFTUP:MOUSEEVENTF_RIGHTUP;
        return SendInput(2,inputs,sizeof(INPUT));
    }
    if(message==WM_APP+10)return guardedInputs;
    if(message==WM_ENTERMENULOOP){++menusOpened;return 0;}
    if(message==WM_APP+6){renderDelay=std::min(1000u,static_cast<unsigned>(wparam));return 0;}
    if(message==WM_TIMER && wparam==autoMenuTimer){KillTimer(window,autoMenuTimer);EndMenu();PostMessageW(window,WM_COMMAND,copyMenuCommand,0);return 0;}
    if(message==WM_COMMAND && (LOWORD(wparam)==copyMenuCommand || LOWORD(wparam)==copyButtonCommand)) {
        if(LOWORD(wparam)==copyMenuCommand)++menuCopies;SendMessageW(editor,WM_COPY,0,0);return 0;
    }
    if(message==WM_APP+5) {
        if(!OpenClipboard(window))return 0;EmptyClipboard();
        auto text=Selection(editor);
        if(wparam!=3)Put(CF_UNICODETEXT,text.c_str(),(text.size()+1)*sizeof(wchar_t));
        if(wparam==1){DWORD excluded=1;Put(RegisterClipboardFormatW(L"ExcludeClipboardContentFromMonitorProcessing"),&excluded,sizeof(excluded));}
        if(wparam==2){
            const wchar_t paths[]=L"E:\\ClariBar-copy-test.txt\0";std::vector<BYTE> data(sizeof(DROPFILES)+sizeof(paths));
            auto* drop=reinterpret_cast<DROPFILES*>(data.data());drop->pFiles=sizeof(DROPFILES);drop->fWide=TRUE;std::memcpy(data.data()+sizeof(DROPFILES),paths,sizeof(paths));Put(CF_HDROP,data.data(),data.size());
        }
        if(wparam==3){
            struct Image {BITMAPINFOHEADER header;DWORD pixel;} image{};image.header.biSize=sizeof(BITMAPINFOHEADER);image.header.biWidth=image.header.biHeight=1;image.header.biPlanes=1;image.header.biBitCount=32;image.pixel=0x003399AA;Put(CF_DIB,&image,sizeof(image));
        }
        CloseClipboard();return 1;
    }
    if(message==WM_RENDERFORMAT && wparam==CF_UNICODETEXT) {
        Put(CF_UNICODETEXT,delayedText.c_str(),(delayedText.size()+1)*sizeof(wchar_t));if(renderDelay)Sleep(renderDelay);
        return 0;
    }
    if(message==WM_APP+1 || (message==WM_ACTIVATE && LOWORD(wparam)!=WA_INACTIVE)) {
        SetFocus(editor);SendMessageW(editor,EM_SETSEL,0,-1);return 0;
    }
    if(message==WM_DESTROY){if(dismissGuard)UnhookWindowsHookEx(dismissGuard);PostQuitMessage(0);return 0;}
    return DefWindowProcW(window,message,wparam,lparam);
}
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,LPWSTR title,int) {
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    WNDCLASSW type{};type.hInstance=instance;type.lpfnWndProc=WindowProc;type.lpszClassName=L"ClariBar.CopyProbe";type.hCursor=LoadCursorW(nullptr,IDC_ARROW);RegisterClassW(&type);
    HWND window=CreateWindowExW(0,type.lpszClassName,title,WS_OVERLAPPEDWINDOW,200,200,490,320,nullptr,nullptr,instance,nullptr);
    if(!window)return 1;
    ShowWindow(window,SW_SHOW);UpdateWindow(window);SetFocus(editor);SendMessageW(editor,EM_SETSEL,0,-1);
    MSG message{};while(GetMessageW(&message,nullptr,0,0)>0){TranslateMessage(&message);DispatchMessageW(&message);}
    return 0;
}
