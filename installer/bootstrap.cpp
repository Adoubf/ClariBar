#include <windows.h>
#include <objbase.h>
#include <filesystem>
#include <string>

namespace {
int Failure(const wchar_t* detail,DWORD code) {
    std::wstring message=std::wstring(detail)+L"\n错误代码："+std::to_wstring(code);
    MessageBoxW(nullptr,message.c_str(),L"澄栏安装程序",MB_OK|MB_ICONERROR);return static_cast<int>(code?code:1);
}
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,PWSTR arguments,int) {
    std::filesystem::path directory;
    try {
        // Set the child installer environment before its self-extraction starts.
        // Do not modify the user's or machine's persistent environment variables.
        wchar_t temporary[32768]{};
        DWORD length=GetTempPathW(32768,temporary);
        if(!length || length>=32768)return Failure(L"无法确定安装缓存目录。",ERROR_PATH_NOT_FOUND);
        auto root=std::filesystem::path(temporary)/L"ClariBar-installers";
        GUID nonce{};if(FAILED(CoCreateGuid(&nonce)))return Failure(L"无法创建安装会话。",ERROR_GEN_FAILURE);
        wchar_t id[40]{};StringFromGUID2(nonce,id,40);directory=root/id;
        std::filesystem::create_directories(directory);
        SetEnvironmentVariableW(L"TEMP",directory.c_str());SetEnvironmentVariableW(L"TMP",directory.c_str());
        HRSRC resource=FindResourceW(instance,MAKEINTRESOURCEW(101),RT_RCDATA);
        if(!resource)return Failure(L"缺少安装数据。",GetLastError());
        auto bytes=SizeofResource(instance,resource);auto data=LockResource(LoadResource(instance,resource));
        auto setup=directory/L"ClariBar-Installer.exe";
        HANDLE file=CreateFileW(setup.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
        if(file==INVALID_HANDLE_VALUE)return Failure(L"无法写入安装缓存。",GetLastError());
        DWORD written{};bool complete=WriteFile(file,data,bytes,&written,nullptr)&&written==bytes;CloseHandle(file);
        if(!complete)return Failure(L"安装数据写入不完整。",GetLastError());
        std::wstring command=L"\""+setup.wstring()+L"\" "+arguments;
        STARTUPINFOW startup{sizeof(startup)};PROCESS_INFORMATION process{};
        if(!CreateProcessW(setup.c_str(),command.data(),nullptr,nullptr,FALSE,0,nullptr,directory.c_str(),&startup,&process))
            return Failure(L"无法启动安装向导。",GetLastError());
        CloseHandle(process.hThread);WaitForSingleObject(process.hProcess,INFINITE);
        DWORD result{};GetExitCodeProcess(process.hProcess,&result);CloseHandle(process.hProcess);
        std::error_code ignored;std::filesystem::remove_all(directory,ignored);
        return static_cast<int>(result);
    }catch(const std::exception&) {
        return Failure(L"安装缓存目录无法访问，请检查磁盘空间和目录权限。",ERROR_ACCESS_DENIED);
    }
}
