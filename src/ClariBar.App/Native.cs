using System.Runtime.InteropServices;

namespace ClariBar.App;

internal static partial class Native
{
    [StructLayout(LayoutKind.Sequential)]internal struct TextInk {public double Above,Below;}
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall,CharSet=CharSet.Unicode)]
    private static extern int CB_MeasureTextInk(string text,string family,double size,uint weight,out TextInk ink);
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]
    private static extern double CB_TaskbarHeight();
    internal static TextInk MeasureInk(Microsoft.UI.Xaml.Controls.TextBlock text)
    {
        try {if(CB_MeasureTextInk(text.Text,text.FontFamily.Source,text.FontSize,text.FontWeight.Weight,out var ink)==1)return ink;}
        catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException) { }
        return new(){Above=text.BaselineOffset,Below=text.DesiredSize.Height-text.BaselineOffset};
    }
    internal static double TaskbarHeight()
    {
        try{return Math.Clamp(CB_TaskbarHeight(),16,256);}
        catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException){return 48;}
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void TrayCallback(int action);
    [DllImport("ClariBar.Native.dll", CallingConvention = CallingConvention.StdCall)]
    internal static extern int CB_Start(TrayCallback callback);
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]
    internal static extern void CB_SetLanguage(int chinese);
    [DllImport("ClariBar.Native.dll", CallingConvention = CallingConvention.StdCall)]
    internal static extern int CB_SetToolHotkey(int slot, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern nint SendMessage(nint window, uint message, nint wparam, nint lparam);
    internal static void ClearCaptionHover(nint window)
    {
        // WinUI can stop non-client leave tracking while X is still hovered.
        // A move to HTNOWHERE updates InputNonClientPointerSource even then.
        // Reset while hidden so the first restored frame is already neutral.
        if(!DesktopCapture.GetCursorPos(out var cursor))return;
        nint point=unchecked((nint)(int)((uint)(ushort)cursor.X | (uint)(ushort)cursor.Y<<16));
        SendMessage(window,0x00A0,0,point); // WM_NCMOUSEMOVE, HTNOWHERE
    }
    [DllImport("ClariBar.Native.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal static extern void CB_SetMonitor(string text, uint foreground, uint background, int opacity, int visible);
    [DllImport("ClariBar.Native.dll", CallingConvention = CallingConvention.StdCall)]
    internal static extern int CB_SetTaskbar(int mode, int opacity);
    [DllImport("ClariBar.Native.dll", CallingConvention = CallingConvention.StdCall)]
    internal static extern void CB_Stop();
    [DllImport("ClariBar.Native.dll", CallingConvention=CallingConvention.StdCall,CharSet=CharSet.Unicode)]
    internal static extern int CB_EnumFonts(System.Text.StringBuilder output,int capacity);
    internal static string[] FontFamilies()
    {
        var text=new System.Text.StringBuilder(65536);CB_EnumFonts(text,text.Capacity);
        return text.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries);
    }
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint window, uint message, nint wparam, nint lparam);

    internal static uint ColorRef(string hex)
    {
        uint rgb = Convert.ToUInt32(hex.AsSpan(1).ToString(), 16);
        return ((rgb & 255) << 16) | (rgb & 0xff00) | ((rgb >> 16) & 255);
    }
}
