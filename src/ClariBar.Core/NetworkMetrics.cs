namespace ClariBar.Core;

public sealed record NetworkAdapterSample(string Id,uint Index,string Name,string Description,bool Physical,bool Up,ulong Received,ulong Sent);
public sealed record NetworkDefaultRoute(uint Index,uint Family,uint RouteMetric,uint InterfaceMetric);
public sealed record NetworkTopology(int Version,ulong Tick,NetworkAdapterSample[] Adapters,NetworkDefaultRoute[] Routes);
public sealed record SelectedNetworkRoute(NetworkAdapterSample Adapter,NetworkDefaultRoute Route);
public sealed record NetworkRouteInfo(string Status,string InterfaceId,uint InterfaceIndex,string Name,string Description,uint Family,
    uint RouteMetric,uint InterfaceMetric,int ExcludedAdapters,string Message);
public sealed record TrafficRates(float? Upload,float? Download,string Status);

public static class NetworkMetrics
{
    public const string UploadId="/claribar/network/default/upload",DownloadId="/claribar/network/default/download";
    public static SelectedNetworkRoute? Select(NetworkTopology topology) =>
        (from route in topology.Routes
         join adapter in topology.Adapters on route.Index equals adapter.Index
         where adapter.Physical && adapter.Up
         orderby route.Family==2?0:1,(ulong)route.RouteMetric+route.InterfaceMetric,adapter.Index
         select new SelectedNetworkRoute(adapter,route)).FirstOrDefault();

    public static bool Migrate(AppSettings settings)
    {
        bool changed=false;
        settings.PinnedSensors=settings.PinnedSensors.Select(id=>
        {
            string replacement=id.StartsWith("/nic/",StringComparison.OrdinalIgnoreCase)?
                id.EndsWith("/throughput/7")?UploadId:id.EndsWith("/throughput/8")?DownloadId:id:id;
            if(replacement!=id)
            {
                changed=true;
                if(settings.SensorColors.TryGetValue(id,out string? color))settings.SensorColors.TryAdd(replacement,color);
                if(settings.SensorNames.TryGetValue(id,out string? name))settings.SensorNames.TryAdd(replacement,name);
            }
            return replacement;
        }).Distinct().ToArray();
        return changed;
    }
}

public sealed class TrafficRateTracker
{
    private string? identity;
    private ulong tick,received,sent;
    public void Reset(){identity=null;tick=received=sent=0;}
    public TrafficRates Observe(string adapterId,ulong now,ulong rx,ulong tx)
    {
        TrafficRates result=new(null,null,"warming");
        if(identity==adapterId && now>tick && rx>=received && tx>=sent)
        {
            double seconds=(now-tick)/1000d;
            result=new(Wire.Finite((float)((tx-sent)/seconds)),Wire.Finite((float)((rx-received)/seconds)),"valid");
        }
        identity=adapterId;tick=now;received=rx;sent=tx;return result;
    }
}
