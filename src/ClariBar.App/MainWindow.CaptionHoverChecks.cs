using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using ClariBar.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    private async Task CheckCaptionHoverAsync(Action<bool,string> require)
    {
        var observations=new List<object>();string theme=settings.Theme;var size=AppWindow.Size;var position=AppWindow.Position;
        var presenter=(OverlappedPresenter)AppWindow.Presenter;bool topmost=presenter.IsAlwaysOnTop;
        DesktopCapture.GetCursorPos(out var originalCursor);var work=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
        void LeaveHiddenWindow(){if(!SetCursorPos(work.X+4,work.Y+4))throw new IOException("Could not move the test pointer outside the hidden window.");}
        nint host=0;
        DesktopCapture.EnumWindows((window,_)=>
        {
            DesktopWindowState.Process(window,out uint process);var name=new StringBuilder(120);GetClassName(window,name,name.Capacity);
            if(process==Environment.ProcessId && name.ToString()=="ClariBar.Host")host=window;return true;
        },0);
        require(host!=0,"The caption regression targets this process's real native tray callback window");
        static nint Point(int x,int y)=>unchecked((nint)(int)((uint)(ushort)x | (uint)(ushort)y<<16));
        PixelRect CloseBounds()
        {
            var region=InputNonClientPointerSource.GetForWindowId(AppWindow.Id).GetRegionRects(NonClientRegionKind.Close).Single();
            DesktopCapture.GetWindowRect(hwnd,out var window);
            return new(window.Left+region.X,window.Top+region.Y,region.Width,region.Height);
        }
        async Task<int> SampleAsync(string name)
        {
            DesktopCapture.DwmFlush();var region=CloseBounds();var frame=DesktopCapture.Capture(region);int red=0;
            for(int pixel=0;pixel<frame.Pixels.Length;pixel+=4)
                if(frame.Pixels[pixel+2]>180 && frame.Pixels[pixel+1]<110 && frame.Pixels[pixel]<120)red++;
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,name+".png"),await frame.PngAsync());
            observations.Add(new{name,redPixels=red,bounds=region});return red;
        }
        async Task HoverAsync()
        {
            var close=CloseBounds();if(!SetCursorPos(close.X+close.Width/2,close.Y+close.Height/2))throw new IOException("Could not hover this test window's close button.");
            Native.SendMessage(hwnd,0x00A0,20,Point(close.X+close.Width/2,close.Y+close.Height/2));await Task.Delay(150);
        }
        static void LegacyClear(nint window)
        {
            void Clear(nint target){Native.SendMessage(target,0x001F,0,0);Native.SendMessage(target,0x02A3,0,0);Native.SendMessage(target,0x02A2,0,0);}
            Clear(window);DesktopCapture.EnumChildWindows(window,(child,_)=>{Clear(child);return true;},0);
        }
        try
        {
            presenter.IsAlwaysOnTop=true;AppWindow.Resize(new(900,650));ShowSettings();await Task.Delay(250);
            foreach(string variant in new[]{"Light","Dark"})
            {
                settings.Theme=variant;ApplyTheme();await Task.Delay(200);
                await HoverAsync();
                require(await SampleAsync("caption-"+variant+"-hover-control")>100,"The "+variant+" close button genuinely renders its red hover state before testing restore");
                Native.ShowWindow(hwnd,0);LegacyClear(hwnd);LeaveHiddenWindow();LegacyClear(hwnd);Native.ShowWindow(hwnd,9);Activate();PageScroll.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);LegacyClear(hwnd);await Task.Delay(150);
                await SampleAsync("caption-"+variant+"-legacy-restore");
                foreach(var (name,events) in new[]{("single",new[]{0x0400}), ("double",new[]{0x0400,0x0203,0x0400}), ("keyboard",new[]{0x0401})})
                {
                    await HoverAsync();Native.ShowWindow(hwnd,0);LeaveHiddenWindow();
                    foreach(int notification in events)Native.PostMessage(host,0x8001,0,(nint)((1<<16)|notification));
                    await Task.Delay(200);
                    require(DesktopCapture.IsWindowVisible(hwnd),variant+" / "+name+": the native notification restores settings");
                    require(await SampleAsync("caption-"+variant+"-"+name+"-restored")==0,variant+" / "+name+": the restored close button has no red pixels");
                }
                await HoverAsync();require(await SampleAsync("caption-"+variant+"-hover-after-restore")>100,"The "+variant+" close button still highlights on a subsequent hover");
                Native.ClearCaptionHover(hwnd);
            }
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"caption-hover-observations.json"),JsonSerializer.Serialize(observations,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
            settings.Theme=theme;ApplyTheme();presenter.IsAlwaysOnTop=topmost;AppWindow.Resize(size);AppWindow.Move(position);Native.ClearCaptionHover(hwnd);SetCursorPos(originalCursor.X,originalCursor.Y);
        }
    }
}
