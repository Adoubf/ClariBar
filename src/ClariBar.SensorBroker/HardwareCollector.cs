using L = ClariBar.Core.Localizer;
using ClariBar.Core;
using LibreHardwareMonitor.Hardware;

namespace ClariBar.SensorBroker;

internal sealed class HardwareCollector : IDisposable
{
    private readonly Computer computer = new()
    {
        IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true,
        IsMotherboardEnabled = true, IsControllerEnabled = true,
        IsStorageEnabled = true, IsNetworkEnabled = false,
    };
    private bool opened;
    private readonly NetworkCollector network=new();

    public SensorSnapshot Read(bool elevated)
    {
        var sensors = new List<SensorReading>();
        var errors = new List<string>();
        try
        {
            if (!opened) { computer.Open(); opened = true; }
            foreach (var hardware in computer.Hardware) Visit(hardware, sensors, errors, elevated);
        }
        catch (Exception ex) { errors.Add(L.F("Hardware access: {0}", ex.Message)); }
        try{sensors.AddRange(network.Read());}
        catch(Exception ex){errors.Add(L.F("Network uplink: {0}", ex.Message));}
        return new SensorSnapshot(SensorSnapshot.ProtocolVersion, DateTimeOffset.UtcNow, elevated,
            errors.Count == 0 ? "ready" : "partial", sensors.ToArray(), errors.ToArray())
        {Network=network.Route,Capabilities=SensorCatalog.Capabilities(sensors)};
    }

    private static void Visit(IHardware hardware, List<SensorReading> output, List<string> errors,bool elevated)
    {
        bool updated = true;
        try { hardware.Update(); }
        catch (Exception ex) { updated = false; errors.Add($"{hardware.Name}：{ex.Message}"); }
        foreach (var sensor in hardware.Sensors)
        {
            var raw = sensor.Value;
            var value = updated ? Wire.Finite(raw) : null;
            bool needsAccess=!elevated && (hardware.HardwareType==HardwareType.Cpu && (sensor.SensorType is SensorType.Temperature or SensorType.Clock or SensorType.Power or SensorType.Voltage) || hardware.HardwareType==HardwareType.SuperIO);
            string status = !updated ? "stale" : value.HasValue ? "valid" : raw.HasValue ? "invalid" : needsAccess?"requires_admin":"unavailable";
            var kind = sensor.SensorType.ToString();
            output.Add(new(sensor.Identifier.ToString(), hardware.Identifier.ToString(), hardware.Name,
                hardware.HardwareType.ToString(), sensor.Name, kind, value, Wire.Unit(kind), status));
        }
        foreach (var sub in hardware.SubHardware) Visit(sub, output, errors, elevated);
    }

    public void Dispose() { if (opened) computer.Close(); }
}
