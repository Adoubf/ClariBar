using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClariBar.Core;

namespace ClariBar.App;

internal static class AppDiagnostics
{
    private static readonly object gate=new();
    private static bool initialized;
    internal static long RecoveredErrors {get;private set;}
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall,CharSet=CharSet.Unicode)]
    private static extern int CB_EnableCrashDiagnostics(string directory);
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]
    internal static extern int CB_WriteDiagnosticDump();
    internal static void Initialize()
    {
        if(initialized)return;initialized=true;
        AppDomain.CurrentDomain.UnhandledException+=(_,args)=>Error("runtime-unhandled",args.ExceptionObject as Exception??new Exception("Non-CLR exception"));
        TaskScheduler.UnobservedTaskException+=(_,args)=>{Error("task-unobserved",args.Exception);args.SetObserved();};
        AppDomain.CurrentDomain.ProcessExit+=(_,_)=>Record("process-exit");
        string directory=Path.Combine(AppPaths.Logs,"crashes");
        try
        {
            Directory.CreateDirectory(directory);
            foreach(var old in new DirectoryInfo(directory).GetFiles("ClariBar-crash-*.dmp").OrderByDescending(file=>file.LastWriteTimeUtc).Skip(2))old.Delete();
            int error=CB_EnableCrashDiagnostics(directory);if(error!=0)Error("crash-diagnostics",new System.ComponentModel.Win32Exception(error));
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException){Error("crash-diagnostics",ex);}
        Record("process-start");
    }
    internal static void Run(string operation,Action action)
    {
        try{action();}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ObjectDisposedException or COMException)
        {RecoveredErrors++;Error(operation,ex);}
    }
    internal static async Task RunAsync(string operation,Func<Task> action)
    {
        try{await action();}
        catch(OperationCanceledException){}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ObjectDisposedException or COMException)
        {RecoveredErrors++;Error(operation,ex);}
    }
    internal static void Error(string operation,Exception error)=>Record(operation,error);
    internal static void Record(string operation,Exception? error=null)
    {
        try
        {
            lock(gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);string path=Path.Combine(AppPaths.Logs,"app-stability.jsonl");
                if(File.Exists(path) && new FileInfo(path).Length>2*1024*1024)File.Move(path,path+".previous",true);
                File.AppendAllText(path,JsonSerializer.Serialize(new{time=DateTimeOffset.Now,version=ProductInfo.DisplayVersion,pid=Environment.ProcessId,thread=Environment.CurrentManagedThreadId,operation,hresult=error is null?null:$"0x{error.HResult:X8}",exception=error?.ToString()},Wire.Json)+Environment.NewLine);
            }
        }
        catch{ /* Diagnostics must never become another failure path. */ }
    }
}
