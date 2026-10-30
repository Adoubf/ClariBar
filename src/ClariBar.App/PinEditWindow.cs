using ClariBar.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed class PinEditWindow:Window
{
    private readonly DesktopToolsController tools;
    private readonly PinModel pin;
    private readonly Canvas root=new();
    private readonly AnnotationSurface surface;
    private readonly AnnotationToolbar toolbar;
    internal AnnotationToolbarWindow ToolbarWindow {get;}
    private readonly ContentControl focus=new(){IsTabStop=true,UseSystemFocusVisuals=false,KeyboardAcceleratorPlacementMode=KeyboardAcceleratorPlacementMode.Hidden,
        HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    private readonly PixelRect imageBox;
    private bool exporting;
    public PinEditWindow(DesktopToolsController tools,PinModel pin)
    {
        this.tools=tools;this.pin=pin;
        DesktopCapture.GetWindowRect(pin.Window.Handle,out var bounds);imageBox=bounds.Pixel;
        root.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);surface=new(pin.Document);surface.Ink.Tool="Pen";
        toolbar=new(surface,OutputAsync,pinned:true);root.Children.Add(surface);
        focus.Content=root;focus.KeyDown+=KeyDown;Content=focus;
        ToolUi.Configure(this,tools,"Edit annotations",imageBox.Width,imageBox.Height,true);SystemBackdrop=new TransparentToolBackdrop();ToolWindowGeometry.Borderless(this);
        AppWindow.MoveAndResize(new RectInt32(imageBox.X,imageBox.Y,imageBox.Width,imageBox.Height));
        ToolbarWindow=new(this,toolbar,tools,KeyDown);
        root.SizeChanged+=(_,_)=>Layout();AppWindow.Changed+=(_,_)=>ToolbarWindow.Reposition();
        root.Loaded+=(_,_)=>{Layout();toolbar.SelectTool("Pen");ToolbarWindow.Activate();ToolbarWindow.Reposition();Activate();focus.Focus(FocusState.Programmatic);};
        AppWindow.Closing+=(_,_)=>surface.FinishText();
        Closed+=(_,_)=>{surface.CancelInput();ToolbarWindow.CloseWithOwner();};
    }
    private void Layout()
    {
        if(root.ActualWidth<=0 || root.ActualHeight<=0)return;
        double sx=root.ActualWidth/Math.Max(1,imageBox.Width),sy=root.ActualHeight/Math.Max(1,imageBox.Height);
        int w=pin.Document.Source.Width,h=pin.Document.Source.Height;int turns=(pin.Record.Turns%4+4)%4;int rw=turns%2==0?w:h,rh=turns%2==0?h:w;
        Point Transform(double x,double y)
        {var p=turns switch{1=>new Point(h-y,x),2=>new Point(w-x,h-y),3=>new Point(y,w-x),_=>new Point(x,y)};if(pin.Record.FlipX)p.X=rw-p.X;if(pin.Record.FlipY)p.Y=rh-p.Y;return p;}
        var p0=Transform(0,0);var px=Transform(1,0);var py=Transform(0,1);double zx=imageBox.Width/(double)rw*sx,zy=imageBox.Height/(double)rh*sy;
        Canvas.SetLeft(surface,0);Canvas.SetTop(surface,0);
        surface.RenderTransform=new MatrixTransform {Matrix=new Matrix((px.X-p0.X)*zx,(px.Y-p0.Y)*zy,(py.X-p0.X)*zx,(py.Y-p0.Y)*zy,p0.X*zx,p0.Y*zy)};
        ToolbarWindow.Reposition();
    }
    private async Task OutputAsync(string action)
    {
        if(exporting)return;surface.FinishText();
        if(action is "Close" or "Pin"){Close();return;}if(action=="Clear"){surface.Clear();return;}
        exporting=true;
        try
        {
            var image=pin.Render();if(action=="Copy")await image.CopyAsync();else if(action=="Save")await tools.SaveAsImageAsync(image,this);else if(action=="QuickSave")await tools.SaveImageAsync(image,this);else if(action=="OCR")tools.OpenOcr(image);else if(action=="Print")ImagePrinter.Print(image,this);
        }
        finally{exporting=false;}
    }
    private async void KeyDown(object sender,KeyRoutedEventArgs e)
    {
        var inputRoot=(sender as FrameworkElement)?.XamlRoot??root.XamlRoot;
        if(inputRoot is not null && FocusManager.GetFocusedElement(inputRoot) is TextBox)return;
        bool ctrl=AnnotationSurface.Down(VirtualKey.Control),shift=AnnotationSurface.Down(VirtualKey.Shift);
        try
        {
            if(e.Key==VirtualKey.Escape){if(!surface.FinishCurrent())Close();}
            else if(ctrl && e.Key==VirtualKey.Z){if(shift)surface.Clear();else surface.Undo();}
            else if(ctrl && e.Key==VirtualKey.Y)surface.Redo();
            else if(ctrl && e.Key==VirtualKey.C)await OutputAsync("Copy");
            else if(ctrl && e.Key==VirtualKey.S)await OutputAsync(shift?"QuickSave":"Save");
            else if(ctrl && e.Key==VirtualKey.P)await OutputAsync("Print");
            else if(e.Key==VirtualKey.Space)Close();
            else if(e.Key==VirtualKey.Tab && surface.Ink.Tool is "Line" or "Arrow")toolbar.SelectTool(surface.Ink.Tool=="Line"?"Arrow":"Line");
            else if((int)e.Key is 49 or 50 or 219 or 221)toolbar.ChangeWidth((int)e.Key is 49 or 219?-1:1);
            else return;
            e.Handled=true;
        }
        catch(Exception ex){await surface.ShowErrorAsync(ex);}
    }
}
internal static class DesktopWindowDpi
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern uint GetDpiForWindow(nint window);
    public static uint For(nint window)=>Math.Max(96,GetDpiForWindow(window));
}
