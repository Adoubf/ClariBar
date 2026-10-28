#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <iphlpapi.h>
#include <netioapi.h>
#include <objbase.h>
#include <cstdint>
#include <string>

namespace {
std::wstring Json(const wchar_t* text) {
    std::wstring result=L"\"";
    for(auto p=text;*p;p++) {
        if(*p==L'"' || *p==L'\\'){result+=L'\\';result+=*p;}
        else if(*p<32){wchar_t code[8]{};swprintf_s(code,L"\\u%04X",static_cast<unsigned>(*p));result+=code;}
        else result+=*p;
    }
    return result+L"\"";
}
bool Physical(const MIB_IF_ROW2& row) {
    return row.InterfaceAndOperStatusFlags.HardwareInterface && row.InterfaceAndOperStatusFlags.ConnectorPresent &&
      !row.InterfaceAndOperStatusFlags.FilterInterface && (row.Type==IF_TYPE_ETHERNET_CSMACD || row.Type==IF_TYPE_IEEE80211);
}
struct Tables {
    MIB_IF_TABLE2* interfaces{};MIB_IPFORWARD_TABLE2* routes{};
    ~Tables(){if(interfaces)FreeMibTable(interfaces);if(routes)FreeMibTable(routes);}
};
}
// A separate library for the sensor broker: no UI, taskbar hooks, services, or
// network traffic. Both interface counters and the route table are read-only.
extern "C" __declspec(dllexport) int __stdcall CB_ReadNetworkSnapshot(wchar_t* buffer,int capacity) {
    if(!buffer || capacity<2)return E_INVALIDARG;buffer[0]=0;
    Tables tables;
    auto error=GetIfTable2(&tables.interfaces);if(error!=NO_ERROR)return HRESULT_FROM_WIN32(error);
    error=GetIpForwardTable2(AF_UNSPEC,&tables.routes);if(error!=NO_ERROR)return HRESULT_FROM_WIN32(error);
    std::wstring json=L"{\"version\":1,\"tick\":"+std::to_wstring(GetTickCount64())+L",\"adapters\":[";
    for(ULONG index=0;index<tables.interfaces->NumEntries;index++) {
        const auto& row=tables.interfaces->Table[index];wchar_t guid[40]{};StringFromGUID2(row.InterfaceGuid,guid,40);
        if(index)json+=L",";
        json+=L"{\"id\":"+Json(guid)+L",\"index\":"+std::to_wstring(row.InterfaceIndex)+L",\"name\":"+Json(row.Alias)+L",\"description\":"+Json(row.Description)+
          L",\"physical\":"+(Physical(row)?L"true":L"false")+L",\"up\":"+(row.OperStatus==IfOperStatusUp?L"true":L"false")+
          L",\"received\":"+std::to_wstring(row.InOctets)+L",\"sent\":"+std::to_wstring(row.OutOctets)+L"}";
    }
    json+=L"],\"routes\":[";bool first=true;
    for(ULONG index=0;index<tables.routes->NumEntries;index++) {
        const auto& route=tables.routes->Table[index];
        if(route.DestinationPrefix.PrefixLength!=0 || route.Loopback || route.ValidLifetime==0)continue;
        MIB_IPINTERFACE_ROW interfaceRow{};interfaceRow.Family=route.DestinationPrefix.Prefix.si_family;
        interfaceRow.InterfaceLuid=route.InterfaceLuid;interfaceRow.InterfaceIndex=route.InterfaceIndex;
        if(GetIpInterfaceEntry(&interfaceRow)!=NO_ERROR || !interfaceRow.Connected)continue;
        if(!first)json+=L",";first=false;
        json+=L"{\"index\":"+std::to_wstring(route.InterfaceIndex)+L",\"family\":"+std::to_wstring(interfaceRow.Family)+
          L",\"routeMetric\":"+std::to_wstring(route.Metric)+L",\"interfaceMetric\":"+std::to_wstring(interfaceRow.Metric)+L"}";
    }
    json+=L"]}";
    if(json.size()+1>static_cast<size_t>(capacity))return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
    wcscpy_s(buffer,capacity,json.c_str());return static_cast<int>(json.size());
}
