#include <windows.h>
#include <dbghelp.h>
#include <atomic>
#include <cwchar>

namespace {
wchar_t dumpPath[32768]{};
HANDLE requested{},completed{},writerThread{};
EXCEPTION_POINTERS* fault{};
DWORD faultThread{};
std::atomic<bool> writing{};
DWORD dumpResult{};
LPTOP_LEVEL_EXCEPTION_FILTER previousFilter{};

DWORD WINAPI WriteDump(void*) {
    while(WaitForSingleObject(requested,INFINITE)==WAIT_OBJECT_0) {
        HANDLE file=CreateFileW(dumpPath,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
        dumpResult=GetLastError();
        if(file!=INVALID_HANDLE_VALUE) {
            MINIDUMP_EXCEPTION_INFORMATION exception{faultThread,fault,FALSE};
            auto flags=static_cast<MINIDUMP_TYPE>(MiniDumpWithThreadInfo|MiniDumpWithUnloadedModules|MiniDumpWithIndirectlyReferencedMemory);
            dumpResult=MiniDumpWriteDump(GetCurrentProcess(),GetCurrentProcessId(),file,flags,fault?&exception:nullptr,nullptr,nullptr)?0:GetLastError();
            CloseHandle(file);
        }
        SetEvent(completed);
    }
    return 0;
}
DWORD Capture(EXCEPTION_POINTERS* error) {
    if(!writerThread || writing.exchange(true))return ERROR_BUSY;
    fault=error;faultThread=GetCurrentThreadId();ResetEvent(completed);SetEvent(requested);
    DWORD wait=WaitForSingleObject(completed,10000);
    // A fatal exception continues to Windows after saving the local evidence.
    if(wait!=WAIT_OBJECT_0)return ERROR_TIMEOUT;
    fault=nullptr;writing=false;return dumpResult;
}
LONG CALLBACK Observe(EXCEPTION_POINTERS* error) {
    if(error && error->ExceptionRecord && error->ExceptionRecord->ExceptionCode==0xC000027B)Capture(error);
    return EXCEPTION_CONTINUE_SEARCH;
}
LONG WINAPI Unhandled(EXCEPTION_POINTERS* error) {
    Capture(error);
    return previousFilter?previousFilter(error):EXCEPTION_CONTINUE_SEARCH;
}
}
extern "C" __declspec(dllexport) int __stdcall CB_EnableCrashDiagnostics(const wchar_t* directory) {
    if(writerThread)return 0;
    if(!directory || !*directory)return ERROR_INVALID_PARAMETER;
    SYSTEMTIME now{};GetSystemTime(&now);
    if(swprintf_s(dumpPath,L"%s\\ClariBar-crash-%04u%02u%02u-%02u%02u%02u-%lu.dmp",directory,now.wYear,now.wMonth,now.wDay,now.wHour,now.wMinute,now.wSecond,GetCurrentProcessId())<0)return ERROR_FILENAME_EXCED_RANGE;
    requested=CreateEventW(nullptr,FALSE,FALSE,nullptr);completed=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    if(!requested || !completed)return static_cast<int>(GetLastError());
    writerThread=CreateThread(nullptr,0,WriteDump,nullptr,0,nullptr);if(!writerThread)return static_cast<int>(GetLastError());
    AddVectoredExceptionHandler(1,Observe);previousFilter=SetUnhandledExceptionFilter(Unhandled);return 0;
}
extern "C" __declspec(dllexport) int __stdcall CB_WriteDiagnosticDump(){return static_cast<int>(Capture(nullptr));}
