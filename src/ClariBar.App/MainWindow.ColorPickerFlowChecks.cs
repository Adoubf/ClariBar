using ClariBar.Core;
using Microsoft.UI.Xaml;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private async Task CheckColorPickerWindowFlowAsync(Action<bool,string> require)
    {
        var clipboard=ClipboardSnapshot.Capture(hwnd);var history=settings.ColorHistory;
        var work=Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
        var point=new DesktopCapture.Point{X=work.X+80,Y=work.Y+80};ColorEditorWindow? editor=null;
        async Task<LiveColorPicker> AwaitPickerAsync()
        {
            for(int attempt=0;attempt<100;attempt++)
            {
                if(InteractionField<LiveColorPicker?>(desktopTools,"livePicker") is {} picker && picker.PreviewHandle!=0)return picker;
                await Task.Delay(30);
            }
            throw new IOException("The color-picking operation did not create its live preview.");
        }
        try
        {
            ShowSettings();await Task.Delay(80);
            var picking=desktopTools.BeginAsync(color:true);var picker=await AwaitPickerAsync();
            require(!DesktopCapture.IsWindowVisible(hwnd),"Starting a screen pick hides the settings window");
            picker.CompleteAt(point,"Edit");await picking;await Task.Delay(100);
            var colors=InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<ColorEditorWindow>().ToArray();
            require(!DesktopCapture.IsWindowVisible(hwnd) && colors.Length==1 && DesktopCapture.IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(colors[0])),"A completed screen pick opens only the color editor and keeps settings hidden");
            editor=colors.Single();

            ShowSettings();editor.Activate();
            var resampling=(Task)typeof(ColorEditorWindow).GetMethod("PickAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(editor,null)!;
            picker=await AwaitPickerAsync();picker.CompleteAt(point,"Copy");await resampling;
            require(!DesktopCapture.IsWindowVisible(hwnd) && DesktopCapture.IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(editor)) && InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<ColorEditorWindow>().Count()==1,"Picking again from the editor returns to that same editor without opening settings or a second editor");
            editor.Close();editor=null;

            ShowSettings();picking=desktopTools.BeginAsync(color:true);picker=await AwaitPickerAsync();picker.CompleteAt(point,"Copy");await picking;
            require(!DesktopCapture.IsWindowVisible(hwnd) && !InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<ColorEditorWindow>().Any(),"Copy-only screen picking completes without opening the settings window or a color editor");

            ShowSettings();picking=desktopTools.BeginAsync(color:true);picker=await AwaitPickerAsync();picker.Dispose();await picking;
            require(DesktopCapture.IsWindowVisible(hwnd),"Cancelling a pick started from settings returns to settings");

            string? chosen=null;picking=desktopTools.BeginAsync(color:true,chosen:value=>chosen=value);picker=await AwaitPickerAsync();picker.CompleteAt(point,"Copy");await picking;
            require(chosen is not null && DesktopCapture.IsWindowVisible(hwnd) && !InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<ColorEditorWindow>().Any(),"Picking for a settings color applies the value and returns to settings without an extra editor");
        }
        finally{editor?.Close();settings.ColorHistory=history;desktopTools.Save();clipboard.Restore(hwnd);ShowSettings();}
    }
}
