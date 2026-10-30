using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using ClariBar.Core;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ClariBar.App;

internal sealed record DetectedWindow(nint Handle, string Title, PixelRect Bounds);
internal sealed record CaptureMark(string Tool, Windows.Foundation.Point[] Points, string Color, int Width, string Text = "")
{
    public int Alpha { get; init; } = 255;
    public string Fill { get; init; } = "Outline";
    public string FontFamily { get; init; } = "Microsoft YaHei UI";
    public float FontSize { get; init; } = 22;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public float Rotation { get; init; }
}

internal sealed class PixelFrame(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;
    public PixelRect Bounds => new(0, 0, Width, Height);
    public ScreenColor ColorAt(int x, int y)
    {
        int i = (Math.Clamp(y, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)) * 4;
        int a=Pixels[i+3];return a==0?new(0,0,0):new((byte)Math.Min(255,Pixels[i+2]*255/a),(byte)Math.Min(255,Pixels[i+1]*255/a),(byte)Math.Min(255,Pixels[i]*255/a));
    }
    public PixelFrame Crop(PixelRect rectangle)
    {
        var area = rectangle.Intersect(Bounds);
        if (area.IsEmpty) throw new ArgumentOutOfRangeException(nameof(rectangle));
        byte[] data = new byte[checked(area.Width * area.Height * 4)];
        for (int y = 0; y < area.Height; y++) System.Buffer.BlockCopy(Pixels, ((y + area.Y) * Width + area.X) * 4, data, y * area.Width * 4, area.Width * 4);
        return new(area.Width, area.Height, data);
    }
    public WriteableBitmap Bitmap()
    {
        var bitmap = new WriteableBitmap(Width, Height);
        using var output = bitmap.PixelBuffer.AsStream(); output.Write(Pixels); bitmap.Invalidate();
        return bitmap;
    }
    public PixelFrame Magnify(int centerX,int centerY,int size,int factor)
    {
        if(size<=0 || factor<=0)throw new ArgumentOutOfRangeException(nameof(size));
        byte[] magnified=new byte[checked(size*size*4)];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            int sx=Math.Clamp(centerX+(int)Math.Floor((x-size/2d+factor/2d)/factor),0,Width-1);
            int sy=Math.Clamp(centerY+(int)Math.Floor((y-size/2d+factor/2d)/factor),0,Height-1);
            System.Buffer.BlockCopy(Pixels,(sy*Width+sx)*4,magnified,(y*size+x)*4,4);
        }
        return new(size,size,magnified);
    }
    public async Task<byte[]> PngAsync()
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)Width, (uint)Height, 96, 96, Pixels);
        await encoder.FlushAsync();
        byte[] png = new byte[checked((int)stream.Size)];
        stream.Seek(0);
        await stream.ReadAsync(png.AsBuffer(), (uint)png.Length, InputStreamOptions.None);
        return png;
    }
    public async Task CopyAsync()
    {
        await ImageClipboard.WriteAsync(this,await PngAsync());
    }
    public static async Task<PixelFrame?> FromClipboardAsync(DataPackageView? supplied=null)
    {
        IRandomAccessStream? png=null;
        if(supplied is null && await ImageClipboard.ReadPngAsync() is {} encoded)
        {png=new InMemoryRandomAccessStream();await png.WriteAsync(encoded.AsBuffer());png.Seek(0);}
        var content = supplied??await ClipboardTransfer.ReadAsync();
        if(png is null && content.Contains("PNG"))
        {
            try
            {
                var value=await content.GetDataAsync("PNG");
                if(value is RandomAccessStreamReference reference)png=await reference.OpenReadAsync();
                else if(value is IRandomAccessStream bytes){png=bytes;png.Seek(0);}
            }
            catch(COMException ex)when((uint)ex.HResult==0x800401D3 && content.Contains(StandardDataFormats.Bitmap)){}
        }
        if(png is null && !content.Contains(StandardDataFormats.Bitmap))return null;
        using var stream = png??await (await content.GetBitmapAsync()).OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        if ((long)decoder.PixelWidth * decoder.PixelHeight > 64_000_000) throw new IOException(Localizer.T("The image is too large."));
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        byte[] pixels = data.DetachPixelData();
        return new((int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight, pixels);
    }
    public PixelFrame Render(IEnumerable<CaptureMark> marks) => AnnotationRenderer.Render(this, marks);
}

internal static class ClipboardTransfer
{
    public static async Task SetAsync(DataPackage package)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetContent(package); Clipboard.Flush(); return; }
            catch (COMException ex) when ((uint)ex.HResult == 0x800401D0 && attempt < 7) { await Task.Delay(40 * (attempt + 1)); }
        }
    }
    public static async Task<DataPackageView> ReadAsync()
    {
        // Let WinRT observe native clipboard changes before returning a cached DataPackageView.
        await Task.Yield();
        for (int attempt = 0; ; attempt++)
        {
            try { return Clipboard.GetContent(); }
            catch (COMException ex) when ((uint)ex.HResult == 0x800401D0 && attempt < 7) { await Task.Delay(40 * (attempt + 1)); }
        }
    }
}

internal static partial class DesktopCapture
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public readonly PixelRect Pixel => new(Left, Top, Right - Left, Bottom - Top); }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint Used, Important;
    }
    internal delegate bool EnumCallback(nint window, nint parameter);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumCallback callback, nint parameter);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint parent, EnumCallback callback, nint parameter);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out Rect rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, System.Text.StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, System.Text.StringBuilder text, int length);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out Rect value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetCloaked(nint window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint dc, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] private static extern nint CreatePen(int style, int width, uint color);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    [DllImport("gdi32.dll")] private static extern bool MoveToEx(nint dc, int x, int y, nint previous);
    [DllImport("gdi32.dll")] private static extern bool LineTo(nint dc, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool Rectangle(nint dc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool Ellipse(nint dc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateFont(int height, int width, int escape, int orientation, int weight, uint italic, uint underline, uint strike, uint charset, uint precision, uint clipping, uint quality, uint family, string face);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool TextOut(nint dc, int x, int y, string text, int count);

    public static PixelRect DesktopBounds => new(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
    private sealed class Surface : IDisposable
    {
        public nint Dc { get; }
        public nint Bits { get; }
        private readonly nint bitmap, old;
        public Surface(int width, int height)
        {
            Dc = CreateCompatibleDC(0);
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(Dc, ref info, 0, out nint bits, 0, 0); Bits = bits;
            if (Dc == 0 || bitmap == 0 || bits == 0) { Dispose(); throw new IOException(Localizer.T("Could not capture the desktop.")); }
            old = SelectObject(Dc, bitmap);
        }
        public void Dispose() { if (old != 0) SelectObject(Dc, old); if (bitmap != 0) DeleteObject(bitmap); if (Dc != 0) DeleteDC(Dc); }
    }
    public static PixelFrame Capture(PixelRect bounds)
    {
        if (bounds.IsEmpty || (long)bounds.Width * bounds.Height > 64_000_000) throw new IOException(Localizer.T("The image is too large."));
        using var surface = new Surface(bounds.Width, bounds.Height); nint screen = GetDC(0);
        try { if (screen == 0 || !BitBlt(surface.Dc, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, 0x40CC0020)) throw new IOException(Localizer.T("Could not capture the desktop.")); }
        finally { if (screen != 0) ReleaseDC(0, screen); }
        byte[] data = new byte[checked(bounds.Width * bounds.Height * 4)]; Marshal.Copy(surface.Bits, data, 0, data.Length);
        for (int i = 3; i < data.Length; i += 4) data[i] = 255;
        return new(bounds.Width, bounds.Height, data);
    }
    public static List<DetectedWindow> Windows(PixelRect desktop, params nint[] excluded)
    {
        var result = new List<DetectedWindow>();
        EnumWindows((window, _) =>
        {
            if (excluded.Contains(window) || !IsWindowVisible(window) || IsIconic(window)) return true;
            if (DwmGetCloaked(window, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
            if (DwmGetWindowAttribute(window, 9, out var bounds, Marshal.SizeOf<Rect>()) != 0 && !GetWindowRect(window, out bounds)) return true;
            var area = bounds.Pixel.Intersect(desktop); if (area.Width < 10 || area.Height < 10) return true;
            var text = new System.Text.StringBuilder(512); GetWindowText(window, text, text.Capacity);
            // Shell surfaces without a title must not swallow every smaller application window.
            if(text.Length==0){var cls=new System.Text.StringBuilder(256);GetClassName(window,cls,cls.Capacity);if(cls.ToString() is "Progman" or "WorkerW" or "SysShadow")return true;text.Append(cls);}
            result.Add(new(window, text.ToString(), area)); return true;
        }, 0);
        return result;
    }
    public static PixelFrame Render(PixelFrame original, IEnumerable<CaptureMark> marks)
    {
        byte[] pixels = (byte[])original.Pixels.Clone();
        using var surface = new Surface(original.Width, original.Height);
        Marshal.Copy(pixels, 0, surface.Bits, pixels.Length);
        nint oldBrush = SelectObject(surface.Dc, GetStockObject(5));
        foreach (var mark in marks)
        {
            if (mark.Points.Length < 2) continue;
            var a = mark.Points[0]; var b = mark.Points[^1];
            var box = PixelRect.Between((int)a.X, (int)a.Y, (int)b.X, (int)b.Y).Intersect(original.Bounds);
            if (mark.Tool == "Mosaic")
            {
                Marshal.Copy(surface.Bits, pixels, 0, pixels.Length);
                const int block = 12;
                for (int y = box.Y; y < box.Bottom; y += block) for (int x = box.X; x < box.Right; x += block)
                {
                    long r = 0, g = 0, bl = 0; int count = 0;
                    for (int yy = y; yy < Math.Min(y + block, box.Bottom); yy++) for (int xx = x; xx < Math.Min(x + block, box.Right); xx++)
                    { int i = (yy * original.Width + xx) * 4; bl += pixels[i]; g += pixels[i + 1]; r += pixels[i + 2]; count++; }
                    for (int yy = y; yy < Math.Min(y + block, box.Bottom); yy++) for (int xx = x; xx < Math.Min(x + block, box.Right); xx++)
                    { int i = (yy * original.Width + xx) * 4; pixels[i] = (byte)(bl / count); pixels[i + 1] = (byte)(g / count); pixels[i + 2] = (byte)(r / count); }
                }
                Marshal.Copy(pixels, 0, surface.Bits, pixels.Length); continue;
            }
            nint pen = CreatePen(0, mark.Width, Native.ColorRef(mark.Color)); nint oldPen = SelectObject(surface.Dc, pen);
            try
            {
                if (mark.Tool == "Rectangle") Rectangle(surface.Dc, box.X, box.Y, box.Right, box.Bottom);
                else if (mark.Tool == "Ellipse") Ellipse(surface.Dc, box.X, box.Y, box.Right, box.Bottom);
                else if (mark.Tool == "Text")
                {
                    nint font = CreateFont(-Math.Max(16, mark.Width * 7), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 4, 0, "Microsoft YaHei UI");
                    nint oldFont = SelectObject(surface.Dc, font); SetBkMode(surface.Dc, 1); SetTextColor(surface.Dc, Native.ColorRef(mark.Color));
                    TextOut(surface.Dc, (int)a.X, (int)a.Y, mark.Text, mark.Text.Length); SelectObject(surface.Dc, oldFont); DeleteObject(font);
                }
                else
                {
                    MoveToEx(surface.Dc, (int)a.X, (int)a.Y, 0);
                    foreach (var p in mark.Points.Skip(1)) LineTo(surface.Dc, (int)p.X, (int)p.Y);
                    if (mark.Tool == "Arrow")
                    {
                        double angle = Math.Atan2(b.Y - a.Y, b.X - a.X), length = Math.Max(12, mark.Width * 4);
                        foreach (double offset in new[] { -.5, .5 })
                        { MoveToEx(surface.Dc, (int)b.X, (int)b.Y, 0); LineTo(surface.Dc, (int)(b.X - length * Math.Cos(angle + offset)), (int)(b.Y - length * Math.Sin(angle + offset))); }
                    }
                }
            }
            finally { SelectObject(surface.Dc, oldPen); DeleteObject(pen); }
        }
        SelectObject(surface.Dc, oldBrush); Marshal.Copy(surface.Bits, pixels, 0, pixels.Length);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return new(original.Width, original.Height, pixels);
    }
}
