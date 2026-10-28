using System.Security.Cryptography;

namespace ClariBar.Core;

[Flags]
public enum ColorScope { Monitor = 1, System = 2, All = Monitor | System }
public sealed record ColorScheme(string Name, string[] Colors);

public static class ColorSchemes
{
    public static IReadOnlyList<ColorScheme> Presets { get; } = new[]
    {
        new ColorScheme("Glacier Blue", ["#5CC8FF", "#8BAAFF", "#70E1E8", "#B0CBFF", "#56A9E8", "#97E8EF", "#AEC6FF", "#7CC9EB"]),
        new ColorScheme("Mint Green", ["#67D9AF", "#A4DE88", "#66CBBB", "#C4E6A1", "#87D9CC", "#B6E3BC", "#6BCB8A", "#B4E8D3"]),
        new ColorScheme("Sunset Orange", ["#FFB86C", "#FF8D85", "#F5D178", "#F6A3B2", "#EFB15F", "#FFC99B", "#FFAB91", "#E8CB9D"]),
        new ColorScheme("Soft Violet", ["#C1A0F5", "#E7B4FA", "#AFA8EF", "#E4ACC4", "#BCA5DE", "#D7C1F5", "#B8BDF0", "#F0C1E1"])
    };

    public static ColorScheme Random()
    {
        int start = RandomNumberGenerator.GetInt32(360);
        var colors = Enumerable.Range(0, 8).Select(index =>
            FromHsl((start + index * 43 + RandomNumberGenerator.GetInt32(12)) % 360, .68, .62)).ToArray();
        return new("Random palette", colors);
    }

    public static void Apply(AppSettings settings, IEnumerable<string> metricIds, ColorScheme scheme, ColorScope scope)
    {
        if (scheme.Colors.Length < 8 || scheme.Colors.Any(color => !AppSettings.ValidColor(color)))
            throw new ArgumentException(Localizer.T("A color palette requires 8 valid colors."), nameof(scheme));
        if ((scope & ColorScope.Monitor) != 0)
        {
            settings.MonitorColor = scheme.Colors[0];
            int index = 0;
            foreach (string id in metricIds.Distinct().Take(8)) settings.SensorColors[id] = scheme.Colors[index++];
        }
        if ((scope & ColorScope.System) != 0)
        {
            settings.TimeColor = scheme.Colors[0]; settings.DateColor = scheme.Colors[1];
            settings.TrayColor = scheme.Colors[2]; settings.NetworkColor = scheme.Colors[3];
            settings.VolumeColor = scheme.Colors[4]; settings.InputColor = scheme.Colors[5]; settings.BatteryColor = scheme.Colors[6];
            settings.TimeColorEnabled = settings.DateColorEnabled = settings.TrayColorEnabled = true;
            settings.NetworkColorEnabled = settings.VolumeColorEnabled = settings.InputColorEnabled = settings.BatteryColorEnabled = true;
        }
    }

    private static string FromHsl(double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = lightness - chroma / 2;
        (double r, double g, double b) = hue switch
        {
            < 60 => (chroma, x, 0d), < 120 => (x, chroma, 0d), < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma), < 300 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        return $"#{(int)Math.Round((r + m) * 255):X2}{(int)Math.Round((g + m) * 255):X2}{(int)Math.Round((b + m) * 255):X2}";
    }
}
