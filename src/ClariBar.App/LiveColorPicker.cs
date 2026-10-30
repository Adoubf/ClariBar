using System.ComponentModel;
using System.Runtime.InteropServices;
using ClariBar.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SkiaSharp;
using Windows.Graphics;

namespace ClariBar.App;

internal sealed class LiveColorPicker:IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void InputCallback(int action,int x,int y,int data);
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]private static extern int CB_BeginLivePicker(InputCallback callback,int observeOnly);
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]private static extern int CB_UpdateLivePicker(int x,int y,int width,int height,byte[] pixels);
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]private static extern void CB_EndLivePicker();
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]private static extern nint CB_LivePickerWindow();
    [DllImport("user32.dll")]private static extern nint WindowFromPoint(DesktopCapture.Point point);
    private readonly MainWindow owner;
    private readonly DesktopToolsController tools;
    private readonly string? leftAction;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    private readonly InputCallback callback;
    private readonly TaskCompletionSource<CaptureSelection?> completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool started,finished;
    private PixelFrame? preview;
    internal ScreenColor Color {get;private set;}
    internal PixelRect SampleBounds {get;private set;}
    internal PixelRect PreviewBounds {get;private set;}
    internal int Zoom {get;private set;}
    internal nint PreviewHandle=>CB_LivePickerWindow();
    internal LiveColorPicker(MainWindow owner,DesktopToolsController tools,string? leftAction=null)
    {
        this.owner=owner;this.tools=tools;this.leftAction=leftAction;
        callback=(action,x,y,data)=>owner.DispatcherQueue.TryEnqueue(()=>
        {
            if(finished)return;
            try
            {
                if(action==0){Dispose();return;}
                if(action==4){ChangeZoom(data);RefreshAt(new(){X=x,Y=y});return;}
                string mode=action==1?this.leftAction??tools.Settings.ColorLeftAction:action==2?tools.Settings.ColorMiddleAction:tools.Settings.ColorRightAction;
                CompleteAt(new(){X=x,Y=y},mode);
            }
            catch(Exception ex){Fail(ex);}
        });
        timer.Tick+=(_,_)=>{if(!finished)try{DesktopCapture.GetCursorPos(out var point);RefreshAt(point);}catch(Exception ex){Fail(ex);}};
    }
    internal Task<CaptureSelection?> SelectAsync(bool observeOnly=false)
    {
        // App-owned UI checks must never intercept the user's desktop input.
        observeOnly|=Environment.GetCommandLineArgs().Contains("--desktop-tools-check");
        int error=CB_BeginLivePicker(callback,observeOnly?1:0);if(error!=0)throw new Win32Exception(error);
        started=true;DesktopCapture.GetCursorPos(out var point);RefreshAt(point);timer.Start();return completion.Task;
    }
    internal void ChangeZoom(int delta)=>Zoom=Math.Clamp(Zoom+(delta>0?1:-1),0,5);
    internal void RefreshAt(DesktopCapture.Point point)
    {
        if(finished)return;
        double scale=DesktopWindowDpi.For(WindowFromPoint(point))/96d;
        SampleBounds=new PixelRect(point.X-24,point.Y-24,49,49).Intersect(DesktopCapture.DesktopBounds);
        if(preview is not null && !PreviewBounds.Intersect(SampleBounds).IsEmpty)
        {
            var relocated=Place(point,preview.Width,preview.Height,(int)Math.Ceiling(28*scale));
            CB_UpdateLivePicker(relocated.X,relocated.Y,preview.Width,preview.Height,preview.Pixels);DesktopCapture.DwmFlush();
        }
        // Capture only the current neighborhood. No full-screen window, cached desktop or mouse capture.
        var sample=DesktopCapture.Capture(SampleBounds);int x=point.X-SampleBounds.X,y=point.Y-SampleBounds.Y;Color=sample.ColorAt(x,y);
        preview=Render(sample,x,y,scale);PreviewBounds=Place(point,preview.Width,preview.Height,(int)Math.Ceiling(28*scale));
        int updated=CB_UpdateLivePicker(PreviewBounds.X,PreviewBounds.Y,preview.Width,preview.Height,preview.Pixels);
        if(updated!=1)throw new Win32Exception(updated<0?-updated:31);
    }
    private static PixelRect Place(DesktopCapture.Point point,int width,int height,int gap)
    {
        var monitor=DisplayArea.GetFromPoint(new(point.X,point.Y),DisplayAreaFallback.Nearest).OuterBounds;
        int x=point.X+gap,y=point.Y+gap;
        if(x+width>monitor.X+monitor.Width)x=point.X-width-gap;
        if(y+height>monitor.Y+monitor.Height)y=point.Y-height-gap;
        return new(Math.Clamp(x,monitor.X,Math.Max(monitor.X,monitor.X+monitor.Width-width)),Math.Clamp(y,monitor.Y,Math.Max(monitor.Y,monitor.Y+monitor.Height-height)),width,height);
    }
    private PixelFrame Render(PixelFrame sample,int x,int y,double scale)
    {
        bool dark=tools.Theme==ElementTheme.Dark || tools.Theme==ElementTheme.Default && Application.Current.RequestedTheme==ApplicationTheme.Dark;
        string text=ColorFormats.DefaultText(Color,tools.Settings),name=tools.Settings.ColorShowName?ColorFormats.Name(Color):"";
        using var face=SKTypeface.FromFamilyName("Microsoft YaHei UI");using var mainFont=new SKFont(face,16);using var nameFont=new SKFont(face,12);
        int cardWidth=(int)Math.Clamp(mainFont.MeasureText(text)+78,180,350),width=Zoom>0?Math.Max(208,cardWidth):cardWidth,height=Zoom>0?272:72;
        using var bitmap=new SKBitmap(new SKImageInfo((int)Math.Ceiling(width*scale),(int)Math.Ceiling(height*scale),SKColorType.Bgra8888,SKAlphaType.Premul));using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.Transparent);canvas.Scale((float)scale);
        using var fill=new SKPaint{IsAntialias=true,Color=SKColor.Parse(dark?"#282828":"#FCFCFC")};using var border=new SKPaint{IsAntialias=true,Color=SKColor.Parse(dark?"#777777":"#AAB0B8"),Style=SKPaintStyle.Stroke,StrokeWidth=1};
        canvas.DrawRoundRect(new(1,1,width-1,height-1),8,8,fill);canvas.DrawRoundRect(new(1,1,width-1,height-1),8,8,border);
        int cardY=Zoom>0?202:2;
        if(Zoom>0)
        {
            int factor=new[]{4,8,12,16,24}[Zoom-1];using var lens=AnnotationRenderer.Bitmap(sample.Magnify(x,y,192,factor));canvas.DrawBitmap(lens,8,8);
            using var halo=new SKPaint{Color=SKColors.Black,StrokeWidth=3,Style=SKPaintStyle.Stroke};using var marker=new SKPaint{Color=SKColors.White,StrokeWidth=1,Style=SKPaintStyle.Stroke};
            var center=new SKRect(104-factor/2f,104-factor/2f,104+factor/2f,104+factor/2f);canvas.DrawRect(center,halo);canvas.DrawRect(center,marker);
        }
        fill.Color=SKColor.Parse(Color.Hex);canvas.DrawRoundRect(new(8,cardY+8,60,cardY+60),5,5,fill);
        using var foreground=new SKPaint{IsAntialias=true,Color=SKColor.Parse(dark?"#F7F7F7":"#242424")};canvas.DrawText(text,70,cardY+(name.Length>0?29:39),SKTextAlign.Left,mainFont,foreground);
        if(name.Length>0){foreground.Color=SKColor.Parse(dark?"#BBBBBB":"#717171");canvas.DrawText(name,70,cardY+49,SKTextAlign.Left,nameFont,foreground);}
        return AnnotationRenderer.Frame(bitmap);
    }
    internal void CompleteAt(DesktopCapture.Point point,string action)
    {
        if(finished)return;if(action=="Cancel"){Dispose();return;}RefreshAt(point);
        var result=new CaptureSelection(Color:Color,Action:action);Stop();completion.TrySetResult(result);
    }
    private void Fail(Exception error){Stop();completion.TrySetException(error);}
    private void Stop(){if(finished)return;finished=true;timer.Stop();if(started){CB_EndLivePicker();started=false;}}
    public void Dispose(){Stop();completion.TrySetResult(null);GC.KeepAlive(callback);}
}
