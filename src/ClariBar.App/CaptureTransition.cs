using System.Runtime.InteropServices;
using ClariBar.Core;

namespace ClariBar.App;

internal sealed class CaptureTransition:IDisposable
{
    private bool disposed;
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]private static extern int CB_ShowCaptureCover(byte[] pixels,int width,int height,int x,int y,int left,int top,int right,int bottom);
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]private static extern void CB_RaiseCaptureCover();
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]private static extern void CB_CloseCaptureCover();
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
    internal nint Handle=>disposed?0:Native.FindWindow("ClariBar.CaptureCover",null);
    internal static void Prepare(Microsoft.UI.Xaml.Window window)
    {
        ToolWindowGeometry.Borderless(window);
        int disabled=1;DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(window),3,ref disabled,4);
    }
    internal CaptureTransition(PixelFrame frame,PixelRect desktop,PixelRect? selected=null)
    {
        var box=selected?.Intersect(frame.Bounds)??default;
        int error=CB_ShowCaptureCover(frame.Pixels,frame.Width,frame.Height,desktop.X,desktop.Y,box.X,box.Y,box.Right,box.Bottom);
        if(error!=0)throw new System.ComponentModel.Win32Exception(error);
    }
    internal void Raise(){if(!disposed)CB_RaiseCaptureCover();}
    public void Dispose(){if(disposed)return;disposed=true;CB_CloseCaptureCover();}
}
