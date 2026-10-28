using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ClariBar.App;

[StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
internal struct TaskbarMetric
{
    public uint Color;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=192)]public string Text;
}
[StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
internal struct TaskbarConfig
{
    public uint Enabled,BackgroundMode,BackgroundArgb,Embedded,Layout,ClickThrough,FontSize,Width;
    public uint ColorFlags,TimeArgb,DateArgb,TrayArgb,NetworkArgb,VolumeArgb,InputArgb,BatteryArgb;
    public uint MetricCount;
    [MarshalAs(UnmanagedType.ByValArray,SizeConst=8)]public TaskbarMetric[] Metrics;
    public uint AutoWidth,FontWeight;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=96)]public string FontFamily;
    public static TaskbarConfig Empty()=>new()
    {
        Enabled=1,ClickThrough=1,FontSize=11,Width=300,Layout=1,AutoWidth=1,FontWeight=400,FontFamily="Microsoft YaHei UI",
        Metrics=Enumerable.Range(0,8).Select(_=>new TaskbarMetric{Color=0xffffffff,Text=""}).ToArray()
    };
}
internal sealed class TaskbarStatus
{
    public uint Connected{get;set;}
    public uint ExplorerPid{get;set;}
    public uint Frames{get;set;}
    public uint Backgrounds{get;set;}
    public uint Times{get;set;}
    public uint Dates{get;set;}
    public uint SystemIcons{get;set;}
    public uint Networks{get;set;}
    public uint Volumes{get;set;}
    public uint Inputs{get;set;}
    public uint Batteries{get;set;}
    public uint BitmapIcons{get;set;}
    public uint Panels{get;set;}
    public uint MetricCount{get;set;}
    public uint PatchedProperties{get;set;}
    public uint AppliedRevision{get;set;}
    public uint Restored{get;set;}
    public int LastError{get;set;}
    public int AttachResult{get;set;}
    public ulong Tick{get;set;}
}
internal static partial class Native
{
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall,CharSet=CharSet.Unicode)]
    internal static extern int CB_XamlAttach(string path);
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]
    internal static extern void CB_XamlConfigure([In]ref TaskbarConfig config);
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall,CharSet=CharSet.Unicode)]
    internal static extern int CB_XamlStatus(StringBuilder buffer,int capacity);
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall,CharSet=CharSet.Unicode)]
    internal static extern int CB_XamlDump(StringBuilder buffer,int capacity);
    internal static TaskbarStatus ReadTaskbarStatus()
    {
        var text=new StringBuilder(4096);CB_XamlStatus(text,text.Capacity);
        return JsonSerializer.Deserialize<TaskbarStatus>(text.ToString(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??new();
    }
    internal static string ReadTaskbarTree()
    {
        var text=new StringBuilder(60000);CB_XamlDump(text,text.Capacity);return text.ToString();
    }
}
