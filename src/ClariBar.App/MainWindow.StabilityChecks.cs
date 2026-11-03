using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Xaml;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    [DllImport("user32.dll")]private static extern uint GetGuiResources(nint process,uint flags);
    private async Task CheckStabilityAsync(Action<bool,string> require)
    {
        HideForTool();var windowSet=InteractionField<HashSet<Window>>(desktopTools,"windows");int initialWindows=windowSet.Count;
        long initialErrors=AppDiagnostics.RecoveredErrors;var references=new List<WeakReference>();
        DesktopCapture.GetCursorPos(out var cursor);
        for(int index=0;index<60;index++)
        {
            CancellationToken initialToken=default;var release=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            CopyTranslationWindow? popup=null;
            popup=new CopyTranslationWindow(desktopTools,async(text,token)=>
            {
                InteractionField<DispatcherTimer>(popup!,"dismiss").Stop();
                if(text=="Pending"){initialToken=token;return await release.Task.WaitAsync(token);}
                if(text=="Failure")throw new IOException("Expected transient translation failure");
                await Task.Yield();return "Translated "+text;
            });
            desktopTools.Track(popup);var pending=popup.ShowTextAsync("Pending",cursor.X,cursor.Y);
            InteractionField<DispatcherTimer>(popup,"dismiss").Stop();
            if(index%3==0)
            {
                await popup.ShowTextAsync("Newest",cursor.X,cursor.Y);InteractionField<DispatcherTimer>(popup,"dismiss").Stop();
                await pending;
                require(initialToken.IsCancellationRequested && popup.TranslatedText=="Translated Newest","A newer copy cancels the old request without replacing its result: "+index+" (canceled="+initialToken.IsCancellationRequested+", result="+popup.TranslatedText+")");
            }
            else if(index%3==1)
            {
                await popup.ShowTextAsync("Failure",cursor.X,cursor.Y);InteractionField<DispatcherTimer>(popup,"dismiss").Stop();
                await pending;
                require(initialToken.IsCancellationRequested && popup.DisplayedText=="Expected transient translation failure" && !popup.IsRunning,"An ordinary translation failure stays within its popup: "+index);
            }
            popup.Close();release.TrySetResult("Late text");await pending;
            require(initialToken.IsCancellationRequested && windowSet.Count==initialWindows,"Closing a pending popup cancels and releases its tracked window: "+index);
            references.Add(new(popup));
            if(index%10==9){GC.Collect();GC.WaitForPendingFinalizers();await Task.Delay(80);}
        }
        GC.Collect();GC.WaitForPendingFinalizers();await Task.Delay(200);GC.Collect();
        int retained=references.Count(reference=>reference.IsAlive);
        AppDiagnostics.Record("stability-window-retention-"+retained);
        require(retained<=4,"Closed copy windows are collected instead of accumulating while the app runs in the background (retained="+retained+")");
        var compositor=typeof(TransparentToolBackdrop).GetField("compositor",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null);
        require(compositor is not null,"All transparent cards retain the UI thread composition channel across window closure");
        require(windowSet.Count==initialWindows && AppDiagnostics.RecoveredErrors==initialErrors,"Repeated popup replacement, service failure and cancellation produce no unexpected recovered error");

        bool escaped=false;try{AppDiagnostics.Run("stability-unexpected",()=>throw new InvalidOperationException("Expected test exception"));}catch(InvalidOperationException){escaped=true;}
        require(escaped,"Unexpected application errors are not silently marked handled by a global exception hook");
        AppDiagnostics.Run("stability-transient-test",()=>throw new IOException("Expected isolated I/O failure"));
        require(AppDiagnostics.RecoveredErrors==initialErrors+1 && File.ReadAllText(Path.Combine(AppPaths.Logs,"app-stability.jsonl")).Contains("stability-transient-test",StringComparison.Ordinal),"A scoped recoverable I/O failure is recorded with its context without terminating the UI");
        require(AppDiagnostics.CB_WriteDiagnosticDump()==0,"Native crash diagnostics can save a local dump without a system-wide registry change");
        var dump=new DirectoryInfo(Path.Combine(AppPaths.Logs,"crashes")).GetFiles($"ClariBar-crash-*-{Environment.ProcessId}.dmp").Single();
        using(var data=File.OpenRead(dump.FullName))
        {byte[] signature=new byte[4];data.ReadExactly(signature);require(signature.SequenceEqual(new byte[]{(byte)'M',(byte)'D',(byte)'M',(byte)'P'}) && data.Length>4096,"The diagnostic artifact is a valid minidump with thread and module evidence");}

        using var process=Process.GetCurrentProcess();process.Refresh();long startMemory=process.PrivateMemorySize64;int startHandles=process.HandleCount;
        uint startGdi=GetGuiResources(process.Handle,0),startUser=GetGuiResources(process.Handle,1);int samples=0;DateTimeOffset previous=DateTimeOffset.MinValue;
        var elapsed=Stopwatch.StartNew();
        while(elapsed.Elapsed<TimeSpan.FromSeconds(90))
        {
            if(snapshot.IsFresh && snapshot.CapturedAt!=previous){samples++;previous=snapshot.CapturedAt;}
            await Task.Delay(500);
        }
        GC.Collect();GC.WaitForPendingFinalizers();await Task.Delay(200);process.Refresh();
        require(samples>=10 && snapshot.IsFresh && snapshot.Sensors.Length>0,"Monitoring continues receiving fresh real hardware samples while all settings and tool windows remain hidden");
        require(process.HandleCount<startHandles+100 && GetGuiResources(process.Handle,0)<startGdi+30 && GetGuiResources(process.Handle,1)<startUser+30,"Background monitoring does not accumulate OS, GDI or USER handles during the soak check");
        require(AppDiagnostics.RecoveredErrors==initialErrors+1,"The background soak adds no unexpected recovered exception");
        await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"stability-evidence.json"),JsonSerializer.Serialize(new{popupCycles=60,collectedWindows=references.Count(reference=>!reference.IsAlive),seconds=elapsed.Elapsed.TotalSeconds,samples,startMemory,endMemory=process.PrivateMemorySize64,startHandles,endHandles=process.HandleCount,startGdi,endGdi=GetGuiResources(process.Handle,0),startUser,endUser=GetGuiResources(process.Handle,1),dump=dump.FullName},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
    }
}
