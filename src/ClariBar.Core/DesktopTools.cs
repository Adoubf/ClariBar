using System.Globalization;

namespace ClariBar.Core;

public static class DesktopCommand
{
    public static int FromArguments(IEnumerable<string> arguments)
    {
        foreach(string argument in arguments)
            switch(argument)
            {
                case "--capture":return 10;
                case "--pick-color":return 11;
                case "--pin":return 12;
                case "--ocr":return 13;
                case "--translate":return 22;
                case "--hide-pins":return 14;
                case "--delayed-capture":return 16;
            }
        return 0;
    }
}

/// <summary>Physical-pixel rectangle, including desktops with a negative origin.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;
    public PixelRect Intersect(PixelRect other)
    {
        int left = Math.Max(X, other.X), top = Math.Max(Y, other.Y);
        return new(left, top, Math.Max(0, Math.Min(Right, other.Right) - left), Math.Max(0, Math.Min(Bottom, other.Bottom) - top));
    }
    public static PixelRect Between(int x1, int y1, int x2, int y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
}

public readonly record struct ScreenColor(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public string Rgb => $"rgb({R}, {G}, {B})";
    public (double H, double S, double L) HslValues
    {
        get
        {
            double r = R / 255d, g = G / 255d, b = B / 255d;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, delta = max - min;
            if (delta == 0) return (0, 0, l * 100);
            double h = max == r ? ((g - b) / delta + (g < b ? 6 : 0)) : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
            return (h * 60, delta / (1 - Math.Abs(2 * l - 1)) * 100, l * 100);
        }
    }
    public string Hsl
    {
        get { var (h, s, l) = HslValues; return FormattableString.Invariant($"hsl({h:0.#}, {s:0.#}%, {l:0.#}%)"); }
    }
    public ScreenColor Shade(double lightnessDelta)
    {
        var (h, s, l) = HslValues;
        return FromHsl(h, s, Math.Clamp(l + lightnessDelta, 0, 100));
    }
    public static ScreenColor FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 60; s = Math.Clamp(s, 0, 100) / 100; l = Math.Clamp(l, 0, 100) / 100;
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = l - c / 2;
        var (r, g, b) = h switch { < 1 => (c, x, 0d), < 2 => (x, c, 0d), < 3 => (0d, c, x), < 4 => (0d, x, c), < 5 => (x, 0d, c), _ => (c, 0d, x) };
        return new((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
    public static bool TryParse(string? value, out ScreenColor color)
    {
        color = default;
        string text = value?.Trim().TrimStart('#') ?? "";
        if (text.Length != 6 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)) return false;
        color = new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); return true;
    }
    public static string[] Remember(IEnumerable<string>? history, string hex) =>
        new[] { hex }.Concat(history ?? []).Where(AppSettings.ValidColor).Select(x => x.ToUpperInvariant()).Distinct().Take(24).ToArray();
}

public readonly record struct ToolHotkey(uint Modifiers, uint Key)
{
    public static bool TryParse(string? text, out ToolHotkey hotkey)
    {
        hotkey = default;
        var parts = (text ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint mods = 0, key = 0;
        foreach (string part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": mods |= 2; break;
                case "ALT": mods |= 1; break;
                case "SHIFT": mods |= 4; break;
                case "WIN": case "WINDOWS": mods |= 8; break;
                default:
                    if (key != 0) return false;
                    if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0])) key = char.ToUpperInvariant(part[0]);
                    else if (part.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(part.AsSpan(1), out int f) && f is >= 1 and <= 24) key = (uint)(111 + f);
                    else return false;
                    break;
            }
        }
        if (key == 0 || mods == 0 && key is not (>=112 and <=135)) return false;
        hotkey = new(mods, key); return true;
    }
}
