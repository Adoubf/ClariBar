using System.Reflection;
using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private async Task CheckPinnedEditorAsync(Action<bool,string> require)
    {
        static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(value)!;
        var bytes=new byte[720*400*4];for(int i=0;i<bytes.Length;i+=4){bytes[i]=40;bytes[i+1]=60;bytes[i+2]=220;bytes[i+3]=255;}
        var source=new PixelFrame(720,400,bytes);var work=Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
        var pin=desktopTools.Pins.Create(new(source),position:new(work.X+120,work.Y+120,720,400));
        PinEditWindow? editor=null;
        try
        {
            var pinnedRoot=(FrameworkElement)((ContentControl)pin.Window.Content).Content;
            for(int attempt=0;attempt<100 && (pinnedRoot.XamlRoot is null || !pinnedRoot.IsLoaded || pinnedRoot.ActualWidth<=0);attempt++)await Task.Delay(30);
            require(pinnedRoot.XamlRoot is not null && pinnedRoot.IsLoaded && pinnedRoot.ActualWidth>0,$"Pinned image is attached to a visible XAML tree before opening its menu (visible={DesktopCapture.IsWindowVisible(pin.Window.Handle)}, loaded={pinnedRoot.IsLoaded}, width={pinnedRoot.ActualWidth:0.0})");
            var pinnedBounds=ToolWindowGeometry.ClientBounds(pin.Window);
            require(pinnedBounds.X==pin.Window.AppWindow.Position.X && pinnedBounds.Y==pin.Window.AppWindow.Position.Y && pinnedBounds.Width==pin.Window.AppWindow.Size.Width && pinnedBounds.Height==pin.Window.AppWindow.Size.Height,"The pinned image has no native non-client border or extra frame margins");
            CheckImageEdges(pin.Window,"Pinned image");
            var menu=(MenuFlyout)pinnedRoot.ContextFlyout;menu.ShowAt(pinnedRoot,new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions{Position=new(24,24)});await Task.Delay(100);
            var edit=menu.Items.OfType<MenuFlyoutItem>().Single(item=>item.Text==L.T("Edit annotations"));
            var invoke=(IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(edit)!.GetPattern(PatternInterface.Invoke);invoke.Invoke();menu.Hide();await Task.Delay(400);
            editor=Field<PinEditWindow>(pin.Window,"editor");var focus=(ContentControl)editor.Content;var root=(FrameworkElement)focus.Content;
            var toolbar=Field<AnnotationToolbar>(editor,"toolbar");var surface=Field<AnnotationSurface>(editor,"surface");
            var imageBox=Field<PixelRect>(editor,"imageBox");var transform=(MatrixTransform)surface.RenderTransform;
            var state=new{language=L.LanguageCode,rootWidth=root.ActualWidth,rootHeight=root.ActualHeight,root.DesiredSize,focusWidth=focus.ActualWidth,focusHeight=focus.ActualHeight,focus.HorizontalContentAlignment,focus.VerticalContentAlignment,toolbarWidth=toolbar.ActualWidth,toolbarHeight=toolbar.ActualHeight,surface.Width,surface.Height,matrix=transform.Matrix.ToString(),windowWidth=editor.AppWindow.Size.Width,windowHeight=editor.AppWindow.Size.Height,imageBox};
            await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"pin-editor-layout.json"),JsonSerializer.Serialize(state,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
            var toolbarHost=editor.ToolbarWindow;var toolbarRoot=(FrameworkElement)toolbarHost.Content;
            var pictureBounds=ToolWindowGeometry.ClientBounds(editor);var toolbarBounds=ToolWindowGeometry.ClientBounds(toolbarHost);
            require(pictureBounds.Width==editor.AppWindow.Size.Width && pictureBounds.Height==editor.AppWindow.Size.Height && toolbarBounds.Width==toolbarHost.AppWindow.Size.Width && toolbarBounds.Height==toolbarHost.AppWindow.Size.Height,"Both the annotation image and floating toolbar use their full window area without an extra white system frame");
            CheckImageEdges(editor,"Annotation image");
            var visibleToolbar=toolbar.TransformToVisual(toolbarRoot).TransformPoint(new());double toolbarScale=toolbarRoot.XamlRoot.RasterizationScale;
            double gap=toolbarBounds.Y+visibleToolbar.Y*toolbarScale+3*toolbarScale-pictureBounds.Bottom;
            require(editor.AppWindow.Size.Width==imageBox.Width && editor.AppWindow.Size.Height==imageBox.Height,"Editing preserves the original image window size instead of expanding it around the toolbar");
            require(!ReferenceEquals(toolbar.XamlRoot,root.XamlRoot) && !toolbarHost.IsAboveImage && gap>=0 && gap<=10*toolbarScale,"The toolbar is a separate floating window attached immediately below the picture");
            var union=new PixelRect(Math.Min(imageBox.X,toolbarHost.AppWindow.Position.X),Math.Min(imageBox.Y,toolbarHost.AppWindow.Position.Y),Math.Max(imageBox.Right,toolbarHost.AppWindow.Position.X+toolbarHost.AppWindow.Size.Width)-Math.Min(imageBox.X,toolbarHost.AppWindow.Position.X),Math.Max(imageBox.Bottom,toolbarHost.AppWindow.Position.Y+toolbarHost.AppWindow.Size.Height)-Math.Min(imageBox.Y,toolbarHost.AppWindow.Position.Y));
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-pin-editor-screen.png"),await DesktopCapture.Capture(union).PngAsync());
            require(!DesktopCapture.IsWindowVisible(pin.Window.Handle),"Context-menu Edit annotations opens a single editor while hiding the original pin");
            require(root.ActualWidth>100 && root.ActualHeight>100 && toolbar.ActualWidth>=300 && toolbar.ActualHeight>=32,"The pinned editor lays out its image and toolbar at nonzero sizes");
            var point=surface.TransformToVisual(root).TransformPoint(new(100,100));double scale=root.XamlRoot.RasterizationScale;
            var screen=DesktopCapture.Capture(new(editor.AppWindow.Position.X+(int)(point.X*scale),editor.AppWindow.Position.Y+(int)(point.Y*scale),1,1)).ColorAt(0,0);
            require(Math.Abs(screen.R-220)<10 && Math.Abs(screen.G-60)<10 && Math.Abs(screen.B-40)<10,"The editor actually displays source image pixels instead of a transparent hole");
            require(toolbar.PrimaryActions.All(button=>{var p=button.TransformToVisual(toolbarRoot).TransformPoint(new());return button.ActualWidth>=28 && p.X>=0 && p.Y>=0 && p.X+button.ActualWidth<=toolbarRoot.ActualWidth+1 && p.Y+button.ActualHeight<=toolbarRoot.ActualHeight+1;}),"Pinned editor Copy, Save, Close, Pin and OCR buttons stay inside the window");
            await CheckAnnotationButtonsAsync(toolbar,require);
            var originalPosition=editor.AppWindow.Position;
            editor.AppWindow.Move(new(originalPosition.X,work.Y+work.Height-editor.AppWindow.Size.Height-8));await Task.Delay(100);
            var above=ToolWindowGeometry.ClientBounds(toolbarHost);var movedPicture=ToolWindowGeometry.ClientBounds(editor);
            require(toolbarHost.IsAboveImage && above.Bottom<=movedPicture.Y+6*toolbarScale,"The floating toolbar moves above the picture when the work area has no space below");
            editor.AppWindow.Move(originalPosition);await Task.Delay(100);
            nint toolbarHandle=WinRT.Interop.WindowNative.GetWindowHandle(toolbarHost);
            var close=toolbar.PrimaryActions.Single(button=>Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)==L.T("Close"));
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(close)!.GetPattern(PatternInterface.Invoke)).Invoke();editor=null;await Task.Delay(150);
            require(DesktopCapture.IsWindowVisible(pin.Window.Handle) && Field<PinEditWindow?>(pin.Window,"editor") is null,"Closing the annotation toolbar restores the original pinned image");
            require(!DesktopCapture.IsWindowVisible(toolbarHandle),"Closing the editor also closes its independent toolbar window");
            for(int repeat=0;repeat<2;repeat++)
            {
                pin.Window.OpenEditor();await Task.Delay(120);editor=Field<PinEditWindow>(pin.Window,"editor");
                pin.Window.OpenEditor();require(ReferenceEquals(editor,Field<PinEditWindow>(pin.Window,"editor")),"Repeated Edit commands reuse the existing editor");
                var reopened=(FrameworkElement)((ContentControl)editor.Content).Content;
                require(reopened.ActualWidth>100 && reopened.ActualHeight>100,"Reopening the pinned toolbar retains a usable layout");editor.Close();editor=null;await Task.Delay(100);
            }
            require(pin.Render().Pixels.SequenceEqual(source.Pixels) && DesktopCapture.IsWindowVisible(pin.Window.Handle),"Repeated toolbar editing and closing preserve the source image pixels and pinned visibility");
        }
        finally{editor?.Close();desktopTools.Pins.Destroy(pin);}
        void CheckImageEdges(Window window,string name)
        {
            var frame=DesktopCapture.Capture(new(window.AppWindow.Position.X,window.AppWindow.Position.Y,window.AppWindow.Size.Width,window.AppWindow.Size.Height));
            var edges=new[]{(0,0),(frame.Width-1,0),(0,frame.Height-1),(frame.Width-1,frame.Height-1),(frame.Width/2,0),(frame.Width/2,frame.Height-1),(0,frame.Height/2),(frame.Width-1,frame.Height/2)};
            require(edges.All(point=>{var c=frame.ColorAt(point.Item1,point.Item2);return Math.Abs(c.R-220)<10 && Math.Abs(c.G-60)<10 && Math.Abs(c.B-40)<10;}),name+" displays the source pixels up to all four edges and corners without a white outline");
        }
    }
}
