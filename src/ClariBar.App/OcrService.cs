using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using ClariBar.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using L = ClariBar.Core.Localizer;

namespace ClariBar.App;

internal static partial class OcrService
{
    public static string[] Languages => OcrEngine.AvailableRecognizerLanguages.Select(language => language.LanguageTag).ToArray();
    public static async Task<(string Text, string Language)> LocalAsync(PixelFrame frame, string preference, CancellationToken cancellation = default)
    {
        var output=await LayoutAsync(frame,preference,cancellation);
        return (string.Join(Environment.NewLine,output.Lines.Select(line=>line.Source)),output.Language);
    }
    internal static async Task<(TranslationLine[] Lines,string Language)> LayoutAsync(PixelFrame frame,string preference,CancellationToken cancellation=default)
    {
        OcrEngine? engine;
        if (preference != "Auto") engine = OcrEngine.TryCreateFromLanguage(new Language(preference));
        else
        {
            // Choose the Windows UI language before falling back to the installed engines.
            string system = AppLocalization.SystemLanguage;
            engine = OcrEngine.IsLanguageSupported(new Language(system)) ? OcrEngine.TryCreateFromLanguage(new Language(system)) : null;
            engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null && OcrEngine.AvailableRecognizerLanguages.FirstOrDefault() is { } available) engine = OcrEngine.TryCreateFromLanguage(available);
        }
        if (engine is null) throw new InvalidOperationException(L.T("No local OCR language is available. Install a language with OCR in Windows Settings, or configure AI OCR."));
        using var stream = new InMemoryRandomAccessStream(); await stream.WriteAsync((await frame.PngAsync()).AsBuffer()); stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        double scale = Math.Min(1, OcrEngine.MaxImageDimension / (double)Math.Max(frame.Width, frame.Height));
        // Enlarge small text without exceeding the OCR engine's maximum image dimension.
        if (scale == 1 && Math.Max(frame.Width, frame.Height) < 1400) scale = Math.Min(2, OcrEngine.MaxImageDimension / (double)Math.Max(frame.Width, frame.Height));
        var transform = new BitmapTransform { ScaledWidth = (uint)Math.Max(1, Math.Round(frame.Width * scale)), ScaledHeight = (uint)Math.Max(1, Math.Round(frame.Height * scale)), InterpolationMode = BitmapInterpolationMode.Cubic };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        cancellation.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellation);
        var lines=result.Lines.Where(line=>!string.IsNullOrWhiteSpace(line.Text) && line.Words.Count>0).Select(line=>
        {
            double x=line.Words.Min(word=>word.BoundingRect.X)/scale,y=line.Words.Min(word=>word.BoundingRect.Y)/scale;
            double right=line.Words.Max(word=>word.BoundingRect.Right)/scale,bottom=line.Words.Max(word=>word.BoundingRect.Bottom)/scale;
            return new TranslationLine(line.Text,new PixelRect((int)Math.Floor(x),(int)Math.Floor(y),(int)Math.Ceiling(right-x),(int)Math.Ceiling(bottom-y)).Intersect(frame.Bounds)){TextHeight=(int)Math.Ceiling(bottom-y)};
        }).ToArray();
        return (lines,engine.RecognizerLanguage.LanguageTag);
    }
}

/// <summary>Current-user DPAPI storage, deliberately separate from exported application settings.</summary>
internal static class OcrCredential
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    private static string FilePath => Path.Combine(AppPaths.Data, "ocr-credential.bin");
    public static bool Exists => File.Exists(FilePath);
    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new Blob { Size = data.Length, Data = Marshal.AllocHGlobal(data.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            bool ok = protect ? CryptProtectData(ref input, "ClariBar AI OCR", 0, 0, 0, 1, out output) : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            byte[] result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { if (output.Data != 0) LocalFree(output.Data); if (input.Data != 0) { for (int i = 0; i < input.Size; i++) Marshal.WriteByte(input.Data, i, 0); Marshal.FreeHGlobal(input.Data); } }
    }
    public static void Save(string key)
    {
        byte[] plain = Encoding.UTF8.GetBytes(key);
        try { Directory.CreateDirectory(AppPaths.Data); File.WriteAllBytes(FilePath, Transform(plain, true)); }
        finally { Array.Clear(plain); }
    }
    public static string Read()
    {
        if (!Exists) return "";
        byte[] plain = Transform(File.ReadAllBytes(FilePath), false);
        try { return Encoding.UTF8.GetString(plain); } finally { Array.Clear(plain); }
    }
    public static void Clear() { if (File.Exists(FilePath)) File.Delete(FilePath); }
}
