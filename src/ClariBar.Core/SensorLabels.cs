using L = ClariBar.Core.Localizer;
using System.Text.RegularExpressions;

namespace ClariBar.Core;

public static class SensorLabels
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Memory"]="内存占用", ["Memory Used"]="已用内存", ["Memory Available"]="可用内存",
        ["CPU Total"]="CPU 总占用", ["CPU Package"]="CPU 封装", ["CPU Cores"]="CPU 核心",
        ["CPU Memory"]="CPU 内存", ["CPU Platform"]="CPU 平台", ["CPU Core"]="CPU 核心",
        ["CPU Core Max"]="CPU 核心最高占用", ["Core Max"]="核心最高", ["Core Average"]="核心平均",
        ["Bus Speed"]="总线频率", ["GPU Core"]="GPU 核心", ["GPU Power"]="GPU 功耗",
        ["GPU Memory"]="GPU 显存", ["GPU Memory Controller"]="GPU 显存控制器", ["GPU Video Engine"]="GPU 视频引擎",
        ["D3D Shared Memory Total"]="GPU 共享内存总量", ["D3D Shared Memory Free"]="GPU 可用共享内存",
        ["D3D Shared Memory Used"]="GPU 已用共享内存", ["D3D 3D"]="GPU 三维", ["D3D Copy"]="GPU 复制引擎",
        ["D3D Video Decode"]="GPU 视频解码", ["D3D Video Encode"]="GPU 视频编码",
        ["D3D Video Processing"]="GPU 视频处理", ["D3D Overlay"]="GPU 叠加层", ["D3D"]="GPU 引擎",
        ["Data Uploaded"]="累计上传", ["Data Downloaded"]="累计下载", ["Upload Speed"]="上传速度",
        ["Download Speed"]="下载速度", ["Network Utilization"]="网络占用",
        ["Composite Temperature"]="综合温度", ["Temperature"]="温度", ["Warning Temperature"]="警告温度",
        ["Critical Temperature"]="临界温度", ["Life"]="剩余寿命", ["Data Read"]="累计读取", ["Data Written"]="累计写入",
        ["Power On Count"]="通电次数", ["Power On Hours"]="通电时长", ["Used Space"]="空间占用",
        ["Free Space"]="可用空间", ["Total Space"]="总空间", ["Read Activity"]="读取占用", ["Write Activity"]="写入占用",
        ["Total Activity"]="磁盘占用", ["Read Rate"]="读取速度", ["Write Rate"]="写入速度",
        ["Available Spare"]="可用备用空间", ["Available Spare Threshold"]="备用空间阈值", ["Percentage Used"]="寿命消耗",
        ["Vcore"]="核心电压", ["AVCC"]="模拟供电电压", ["CPU Termination"]="CPU 终端电压",
        ["+3V Standby"]="待机供电电压", ["Capacity"]="内存容量", ["Temperature Sensor Resolution"]="温度精度",
        ["Thermal Sensor Low Limit"]="温度下限", ["Thermal Sensor High Limit"]="温度上限",
        ["Thermal Sensor Critical Low Limit"]="临界温度下限", ["Thermal Sensor Critical High Limit"]="临界温度上限"
    };

    public static string Kind(string kind) => kind switch
    {
        "Temperature"=>L.T("Temperature"), "Load"=>L.T("Usage"), "Clock"=>L.T("Frequency"), "Frequency"=>L.T("Frequency"), "Fan"=>L.T("Fan speed"),
        "Throughput"=>L.T("Transfer rate"), "Data" or "SmallData"=>L.T("Capacity"), "Power"=>L.T("Power"), "Voltage"=>L.T("Voltage"),
        "Control"=>L.T("Control feedback"), "Level"=>L.T("Level"), "Current"=>L.T("Current"), "Factor"=>L.T("Factor"), "TimeSpan"=>L.T("Duration"),
        "Energy"=>L.T("Energy"), "Noise"=>L.T("Noise"), "Conductivity"=>L.T("Conductivity"), "Humidity"=>L.T("Humidity"), _=>L.T("Parameter")
    };

    public static string Name(SensorReading sensor)
    {
        if(!Localizer.IsChinese)return EnglishName(sensor);
        string raw = sensor.Name.Trim();
        string name;
        if (!Names.TryGetValue(raw, out name!))
        {
            name = raw;
            foreach (var replacement in new[]
            {
                ("Distance to TjMax", "距离温度上限"), ("P-Core", "性能核心"), ("E-Core", "能效核心"),
                ("CPU Core", "CPU 核心"), ("GPU Core", "GPU 核心"), ("Thread", "线程"), ("Temperature", "温度"),
                ("Voltage", "电压"), ("Fan", sensor.Kind=="Control"?"风扇控制":"风扇"), ("DIMM", "内存条")
            }) name = name.Replace(replacement.Item1, replacement.Item2, StringComparison.OrdinalIgnoreCase);
            name = Regex.Replace(name, @"#(?=\d)", "");
            // Keep detailed vendor names in the raw reading/tooltip; unknown
            // hardware labels still receive a Chinese display label.
            if (Regex.IsMatch(name.Replace("CPU", "").Replace("GPU", ""), "[A-Za-z]"))
            {
                string channel = Regex.Match(raw, @"#\d+|\b\d+\b").Value.TrimStart('#');
                name = $"{sensor.Category} {Kind(sensor.Kind)}通道{(channel.Length>0?" "+channel:"")}";
            }
        }
        string suffix = Kind(sensor.Kind);
        if ((sensor.Kind is "Load" or "Temperature" or "Clock" or "Power" or "Fan") &&
            !name.Contains(suffix) && !new[]{"最高占用","空间占用","寿命","温度上限"}.Any(name.Contains)) name += suffix;
        return name;
    }

    private static string EnglishName(SensorReading sensor)
    {
        string raw=sensor.Name.Trim();
        return (raw,sensor.Kind) switch
        {
            ("CPU Total","Load")=>"CPU total usage",
            ("CPU Package","Temperature")=>"CPU temperature",
            ("GPU Core","Load")=>"GPU usage",
            ("D3D 3D","Load")=>"GPU 3D usage",
            ("GPU Core","Temperature")=>"GPU temperature",
            ("GPU Core","Clock")=>"GPU clock",
            ("Memory","Load")=>"Memory usage",
            ("Total Activity","Load")=>"Disk usage",
            ("Read Rate","Throughput")=>"Read speed",
            ("Write Rate","Throughput")=>"Write speed",
            ("Upload Speed","Throughput")=>"Upload speed",
            ("Download Speed","Throughput")=>"Download speed",
            _=>Regex.Replace(raw,@"#(?=\d)","")
        };
    }
    public static string Compact(SensorReading sensor) => sensor switch
    {
        { HardwareType: "Cpu", Kind: "Load", Name: "CPU Total" } => "CPU",
        { HardwareType: "GpuIntel" or "GpuNvidia" or "GpuAmd", Kind: "Clock", Name: "GPU Core" } => L.T("GPU clock"),
        { HardwareType: "Memory", Kind: "Load", Name: "Memory" } => L.T("Memory"),
        { Name: "CPU Package", Kind: "Temperature" } => L.T("CPU temperature"),
        { Kind: "Throughput" } when sensor.Name.Contains("Upload") => L.T("Upload"),
        { Kind: "Throughput" } when sensor.Name.Contains("Download") => L.T("Download"),
        _ => Name(sensor)
    };
}
