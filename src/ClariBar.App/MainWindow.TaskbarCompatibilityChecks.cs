using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private async Task CheckTaskbarCompatibilityAsync(Action<bool,string> require)
    {
        bool enabled=settings.TaskbarMonitorEnabled;
        try
        {
            NavigateTo("taskbar");await Task.Delay(180);settings.TaskbarMonitorEnabled=true;
            ApplyTaskbarInfo(new TaskbarStatus());
            require(taskbarConnectionButton?.Content?.ToString()==L.T("Not connected"),"A disconnected taskbar exposes the Not connected action");
            var state=new TaskbarStatus{Connected=1,Frames=2,Backgrounds=4,Panels=0};ApplyTaskbarInfo(state);
            require(taskbarConnectionButton?.Content?.ToString()==L.T("Recheck") && taskbarConnectionMessage!.Text.Contains(L.T("Taskbar monitor not embedded"),StringComparison.Ordinal),"A connected taskbar switches to Recheck and identifies missing embedded panels in its setting row");
            require(taskbarConnectionMessage!.Text.Contains(L.T("Taskbar styling is connected, but monitoring could not be embedded in every taskbar layout. Sensor readings and the preview work independently. Reconnect or export diagnostics."),StringComparison.Ordinal),"Compatibility feedback separates sensor readings and preview from actual taskbar embedding");
            require(taskbarDiagnosticsButton is {Visibility:Visibility.Visible},"A missing embedded monitor exposes diagnostic export directly on the taskbar page");
            state.Panels=1;ApplyTaskbarInfo(state);
            require(taskbarConnectionMessage.Text.Contains(L.T("Taskbar monitor partially embedded"),StringComparison.Ordinal),"Multi-monitor partial embedding stays visible in the connection row");
            state.Panels=2;ApplyTaskbarInfo(state);
            require(taskbarConnectionButton?.Content?.ToString()==L.T("Recheck") && taskbarDiagnosticsButton!.Visibility==Visibility.Collapsed,"Full embedding clears its diagnostic action and keeps the Recheck button");
            settings.TaskbarMonitorEnabled=false;state.Panels=0;ApplyTaskbarInfo(state);
            require(taskbarDiagnosticsButton!.Visibility==Visibility.Collapsed,"A deliberately disabled embedded monitor is not reported as a compatibility failure");
            state.LastError=unchecked((int)0x80004005);ApplyTaskbarInfo(state);
            require(taskbarDiagnosticsButton!.Visibility==Visibility.Visible && taskbarConnectionMessage.Text.Contains("80004005",StringComparison.Ordinal),"Native taskbar errors remain visible independently of monitor embedding");
            string details=System.Text.Json.JsonSerializer.Serialize(TaskbarController.CompatibilityDetails());
            require(details.Contains("explorerVersion",StringComparison.Ordinal) && details.Contains("runtimeHostDetection",StringComparison.Ordinal),"Diagnostics identify the OS and Explorer build without a version allowlist");
            await RenderCheckImageAsync(L.LanguageCode+"-taskbar-compatibility");
        }
        finally{settings.TaskbarMonitorEnabled=enabled;}
    }
}
