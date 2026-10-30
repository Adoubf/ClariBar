using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    [DllImport("user32.dll")]private static extern bool ClientToScreen(nint window,ref DesktopCapture.Point point);
    private async Task CheckSnipasteParityAsync(Action<bool,string> require)
    {
        string fixtures=Path.Combine(AppPaths.Cache,"temp","parity-fixtures");Directory.CreateDirectory(fixtures);
        var limit=new PixelRect(-1920,-200,4480,1640);
        require(CaptureGeometry.Move(new(-1900,-150,300,200),-1000,-1000,limit)==new PixelRect(-1920,-200,300,200),"Selection movement clips correctly on a negative-coordinate monitor");
        require(CaptureGeometry.Resize(new(20,20,40,30),"NW",100,100,new(0,0,100,100))==new PixelRect(59,49,1,1),"Selection resize cannot cross the opposite edge");
        require(CaptureGeometry.Resize(new(20,20,40,30),"SE",100,100,new(0,0,100,100))==new PixelRect(20,20,80,80),"Selection resize clips to the physical image");
        var square=CaptureGeometry.Constrain(50,50,20,70,true,false);var angle=CaptureGeometry.Constrain(0,0,80,70,false,true);
        require(square==(20d,80d) && Math.Abs(angle.X-angle.Y)<.001,"Shift constrains square geometry and 45-degree lines");
        require(ToolHotkey.TryParse("F1",out var f1) && f1.Key==0x70 && ToolHotkey.TryParse("f24",out _),"Unmodified function keys can replace Snipaste shortcuts without requiring a modifier");

        static PixelFrame Solid(int width,int height,byte blue,byte green,byte red,byte alpha=255)
        {byte[] data=new byte[width*height*4];for(int i=0;i<data.Length;i+=4){data[i]=(byte)(blue*alpha/255);data[i+1]=(byte)(green*alpha/255);data[i+2]=(byte)(red*alpha/255);data[i+3]=alpha;}return new(width,height,data);}
        var transparent=Solid(120,100,0,0,255,128);var white=Solid(320,220,255,255,255);
        string png=Path.Combine(fixtures,"transparent.png");await File.WriteAllBytesAsync(png,await transparent.PngAsync());
        var loaded=await ImageContent.FromFileAsync(png);
        require(loaded.Frame.Pixels.SequenceEqual(transparent.Pixels) && loaded.Frame.ColorAt(0,0)==new ScreenColor(255,0,0),"PNG import preserves premultiplied alpha and unpremultiplied color values");
        await transparent.CopyAsync();var clipboard=await PixelFrame.FromClipboardAsync();
        require(clipboard is not null && clipboard.Pixels.SequenceEqual(transparent.Pixels),"Real Windows clipboard retains transparent PNG pixels");
        var marked=white.Render([new("Marker",[new(20,80),new(280,80)],"#FF0000",30){Alpha=90}]);
        require(marked.ColorAt(100,80).R==255 && marked.ColorAt(100,80).G is >130 and <210 && marked.ColorAt(5,5)==new ScreenColor(255,255,255),"Marker alpha blends with the source only within the stroke");
        var erased=white.Render([new("Rectangle",[new(20,20),new(200,180)],"#FF0000",4){Fill="Filled"},new("Eraser",[new(50,50),new(100,100)],"#000000",20){Fill="Rectangle"}]);
        require(erased.ColorAt(70,70)==new ScreenColor(255,255,255) && erased.ColorAt(30,30)==new ScreenColor(255,0,0),"Eraser restores original pixels and retains surrounding annotations");
        var erasedAlpha=transparent.Render([new("Rectangle",[new(0,0),new(120,100)],"#0000FF",2){Fill="Filled"},new("Eraser",[new(20,20),new(100,80)],"#000000",20){Fill="Rectangle"}]);
        require(erasedAlpha.Pixels[(40*120+40)*4+3]==128 && erasedAlpha.ColorAt(40,40)==new ScreenColor(255,0,0),"Erasing on a transparent source restores its original alpha");
        var mosaicAlpha=transparent.Render([new("Mosaic",[new(20,20),new(100,80)],"#000000",3)]);
        require(mosaicAlpha.Pixels.SequenceEqual(transparent.Pixels),"Mosaic does not double the alpha of a uniform transparent source");
        var sharp=white.Render([new("Rectangle",[new(20,20),new(160,200)],"#000000",2){Fill="Filled"}]);
        var blurred=sharp.Render([new("Blur",[new(80,50),new(240,180)],"#000000",6)]);
        require(blurred.ColorAt(159,100).R is >0 and <255 && blurred.ColorAt(5,5)==sharp.ColorAt(5,5),"Gaussian blur softens an edge without affecting pixels outside its region");
        var styled=white.Render([new("Text",[new(110,110),new(150,150)],"#0044CC",3,"Text 文字"){FontSize=30,Bold=true,Italic=true,Rotation=25}]);
        require(!styled.Pixels.SequenceEqual(white.Pixels),"Rotated bold italic text with Chinese glyphs renders into exported pixels");
        var document=new AnnotationDocument(transparent);var stroke=new CaptureMark("Pen",[new(10,10),new(80,80)],"#00FF00",4);
        document.Add(stroke);document.Undo();require(document.Render().Pixels.SequenceEqual(transparent.Pixels) && document.CanRedo,"Undo restores transparent source pixels and retains redo");
        document.Redo();document.Clear();require(!document.CanRedo && !document.CanUndo,"Clear annotations discards the undo and redo history");
        var rotate=loaded.Frame;for(int i=0;i<4;i++)rotate=AnnotationRenderer.Transform(rotate,1);
        require(rotate.Width==transparent.Width && rotate.Pixels.SequenceEqual(transparent.Pixels),"Four quarter-turns preserve all image pixels");
        var flipped=AnnotationRenderer.Transform(AnnotationRenderer.Transform(styled,0,true,true),0,true,true);
        require(flipped.Pixels.SequenceEqual(styled.Pixels),"Horizontal and vertical mirror operations are reversible");

        string tga=Path.Combine(fixtures,"orientation.tga");await File.WriteAllBytesAsync(tga,Convert.FromBase64String("AAACAAAAAAAAAAAAAgACABgAAAD//wAAAP8A////"));
        var tgaImage=(await ImageContent.FromFileAsync(tga)).Frame;
        require(tgaImage.Width==2 && tgaImage.ColorAt(0,0)==new ScreenColor(0,255,0) && tgaImage.ColorAt(1,0)==new ScreenColor(255,255,255) && tgaImage.ColorAt(0,1)==new ScreenColor(255,0,0),"TGA import respects bottom-left image origin");
        string gif=Path.Combine(fixtures,"animation.gif");await File.WriteAllBytesAsync(gif,Convert.FromBase64String("R0lGODlhAgACAIAAAP8AAAAA/yH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgACAAACBARBEAUAIfkEAAwAAAAsAAAAAAIAAgAAAgQMwzAFADs="));
        var animated=await ImageContent.FromFileAsync(gif);
        using(var animation=new ImageAnimation(animated.AnimationBytes!))
        {require(animation.Count==2 && animation.Duration(0)==80 && animation.Duration(1)==120 && animation.Frame(0).ColorAt(0,0)==new ScreenColor(255,0,0) && animation.Frame(1).ColorAt(0,0)==new ScreenColor(0,0,255),"Animated GIF retains both frame colors and individual delays");}
        foreach(string format in new[]{"png","jpg","bmp","webp"})
        {
            string path=Path.Combine(fixtures,"export."+format);await File.WriteAllBytesAsync(path,await ImageExport.EncodeAsync(transparent,format,92));
            var decoded=(await ImageContent.FromFileAsync(path)).Frame;
            require(decoded.Width==transparent.Width && decoded.Height==transparent.Height,"Export produces a decodable "+format.ToUpperInvariant()+" file");
            if(format=="jpg")require(decoded.Pixels[3]==255 && decoded.ColorAt(5,5).G is >110 and <140,"JPEG composites transparent pixels on white");
        }
        var naming=new AppSettings {CaptureFormat="png",CaptureNamePattern="same:name"};
        string first=await ImageExport.QuickAsync(white,naming,fixtures),second=await ImageExport.QuickAsync(transparent,naming,fixtures);
        require(first!=second && !Path.GetFileName(first).Contains(':') && (await ImageContent.FromFileAsync(first)).Frame.Width==white.Width,"Quick Save sanitizes names and preserves existing files on collision");
        var package=new DataPackage();package.SetText("Plain text 文字");
        var text=await ImageContent.ClipboardAsync(settings,supplied:package.GetView());
        require(text is {Kind:"Text",PlainText:"Plain text 文字"} && text.Frame.Width>20,"Clipboard text converts to an image and retains its copyable source");
        package=new();package.SetHtmlFormat(HtmlFormatHelper.CreateHtmlFormat("<p><b>Hello</b> <span style='color:#ff0000'>世界</span></p>"));package.SetText("Hello 世界");
        var html=await ImageContent.ClipboardAsync(settings,supplied:package.GetView());
        require(html is {Kind:"Html"} && html.Html.Contains("#ff0000") && html.PlainText.Contains("Hello"),"Clipboard HTML keeps text and renders basic offline rich text");
        var plainSettings=new AppSettings {PinIgnoreHtml=true};var ignored=await ImageContent.ClipboardAsync(plainSettings,supplied:package.GetView());
        require(ignored is {Kind:"Text",PlainText:"Hello 世界"},"Ignore HTML preference falls back to clipboard plain text");
        package=new();package.SetText("rgb(80, 190, 210)");var swatch=await ImageContent.ClipboardAsync(settings,supplied:package.GetView());
        require(swatch is {Kind:"Color"} && swatch.Frame.ColorAt(5,5)==new ScreenColor(80,190,210),"Clipboard color values create an accurate color card");
        package=new();package.SetStorageItems([await StorageFile.GetFileFromPathAsync(png)]);
        var fileImage=await ImageContent.ClipboardAsync(settings,supplied:package.GetView());var fileText=await ImageContent.ClipboardAsync(settings,true,package.GetView());
        require(fileImage?.Frame.Pixels.SequenceEqual(transparent.Pixels)==true && fileText is {Kind:"Text"} && fileText.PlainText==png,"Copied image files paste as images and optionally as file paths");

        bool deactivate=settings.CaptureCancelOnDeactivate;settings.CaptureCancelOnDeactivate=false;
        try
        {
            var scene=Solid(960,540,247,247,247);var bounds=DesktopCapture.DesktopBounds;var chosen=new PixelRect(70,60,480,280);
            var overlay=new CaptureOverlay(desktopTools,scene,bounds,[],false,preset:chosen);desktopTools.Track(overlay);var selected=overlay.SelectAsync();await Task.Delay(220);
            require(!selected.IsCompleted && overlay.Document?.Source.Width==480,"A completed selection remains open for inline annotation");
            overlay.Document!.Add(stroke);var setArea=typeof(CaptureOverlay).GetMethod("SetArea",BindingFlags.Instance|BindingFlags.NonPublic)!;
            setArea.Invoke(overlay,[new PixelRect(100,100,420,250),true,null]);
            require(overlay.Document.Source.Width==420 && overlay.Document.Marks.Count==1,"Moving and resizing the selection preserve inline annotations");
            await RenderCheckImageAsync(L.LanguageCode+"-inline-capture",(FrameworkElement)overlay.Content);
            var output=(Task)typeof(CaptureOverlay).GetMethod("OutputAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(overlay,["Copy"])!;await output;
            var result=await selected;
            require(result is {Action:"Copy",Image.Width:420,Image.Height:250} && result.Marks?.Length==1,"Inline Copy produces the selected pixels and associated annotations");
            require(desktopTools.Sessions.LastArea==new PixelRect(bounds.X+100,bounds.Y+100,420,250) && desktopTools.Sessions.History.Count>0,"Only a successful capture updates last region and capture history");
            int count=desktopTools.Sessions.History.Count;var cancelled=new CaptureOverlay(desktopTools,scene,bounds,[],false);desktopTools.Track(cancelled);var cancelTask=cancelled.SelectAsync();await Task.Delay(100);cancelled.Close();await cancelTask;
            require(desktopTools.Sessions.History.Count==count,"Cancelling a capture does not create a history record");
        }
        finally{settings.CaptureCancelOnDeactivate=deactivate;}

        var pins=desktopTools.Pins;
        await transparent.CopyAsync();int previousCount=pins.Images.Count;await pins.PasteAsync();var pin=pins.Images.Last();
        require(pins.Images.Count==previousCount+1 && DesktopCapture.IsWindowVisible(pin.Window.Handle),"Paste creates a visible topmost image window");
        var backdrop=new Window {Content=new Grid {Background=ToolUi.Brush("#00FF00")}};desktopTools.Track(backdrop);
        if(backdrop.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter backdropPresenter){backdropPresenter.SetBorderAndTitleBar(false,false);backdropPresenter.IsAlwaysOnTop=true;}
        var work=Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;backdrop.AppWindow.MoveAndResize(new(work.X+100,work.Y+100,300,250));backdrop.Activate();
        try
        {
            pin.Window.SetVisible(false);await Task.Delay(200);
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,"pin-green-backdrop.png"),await DesktopCapture.Capture(new(work.X+135,work.Y+135,100,80)).PngAsync());
            pin.Window.SetVisible(true);
            pin.Window.AppWindow.Move(new(work.X+130,work.Y+130));pin.Window.Activate();await Task.Delay(350);DesktopCapture.DwmFlush();
            var composed=DesktopCapture.Capture(new(work.X+135,work.Y+135,100,80));var center=composed.ColorAt(60,50);
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,"pin-alpha-composition.png"),await composed.PngAsync());
            require(Math.Abs(center.R-128)<12 && Math.Abs(center.G-127)<12 && center.B<12,"Transparent pin composits over a controlled green desktop surface");
        }
        finally{backdrop.Close();}
        pins.Close(pin);require(pin.Record.State=="Closed" && !DesktopCapture.IsWindowVisible(pin.Window.Handle),"Close hides a recoverable image without destroying it");
        await pins.PasteAsync();require(pins.Images.Count==previousCount+1 && ReferenceEquals(pins.Images.Last(),pin) && pin.Record.State=="Visible","Paste with unchanged clipboard recovers the closed window");
        pins.ToggleAll();require(!DesktopCapture.IsWindowVisible(pin.Window.Handle) && pin.Record.State=="Visible","Hide all is distinct from recoverable close");pins.ToggleAll();
        pins.AddGroup("Fixture group");require(!DesktopCapture.IsWindowVisible(pin.Window.Handle),"Switching image groups hides images from other groups");
        pins.MoveToGroup(pin,"Fixture group");require(DesktopCapture.IsWindowVisible(pin.Window.Handle),"Moving an image to the active group shows it");
        require(pins.RenameGroup("Fixture group","Renamed fixture") && pin.Record.Group=="Renamed fixture" && !pins.RenameGroup("Default","Other"),"Group rename updates member images and protects the default group");
        pin.Window.SetClickThrough(true);require(pin.Record.ClickThrough,"Pin click-through state is explicit and persisted");pin.Window.SetClickThrough(false);
        pin.Record.Turns=1;pin.Record.FlipX=true;pin.Record.Opacity=65;pin.Document.Add(stroke);pin.Window.Refresh();pin.Window.UpdateRecord();
        await pins.PersistAsync();var session=desktopTools.Sessions.LoadPins();var saved=session.Pins.Single(x=>x.Id==pin.Record.Id);
        require(saved.Turns==1 && saved.FlipX && saved.Opacity==65 && saved.Marks.Length==1 && File.Exists(desktopTools.Sessions.PinPath(saved.Id)),"Pin backup retains source pixels, transforms, opacity and editable annotations");
        var restoredTools=new DesktopToolsController(this,settings,()=>{});
        try
        {
            await restoredTools.Pins.RestoreAsync();var restored=restoredTools.Pins.Images.Single(x=>x.Record.Id==pin.Record.Id);
            require(restored.Render().Pixels.SequenceEqual(pin.Render().Pixels) && restored.Record.Group==pin.Record.Group && restored.Record.Opacity==65,"Restoring a pin session reproduces its pixels, group and opacity");
        }
        finally{restoredTools.CloseAll();}
        pins.Destroy(pin);require(!pins.Images.Contains(pin) && pin.Record.State=="Destroyed","Destroy permanently removes a pin from recovery and persistence");

        ShowSettings();await Task.Delay(200);DesktopCapture.Point origin=new();ClientToScreen(hwnd,ref origin);
        var helpAt=HelpButton.TransformToVisual(Root).TransformPoint(new(HelpButton.ActualWidth/2,HelpButton.ActualHeight/2));double dpi=Root.XamlRoot.RasterizationScale;
        int screenX=origin.X+(int)(helpAt.X*dpi),screenY=origin.Y+(int)(helpAt.Y*dpi);
        DesktopCapture.GetWindowRect(hwnd,out var windowRect);var window=new DetectedWindow(hwnd,"Controlled ClariBar settings",windowRect.Pixel);var detector=new ElementDetector();PixelRect? element=null;
        for(int attempt=0;attempt<8;attempt++){element=await detector.AtAsync(window,screenX,screenY,DesktopCapture.DesktopBounds);if(element is {} box && box.Width<windowRect.Pixel.Width/2)break;await Task.Delay(180);}
        require(element is {} detected && detected.Contains(screenX,screenY) && detected.Width<windowRect.Pixel.Width/2,"UI element detection identifies a controlled settings button inside its window");
        await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"snipaste-parity-details.json"),JsonSerializer.Serialize(new {language=L.LanguageCode,scope="Snipaste 2 Free workflows plus OCR",fixtures,history=desktopTools.Sessions.History.Count,uiElement=element},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
    }
}
