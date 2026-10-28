using L = ClariBar.Core.Localizer;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClariBar.Core;

public sealed record SensorReading(string Id, string HardwareId, string Hardware,
    string HardwareType, string Name, string Kind, float? Value, string Unit, string Status)
{
    [JsonIgnore] public bool IsValid => Status == "valid" && Value.HasValue && float.IsFinite(Value.Value);
    [JsonIgnore] public string DisplayName => SensorLabels.Name(this);
    [JsonIgnore] public string DisplayValue => IsValid
        ? $"{Value!.Value:0.##}{(Unit.Length > 0 ? " " + Unit : "")}" : "—";
    [JsonIgnore] public string StatusText => Status switch
    { "valid"=>L.T("Available"), "invalid"=>L.T("Invalid reading"), "stale"=>L.T("Stale data"), "requires_admin"=>L.T("Needs access"),
      "warming"=>L.T("Sampling"), "offline"=>L.T("Disconnected"), "unsupported"=>L.T("Not exposed"), _=>L.T("No reading") };
    [JsonIgnore] public string Category => HardwareType switch
    {
        "Cpu" => "CPU", "GpuIntel" or "GpuNvidia" or "GpuAmd" => "GPU",
        "Storage" => L.T("Disk"), "Network" => L.T("Network"), "Memory" => L.T("Memory"),
        "SuperIO" or "Motherboard" or "EmbeddedController" or "Cooler" => L.T("Motherboard & fans"),
        "Battery" => L.T("Battery"), "Psu" => L.T("Power supply"), _ => L.T("Other devices")
    };
}

public sealed record SensorSnapshot(int Version, DateTimeOffset CapturedAt, bool Elevated,
    string Status, SensorReading[] Sensors, string[] Errors)
{
    public NetworkRouteInfo? Network {get;init;}
    public SensorCapability[] Capabilities {get;init;} = [];
    public const int ProtocolVersion = 1;
    public static SensorSnapshot Empty(string status) => new(ProtocolVersion, DateTimeOffset.UtcNow,
        false, status, [], []);
    [JsonIgnore] public bool IsFresh => DateTimeOffset.UtcNow - CapturedAt < TimeSpan.FromSeconds(8);
}

public sealed record BrokerRequest(string Command, string Token, int IntervalMs = 1000);

public static class Wire
{
    public const int MaximumLineLength = 4 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = false };

    public static float? Finite(float? value) => value.HasValue && float.IsFinite(value.Value) ? value : null;
    public static string Unit(string kind) => kind switch
    {
        "Temperature" => "°C", "Load" or "Control" or "Level" => "%", "Fan" => "RPM",
        "Clock" => "MHz", "Power" => "W", "Voltage" => "V", "Data" => "GiB",
        "SmallData" => "MiB", "Throughput" => "B/s", "Frequency" => "Hz", _ => ""
    };
}

public static class SensorFormatting
{
    public static string Value(SensorReading? sensor, bool fresh = true)
    {
        if (!fresh || sensor is null || !sensor.IsValid) return "—";
        if (sensor.Kind == "Throughput")
        {
            double value = sensor.Value!.Value;
            var units = new[] { "B/s", "KiB/s", "MiB/s", "GiB/s" };
            int index = 0;
            while (Math.Abs(value) >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
            return $"{value:0.#} {units[index]}";
        }
        return sensor.DisplayValue;
    }
}
