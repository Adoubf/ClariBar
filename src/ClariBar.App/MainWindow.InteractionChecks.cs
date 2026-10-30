using System.Reflection;
using System.Runtime.InteropServices;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private static T InteractionField<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
    private async Task CheckAnnotationGesturesAsync(Action<bool,string> require)
    {
        var bytes=new byte[320*240*4];Array.Fill(bytes,(byte)255);var source=new PixelFrame(320,240,bytes);var document=new AnnotationDocument(source);var surface=new AnnotationSurface(document);var host=new Window{Content=surface};desktopTools.Track(host);host.Activate();await Task.Delay(150);
        try
        {
            var handles=InteractionField<Canvas>(surface,"handles");var preview=InteractionField<Canvas>(surface,"preview");
            surface.Ink.Tool="Arrow";surface.BeginInput(new(40,80));surface.CompleteInput(new(40,80));
            require(document.Marks.Count==0 && handles.Children.Count==0 && preview.Children.Count==0,"A single arrow click creates no arrowhead, mark or cyan handles");
            surface.BeginInput(new(40,80));surface.UpdateInput(new(41,81));surface.CompleteInput(new(41,81));require(document.Marks.Count==0,"Sub-threshold arrow jitter is discarded");
            surface.BeginInput(new(25,100));surface.UpdateInput(new(220,160));
            require(document.Marks.Count==0 && preview.Children.Count==2,"A dragged arrow previews both shaft and arrowhead before release without committing a mark");
            var live=await RenderOwnElementAsync(surface);int red=0;
            for(int y=143;y<153;y++)for(int x=200;x<216;x++){var color=live.ColorAt(x,y);if(color.R>200 && color.G<150 && color.B<150)red++;}
            require(red>0,"Rendered drag preview contains the arrowhead pixels before mouse release");
            surface.CompleteInput(new(220,160));require(document.Marks.Count==1 && preview.Children.Count==0,"Mouse release commits one arrow and removes the transient preview");
            surface.Undo();require(document.Marks.Count==0,"The completed arrow remains undoable");
            foreach(string tool in new[]{"Pen","Marker","Eraser","Mosaic"})
            {
                surface.Clear();surface.Ink.Tool=tool;surface.Ink.BrushMode="Brush";surface.BeginInput(new(80,70));surface.CompleteInput(new(80,70));
                require(handles.Children.Count==0,$"{tool} clicking does not create cyan object resize handles");
                surface.Clear();surface.BeginInput(new(20,20));surface.UpdateInput(new(120,110));surface.CompleteInput(new(220,180));var first=document.Marks[0].Points.ToArray();
                surface.BeginInput(new(70,70));surface.UpdateInput(new(90,90));surface.CompleteInput(new(110,80));
                require(document.Marks.Count==2 && document.Marks[0].Points.SequenceEqual(first) && document.Marks[1].Points[0]==new Point(70,70),$"{tool} starts a fresh stroke inside the previous stroke bounds without moving or stretching it");
                int count=document.Marks.Count;surface.BeginInput(new(10,10));surface.UpdateInput(new(250,10));surface.CancelInput();surface.UpdateInput(new(300,0));surface.CompleteInput(new(300,0));
                require(document.Marks.Count==count && preview.Children.Count==0,$"{tool} capture cancellation clears pending points and cannot draw a stray line");
            }
            surface.Clear();surface.Ink.Tool="Pen";surface.BeginInput(new(30,170));surface.UpdateInput(new(70,180));surface.CompleteInput(new(90,160));
            require(document.Marks.Single().Points.All(point=>point.Y>=160),"A stroke after cancellation contains no stale coordinates from the top of the image");
        }
        finally{host.Close();}
    }
    [StructLayout(LayoutKind.Sequential)]private struct PickerCursorInfo{public int Size,Flags;public nint Cursor;public DesktopCapture.Point Position;}
    [DllImport("user32.dll")]private static extern bool GetCursorInfo(ref PickerCursorInfo info);
    [DllImport("user32.dll")]private static extern nint GetCapture();
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint ReadPickerWindowStyle(nint window,int index);
    private async Task CheckLiveColorPickerAsync(Action<bool,string> require)
    {
        var grid=new Grid{Background=ToolUi.Brush("#E03020")};var host=new Window{Content=grid};desktopTools.Track(host);var work=Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
        ToolWindowGeometry.Borderless(host);host.AppWindow.MoveAndResize(new(work.X+100,work.Y+100,420,300));host.Activate();
        for(int attempt=0;attempt<100 && (grid.XamlRoot is null || !grid.IsLoaded || grid.ActualWidth<=0);attempt++)await Task.Delay(30);
        var client=ToolWindowGeometry.ClientBounds(host);var point=new DesktopCapture.Point{X=client.X+client.Width/2,Y=client.Y+client.Height/2};
        async Task AwaitSourcePixelAsync(ScreenColor expected)
        {
            ScreenColor actual=default;
            for(int attempt=0;attempt<100;attempt++)
            {
                DesktopCapture.DwmFlush();actual=DesktopCapture.Capture(new(point.X,point.Y,1,1)).ColorAt(0,0);
                if(actual==expected)return;await Task.Delay(30);
            }
            throw new IOException($"Live-picker test surface was not visible: expected={expected.Hex}, actual={actual.Hex}, loaded={grid.IsLoaded}, client={client}");
        }
        using var picker=new LiveColorPicker(this,desktopTools,"Copy");
        try
        {
            await AwaitSourcePixelAsync(new(224,48,32));
            var before=new PickerCursorInfo{Size=Marshal.SizeOf<PickerCursorInfo>()};GetCursorInfo(ref before);nint foreground=DesktopWindowState.Foreground,capture=GetCapture();
            var pending=picker.SelectAsync(observeOnly:true);InteractionField<DispatcherTimer>(picker,"timer").Stop();picker.RefreshAt(point);await Task.Delay(30);
            require(picker.Color==new ScreenColor(224,48,32),"The live picker samples the current visible application pixel");
            require(picker.SampleBounds.Width<=49 && picker.SampleBounds.Height<=49,"Live picking captures a small pointer neighborhood instead of freezing the desktop");
            var after=new PickerCursorInfo{Size=Marshal.SizeOf<PickerCursorInfo>()};GetCursorInfo(ref after);
            require(DesktopWindowState.Foreground==foreground && GetCapture()==capture && before.Cursor==after.Cursor,"The color preview does not activate a window, capture the mouse or replace its cursor");
            long style=ReadPickerWindowStyle(picker.PreviewHandle,-20).ToInt64();require((style&0x08080020)==0x08080020,"The native preview is layered, does not activate and allows mouse input to pass through");
            grid.Background=ToolUi.Brush("#2050E0");await AwaitSourcePixelAsync(new(32,80,224));picker.RefreshAt(point);
            require(picker.Color==new ScreenColor(32,80,224),"Changes in the underlying window appear immediately without restarting the picker");
            picker.ChangeZoom(120);picker.ChangeZoom(120);picker.RefreshAt(point);
            require(picker.Zoom==2 && picker.PreviewBounds.Height>200,"Wheel zoom shows an enlarged region around the pointer");
            grid.Background=ToolUi.Brush("#30B060");await AwaitSourcePixelAsync(new(48,176,96));picker.RefreshAt(point);
            require(picker.Color==new ScreenColor(48,176,96),"The magnifier also keeps sampling the live window while zoomed");
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-live-picker.png"),await InteractionField<PixelFrame>(picker,"preview").PngAsync());
            picker.CompleteAt(point,"Copy");require(await pending is {Action:"Copy",Color.R:48,Color.G:176,Color.B:96} && picker.PreviewHandle==0,"Completing a live pick returns the current color and closes the native preview");
        }
        finally{host.Close();}
    }
}
