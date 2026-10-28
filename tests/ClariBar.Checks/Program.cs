using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClariBar.Core;

string cache = Environment.GetEnvironmentVariable("CLARIBAR_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClariBar");
if(args.FirstOrDefault()=="--runtime")
{
    var live=JsonSerializer.Deserialize<SensorSnapshot>(File.ReadAllText(Path.Combine(cache,"logs","claribar-current-sensors.json")),Wire.Json)!;
    var preferences=new SettingsStore(Path.Combine(cache,"data")).Load();
    var common=SensorCatalog.Common(live);
    var cpuTemp=common.First(sensor=>sensor.HardwareType=="Cpu" && sensor.Kind=="Temperature");
    var frequencies=live.Sensors.Where(sensor=>sensor.HardwareType=="Cpu" && sensor.Kind=="Clock" && (sensor.Name.StartsWith("P-Core") || sensor.Name.StartsWith("E-Core"))).ToArray();
    Require(live.IsFresh && live.Elevated && live.Errors.Length==0,"Fresh elevated hardware snapshot without collector errors");
    Require(cpuTemp.IsValid && frequencies.Length>0 && frequencies.All(sensor=>sensor.IsValid),"Actual CPU temperature and all enumerated core frequencies are readable");
    Require(common.All(sensor=>sensor.IsValid),"Every displayed common sensor has a valid reading");
    Require(common.Single(sensor=>sensor.HardwareType=="Memory").HardwareId=="/ram","Common memory uses physical RAM");
    Require(common.Any(sensor=>sensor.HardwareType=="Storage" && sensor.Name=="Total Activity") && common.Any(sensor=>sensor.Name=="Read Rate") && common.Any(sensor=>sensor.Name=="Write Rate"),"Physical disks expose active time and read/write rates");
    Require(live.Network is {Status:"valid",InterfaceIndex:>0} && live.Sensors.Count(sensor=>sensor.HardwareType=="Network")==2,"One default physical uplink and downlink");
    Require(preferences.HardwareAccessEnabled && !preferences.PinnedSensors.Any(id=>id.StartsWith("/nic/")) && preferences.PinnedSensors.Contains(NetworkMetrics.UploadId) && preferences.PinnedSensors.Contains(NetworkMetrics.DownloadId),"Hardware access preference remembered and legacy network pins migrated");
    var report=new {capturedAt=DateTimeOffset.Now,passed=true,elevated=live.Elevated,live.Network,cpuTemperature=cpuTemp.Value,readableCoreFrequencies=frequencies.Length,
        rawSensorCount=live.Sensors.Length,commonCount=common.Length,commonSensors=common,capabilities=live.Capabilities,
        diskCount=live.Sensors.Where(sensor=>sensor.HardwareType=="Storage").Select(sensor=>sensor.HardwareId).Distinct().Count(),
        rememberedHardwareAccess=preferences.HardwareAccessEnabled,pinned=preferences.PinnedSensors};
    File.WriteAllText(Path.Combine(cache,"logs","claribar-runtime-validation.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
    Console.WriteLine($"Runtime checks passed: CPU {cpuTemp.Value} °C, {frequencies.Length} core clocks, {common.Length} valid common metrics, physical interface {live.Network!.InterfaceIndex}.");
    return 0;
}
string root = Path.Combine(cache, "temp", "checks-" + Guid.NewGuid().ToString("N"));
var results = new List<object>();
Directory.CreateDirectory(root);
try
{
    var store = new SettingsStore(root);
    var settings = new AppSettings { IntervalMs = 1, MonitorOpacity = 200, MonitorColor = "broken",
        PinnedSensors = ["one", "one", "two"] };
    store.Save(settings);
    var loaded = store.Load();
    Require(loaded.IntervalMs == 500 && loaded.MonitorOpacity == 100 && loaded.MonitorColor == "#FFFFFF", "Settings normalization");
    Require(loaded.PinnedSensors.SequenceEqual(new[] { "one", "two" }), "Pinned sensor identities");
    Require(loaded.MonitorClickThrough && loaded.MonitorLayout=="TwoColumns", "Monitor defaults: two columns and mouse pass-through");
    loaded.SensorColors["fan/1"]="#55AAFF";loaded.SensorColors["fan/2"]="invalid";
    loaded.MonitorFontSize=200;loaded.MonitorWidth=-1;loaded.TimeColorEnabled=true;loaded.TimeColor="#AABBCC";
    store.Save(loaded);loaded=store.Load();
    Require(loaded.SensorColors.GetValueOrDefault("fan/1")=="#55AAFF" && !loaded.SensorColors.ContainsKey("fan/2"), "Per-metric colors persist and validate");
    Require(loaded.MonitorFontSize==24 && loaded.MonitorWidth==100 && loaded.TimeColorEnabled && loaded.TimeColor=="#AABBCC", "Taskbar options normalize and persist");
    Require(loaded.MonitorAutoWidth && loaded.MonitorFontFamily=="Microsoft YaHei UI" && !loaded.MonitorBold,"New and old settings use adaptive width and Chinese-capable font");
    loaded.MonitorFontFamily="Consolas";loaded.MonitorBold=true;loaded.MonitorAutoWidth=false;store.Save(loaded);loaded=store.Load();
    Require(loaded.MonitorFontFamily=="Consolas" && loaded.MonitorBold && !loaded.MonitorAutoWidth,"Font, weight and manual width mode persist");
    var memory=new SensorReading("memory","ram","DDR5","Memory","Memory","Load",20.86f,"%","valid");
    var gpu=memory with {Name="GPU Core",HardwareType="GpuIntel",Kind="Clock"};
    Require(memory.DisplayName=="内存占用" && SensorLabels.Compact(memory)=="内存" && SensorLabels.Compact(gpu)=="GPU 频率","Chinese metric display labels");
    Require(memory.Name=="Memory" && memory.Id=="memory","Localization preserves raw hardware identities");
    Require(SensorLabels.Name(memory with {Name="CPU Core #1 Thread #2",HardwareType="Cpu"})=="CPU 核心 1 线程 2占用","CPU core and thread labels are translated");
    loaded.SensorColors["unrelated"]="#123456";loaded.TimeColor="#123456";
    foreach(var scheme in ColorSchemes.Presets.Append(ColorSchemes.Random()))
    {
        ColorSchemes.Apply(loaded,new[]{"memory","gpu"},scheme,ColorScope.Monitor);
        Require(loaded.SensorColors["memory"]==scheme.Colors[0] && loaded.SensorColors["gpu"]==scheme.Colors[1],"Palette applies to each metric");
        Require(loaded.TimeColor=="#123456" && loaded.SensorColors["unrelated"]=="#123456","Monitor-only palette preserves system and unrelated colors");
        Require(scheme.Colors.Distinct().Count()==8 && scheme.Colors.All(AppSettings.ValidColor),"Random and preset palettes have eight valid distinct colors");
    }
    ColorSchemes.Apply(loaded,new[]{"memory"},ColorSchemes.Presets[2],ColorScope.All);store.Save(loaded);loaded=store.Load();
    Require(loaded.TimeColorEnabled && loaded.NetworkColorEnabled && loaded.InputColorEnabled && loaded.TimeColor==ColorSchemes.Presets[2].Colors[0],"All-text palette enables and persists system colors");
    results.Add(new {name="localization-palettes-typography-and-width",passed=true});
    File.WriteAllText(store.FilePath, "{invalid");
    store.Load();
    Require(store.LoadWarning is not null && File.ReadAllText(store.FilePath) == "{invalid", "Corrupt settings preserved");
    results.Add(new { name = "settings-recovery", passed = true });

    Require(Wire.Finite(float.NaN) is null && Wire.Finite(float.PositiveInfinity) is null, "Invalid readings are unavailable");
    var unavailable = new SensorReading("x", "d", "device", "Cpu", "temperature", "Temperature", null, "°C", "unavailable");
    Require(SensorFormatting.Value(unavailable) == "—", "Unavailable temperature must not display zero");
    var stopped = unavailable with { Kind = "Fan", Unit = "RPM", Status = "valid", Value = 0 };
    Require(SensorFormatting.Value(stopped).Contains('0'), "Stopped fan is a valid zero");
    Require(SensorFormatting.Value(stopped, false) == "—", "Stale samples must not display old values");
    results.Add(new { name = "reading-validity", passed = true });

    var virtualAdapter=new NetworkAdapterSample("vm",7,"vEthernet","Hyper-V",false,true,100,100);
    var ethernet=new NetworkAdapterSample("real",19,"以太网","Realtek",true,true,1000,2000);
    var wifi=new NetworkAdapterSample("wifi",21,"无线网络","Wireless",true,true,0,0);
    var topology=new NetworkTopology(1,1000,[virtualAdapter,ethernet,wifi],[new(7,2,0,1),new(19,2,10,5),new(21,2,0,50)]);
    Require(NetworkMetrics.Select(topology)?.Adapter.Id=="real","Default physical route uses route plus interface metric and ignores virtual interfaces");
    Require(NetworkMetrics.Select(topology with {Routes=[new(19,23,0,10)]})?.Adapter.Id=="real","IPv6 default route works when IPv4 is absent");
    Require(NetworkMetrics.Select(topology with {Routes=[new(7,2,0,1)]}) is null,"No physical default route never falls back to a virtual adapter");
    var tracker=new TrafficRateTracker();
    Require(tracker.Observe("real",1000,1000,2000).Status=="warming","First network counter sample is a baseline");
    var rate=tracker.Observe("real",2000,3048,3024);
    Require(rate.Upload==1024 && rate.Download==2048,"Network rates use delta bytes and elapsed time");
    Require(tracker.Observe("real",3000,3048,3024) is {Upload:0,Download:0,Status:"valid"},"Idle physical adapter remains a valid zero rate");
    Require(tracker.Observe("real",4000,10,10).Status=="warming" && tracker.Observe("wifi",5000,100000,100000).Status=="warming","Counter reset and route switch do not create traffic spikes");
    var legacy=new AppSettings {PinnedSensors=["/nic/VM/throughput/7","/nic/VM/throughput/8","cpu"],SensorColors=new(){["/nic/VM/throughput/7"]="#AABBCC"}};
    Require(NetworkMetrics.Migrate(legacy) && legacy.PinnedSensors.SequenceEqual(new[]{NetworkMetrics.UploadId,NetworkMetrics.DownloadId,"cpu"}) && legacy.SensorColors[NetworkMetrics.UploadId]=="#AABBCC","Legacy fixed NIC pins migrate without losing colors");
    Require(!NetworkMetrics.Migrate(legacy),"Network migration is idempotent");
    var cpuTotal=memory with {Id="cpu",HardwareType="Cpu",HardwareId="cpu",Name="CPU Total"};
    var cpuThread=cpuTotal with {Id="thread",Name="CPU Core #1 Thread #1"};
    var cpuClock=cpuTotal with {Id="clock",Name="P-Core #1",Kind="Clock",Value=null,Status="requires_admin"};
    var cpuTemp=cpuTotal with {Id="temp",Name="CPU Package",Kind="Temperature",Value=null,Status="requires_admin"};
    var networkUp=memory with {Id=NetworkMetrics.UploadId,HardwareType="Network",Name="Upload Speed",Kind="Throughput"};
    var fixture=new SensorSnapshot(1,DateTimeOffset.Now,false,"ready",[cpuTotal,cpuThread,cpuClock,cpuTemp,memory with {Id="virtual-memory",HardwareId="/vram",Hardware="Virtual Memory"},memory,gpu with {Id="gpu",HardwareId="gpu",Name="D3D 3D",Kind="Load"},gpu with {Id="gpu-copy",HardwareId="gpu",Name="D3D Copy",Kind="Load"},networkUp],[]);
    var common=SensorCatalog.Common(fixture);
    Require(common.Single(sensor=>sensor.HardwareType=="Memory").Id=="memory","Common memory occupancy uses physical RAM, not virtual memory");
    Require(common.Any(sensor=>sensor.Id=="cpu") && common.Any(sensor=>sensor.Id=="temp") && !common.Any(sensor=>sensor.Id is "thread" or "clock" || sensor.Name=="D3D Copy"),"Common list keeps device totals and primary temperature, excluding per-core frequency and secondary GPU engines");
    Require(!SensorCatalog.Filter(fixture,"全部指标",[],false).Any(sensor=>sensor.Id=="clock") && SensorCatalog.Filter(fixture,"全部指标",[],true).Any(sensor=>sensor.Id=="clock"),"Unavailable advanced channels require explicit display");
    Require(SensorCatalog.Capabilities(fixture.Sensors).Any(item=>item.Label=="CPU 温度" && item.Status=="requires_admin") && SensorCatalog.Capabilities(fixture.Sensors).Any(item=>item.Label=="GPU 温度" && item.Status=="unsupported"),"Permission requirements and absent GPU temperature remain distinct");
    results.Add(new{name="physical-default-route-rates-common-selection-and-permissions",passed=true});

    string broker = args.FirstOrDefault() ?? throw new ArgumentException("Pass the built broker DLL.");
    string dotnet = Path.Combine(cache, "programs", "dotnet", "dotnet.exe");
    await CheckBroker(false);
    await CheckBroker(true);
    results.Add(new { name = "broker-authentication-and-disconnect", passed = true });
    Directory.CreateDirectory(Path.Combine(cache, "logs"));
    File.WriteAllText(Path.Combine(cache, "logs", "claribar-checks.json"), JsonSerializer.Serialize(new
    { capturedAt = DateTimeOffset.Now, passed = true, results }, new JsonSerializerOptions(Wire.Json) { WriteIndented = true }));
    Console.WriteLine("ClariBar checks passed: settings recovery, data validity, real broker IPC, token rejection, clean disconnect.");
    return 0;

    async Task CheckBroker(bool badToken)
    {
        string name = "ClariBar-check-" + Guid.NewGuid().ToString("N");
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var info = new ProcessStartInfo(dotnet)
        { Arguments = $"\"{broker}\" --pipe {name} --token {token}", UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(info)!;
        try
        {
            using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                await pipe.ConnectAsync(20000);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 65536, true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 65536, true) { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(new BrokerRequest("sample", badToken ? new string('0', 64) : token), Wire.Json));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                string? line;
                try { line = await reader.ReadLineAsync(timeout.Token); }
                catch (IOException) when (badToken) { line = null; }
                if (badToken) Require(line is null, "Invalid token rejected without a sample");
                else
                {
                    var data = JsonSerializer.Deserialize<SensorSnapshot>(line!, Wire.Json)!;
                    await Task.Delay(1200);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new BrokerRequest("sample",token),Wire.Json));
                    data=JsonSerializer.Deserialize<SensorSnapshot>((await reader.ReadLineAsync(timeout.Token))!,Wire.Json)!;
                    Require(data.Network is {InterfaceIndex:>0,Status:"valid"},"Real default physical route selected");
                    Require(data.Sensors.Count(sensor=>sensor.HardwareType=="Network")==2 && data.Sensors.Any(sensor=>sensor.Id==NetworkMetrics.UploadId && sensor.IsValid) && data.Sensors.Any(sensor=>sensor.Id==NetworkMetrics.DownloadId && sensor.IsValid),"Only the physical default uplink and downlink are exposed");
                    Require(SensorCatalog.Common(data).Single(sensor=>sensor.HardwareType=="Memory").HardwareId=="/ram","Live common memory reading uses physical RAM");
                    Require(!SensorCatalog.Common(data).Any(sensor=>sensor.Kind=="Clock" || sensor.Name.Contains("Thread #")),"Live common list excludes per-core and frequency channels");
                    File.WriteAllText(Path.Combine(cache,"logs","claribar-broker-user.json"),JsonSerializer.Serialize(data,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
                    Console.WriteLine($"Default physical interface {data.Network!.InterfaceIndex}: {data.Network.Name}; excluded {data.Network.ExcludedAdapters} other interfaces.");
                    Require(data.Version == 1 && data.Sensors.Length > 0 && data.IsFresh, "Real sensor sample received");
                    Require(data.Sensors.All(s => s.Value is null || float.IsFinite(s.Value.Value)), "IPC excludes non-finite JSON numbers");
                    File.WriteAllText(Path.Combine(cache,"logs","claribar-localized-sensors.json"),JsonSerializer.Serialize(data.Sensors.Select(sensor=>new {sensor.Id,rawName=sensor.Name,displayName=sensor.DisplayName}),new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
                    Require(data.Sensors.All(sensor=>!System.Text.RegularExpressions.Regex.IsMatch(sensor.DisplayName.Replace("CPU","").Replace("GPU",""),"[A-Za-z]")),"Live sensor labels use Chinese except CPU and GPU");
                    Console.WriteLine($"Real broker returned {data.Sensors.Length} sensors; {data.Errors.Length} device errors.");
                }
            }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Require(process.ExitCode == 0, "Broker exits after disconnect");
        }
        finally { if (!process.HasExited) process.Kill(); }
    }
}
finally { Directory.Delete(root, true); }

static void Require(bool condition, string message)
{ if (!condition) throw new InvalidOperationException("Check failed: " + message); }
