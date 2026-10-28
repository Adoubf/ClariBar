using L = ClariBar.Core.Localizer;
namespace ClariBar.Core;

public sealed record SensorCapability(string Key,string Label,string Status,string Explanation)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayLabel
    {
        get
        {
            string prefix=Label.StartsWith("CPU",StringComparison.Ordinal)?"CPU":Label.StartsWith("GPU",StringComparison.Ordinal)?"GPU":"";
            if(prefix.Length==0)return Localizer.T(Label);
            string kind=Key.EndsWith("/Temperature",StringComparison.Ordinal)?"Temperature":Key.EndsWith("/Clock",StringComparison.Ordinal)?"Frequency":"";
            return kind.Length==0?Localizer.T(Label):prefix+" "+Localizer.T(kind);
        }
    }
}

public static class SensorCatalog
{
    // Common means a device-level reading, never every core, engine, DIMM, or
    // vendor diagnostic channel of a common sensor type.
    public static SensorReading[] Common(SensorSnapshot snapshot)
    {
        var result=new List<SensorReading>();
        void Pick(IEnumerable<SensorReading> candidates,params string[] names)
        {
            var item=candidates.Where(sensor=>names.Contains(sensor.Name))
                .OrderBy(sensor=>sensor.IsValid?0:1).ThenBy(sensor=>Array.IndexOf(names,sensor.Name)).FirstOrDefault();
            if(item is not null)result.Add(item);
        }
        foreach(var cpu in snapshot.Sensors.Where(sensor=>sensor.HardwareType=="Cpu").GroupBy(sensor=>sensor.HardwareId))
        {
            Pick(cpu.Where(sensor=>sensor.Kind=="Load"),"CPU Total","CPU Utility");
            Pick(cpu.Where(sensor=>sensor.Kind=="Temperature"),"CPU Package","Core (Tctl/Tdie)","Core (Tdie)","CPU (Tctl/Tdie)","Core Average","Core Max");
        }
        foreach(var gpu in snapshot.Sensors.Where(sensor=>sensor.Category=="GPU").GroupBy(sensor=>sensor.HardwareId))
        {
            Pick(gpu.Where(sensor=>sensor.Kind=="Load"),"GPU Core","D3D 3D","GPU Total");
            Pick(gpu.Where(sensor=>sensor.Kind=="Temperature"),"GPU Core","GPU Temperature","GPU Hot Spot");
        }
        Pick(snapshot.Sensors.Where(sensor=>sensor.HardwareType=="Memory" && sensor.Kind=="Load" && sensor.HardwareId!="/vram" && !sensor.Hardware.Contains("Virtual",StringComparison.OrdinalIgnoreCase))
            .OrderBy(sensor=>sensor.HardwareId=="/ram"?0:1),"Memory");
        foreach(var disk in snapshot.Sensors.Where(sensor=>sensor.HardwareType=="Storage").GroupBy(sensor=>sensor.HardwareId))
        {
            Pick(disk.Where(sensor=>sensor.Kind=="Load"),"Total Activity");
            Pick(disk.Where(sensor=>sensor.Kind=="Throughput"),"Read Rate");
            Pick(disk.Where(sensor=>sensor.Kind=="Throughput"),"Write Rate");
        }
        result.AddRange(snapshot.Sensors.Where(sensor=>sensor.Id is NetworkMetrics.UploadId or NetworkMetrics.DownloadId));
        return result.DistinctBy(sensor=>sensor.Id).ToArray();
    }
    public static SensorReading[] Defaults(SensorSnapshot snapshot)
    {
        var common=Common(snapshot);
        return common.OrderBy(sensor=>sensor.HardwareType switch {"Network"=>0,"Cpu"=>1,"Memory"=>2,_=>sensor.Category=="GPU"?3:4})
            .Where(sensor=>sensor.IsValid || sensor.Status is "requires_admin" or "warming")
            .Where(sensor=>sensor.HardwareType!="Storage" || sensor.Kind=="Load").Take(8).ToArray();
    }
    public static SensorReading[] Filter(SensorSnapshot snapshot,string filter,IEnumerable<string> pinned,bool showUnavailable)
    {
        var selected=pinned.ToHashSet();
        IEnumerable<SensorReading> items=filter==L.T("Common metrics")?Common(snapshot):snapshot.Sensors;
        items=items.Where(sensor=>sensor.IsValid || sensor.Status=="warming" || showUnavailable ||
            (filter==L.T("Common metrics") && sensor.Status=="requires_admin"));
        return items.Where(sensor=>filter switch
        {
_ when filter == L.T("Temperatures only") =>sensor.Kind=="Temperature", _ when filter == L.T("Fans only") =>sensor.Kind=="Fan", _ when filter == L.T("Usage only") =>sensor.Kind=="Load",
_ when filter == L.T("Transfer rates only") =>sensor.Kind=="Throughput", _ when filter == L.T("Pinned") =>selected.Contains(sensor.Id), _=>true
        }).ToArray();
    }
    public static SensorCapability[] Capabilities(IEnumerable<SensorReading> sensors)
    {
        var result=new List<SensorCapability>();
        foreach(var device in sensors.Where(sensor=>sensor.HardwareType=="Cpu" || sensor.Category=="GPU").GroupBy(sensor=>sensor.HardwareId))
        {
            bool cpu=device.First().HardwareType=="Cpu";
            foreach(string kind in cpu?new[]{"Temperature","Clock"}:new[]{"Temperature"})
            {
                var matching=device.Where(sensor=>sensor.Kind==kind).ToArray();
                string status=matching.Any(sensor=>sensor.IsValid)?"valid":matching.Any(sensor=>sensor.Status=="requires_admin")?"requires_admin":matching.Length==0?"unsupported":"unavailable";
                string label=$"{(cpu?"CPU":"GPU")} {(kind=="Temperature"?L.T("Temperature"):L.T("Frequency"))}";
                string explanation=status switch
                {
                    "valid"=>L.T("Live hardware readings are available."), "requires_admin"=>L.T("Authorize the separate sensor process to access hardware."),
                    "unsupported"=>L.T("The hardware interface does not expose this reading."), _=>L.T("Hardware access was granted, but no valid reading was returned.")
                };
                result.Add(new(device.Key+"/"+kind,label,status,explanation));
            }
        }
        return result.ToArray();
    }
}
