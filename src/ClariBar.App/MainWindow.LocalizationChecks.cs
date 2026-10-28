using ClariBar.Core;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private bool ContainsBareShortcutTip(DependencyObject node)
    {
        if(node is TextBlock {Text:"F1"})return true;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
            if(ContainsBareShortcutTip(VisualTreeHelper.GetChild(node,i)))return true;
        return false;
    }
    private void CheckShortcutTooltips()
    {
        bool hidden=Root.KeyboardAcceleratorPlacementMode==Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
        bool enabled=Root.KeyboardAccelerators.Count==3 && Root.KeyboardAccelerators.All(shortcut=>shortcut.IsEnabled);
        bool floating=VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot)
            .Any(popup=>popup.IsOpen && popup.Child is {} content && ContainsBareShortcutTip(content));
        if(!hidden || !enabled || floating)throw new IOException("A floating root shortcut tooltip is visible, or application shortcuts are disabled.");
    }
    private async Task CheckLocalizationAsync()
    {
        string original=settings.Language,theme=settings.Theme;
        string startupLanguage=Localizer.LanguageCode;
        var originalPins=settings.PinnedSensors.ToArray();
        string colors=JsonSerializer.Serialize(settings.SensorColors);
        string font=settings.MonitorFontFamily;int size=settings.MonitorFontSize;bool bold=settings.MonitorBold;
        const string marker="localization-preservation-check",customName="My fan 自定义";
        bool hadMarker=settings.SensorNames.TryGetValue(marker,out string? oldMarker);
        settings.SensorNames[marker]=customName;
        var reports=new List<object>();
        try
        {
            foreach(string locale in new[]{"en-US","zh-CN"})
            {
                ChangeLanguage(locale);await Task.Delay(300);
                Nav.SelectedItem=Nav.MenuItems[4];await Task.Delay(250);
                int expectedIndex=locale=="en-US"?1:2;
                if(Root.Language!=locale || store.Load().Language!=locale || languageSelector?.SelectedIndex!=expectedIndex)
                    throw new IOException("The language selector, active UI and saved preference disagree.");
                if(settings.SensorNames[marker]!=customName || !settings.PinnedSensors.SequenceEqual(originalPins) ||
                   JsonSerializer.Serialize(settings.SensorColors)!=colors || settings.MonitorFontFamily!=font ||
                   settings.MonitorFontSize!=size || settings.MonitorBold!=bold)
                    throw new IOException("Changing language modified monitoring preferences or a custom name.");
                for(int i=0;i<Nav.MenuItems.Count;i++)
                {
                    Nav.SelectedItem=Nav.MenuItems[i];await Task.Delay(220);
                    CheckShortcutTooltips();
                    if(i==2)CheckSensorControls();
                    string expected=locale=="en-US"?new[]{"Overview","Taskbar","Resource monitor","Text & icons","General","About"}[i]:
                        new[]{"概览","任务栏外观","资源监控","文字与图标","通用设置","关于"}[i];
                    if((Nav.MenuItems[i] as NavigationViewItem)?.Content?.ToString()!=expected)
                        throw new IOException("Navigation labels did not update to "+locale);
                }
                settings.Theme="Light";ApplyTheme();
                foreach(int i in new[]{0,2,3})
                {
                    Nav.SelectedItem=Nav.MenuItems[i];await Task.Delay(350);
                    string pageName=i==0?"overview-light":i==2?"sensors":"colors";
                    await RenderCheckImageAsync(locale+"-"+pageName);
                }
                settings.Theme="Dark";ApplyTheme();Nav.SelectedItem=Nav.MenuItems[0];await Task.Delay(400);
                CheckShortcutTooltips();await RenderCheckImageAsync(locale+"-overview-dark");
                settings.Theme="Light";ApplyTheme();await Task.Delay(200);
                await CheckShellAsync();await CheckProjectAboutAsync();
                string shell=Path.Combine(AppPaths.Logs,"claribar-shell-check.json");
                File.Copy(shell,Path.Combine(AppPaths.Logs,"claribar-shell-"+locale+".json"),true);
                using var document=JsonDocument.Parse(File.ReadAllText(shell));
                reports.Add(new{locale,selectionSaved=true,customNamesPreserved=true,monitorPreferencesPreserved=true,
                    responsiveCases=document.RootElement.GetProperty("checks").GetArrayLength(),rootShortcutTooltipHidden=true});
            }
            Nav.SelectedItem=Nav.MenuItems[4];ChangeLanguage("System");await Task.Delay(250);
            string expectedSystem=Localizer.ResolveLanguage("System",AppLocalization.SystemLanguage);
            if(Root.Language!=expectedSystem || store.Load().Language!="System" || languageSelector?.SelectedIndex!=0)
                throw new IOException("Follow system did not resolve or persist correctly.");
            CheckShortcutTooltips();
            var report=new{passed=true,systemLanguage=AppLocalization.SystemLanguage,startupLanguage,
                followedSystem=Root.Language,followSystemSaved=true,reports};
            File.WriteAllText(Path.Combine(AppPaths.Logs,"claribar-localization-check.json"),
                JsonSerializer.Serialize(report,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
        }
        finally
        {
            if(hadMarker)settings.SensorNames[marker]=oldMarker!;else settings.SensorNames.Remove(marker);
            settings.Theme=theme;ChangeLanguage(original);ApplyTheme();Save();
        }
    }
}
