using System.Text.Json;

namespace ClariBar.Core;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public string Theme { get; set; } = "System";
    public bool NavigationCollapsed {get;set;}
    public int IntervalMs { get; set; } = 1000;
    public bool HardwareAccessEnabled {get;set;}
    public bool CloseToTray { get; set; } = true;
    public bool MiniMonitorEnabled { get; set; }
    public string MonitorColor { get; set; } = "#FFFFFF";
    public string MonitorBackground { get; set; } = "#202020";
    public int MonitorOpacity { get; set; } = 92;
    public string MonitorLayout { get; set; } = "TwoColumns";
    public int MonitorFontSize { get; set; } = 11;
    public int MonitorWidth { get; set; } = 300;
    public bool MonitorAutoWidth { get; set; } = true;
    public string MonitorFontFamily { get; set; } = "Microsoft YaHei UI";
    public bool MonitorBold { get; set; }
    public bool MonitorClickThrough { get; set; } = true;
    public bool TaskbarMonitorEnabled { get; set; }
    public string TaskbarBackgroundMode { get; set; } = "System";
    public string TaskbarBackgroundColor { get; set; } = "#202020";
    public int TaskbarOpacity { get; set; } = 40;
    public Dictionary<string,string> SensorColors { get; set; } = [];
    public bool TimeColorEnabled { get; set; }
    public string TimeColor { get; set; } = "#FFFFFF";
    public bool DateColorEnabled { get; set; }
    public string DateColor { get; set; } = "#FFFFFF";
    public bool TrayColorEnabled { get; set; }
    public string TrayColor { get; set; } = "#FFFFFF";
    public bool NetworkColorEnabled { get; set; }
    public string NetworkColor { get; set; } = "#FFFFFF";
    public bool VolumeColorEnabled { get; set; }
    public string VolumeColor { get; set; } = "#FFFFFF";
    public bool InputColorEnabled { get; set; }
    public string InputColor { get; set; } = "#FFFFFF";
    public bool BatteryColorEnabled { get; set; }
    public string BatteryColor { get; set; } = "#FFFFFF";
    public string[] PinnedSensors { get; set; } = [];
    public Dictionary<string, string> SensorNames { get; set; } = [];

    public void Normalize()
    {
        IntervalMs = Math.Clamp(IntervalMs, 500, 5000);
        MonitorOpacity = Math.Clamp(MonitorOpacity, 30, 100);
        MonitorFontSize = Math.Clamp(MonitorFontSize, 8, 24);
        MonitorWidth = Math.Clamp(MonitorWidth, 100, 900);
        MonitorFontFamily = MonitorFontFamily?.Trim() ?? "";
        if (MonitorFontFamily.Length is 0 or > 95 || MonitorFontFamily.Any(char.IsControl))
            MonitorFontFamily = "Microsoft YaHei UI";
        TaskbarOpacity = Math.Clamp(TaskbarOpacity, 0, 100);
        if (MonitorLayout is not ("SingleLine" or "TwoColumns" or "TwoRows")) MonitorLayout="TwoColumns";
        if (TaskbarBackgroundMode is not ("System" or "Transparent" or "Tinted")) TaskbarBackgroundMode="System";
        if (!ValidColor(TaskbarBackgroundColor))TaskbarBackgroundColor="#202020";
        if (!ValidColor(TimeColor))TimeColor="#FFFFFF";
        if (!ValidColor(DateColor))DateColor="#FFFFFF";
        if (!ValidColor(TrayColor))TrayColor="#FFFFFF";
        if (!ValidColor(NetworkColor))NetworkColor="#FFFFFF";
        if (!ValidColor(VolumeColor))VolumeColor="#FFFFFF";
        if (!ValidColor(InputColor))InputColor="#FFFFFF";
        if (!ValidColor(BatteryColor))BatteryColor="#FFFFFF";
        SensorColors ??= [];
        foreach (string key in SensorColors.Where(pair=>!ValidColor(pair.Value)).Select(pair=>pair.Key).ToArray())SensorColors.Remove(key);
        if (Theme is not ("System" or "Light" or "Dark")) Theme = "System";
        if (!ValidColor(MonitorColor)) MonitorColor = "#FFFFFF";
        if (!ValidColor(MonitorBackground)) MonitorBackground = "#202020";
        PinnedSensors = (PinnedSensors ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Take(8).ToArray();
        SensorNames ??= [];
    }

    public static bool ValidColor(string? value) => value?.Length == 7 && value[0] == '#'
        && uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _);
}

public sealed class SettingsStore(string root)
{
    public string DirectoryPath { get; } = root;
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public string? LoadWarning { get; private set; }

    public AppSettings Load()
    {
        LoadWarning = null;
        if (!File.Exists(FilePath)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Wire.Json)
                ?? throw new JsonException("配置为空");
            if (settings.Version != 1) throw new JsonException("不支持的配置版本");
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LoadWarning = $"配置无法读取，已使用默认值：{ex.Message}";
            // Keep the original file for recovery; never replace malformed input on load.
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings,
                new JsonSerializerOptions(Wire.Json) { WriteIndented = true }));
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
