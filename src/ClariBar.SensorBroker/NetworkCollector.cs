using L = ClariBar.Core.Localizer;
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
        var topology=JsonSerializer.Deserialize<NetworkTopology>(text.ToString(),Wire.Json)??throw new IOException(L.T("Empty network uplink data."));
        if(topology.Version!=1)throw new IOException(L.T("Network uplink protocol mismatch."));
        var chosen=NetworkMetrics.Select(topology);int excluded=topology.Adapters.Count(adapter=>!adapter.Physical);
        string name=L.T("Default physical uplink");TrafficRates rates;
        if(chosen is null)
        {
            tracker.Reset();rates=new(null,null,"offline");
            Route=new("offline","",0,name,"",0,0,0,excluded,L.T("No connected physical default uplink was found."));
        }
        else
        {
            var adapter=chosen.Adapter;var route=chosen.Route;name=adapter.Name;
            rates=tracker.Observe(adapter.Id,topology.Tick,adapter.Received,adapter.Sent);
            Route=new(rates.Status,adapter.Id,adapter.Index,adapter.Name,adapter.Description,route.Family,route.RouteMetric,route.InterfaceMetric,excluded,
                L.T("Uses the default physical route and interface metrics. Counts all traffic through that interface without adding virtual adapter counters."));
        }
        return new[]
        {
            new SensorReading(NetworkMetrics.UploadId,"/claribar/network/default",name,"Network","Upload Speed","Throughput",rates.Upload,"B/s",rates.Status),
            new SensorReading(NetworkMetrics.DownloadId,"/claribar/network/default",name,"Network","Download Speed","Throughput",rates.Download,"B/s",rates.Status)
        };
    }
}
