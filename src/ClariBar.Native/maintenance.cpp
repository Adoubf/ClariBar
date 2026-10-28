#include <windows.h>
#include <tlhelp32.h>
#include <string>

// Read-only process inspection for Setup/Uninstall. Match complete executable
// paths so an installation never waits on or stops an unrelated application.
extern "C" __declspec(dllexport) int __stdcall CB_CountApplicationProcesses(const wchar_t* directory) {
    if(!directory || !*directory)return -1;
    std::wstring root=directory;
    while(!root.empty() && (root.back()==L'\\' || root.back()==L'/'))root.pop_back();
    std::wstring ui=root+L"\\ClariBar.exe",broker=root+L"\\broker\\ClariBar.SensorBroker.exe";
    HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0);
    if(snapshot==INVALID_HANDLE_VALUE)return -1;
    int count=0;PROCESSENTRY32W entry{};entry.dwSize=sizeof(entry);
    if(Process32FirstW(snapshot,&entry))do {
        if(_wcsicmp(entry.szExeFile,L"ClariBar.exe") && _wcsicmp(entry.szExeFile,L"ClariBar.SensorBroker.exe"))continue;
        HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,entry.th32ProcessID);
        if(!process)continue;
        wchar_t path[32768]{};DWORD size=32768;
        if(QueryFullProcessImageNameW(process,0,path,&size) && (!_wcsicmp(path,ui.c_str()) || !_wcsicmp(path,broker.c_str())))count++;
        CloseHandle(process);
    }while(Process32NextW(snapshot,&entry));
    CloseHandle(snapshot);return count;
}
