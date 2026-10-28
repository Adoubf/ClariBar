using System.Runtime.InteropServices;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClariBar.App;

if(args.Length<2)throw new ArgumentException("Pass native DLL directory and immutable TAP DLL path.");
NativeLibrary.SetDllImportResolver(typeof(Native).Assembly,(name,assembly,path)=>name=="ClariBar.Native.dll"?NativeLibrary.Load(Path.Combine(args[0],name)):0);
string cache=Environment.GetEnvironmentVariable("CLARIBAR_ROOT")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClariBar");
Directory.CreateDirectory(Path.Combine(cache,"logs"));
NativeMethods.Callback callback=_=>{};
NativeMethods.Start(callback);
var config=TaskbarConfig.Empty();Native.CB_XamlConfigure(ref config);
int hr=Native.CB_XamlAttach(args[1]);
var fontBuffer=new System.Text.StringBuilder(65536);NativeMethods.Fonts(fontBuffer,fontBuffer.Capacity);
Console.WriteLine($"Installed font families {fontBuffer.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries).Length}");
Console.WriteLine($"Attach HRESULT 0x{hr:X8}; Config ABI size {Marshal.SizeOf<TaskbarConfig>()}");
try
{
    TaskbarStatus status=new();string tree="";
    for(int i=0;i<35;i++)
    {
        await Task.Delay(500);status=Native.ReadTaskbarStatus();
        tree=Native.ReadTaskbarTree();
        if(status.Frames>0 && status.Backgrounds>0 && Regex.Matches(tree,@"#DateInnerTextBlock").Count==status.Dates && tree.Length>0)break;
    }
    if(tree.Length==0)throw new IOException("Initial taskbar tree was not published in time.");
    File.WriteAllText(Path.Combine(cache,"logs","taskbar-tree.txt"),tree);
    File.WriteAllText(Path.Combine(cache,"logs","taskbar-probe-before.json"),JsonSerializer.Serialize(status,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine(JsonSerializer.Serialize(status));
    if(args.Contains("--apply"))
    {
        config.BackgroundMode=1;config.BackgroundArgb=0;
        config.Embedded=1;config.Layout=1;config.Width=270;
        config.MetricCount=4;
        config.Metrics[0]=new(){Color=0xff70d8ee,Text="CPU  12 %"};
        config.Metrics[1]=new(){Color=0xffffb86c,Text="温度  45 °C"};
        config.Metrics[2]=new(){Color=0xffa5e6a1,Text="↑  128 KiB/s"};
        config.Metrics[3]=new(){Color=0xffe7b4fa,Text="↓  1.2 MiB/s"};
        config.ColorFlags=1|2|4;config.TimeArgb=0xff66e0ff;config.DateArgb=0xffffc77a;config.TrayArgb=0xffa4e6c3;
        Native.CB_XamlConfigure(ref config);
        await Task.Delay(7500);
        var applied=Native.ReadTaskbarStatus();Console.WriteLine("APPLIED "+JsonSerializer.Serialize(applied));
        File.WriteAllText(Path.Combine(cache,"logs","taskbar-probe-applied.json"),JsonSerializer.Serialize(applied,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(cache,"logs","taskbar-tree-applied.txt"),Native.ReadTaskbarTree());
        string appliedTree=Native.ReadTaskbarTree();
        var layoutChecks=new List<object>();
        if(args.Contains("--extended"))
        {
            config.MetricCount=6;
            config.Metrics[4]=new(){Color=0xffffd080,Text="GPU  15 %"};
            config.Metrics[5]=new(){Color=0xffa0c4ff,Text="风扇  900 RPM"};
            config.ColorFlags=1|2|4|8|16|32;
            config.NetworkArgb=0xff12ab34;config.VolumeArgb=0xffbc23de;config.InputArgb=0xffde8912;
            config.BackgroundMode=2;config.BackgroundArgb=0x80446688;
            foreach(var layout in new[]{(Id:0u,Columns:6,Rows:1,Font:16u),(Id:1u,Columns:2,Rows:3,Font:8u),(Id:2u,Columns:3,Rows:2,Font:12u)})
            {
                config.Layout=layout.Id;config.FontSize=layout.Font;config.ClickThrough=layout.Id==1?0u:1u;
                Native.CB_XamlConfigure(ref config);await Task.Delay(7200);
                string layoutTree=Native.ReadTaskbarTree();var layoutStatus=Native.ReadTaskbarStatus();
                File.WriteAllText(Path.Combine(cache,"logs",$"taskbar-tree-layout-{layout.Id}.txt"),layoutTree);
                bool grid=Regex.Matches(layoutTree,$@"#ClariBarEmbeddedMonitor[^\r\n]*columns={layout.Columns} rows={layout.Rows}[^\r\n]*hitTest={(config.ClickThrough==1?0:1)}").Count==applied.Panels;
                bool cells=Regex.Matches(layoutTree,@"#ClariBarMetric[0-5] ").Count==6*applied.Panels;
                bool font=Regex.Matches(layoutTree,$@"#ClariBarMetric[0-5][^\r\n]*font={layout.Font}\.000000").Count==6*applied.Panels;
                bool network=layoutStatus.Networks>0 && Regex.Matches(layoutTree,"(?:foreground|fill)=#FF12AB34").Count==layoutStatus.Networks;
                bool volume=layoutStatus.Volumes>0 && Regex.Matches(layoutTree,"(?:foreground|fill)=#FFBC23DE").Count==layoutStatus.Volumes;
                bool input=layoutStatus.Inputs>0 && Regex.Matches(layoutTree,"(?:foreground|fill)=#FFDE8912").Count==layoutStatus.Inputs;
                bool tinted=Regex.Matches(layoutTree,@"#BackgroundFill[^\r\n]*fill=#80446688").Count>=status.Frames;
                bool passed=grid && cells && font && network && volume && input && tinted && layoutStatus.LastError==0;
                var result=new{layout=layout.Id,grid,cells,font,network,volume,input,tinted,passed};
                layoutChecks.Add(result);Console.WriteLine("LAYOUT "+JsonSerializer.Serialize(result));
                File.WriteAllText(Path.Combine(cache,"logs","taskbar-layout-verification.json"),JsonSerializer.Serialize(layoutChecks,new JsonSerializerOptions{WriteIndented=true}));
                if(!passed)return 8;
            }
        }
        if(args.Contains("--adaptive"))
        {
            config.MetricCount=6;config.Layout=2;config.FontSize=12;
            string[] labels=["GPU 核心频率 1550 MHz","CPU 100 %","内存 20.86 %","上传 128.8 KiB/s","下载 1023.9 MiB/s","风扇 1200 RPM"];
            for(int index=0;index<labels.Length;index++)config.Metrics[index].Text=labels[index];
            var checks=new List<object>();
            foreach(var style in new[]{(Auto:0u,Width:420u,Family:"Microsoft YaHei UI",Weight:400u),(Auto:1u,Width:100u,Family:"Microsoft YaHei UI",Weight:700u),(Auto:1u,Width:100u,Family:"Consolas",Weight:400u)})
            {
                config.AutoWidth=style.Auto;config.Width=style.Width;config.FontFamily=style.Family;config.FontWeight=style.Weight;
                Native.CB_XamlConfigure(ref config);await Task.Delay(7200);
                string actual=Native.ReadTaskbarTree();
                File.WriteAllText(Path.Combine(cache,"logs",$"taskbar-adaptive-{style.Auto}-{style.Weight}.txt"),actual);
                var panels=Regex.Matches(actual,@"#ClariBarEmbeddedMonitor \[(\d+)x").Select(match=>int.Parse(match.Groups[1].Value)).ToArray();
                var lines=actual.Split('\n').Where(line=>Regex.IsMatch(line,@"#ClariBarMetric[0-5] ")).ToArray();
                bool font=lines.Length==6*status.Frames && lines.All(line=>line.Contains("family="+style.Family+" weight="+style.Weight));
                bool noTrimming=lines.Length>0 && lines.All(line=>line.Contains("trimmed=0"));
                bool width=panels.Length==status.Frames && panels.All(width=>style.Auto==0?width==420:width>100 && width<900);
                bool passed=font && width && (style.Auto==0 || noTrimming);
                var result=new{style.Auto,style.Width,style.Family,style.Weight,panels,font,noTrimming,width,passed};checks.Add(result);
                Console.WriteLine("ADAPTIVE "+JsonSerializer.Serialize(result));
                File.WriteAllText(Path.Combine(cache,"logs","taskbar-adaptive-verification.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
                if(!passed)return 9;
            }
        }
        if(args.Contains("--centering"))
        {
            var checks=new List<object>();
            foreach(var style in new[]{
                (Font:13u,Layout:2u,Count:6u,Family:"Microsoft YaHei UI",Weight:700u),
                (Font:8u,Layout:2u,Count:6u,Family:"Microsoft YaHei UI",Weight:700u),
                (Font:15u,Layout:2u,Count:6u,Family:"Microsoft YaHei UI",Weight:700u),
                (Font:24u,Layout:2u,Count:6u,Family:"Microsoft YaHei UI",Weight:700u),
                (Font:13u,Layout:2u,Count:6u,Family:"Segoe UI",Weight:400u),
                (Font:13u,Layout:1u,Count:5u,Family:"Microsoft YaHei UI",Weight:700u),
                (Font:24u,Layout:1u,Count:8u,Family:"Microsoft YaHei UI",Weight:700u),
                (Font:16u,Layout:0u,Count:6u,Family:"Consolas",Weight:400u)})
            {
                config.FontSize=style.Font;config.Layout=style.Layout;config.MetricCount=style.Count;
                config.FontFamily=style.Family;config.FontWeight=style.Weight;config.AutoWidth=1;config.ClickThrough=1;
                string[] labels=[$"CPU {checks.Count+1} %","CPU 核心温度 45 °C","内存 23.86 %","上传 9 KiB/s","下载 97.5 KiB/s","磁盘占用 0.98 %","GPU gjpq Â 15 %","风扇 1200 RPM"];
                for(int i=0;i<style.Count;i++)config.Metrics[i]=new(){Text=labels[i],Color=0xff70d8ee};
                Native.CB_XamlConfigure(ref config);
                string actual="";
                for(int attempt=0;attempt<20;attempt++)
                {
                    await Task.Delay(400);actual=Native.ReadTaskbarTree();
                    if(Regex.Matches(actual,@"#ClariBarMetric0[^\r\n]*text="+Regex.Escape(labels[0])+@" font=").Count==applied.Panels)break;
                }
                File.WriteAllText(Path.Combine(cache,"logs",$"taskbar-centering-{checks.Count}.txt"),actual);
                var panels=new List<object>();bool passed=true;
                double Read(string line,string key)=>double.Parse(Regex.Match(line,$@"(?:^| ){key}=([-\d.]+)").Groups[1].Value,CultureInfo.InvariantCulture);
                foreach(string part in actual.Split("#ClariBarEmbeddedMonitor ").Skip(1))
                {
                    string heading=part.Split('\n')[0];
                    var cells=part.Split('\n').Where(line=>Regex.IsMatch(line,@"#ClariBarMetric\d+ ")).Take((int)style.Count).ToArray();
                    double height=Read(heading,"height"),offset=Read(heading,"y");
                    double areaTop=Read(heading,"areaTop"),areaHeight=Read(heading,"areaHeight"),up=Read(heading,"up");
                    double glyphTop=cells.Min(line=>Read(line,"inkTop")),glyphBottom=cells.Max(line=>Read(line,"inkBottom"));
                    double top=glyphTop-areaTop,bottom=areaTop+areaHeight-glyphBottom;
                    bool centered=cells.Length==style.Count && Math.Abs((glyphTop+glyphBottom)/2-(areaTop+areaHeight/2-up))<=1 && top>=1 && bottom>=1 && Math.Abs(offset)<=1;
                    var rowBounds=cells.GroupBy(line=>Read(line,"row")).OrderBy(row=>row.Key)
                        .Select(row=>(Top:row.Min(line=>Read(line,"inkTop")),Bottom:row.Max(line=>Read(line,"inkBottom")))).ToArray();
                    double minGap=rowBounds.Length<2?4:rowBounds.Zip(rowBounds.Skip(1),(a,b)=>b.Top-a.Bottom).Min();
                    bool separated=minGap>=3;
                    bool fits=cells.All(line=>Read(line,"font")<=style.Font && Read(line,"font")>=7);
                    bool noTrimming=cells.All(line=>line.Contains("trimmed=0"));
                    passed&=centered && separated && fits && noTrimming;
                    panels.Add(new{height,areaTop,areaHeight,up,offset,top,bottom,minGap,centered,separated,fits,noTrimming});
                }
                passed&=panels.Count==applied.Panels && panels.Count>0;
                var result=new{style.Font,style.Layout,style.Count,style.Family,style.Weight,panels,passed};
                checks.Add(result);Console.WriteLine("CENTERING "+JsonSerializer.Serialize(result));
                File.WriteAllText(Path.Combine(cache,"logs","taskbar-centering-verification.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
                if(!passed)return 10;
            }
        }
        if(args.Contains("--hold"))await Task.Delay(20000);
        config.Enabled=0;Native.CB_XamlConfigure(ref config);await Task.Delay(1500);
        var restored=Native.ReadTaskbarStatus();Console.WriteLine("RESTORED "+JsonSerializer.Serialize(restored));
        File.WriteAllText(Path.Combine(cache,"logs","taskbar-probe-restored.json"),JsonSerializer.Serialize(restored,new JsonSerializerOptions{WriteIndented=true}));
        string restoredTree=Native.ReadTaskbarTree();
        File.WriteAllText(Path.Combine(cache,"logs","taskbar-tree-restored.txt"),restoredTree);
        bool backgroundReadback=Regex.Matches(appliedTree,@"#BackgroundFill[^\r\n]*fill=#00000000").Count>=status.Frames;
        bool timeReadback=Regex.Matches(appliedTree,@"#TimeInnerTextBlock[^\r\n]*foreground=#FF66E0FF").Count==status.Times;
        bool dateReadback=Regex.Matches(appliedTree,@"#DateInnerTextBlock[^\r\n]*foreground=#FFFFC77A").Count==status.Dates;
        bool layoutReadback=Regex.Matches(appliedTree,@"#ClariBarEmbeddedMonitor[^\r\n]*columns=2[^\r\n]*hitTest=0").Count==applied.Panels;
        bool metricReadback=new[]{"FF70D8EE","FFFFB86C","FFA5E6A1","FFE7B4FA"}.All(color=>appliedTree.Contains("foreground=#"+color));
        string[] ClockColors(string text)=>Regex.Matches(text,@"#(?:Time|Date)InnerTextBlock[^\r\n]*foreground=#[A-F0-9]{8}")
            .Select(m=>Regex.Match(m.Value,@"foreground=#[A-F0-9]{8}").Value).Order().ToArray();
        bool clockRestore=ClockColors(tree).SequenceEqual(ClockColors(restoredTree));
        bool panelRemoved=!restoredTree.Contains("#ClariBarEmbeddedMonitor");
        var verification=new{backgroundReadback,timeReadback,dateReadback,layoutReadback,metricReadback,clockRestore,panelRemoved};
        Console.WriteLine("READBACK "+JsonSerializer.Serialize(verification));
        File.WriteAllText(Path.Combine(cache,"logs","taskbar-property-verification.json"),JsonSerializer.Serialize(verification,new JsonSerializerOptions{WriteIndented=true}));
        if(!backgroundReadback||!timeReadback||!dateReadback||!layoutReadback||!metricReadback||!clockRestore||!panelRemoved)return 5;
        if(applied.Backgrounds==0 || applied.Panels==0 || applied.Times==0 || applied.Dates==0 || applied.LastError!=0) return 3;
        if(restored.PatchedProperties!=0 || restored.Panels!=0 || restored.Restored!=1)return 4;
        if(args.Contains("--watchdog"))
        {
            config.Enabled=1;Native.CB_XamlConfigure(ref config);await Task.Delay(1000);
            var active=Native.ReadTaskbarStatus();if(active.Panels==0)return 6;
            NativeMethods.PauseHeartbeat(1);await Task.Delay(7800);
            var expired=Native.ReadTaskbarStatus();NativeMethods.PauseHeartbeat(0);
            bool passed=expired.Restored==1 && expired.Panels==0 && expired.PatchedProperties==0;
            Console.WriteLine("WATCHDOG "+JsonSerializer.Serialize(new{passed,expired}));
            File.WriteAllText(Path.Combine(cache,"logs","taskbar-watchdog-verification.json"),JsonSerializer.Serialize(new{passed,expired},new JsonSerializerOptions{WriteIndented=true}));
            if(!passed)return 7;
        }
    }
    return status.Frames>0 && tree.Length>0?0:2;
}
finally{NativeMethods.Stop();GC.KeepAlive(callback);}

internal static class NativeMethods
{
    [DllImport("ClariBar.Native.dll",EntryPoint="CB_EnumFonts",CallingConvention=CallingConvention.StdCall,CharSet=CharSet.Unicode)]internal static extern int Fonts(System.Text.StringBuilder output,int capacity);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]internal delegate void Callback(int action);
    [DllImport("ClariBar.Native.dll",EntryPoint="CB_Start",CallingConvention=CallingConvention.StdCall)]internal static extern int Start(Callback callback);
    [DllImport("ClariBar.Native.dll",EntryPoint="CB_Stop",CallingConvention=CallingConvention.StdCall)]internal static extern void Stop();
    [DllImport("ClariBar.Native.dll",EntryPoint="CB_XamlPauseHeartbeat",CallingConvention=CallingConvention.StdCall)]internal static extern void PauseHeartbeat(int paused);
}
