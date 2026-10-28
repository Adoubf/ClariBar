using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
using ClariBar.Core;
using ClariBar.SensorBroker;

string? Argument(string key)
{
    int i = Array.IndexOf(args, key);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
string? name = Argument("--pipe"), token = Argument("--token");
if (name is null || token is null || !name.StartsWith("ClariBar-") || token.Length != 64)
{
    Console.Error.WriteLine("ClariBar.SensorBroker requires a private pipe and session token.");
    return 2;
}
using var identity = WindowsIdentity.GetCurrent();
bool elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
using var collector = new HardwareCollector();
// Elevated tokens can have Administrators as their default object owner. Set the
// actual user's SID as owner so CurrentUserOnly clients can verify the server.
var user = identity.User ?? throw new InvalidOperationException("缺少当前用户标识。");
var security = new PipeSecurity();
security.SetOwner(user);
security.SetAccessRuleProtection(true, false);
security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
using var server = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1,
    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536, security);
try
{
    using (var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
    {
        startup.CancelAfter(TimeSpan.FromSeconds(45));
        await server.WaitForConnectionAsync(startup.Token);
    }
    using var reader = new StreamReader(server, new UTF8Encoding(false), false, 65536, true);
    await using var writer = new StreamWriter(server, new UTF8Encoding(false), 65536, true) { AutoFlush = true };
    while (!cancellation.IsCancellationRequested && server.IsConnected)
    {
        // No parent heartbeat => close hardware handles and exit, including elevated sessions.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        string? line = await reader.ReadLineAsync(timeout.Token);
        if (line is null || line.Length > Wire.MaximumLineLength) break;
        var request = JsonSerializer.Deserialize<BrokerRequest>(line, Wire.Json);
        if (request is null || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(request.Token), Encoding.UTF8.GetBytes(token))) break;
        if (request.Command == "stop") break;
        if (request.Command != "sample") break;
        var snapshot = collector.Read(elevated);
        await writer.WriteLineAsync(JsonSerializer.Serialize(snapshot, Wire.Json));
    }
    return 0;
}
catch (Exception ex) when (ex is OperationCanceledException or IOException or JsonException)
{
    return 0; // A disconnected/closed UI is normal; never persist as an orphan service.
}
