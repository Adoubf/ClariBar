using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private static async Task<PixelFrame> RenderOwnElementAsync(FrameworkElement element)
    {
        var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(element);
        return new(bitmap.PixelWidth,bitmap.PixelHeight,(await bitmap.GetPixelsAsync()).ToArray());
    }
    private async Task CheckCaptureRefinementsAsync(Action<bool,string> require)
    {
        bool cancel=settings.CaptureCancelOnDeactivate;settings.CaptureCancelOnDeactivate=false;
        var desktop=DesktopCapture.DesktopBounds;byte[] pixels=new byte[checked(desktop.Width*desktop.Height*4)];
        for(int i=0;i<pixels.Length;i+=4){pixels[i]=235;pixels[i+1]=235;pixels[i+2]=235;pixels[i+3]=255;}
        var source=new PixelFrame(desktop.Width,desktop.Height,pixels);
        var box=new PixelRect(280,200,Math.Min(860,desktop.Width-360),Math.Min(500,desktop.Height-390));
        source=source.Render([new("Text",[new(box.X+35,box.Y+35),new(box.X+36,box.Y+36)],"#304354",3,"ClariBar "+ProductInfo.DisplayVersion){FontSize=34},new("Text",[new(box.X+35,box.Y+105),new(box.X+36,box.Y+106)],"#536571",3,L.IsChinese?"截图与标注":"Capture and annotate"){FontSize=24}]);
        var geometry=new List<object>();
        try
        {
            var overlay=new CaptureOverlay(desktopTools,source,desktop,[],false,preset:box);desktopTools.Track(overlay);var pending=overlay.SelectAsync();await Task.Delay(220);
            var root=(FrameworkElement)overlay.Content;
            var toolbar=(AnnotationToolbar)typeof(CaptureOverlay).GetField("toolbar",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(overlay)!;
            var anchors=(Canvas)typeof(CaptureOverlay).GetField("anchors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(overlay)!;
            void CheckResizeCursors()
            {
                var handles=anchors.Children.OfType<ResizeThumb>().ToArray();
                require(handles.Length==8 && handles.Select(handle=>handle.Anchor).Distinct().Count()==8,"All eight selection resize handles have their own pointer cursor");
                require(handles.Where(handle=>handle.Anchor is "NW" or "SE").All(handle=>handle.CursorShape==InputSystemCursorShape.SizeNorthwestSoutheast) &&
                    handles.Where(handle=>handle.Anchor is "NE" or "SW").All(handle=>handle.CursorShape==InputSystemCursorShape.SizeNortheastSouthwest),"Opposite selection corners use the correct diagonal double-arrow cursors");
                require(handles.Where(handle=>handle.Anchor is "N" or "S").All(handle=>handle.CursorShape==InputSystemCursorShape.SizeNorthSouth) &&
                    handles.Where(handle=>handle.Anchor is "W" or "E").All(handle=>handle.CursorShape==InputSystemCursorShape.SizeWestEast),"Selection edges use vertical and horizontal double-arrow cursors");
            }
            CheckResizeCursors();
            var setArea=typeof(CaptureOverlay).GetMethod("SetArea",BindingFlags.Instance|BindingFlags.NonPublic)!;
            setArea.Invoke(overlay,[CaptureGeometry.Resize(box,"NW",-20,-15,source.Bounds),true,null]);
            CheckResizeCursors();setArea.Invoke(overlay,[box,true,null]);
            await CheckAnnotationButtonsAsync(toolbar,require);
            var image=await RenderOwnElementAsync(root);double sx=image.Width/(double)source.Width,sy=image.Height/(double)source.Height;
            ScreenColor At(double x,double y)=>image.ColorAt((int)(x*sx),(int)(y*sy));
            var inside=At(box.X+box.Width*.7,box.Y+box.Height*.6);var outside=At(box.X-60,box.Y+box.Height*.6);
            require(inside==new ScreenColor(235,235,235) && outside.R<95 && outside.G<95 && outside.B<95,"Selection stays at original brightness while the exterior is visibly dimmed");
            int lineX=(int)((box.X+box.Width*.37)*sx),lineY=(int)(box.Y*sy),accent=0;
            for(int sampleY=lineY-8;sampleY<=lineY+8;sampleY++){var color=image.ColorAt(lineX,sampleY);if(color.R<155 && color.G>155 && color.B>180)accent++;}
            require(accent>=2,"Selection outline retains at least two visible accent pixels in the rendered capture");
            foreach(double width in new[]{620d,480d,360d})
            {
                toolbar.SelectTool("Rectangle");toolbar.Width=width;await Task.Delay(110);
                bool visible=Math.Abs(toolbar.ActualWidth-width)<1 && toolbar.PrimaryActions.All(button=>{var at=button.TransformToVisual(toolbar).TransformPoint(new());return button.ActualWidth>=28 && at.X>=0 && at.Y>=0 && at.X+button.ActualWidth<=toolbar.ActualWidth+1;});
                geometry.Add(new{width,toolbar.ActualWidth,toolbar.ActualHeight,toolbar.IsCompact,primaryActionsVisible=visible});
                require(visible,$"Copy, Save, Pin, OCR and Close stay visible at toolbar width {width}");
                foreach(string tool in new[]{"Ellipse","Arrow","Mosaic","Blur","Text","Pen"}){toolbar.SelectTool(tool);toolbar.Width=width;await Task.Delay(20);}
            }
            toolbar.Width=AnnotationToolbar.PreferredWidth;toolbar.SelectTool("Rectangle");await Task.Delay(100);
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-toolbar.png"),await (await RenderOwnElementAsync(toolbar)).PngAsync());
            var selected=await RenderOwnElementAsync(root);
            int x=(int)((box.X-65)*sx),y=(int)((box.Y-65)*sy),right=(int)((box.Right+65)*sx),bottom=(int)((box.Bottom+200)*sy);
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-selection.png"),await selected.Crop(new(x,y,right-x,bottom-y)).PngAsync());
            require(overlay.Document!.Render().Pixels.SequenceEqual(source.Crop(box).Pixels),"The thicker outline and dark overlay do not enter exported image pixels");
            overlay.Close();await pending;

            var preview=new CaptureOverlay(desktopTools,source,desktop,[],false);desktopTools.Track(preview);var previewTask=preview.SelectAsync();await Task.Delay(150);
            typeof(CaptureOverlay).GetMethod("SetHoverArea",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(preview,[box with {X=box.X+desktop.X,Y=box.Y+desktop.Y}]);
            await Task.Delay(80);var previewImage=await RenderOwnElementAsync((FrameworkElement)preview.Content);
            var previewInside=previewImage.ColorAt((int)((box.X+box.Width*.7)*previewImage.Width/source.Width),(int)((box.Y+box.Height*.6)*previewImage.Height/source.Height));
            var previewOutside=previewImage.ColorAt((int)((box.X-60)*previewImage.Width/source.Width),(int)((box.Y+box.Height*.6)*previewImage.Height/source.Height));
            require(previewInside==new ScreenColor(235,235,235) && previewOutside.R<95,"Window detection preview also leaves its highlighted area undimmed");
            preview.Close();await previewTask;
            await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"capture-ui-check.json"),JsonSerializer.Serialize(new{version=ProductInfo.DisplayVersion,language=L.LanguageCode,passed=true,inside,outside,accentPixels=accent,toolbar=geometry},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
        }
        finally{settings.CaptureCancelOnDeactivate=cancel;}
    }
    private static async Task CheckAnnotationButtonsAsync(AnnotationToolbar toolbar,Action<bool,string> require)
    {
        var groups=(Dictionary<string,ToggleButton>)typeof(AnnotationToolbar).GetField("groups",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(toolbar)!;
        var variants=(Dictionary<string,ToggleButton>)typeof(AnnotationToolbar).GetField("variantButtons",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(toolbar)!;
        var surface=(AnnotationSurface)typeof(AnnotationToolbar).GetField("surface",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(toolbar)!;
        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            {var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var descendant in Descendants(child))yield return descendant;}
        }
        void CheckStates(DependencyObject parent,string context)
        {
            int buttons=0,states=0;
            foreach(var button in Descendants(parent).OfType<ButtonBase>().Where(b=>b.Visibility==Visibility.Visible).ToArray())
            {
                button.ApplyTemplate();
                if(VisualTreeHelper.GetChildrenCount(button)==0 || VisualTreeHelper.GetChild(button,0) is not Border {Name:"Chrome"})continue;
                buttons++;
                string[] names=button is ToggleButton?["PointerOver","Pressed","Disabled","Checked","CheckedPointerOver","CheckedPressed","CheckedDisabled"]:["PointerOver","Pressed","Disabled"];
                foreach(string state in names)
                {
                    if(!VisualStateManager.GoToState(button,state,false))throw new IOException($"Missing annotation button state {context}/{state}");
                    states++;
                }
                VisualStateManager.GoToState(button,button is ToggleButton {IsChecked:true}?"Checked":button.IsEnabled?"Normal":"Disabled",false);
            }
            require(buttons>0 && states>=buttons*3,$"Annotation button hover, press and disabled states load without resource errors: {context} ({buttons} buttons, {states} states)");
        }
        async Task ClickAsync(ToggleButton button,string expected)
        {
            bool clicked=false;void Click(object sender,RoutedEventArgs e)=>clicked=true;
            button.Click+=Click;
            try
            {
                var peer=FrameworkElementAutomationPeer.CreatePeerForElement(button);
                var provider=peer?.GetPattern(PatternInterface.Toggle) as IToggleProvider;
                require(provider is not null,"Annotation toggle exposes its native automation provider");
                provider!.Toggle();await Task.Delay(30);
                require(clicked && button.IsChecked==true && surface.Ink.Tool==expected,$"Native annotation button activation raises Click and selects {expected}");
            }
            finally{button.Click-=Click;}
        }
        // Reproduce the user's first hover before Shapes has ever been checked.
        toolbar.SelectTool("");await Task.Delay(30);CheckStates(toolbar,"initial unchecked Shapes");
        await ClickAsync(groups["Shapes"],"Rectangle");await Task.Delay(40);CheckStates(toolbar,"Rectangle");
        var pinButton=toolbar.PrimaryActions.Single(button=>(string?)button.Tag=="Pin");
        var copyButton=toolbar.PrimaryActions.Single(button=>(string?)button.Tag=="Copy");
        require(copyButton.Background is SolidColorBrush {Color.A:0},"Copy is an ordinary action without a permanent selected-state highlight");
        require((pinButton.Background is SolidColorBrush {Color.A:>0})==toolbar.IsPinned && Microsoft.UI.Xaml.Automation.AutomationProperties.GetItemStatus(pinButton)==L.T(toolbar.IsPinned?"Pinned":"Not pinned"),"The Pin button highlight and accessible state match whether this image is pinned");
        var custom=Descendants(toolbar).OfType<Button>().Single(button=>Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)==L.T("Custom color"));
        var colorFlyout=(Flyout)custom.Flyout;var colorPicker=(ColorPicker)colorFlyout.Content;
        bool opened=false;colorFlyout.Opened+=Opened;
        void Opened(object? sender,object e)=>opened=true;
        try
        {
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(custom)!.GetPattern(PatternInterface.Invoke)).Invoke();
            for(int attempt=0;attempt<100 && (!opened || colorPicker.ActualHeight<240);attempt++)await Task.Delay(20);
            require(opened && colorPicker.ActualWidth>=260 && colorPicker.ActualHeight>=240 && Descendants(colorPicker).OfType<ColorSpectrum>().Any(spectrum=>spectrum.ActualHeight>=100),"Custom color opens a complete spectrum and channel editor instead of a clipped hue strip");
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+(toolbar.IsPinned?"-pin-custom-color.png":"-capture-custom-color.png")),await (await RenderOwnElementAsync(colorPicker)).PngAsync());
            colorPicker.Color=Windows.UI.Color.FromArgb(255,48,112,208);await Task.Delay(30);
            require(surface.Ink.Color=="#3070D0","The custom color popup applies the selected color to annotation tools");
        }
        finally{colorFlyout.Opened-=Opened;colorFlyout.Hide();}
        var original=surface.Document.Render();
        surface.Document.Add(surface.Ink.Mark([new(35,35),new(180,130)]));
        var annotated=surface.Document.Render();
        require(surface.Document.Marks.Last().Tool=="Rectangle" && !annotated.Pixels.SequenceEqual(original.Pixels),"Rectangle selected through its button creates a visible annotation");
        async Task InvokeActionAsync(string field)
        {
            var button=(Button)typeof(AnnotationToolbar).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(toolbar)!;
            var provider=FrameworkElementAutomationPeer.CreatePeerForElement(button)?.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            require(button.IsEnabled && provider is not null,$"Annotation {field} is enabled with a native Invoke provider");
            provider!.Invoke();await Task.Delay(40);
        }
        await InvokeActionAsync("undoButton");require(surface.Document.Render().Pixels.SequenceEqual(original.Pixels),"Rectangle Undo button restores original pixels");
        await InvokeActionAsync("redoButton");require(surface.Document.Render().Pixels.SequenceEqual(annotated.Pixels),"Rectangle Redo button restores its annotation");
        var annotationHandles=Descendants(surface).OfType<ResizeThumb>().ToArray();
        require(annotationHandles.Length==4 && annotationHandles.All(handle=>handle.CursorShape==(handle.Anchor is "NW" or "SE"?InputSystemCursorShape.SizeNorthwestSoutheast:InputSystemCursorShape.SizeNortheastSouthwest)),"Annotation corner handles also use diagonal resize cursors instead of inheriting the crosshair");
        await ClickAsync(variants["Ellipse"],"Ellipse");CheckStates(toolbar,"Ellipse");
        foreach(var (group,tool) in new[]{("Lines","Arrow"),("Pen","Pen"),("Marker","Marker"),("Effects","Mosaic"),("Text","Text"),("Eraser","Eraser")})
        {
            await ClickAsync(groups[group],tool);await Task.Delay(30);CheckStates(toolbar,tool);
            if(group=="Lines")foreach(string variant in new[]{"Line","Polyline"}){await ClickAsync(variants[variant],variant);CheckStates(toolbar,variant);}
            if(group=="Effects"){await ClickAsync(variants["Blur"],"Blur");CheckStates(toolbar,"Blur");}
            if(group=="Text")
            {
                var styleButton=(Button)typeof(AnnotationToolbar).GetField("styleButton",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(toolbar)!;
                var flyout=(Flyout)styleButton.Flyout;flyout.ShowAt(styleButton);await Task.Delay(100);CheckStates(flyout.Content,"text style flyout");flyout.Hide();
            }
        }
        surface.Document.Clear();toolbar.SelectTool("");
    }
}
