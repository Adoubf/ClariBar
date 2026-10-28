using ClariBar.Core;
using System.Security.Cryptography;

namespace ClariBar.App;

internal sealed class TaskbarController
{
    public bool IsConnecting { get; private set; }
    public string? Error { get; private set; }
    public bool Attached { get; private set; }
    public TaskbarStatus Status { get; private set; } = new();

    public async Task ConnectAsync()
    {
        if(IsConnecting)return;
        IsConnecting=true;Error=null;
        try
        {
            string path=PrepareAdapter();
            int hr=await Task.Run(()=>Native.CB_XamlAttach(path));
            if(hr<0)throw new InvalidOperationException($"Explorer 连接失败，HRESULT 0x{hr:X8}");
            Attached=true;
            for(int attempt=0;attempt<30;attempt++)
            {
                await Task.Delay(200);Refresh();
                if(Status.Frames>0)break;
            }
            if(Status.Frames==0)Error="已建立诊断会话，尚未找到任务栏控件。";
        }
        catch(Exception ex){Error=ex.Message;}
        finally{IsConnecting=false;}
    }
    private static string PrepareAdapter()
    {
        string packaged=Path.Combine(AppContext.BaseDirectory,"adapter","ClariBar.ExplorerTap.dll");
        if(!File.Exists(packaged))
        {
            string manifest=Path.Combine(AppContext.BaseDirectory,"adapter-path.txt");
            packaged=File.ReadAllText(manifest).Trim();
        }
        if(!File.Exists(packaged))throw new FileNotFoundException("缺少任务栏适配模块，请重新安装澄栏。",packaged);
        string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packaged)));
        string directory=Path.Combine(AppPaths.Cache,"adapters",hash[..16]);Directory.CreateDirectory(directory);
        string target=Path.Combine(directory,"ClariBar.ExplorerTap.dll");
        if(!File.Exists(target))File.Copy(packaged,target);
        if(!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))).Equals(hash,StringComparison.Ordinal))
            throw new IOException("任务栏适配模块校验失败，请清理对应缓存后重新安装。");
        return target;
    }
    public void Refresh()
    {
        if(!Attached)return;
        try{Status=Native.ReadTaskbarStatus();}
        catch(Exception ex){Error=ex.Message;}
    }
    public static bool Needed(AppSettings settings)=>settings.TaskbarMonitorEnabled || settings.TaskbarBackgroundMode!="System" ||
        settings.TimeColorEnabled || settings.DateColorEnabled || settings.TrayColorEnabled || settings.NetworkColorEnabled ||
        settings.VolumeColorEnabled || settings.InputColorEnabled || settings.BatteryColorEnabled;
    public static uint Argb(string hex,int opacity=100)=>((uint)(Math.Clamp(opacity,0,100)*255/100)<<24)|Convert.ToUInt32(hex[1..],16);
    public static TaskbarConfig CreateConfig(AppSettings settings,IEnumerable<(SensorReading Sensor,string Text)> metrics)
    {
        var config=TaskbarConfig.Empty();
        config.BackgroundMode=settings.TaskbarBackgroundMode switch{"Transparent"=>1u,"Tinted"=>2u,_=>0u};
        config.BackgroundArgb=Argb(settings.TaskbarBackgroundColor,settings.TaskbarBackgroundMode=="Transparent"?0:settings.TaskbarOpacity);
        config.Embedded=settings.TaskbarMonitorEnabled?1u:0u;
        config.Layout=settings.MonitorLayout switch{"TwoColumns"=>1u,"TwoRows"=>2u,_=>0u};
        config.ClickThrough=settings.MonitorClickThrough?1u:0u;config.FontSize=(uint)settings.MonitorFontSize;config.Width=(uint)settings.MonitorWidth;
        config.AutoWidth=settings.MonitorAutoWidth?1u:0u;config.FontWeight=settings.MonitorBold?700u:400u;config.FontFamily=settings.MonitorFontFamily;
        config.ColorFlags=(settings.TimeColorEnabled?1u:0u)|(settings.DateColorEnabled?2u:0u)|(settings.TrayColorEnabled?4u:0u)|
            (settings.NetworkColorEnabled?8u:0u)|(settings.VolumeColorEnabled?16u:0u)|(settings.InputColorEnabled?32u:0u)|(settings.BatteryColorEnabled?64u:0u);
        config.TimeArgb=Argb(settings.TimeColor);config.DateArgb=Argb(settings.DateColor);config.TrayArgb=Argb(settings.TrayColor);
        config.NetworkArgb=Argb(settings.NetworkColor);config.VolumeArgb=Argb(settings.VolumeColor);config.InputArgb=Argb(settings.InputColor);config.BatteryArgb=Argb(settings.BatteryColor);
        foreach(var metric in metrics.Take(8))
        {
            string text=metric.Text.Length>191?metric.Text[..191]:metric.Text;
            config.Metrics[config.MetricCount++]=new TaskbarMetric {Text=text,Color=Argb(settings.SensorColors.GetValueOrDefault(metric.Sensor.Id,settings.MonitorColor))};
        }
        return config;
    }
}
