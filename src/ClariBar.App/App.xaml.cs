using Microsoft.UI.Xaml;

namespace ClariBar.App;

public partial class App : Application
{
    private MainWindow? main;
    private Mutex? singleInstance;
    public App()
    {
        AppPaths.Initialize();
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Logs);
                File.AppendAllText(Path.Combine(AppPaths.Logs, "app-errors.log"),
                    $"{DateTimeOffset.Now:o} {e.Exception}\n");
            }
            catch { }
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if(Environment.GetCommandLineArgs().Contains("--quit"))
        {
            var previous=Native.FindWindow("ClariBar.Host",null);
            if(previous!=0)Native.PostMessage(previous,Native.RegisterWindowMessage("ClariBar.Exit"),0,0);
            Exit();return;
        }
        if (!Environment.GetCommandLineArgs().Contains("--smoke-test"))
        {
            singleInstance = new Mutex(false, "Local\\ClariBar.UI", out bool created);
            if (!created)
            {
                var previous = Native.FindWindow("ClariBar.Host", null);
                if (previous != 0) Native.PostMessage(previous, Native.RegisterWindowMessage("ClariBar.ShowSettings"), 0, 0);
                Exit(); return;
            }
        }
        main = new MainWindow();
        main.Activate();
    }
}

internal static class AppPaths
{
    private static string? cache;
    public static string Cache=>cache??=ResolveCache();
    public static string Data=>Path.Combine(Cache,"data");
    public static string Logs=>Path.Combine(Cache,"logs");
    public static string Dotnet=>Path.Combine(Cache,"programs","dotnet","dotnet.exe");
    private static string ResolveCache()
    {
        string configured=Path.Combine(AppContext.BaseDirectory,"runtime-path.txt");
        if(File.Exists(configured))return Path.GetFullPath(File.ReadAllText(configured).Trim());
        return Environment.GetEnvironmentVariable("CLARIBAR_ROOT")??@"E:\haoyue\cache\claribar";
    }
    public static void Initialize()
    {
        foreach(string directory in new[]{Cache,Data,Logs,Path.Combine(Cache,"temp")})Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("CLARIBAR_ROOT",Cache);
        Environment.SetEnvironmentVariable("TEMP",Path.Combine(Cache,"temp"));
        Environment.SetEnvironmentVariable("TMP",Path.Combine(Cache,"temp"));
    }
}
