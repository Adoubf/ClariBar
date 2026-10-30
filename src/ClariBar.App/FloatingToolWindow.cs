using System.Runtime.InteropServices;
using ClariBar.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;

namespace ClariBar.App;

internal static class ToolWindowGeometry
{
    [DllImport("user32.dll")]private static extern bool GetClientRect(nint window,out DesktopCapture.Rect bounds);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(nint window,ref DesktopCapture.Point point);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern nint SetWindowLongPtr(nint window,int index,nint value);
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]private static extern int CB_MakeToolWindowBorderless(nint window);
    internal static PixelRect ClientBounds(Window window)
    {
        nint handle=WinRT.Interop.WindowNative.GetWindowHandle(window);GetClientRect(handle,out var client);DesktopCapture.Point origin=new();ClientToScreen(handle,ref origin);
        return new(origin.X,origin.Y,client.Right-client.Left,client.Bottom-client.Top);
    }
    internal static void Borderless(Window window,Window? owner=null)
    {
        if(window.AppWindow.Presenter is OverlappedPresenter presenter){presenter.SetBorderAndTitleBar(false,false);presenter.IsResizable=false;presenter.IsAlwaysOnTop=true;presenter.PreferredMinimumWidth=1;presenter.PreferredMinimumHeight=1;}
        nint handle=WinRT.Interop.WindowNative.GetWindowHandle(window);int error=CB_MakeToolWindowBorderless(handle);
        if(error!=0)throw new System.ComponentModel.Win32Exception(error);
        if(owner is not null)SetWindowLongPtr(handle,-8,WinRT.Interop.WindowNative.GetWindowHandle(owner));
    }
    internal static void ResizeClient(Window window,int width,int height)
    {
        var client=ClientBounds(window);var size=window.AppWindow.Size;
        int outerWidth=Math.Max(1,width+size.Width-client.Width),outerHeight=Math.Max(1,height+size.Height-client.Height);
        if(size.Width!=outerWidth || size.Height!=outerHeight)window.AppWindow.Resize(new(outerWidth,outerHeight));
    }
}

internal sealed class AnnotationToolbarWindow:Window
{
    private readonly Window owner;
    private readonly AnnotationToolbar toolbar;
    private bool updating,disposing,closed;
    internal bool IsAboveImage {get;private set;}
    internal AnnotationToolbarWindow(Window owner,AnnotationToolbar toolbar,DesktopToolsController tools,Microsoft.UI.Xaml.Input.KeyEventHandler keyDown)
    {
        this.owner=owner;this.toolbar=toolbar;
        var root=new Grid();root.Children.Add(toolbar);root.KeyDown+=keyDown;Content=root;
        ToolUi.Configure(this,tools,"Edit annotations",620,112,true);ToolWindowGeometry.Borderless(this,owner);SystemBackdrop=new TransparentToolBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"ClariBar.ico"));
        root.Loaded+=(_,_)=>Reposition();toolbar.SizeChanged+=(_,_)=>Reposition();
        Closed+=(_,_)=>{closed=true;if(!disposing)owner.Close();};
    }
    internal void Reposition()
    {
        if(updating || closed)return;updating=true;
        try
        {
            var image=ToolWindowGeometry.ClientBounds(owner);if(image.IsEmpty)return;
            var monitor=DisplayArea.GetFromPoint(new(image.X+image.Width/2,image.Y+image.Height/2),DisplayAreaFallback.Nearest).WorkArea;
            double dpi=DesktopWindowDpi.For(WinRT.Interop.WindowNative.GetWindowHandle(owner))/96d;
            double width=Math.Max(240,Math.Min(AnnotationToolbar.PreferredWidth,(monitor.Width-16)/dpi));toolbar.Width=width;toolbar.Measure(new Size(width,double.PositiveInfinity));
            int height=Math.Max(32,(int)Math.Ceiling(toolbar.DesiredSize.Height*dpi));ToolWindowGeometry.ResizeClient(this,(int)Math.Ceiling(width*dpi),height);
            var client=ToolWindowGeometry.ClientBounds(this);var position=AppWindow.Position;
            int offsetX=client.X-position.X,offsetY=client.Y-position.Y,pad=(int)Math.Round(3*dpi),gap=(int)Math.Round(4*dpi);
            int x=Math.Clamp(image.X-offsetX-pad,monitor.X,Math.Max(monitor.X,monitor.X+monitor.Width-AppWindow.Size.Width));
            int y=image.Bottom+gap-offsetY-pad;IsAboveImage=y+AppWindow.Size.Height>monitor.Y+monitor.Height;
            if(IsAboveImage)y=Math.Max(monitor.Y,image.Y-gap-AppWindow.Size.Height+offsetY+pad);
            if(position.X!=x || position.Y!=y)AppWindow.Move(new(x,y));
        }
        finally{updating=false;}
    }
    internal void CloseWithOwner(){if(closed)return;disposing=true;Close();}
}
