using L = ClariBar.Core.Localizer;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClariBar.Core;

namespace ClariBar.App;

internal enum BrokerPhase { Idle, Authorizing, Connecting, Reading, Connected, Cancelled, Failed }
internal sealed record BrokerState(BrokerPhase Phase,string Message);

internal sealed class BrokerClient : IAsyncDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private NamedPipeClientStream? pipe;
    private Process? process;
    private Task? runner;
    public int IntervalMs { get; set; } = 1000;
    public event Action<SensorSnapshot>? Sample;
    public event Action<BrokerState>? StateChanged;
    public event Action<bool>? Failed;

    public void Start(bool elevate) => runner = RunAsync(elevate);

    private async Task RunAsync(bool elevate)
    {
        string name = "ClariBar-" + Guid.NewGuid().ToString("N");
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string broker=Path.Combine(AppContext.BaseDirectory,"broker","ClariBar.SensorBroker.exe");
        bool appHost=File.Exists(broker);
        if(!appHost)broker=Path.ChangeExtension(broker,"dll");
        try
        {
            StateChanged?.Invoke(new(elevate?BrokerPhase.Authorizing:BrokerPhase.Connecting,
                elevate?L.T("Waiting for Windows hardware authorization…"):L.T("Connecting to sensors…")));
            if (!File.Exists(broker)) throw new FileNotFoundException(L.T("Sensor broker is missing. Start ClariBar from its complete application directory."), broker);
            var info = new ProcessStartInfo(appHost?broker:AppPaths.Dotnet)
            {
                Arguments=appHost?$"--pipe {name} --token {token}":$"\"{broker}\" --pipe {name} --token {token}",
                WorkingDirectory = Path.GetDirectoryName(broker),
                UseShellExecute = elevate,
                CreateNoWindow = !elevate,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            if (elevate) info.Verb = "runas";
            process = await Task.Run(() => Process.Start(info), stopping.Token)
                ?? throw new IOException(L.T("Could not start the sensor process."));
            StateChanged?.Invoke(new(BrokerPhase.Connecting,L.T("Sensor process started. Establishing a connection…")));
            pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(40000, stopping.Token);
            StateChanged?.Invoke(new(BrokerPhase.Reading,L.T("Connected. Reading hardware data…")));
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 65536, true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 65536, true) { AutoFlush = true };
            while (!stopping.IsCancellationRequested)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new BrokerRequest("sample", token), Wire.Json));
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var line = await reader.ReadLineAsync(timeout.Token);
                if (line is null) throw new IOException(L.T("The sensor process disconnected."));
                if (line.Length > Wire.MaximumLineLength) throw new IOException(L.T("Sensor response exceeds the size limit."));
                var snapshot = JsonSerializer.Deserialize<SensorSnapshot>(line, Wire.Json)
                    ?? throw new IOException(L.T("Empty sensor response."));
                if (snapshot.Version != SensorSnapshot.ProtocolVersion) throw new IOException(L.T("Sensor protocol version mismatch."));
                Sample?.Invoke(snapshot);
                await Task.Delay(Math.Clamp(IntervalMs, 500, 5000), stopping.Token);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex)
        {
            bool cancelled=ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 };
            StateChanged?.Invoke(new(cancelled?BrokerPhase.Cancelled:BrokerPhase.Failed,
                cancelled?L.T("Hardware authorization was cancelled. Basic monitoring remains available."):L.F("Connection failed: {0}", ex.Message)));
            Failed?.Invoke(cancelled);
        }
        finally { pipe?.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        pipe?.Dispose();
        if (runner is not null)
            try { await runner.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        if (process is not null)
        {
            try
            {
                if (!process.HasExited) await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch { } // Hardware shutdown may outlive pipe disconnect; allow the collector to release its DLLs before UI exit.
            process.Dispose();
        }
        stopping.Dispose();
    }
}
