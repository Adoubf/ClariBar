using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ClariBar.Core;

namespace ClariBar.SensorBroker;

internal sealed class NetworkCollector
{
    private readonly TrafficRateTracker tracker=new();
    [DllImport("ClariBar.SystemMetrics.dll",CallingConvention=CallingConvention.StdCall,CharSet=CharSet.Unicode)]
    private static extern int CB_ReadNetworkSnapshot(StringBuilder buffer,int capacity);
    public NetworkRouteInfo? Route {get;private set;}
    public IEnumerable<SensorReading> Read()
    {
        Route=null;
        var text=new StringBuilder(131072);int result=CB_ReadNetworkSnapshot(text,text.Capacity);
        if(result<0)Marshal.ThrowExceptionForHR(result);
        var topology=JsonSerializer.Deserialize<NetworkTopology>(text.ToString(),Wire.Json)??throw new IOException("出口网卡数据为空。");
        if(topology.Version!=1)throw new IOException("出口网卡协议不匹配。");
        var chosen=NetworkMetrics.Select(topology);int excluded=topology.Adapters.Count(adapter=>!adapter.Physical);
        string name="默认物理出口";TrafficRates rates;
        if(chosen is null)
        {
            tracker.Reset();rates=new(null,null,"offline");
            Route=new("offline","",0,name,"",0,0,0,excluded,"未找到已连接的物理默认出口。");
        }
        else
        {
            var adapter=chosen.Adapter;var route=chosen.Route;name=adapter.Name;
            rates=tracker.Observe(adapter.Id,topology.Tick,adapter.Received,adapter.Sent);
            Route=new(rates.Status,adapter.Id,adapter.Index,adapter.Name,adapter.Description,route.Family,route.RouteMetric,route.InterfaceMetric,excluded,
                "按默认路由和接口跃点数选择物理出口；统计该物理接口的全部上下行，不累加虚拟网卡。");
        }
        return new[]
        {
            new SensorReading(NetworkMetrics.UploadId,"/claribar/network/default",name,"Network","Upload Speed","Throughput",rates.Upload,"B/s",rates.Status),
            new SensorReading(NetworkMetrics.DownloadId,"/claribar/network/default",name,"Network","Download Speed","Throughput",rates.Download,"B/s",rates.Status)
        };
    }
}
