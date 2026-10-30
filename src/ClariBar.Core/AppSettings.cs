using L = ClariBar.Core.Localizer;
using System.Text.Json;

namespace ClariBar.Core;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public string Theme { get; set; } = "System";
    public string Language { get; set; } = "System";
    public bool NavigationCollapsed {get;set;}
    public bool ToolHotkeysEnabled { get; set; } = true;
    public int ToolShortcutVersion { get; set; }
    public string CaptureHotkey { get; set; } = DesktopShortcuts.Capture;
    public string ColorHotkey { get; set; } = "Ctrl+Alt+C";
    public string PinHotkey { get; set; } = DesktopShortcuts.Pin;
    public string OcrHotkey { get; set; } = "Ctrl+Alt+O";
    public string TranslationHotkey { get; set; } = "Ctrl+Alt+A";
    public string TranslationEngine { get; set; } = "Free";
    public string TranslationDisplay { get; set; } = "Bilingual";
    public string TranslationSourceLanguage { get; set; } = "Auto";
    public string TranslationTargetLanguage { get; set; } = "Auto";
    public int TranslationLanguageVersion { get; set; }
    public bool OfflineTranslationEnabled { get; set; }
    public string AiTranslationModel { get; set; } = "";
    public string AiTranslationPrompt { get; set; } = "";
    public string[] ColorHistory { get; set; } = [];
    public string ColorPickerActivation {get;set;}="PickAndEdit";
    public string ColorCopyFormat {get;set;}="HEX";
    public bool ColorShowName {get;set;}=true;
    public string ColorLeftAction {get;set;}="Edit";
    public string ColorMiddleAction {get;set;}="Copy";
    public string ColorRightAction {get;set;}="Cancel";
    public List<ColorFormatDefinition> ColorFormats {get;set;}=ClariBar.Core.ColorFormats.Defaults();
    public string OcrLanguage { get; set; } = "Auto";
    public string OcrDefaultEngine {get;set;}="Local";
    public string AiOcrEndpoint { get; set; } = "";
    public string AiOcrModel { get; set; } = "";
    public string AiOcrApiType {get;set;}="Auto";
    public string AiOcrModelType {get;set;}="Auto";
    public string AiOcrPrompt {get;set;}="";
    public int AiOcrTimeoutSeconds {get;set;}=120;
    public string ScreenshotDirectory { get; set; } = "";
    public bool CaptureDetectElements { get; set; } = true;
    public bool CaptureCursor { get; set; }
    public bool CaptureAutoSave { get; set; }
    public bool CaptureSound { get; set; }
    public bool CaptureCancelOnDeactivate { get; set; } = true;
    public int CaptureDelaySeconds { get; set; } = 3;
    public int CaptureHistoryLimit { get; set; } = 20;
    public string CaptureFormat { get; set; } = "png";
    public int CaptureQuality { get; set; } = 92;
    public string CaptureNamePattern { get; set; } = "ClariBar-{date}-{time}";
    public int CapturePresetWidth { get; set; } = 800;
    public int CapturePresetHeight { get; set; } = 600;
    public bool PinAutoRestore { get; set; } = true;
    public int PinRecoverableCount { get; set; } = 1;
    public int PinDefaultOpacity { get; set; } = 100;
    public int PinThumbnailSize { get; set; } = 120;
    public bool PinIgnoreHtml { get; set; }
    public bool PinFilePathsAsText { get; set; } = true;
    public bool PinUseCapturePosition { get; set; } = true;
    public string HidePinsHotkey { get; set; } = DesktopShortcuts.HidePins;
    public string ClickThroughHotkey { get; set; } = "";
    public string DelayedCaptureHotkey { get; set; } = "";
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
        Language = Localizer.NormalizePreference(Language);
        ColorHistory = (ColorHistory ?? []).Where(ValidColor).Select(x => x.ToUpperInvariant()).Distinct().Take(24).ToArray();
        ClariBar.Core.ColorFormats.Normalize(this);
        CaptureHotkey=NormalizeHotkey(CaptureHotkey,DesktopShortcuts.Capture);
        ColorHotkey=NormalizeHotkey(ColorHotkey,"Ctrl+Alt+C");
        PinHotkey=NormalizeHotkey(PinHotkey,DesktopShortcuts.Pin);
        OcrHotkey=NormalizeHotkey(OcrHotkey,"Ctrl+Alt+O");
        TranslationHotkey=NormalizeHotkey(TranslationHotkey,"Ctrl+Alt+A");
        TranslationEngine=TranslationProtocol.NormalizeEngine(TranslationEngine);
        TranslationDisplay=TranslationDisplay=="Overlay"?"Overlay":"Bilingual";
        TranslationSourceLanguage=TranslationProtocol.NormalizeLanguage(TranslationSourceLanguage,true);
        TranslationTargetLanguage=TranslationProtocol.NormalizeLanguage(TranslationTargetLanguage,false);
        AiTranslationModel=(AiTranslationModel??"").Trim();AiTranslationPrompt=(AiTranslationPrompt??"").Trim();
        OcrLanguage = string.IsNullOrWhiteSpace(OcrLanguage) ? "Auto" : OcrLanguage.Trim();
        OcrDefaultEngine=string.Equals(OcrDefaultEngine,"Ai",StringComparison.OrdinalIgnoreCase)?"Ai":"Local";
        AiOcrEndpoint = (AiOcrEndpoint ?? "").Trim();
        AiOcrModel = (AiOcrModel ?? "").Trim();
        AiOcrApiType=AiOcrProtocol.NormalizeApi(AiOcrApiType);AiOcrModelType=AiOcrProtocol.NormalizeModelType(AiOcrModelType);
        AiOcrPrompt=(AiOcrPrompt??"").Trim();AiOcrTimeoutSeconds=Math.Clamp(AiOcrTimeoutSeconds,5,1800);
        ScreenshotDirectory ??= "";
        CaptureHistoryLimit = Math.Clamp(CaptureHistoryLimit, 0, 100);
        CaptureDelaySeconds = Math.Clamp(CaptureDelaySeconds, 0, 30);
        CaptureQuality = Math.Clamp(CaptureQuality, 1, 100);
        CapturePresetWidth = Math.Clamp(CapturePresetWidth, 1, 16000);
        CapturePresetHeight = Math.Clamp(CapturePresetHeight, 1, 16000);
        PinRecoverableCount = Math.Clamp(PinRecoverableCount, 0, 20);
        PinDefaultOpacity = Math.Clamp(PinDefaultOpacity, 10, 100);
        PinThumbnailSize = Math.Clamp(PinThumbnailSize, 32, 500);
        if(CaptureFormat is not ("png" or "jpg" or "bmp" or "webp")) CaptureFormat = "png";
        if(string.IsNullOrWhiteSpace(CaptureNamePattern) || CaptureNamePattern.Length > 100) CaptureNamePattern = "ClariBar-{date}-{time}";
        HidePinsHotkey=NormalizeHotkey(HidePinsHotkey,DesktopShortcuts.HidePins);
        ClickThroughHotkey=NormalizeHotkey(ClickThroughHotkey,"");
        DelayedCaptureHotkey=NormalizeHotkey(DelayedCaptureHotkey,"");
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
    private static string NormalizeHotkey(string? value,string fallback)=>string.IsNullOrWhiteSpace(value)?"":ToolHotkey.TryParse(value,out _)?value.Trim():fallback;
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
                ?? throw new JsonException(L.T("Settings are empty"));
            if (settings.Version != 1) throw new JsonException(L.T("Unsupported settings version"));
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LoadWarning = L.F("Could not read settings. Defaults are in use: {0}", ex.Message);
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
