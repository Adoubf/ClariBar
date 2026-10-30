using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using SkiaSharp;
using Windows.Graphics.Imaging;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal static class ImageExport
{
    public static string Name(AppSettings settings)
    {
        string name=settings.CaptureNamePattern.Replace("{date}",DateTime.Now.ToString("yyyyMMdd")).Replace("{time}",DateTime.Now.ToString("HHmmss-fff")).Replace("{counter}",DateTime.Now.Ticks.ToString());
        foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');name=name.Trim(' ','.');return string.IsNullOrWhiteSpace(name)?"ClariBar":name;
    }
    public static async Task<byte[]> EncodeAsync(PixelFrame frame,string extension,int quality)
    {
        extension=extension.TrimStart('.').ToLowerInvariant();
        if(extension=="png")return await frame.PngAsync();
        if(extension is "jpg" or "jpeg" or "webp")
        {
            using var bitmap=AnnotationRenderer.Bitmap(frame);
            if(extension is "jpg" or "jpeg")
            {
                using var opaque=new SKBitmap(bitmap.Info.WithAlphaType(SKAlphaType.Opaque));using(var canvas=new SKCanvas(opaque)){canvas.Clear(SKColors.White);canvas.DrawBitmap(bitmap,0,0);}
                using var image=SKImage.FromBitmap(opaque);using var data=image.Encode(SKEncodedImageFormat.Jpeg,quality);return data.ToArray();
            }
            using(var image=SKImage.FromBitmap(bitmap))using(var data=image.Encode(SKEncodedImageFormat.Webp,quality))return data.ToArray();
        }
        using var stream=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)frame.Width,(uint)frame.Height,96,96,frame.Pixels);await encoder.FlushAsync();
        stream.Seek(0);byte[] bytes=new byte[(int)stream.Size];await stream.ReadAsync(bytes.AsBuffer(),(uint)bytes.Length,InputStreamOptions.None);return bytes;
    }
    public static async Task<string> QuickAsync(PixelFrame frame,AppSettings settings,string directory)
    {
        Directory.CreateDirectory(directory);string stem=Name(settings),extension=settings.CaptureFormat,path=Path.Combine(directory,stem+"."+extension);int count=1;
        while(File.Exists(path))path=Path.Combine(directory,stem+"-"+(count++)+"."+extension);
        await File.WriteAllBytesAsync(path,await EncodeAsync(frame,extension,settings.CaptureQuality));return path;
    }
    public static async Task<string?> SaveAsAsync(PixelFrame frame,Window owner,AppSettings settings)
    {
        var picker=new FileSavePicker {SuggestedFileName=Name(settings),SettingsIdentifier="ClariBarScreenshot"};
        foreach(var (name,extensions) in new[]{("PNG",new[]{".png"}),("JPEG",new[]{".jpg",".jpeg"}),("BMP",new[]{".bmp"}),("WebP",new[]{".webp"})})picker.FileTypeChoices.Add(name,extensions);
        picker.DefaultFileExtension="."+settings.CaptureFormat;
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(owner));
        var file=await picker.PickSaveFileAsync();if(file is null)return null;
        await File.WriteAllBytesAsync(file.Path,await EncodeAsync(frame,Path.GetExtension(file.Path),settings.CaptureQuality));return file.Path;
    }
}

internal static class ImagePrinter
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct PrintDialog
    {
        public uint Size;public nint Owner,DevMode,DevNames,Dc;public uint Flags;public ushort From,To,Min,Max,Copies;
        public nint Instance,CustomData,PrintHook,SetupHook,PrintTemplateName,SetupTemplateName,PrintTemplate,SetupTemplate;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct DocumentInfo
    {public int Size;[MarshalAs(UnmanagedType.LPWStr)]public string Name;public nint Output,DataType;public uint Type;}
    [StructLayout(LayoutKind.Sequential)]private struct DibInfo
    {public uint Size;public int Width,Height;public ushort Planes,Bits;public uint Compression,ImageSize;public int XPels,YPels;public uint Used,Important;}
    [DllImport("comdlg32.dll",CharSet=CharSet.Unicode)]private static extern bool PrintDlg(ref PrintDialog dialog);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)]private static extern int StartDoc(nint dc,ref DocumentInfo info);
    [DllImport("gdi32.dll")]private static extern int EndDoc(nint dc);
    [DllImport("gdi32.dll")]private static extern int AbortDoc(nint dc);
    [DllImport("gdi32.dll")]private static extern int StartPage(nint dc);
    [DllImport("gdi32.dll")]private static extern int EndPage(nint dc);
    [DllImport("gdi32.dll")]private static extern int GetDeviceCaps(nint dc,int index);
    [DllImport("gdi32.dll")]private static extern bool DeleteDC(nint dc);
    [DllImport("kernel32.dll")]private static extern nint GlobalFree(nint memory);
    [DllImport("gdi32.dll")]private static extern int StretchDIBits(nint dc,int x,int y,int width,int height,int sx,int sy,int sourceWidth,int sourceHeight,byte[] pixels,ref DibInfo info,uint color,uint operation);
    public static bool Print(PixelFrame frame,Window owner)
    {
        var dialog=new PrintDialog {Size=(uint)Marshal.SizeOf<PrintDialog>(),Owner=WinRT.Interop.WindowNative.GetWindowHandle(owner),Flags=0x100|0x4|0x8|0x40000,Copies=1,Min=1,Max=1,From=1,To=1};
        bool started=false;
        try
        {
            if(!PrintDlg(ref dialog))return false;
            var info=new DocumentInfo {Size=Marshal.SizeOf<DocumentInfo>(),Name="ClariBar"};if(StartDoc(dialog.Dc,ref info)<=0)throw new IOException(L.T("Could not start the print job."));started=true;StartPage(dialog.Dc);
            int width=GetDeviceCaps(dialog.Dc,8),height=GetDeviceCaps(dialog.Dc,10);double scale=Math.Min(width/(double)frame.Width,height/(double)frame.Height);
            int w=(int)(frame.Width*scale),h=(int)(frame.Height*scale);
            using var image=AnnotationRenderer.Bitmap(frame);using var opaque=new SKBitmap(image.Info.WithAlphaType(SKAlphaType.Opaque));using(var canvas=new SKCanvas(opaque)){canvas.Clear(SKColors.White);canvas.DrawBitmap(image,0,0);}
            var dib=new DibInfo {Size=40,Width=frame.Width,Height=-frame.Height,Planes=1,Bits=32};
            if(StretchDIBits(dialog.Dc,(width-w)/2,(height-h)/2,w,h,0,0,frame.Width,frame.Height,AnnotationRenderer.Frame(opaque).Pixels,ref dib,0,0xCC0020)==-1)throw new IOException(L.T("Could not print this image."));
            EndPage(dialog.Dc);EndDoc(dialog.Dc);started=false;return true;
        }
        finally{if(started)AbortDoc(dialog.Dc);if(dialog.Dc!=0)DeleteDC(dialog.Dc);if(dialog.DevMode!=0)GlobalFree(dialog.DevMode);if(dialog.DevNames!=0)GlobalFree(dialog.DevNames);}
    }
}
