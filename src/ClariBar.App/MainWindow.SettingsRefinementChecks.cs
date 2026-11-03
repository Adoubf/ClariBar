using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using L = ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private async Task CheckSettingsRefinementAsync(Action<bool,string> require)
    {
        bool ready=nativeReady;string background=settings.TaskbarBackgroundMode;int opacity=settings.TaskbarOpacity;
        try
        {
            nativeReady=false;
            await CheckTaskbarCompatibilityAsync(require);
            settings.TaskbarBackgroundMode="Tinted";settings.TaskbarOpacity=37;
            RenderPage();await Task.Delay(180);
            require(taskbarOpacityOption?.IsChecked==true && taskbarStyleOption?.IsChecked==false &&
                taskbarOpacitySlider!.IsEnabled && !taskbarStyleSelector!.IsEnabled,"An existing tinted background opens exclusively in opacity mode");
            var config=MonitorConfig();
            require(config.BackgroundMode==2 && config.BackgroundArgb>>24==37*255/100,"Opacity mode applies the selected alpha through a single native background mode");
            taskbarStyleOption!.IsChecked=true;taskbarStyleSelector!.SelectedIndex=1;
            config=MonitorConfig();
            require(taskbarStyleOption.IsChecked==true && taskbarOpacityOption!.IsChecked==false &&
                taskbarStyleSelector.IsEnabled && !taskbarOpacitySlider!.IsEnabled,"Selecting a background style disables the opacity controls");
            require(config.BackgroundMode==1 && config.BackgroundArgb>>24==0,"Full transparency ignores the retained opacity value");
            taskbarOpacitySlider!.Value=85;
            require(settings.TaskbarOpacity==37 && MonitorConfig().BackgroundArgb>>24==0,"Changing an inactive slider cannot affect the chosen background style");
            taskbarStyleSelector.SelectedIndex=0;config=MonitorConfig();
            require(config.BackgroundMode==0 && config.BackgroundArgb>>24==255,"The Windows background does not receive custom opacity");
            taskbarOpacityOption!.IsChecked=true;taskbarOpacitySlider.Value=53;
            taskbarStyleSelector.SelectedIndex=1;config=MonitorConfig();
            require(settings.TaskbarBackgroundMode=="Tinted" && !taskbarStyleSelector.IsEnabled && taskbarOpacitySlider.IsEnabled &&
                config.BackgroundMode==2 && config.BackgroundArgb>>24==53*255/100,"Selecting opacity mode disables and ignores the style selector");
            var saved=store.Load();
            require(saved.TaskbarBackgroundMode=="Tinted" && saved.TaskbarOpacity==53,"The exclusive background choice and opacity persist together");
            await RenderCheckImageAsync(L.LanguageCode+"-exclusive-background");

            NavigateTo("overview");await Task.Delay(100);
            require(!PageContent.Children.OfType<InfoBar>().Any(),"Overview omits the permanent hardware authorization notice");
            NavigateTo("tools");await Task.Delay(100);
            require(!PageContent.Children.OfType<InfoBar>().Any(info=>info.Title==L.T("Capture, pick and pin")),"Desktop tools omits the capture, pick and pin notice");
            require(!PageContent.Children.OfType<Border>().Select(card=>card.Child).OfType<Grid>()
                .SelectMany(grid=>grid.Children.OfType<StackPanel>()).SelectMany(panel=>panel.Children.OfType<TextBlock>())
                .Any(text=>text.Text==L.T("Snipaste shortcuts")),"Global shortcuts omits the Snipaste preset row");
            NavigateTo("colors");await Task.Delay(150);
            require(!PageContent.Children.OfType<InfoBar>().Any(),"System taskbar colors omit the permanent taskbar status notice");
            var selector=metricColorSelector!;
            var controls=(Grid)selector.Parent;
            var buttons=controls.Children.OfType<Button>().ToArray();
            require(buttons.Length==2 && buttons.All(button=>Grid.GetRow(button)==0 && Grid.GetColumn(button)>Grid.GetColumn(selector)),
                "Per-metric color actions appear in the same row to the right of the metric selector");
            var row=PageContent.Children.OfType<Border>().Select(card=>card.Child).OfType<Grid>()
                .Single(grid=>grid.Children.OfType<IconElement>().Any(icon=>icon.Tag?.ToString()==L.T("Select a metric")));
            require(row.Children.OfType<IconElement>().Count()==1 && row.Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().Count()==2,
                "The metric selector uses the shared setting row with a semantic icon and description");
            selector.ItemsSource=new[]{new SensorReading("refinement/color","test","CPU","Cpu","CPU Total","Load",25,"%","valid")};
            selector.SelectedIndex=0;
            require(buttons.All(button=>button.IsEnabled),"Selecting a metric enables both color actions");
            var at=row.TransformToVisual(PageContent).TransformPoint(new());
            PageScroll.ChangeView(null,Math.Max(0,at.Y-80),null,true);await Task.Delay(180);
            await RenderCheckImageAsync(L.LanguageCode+"-metric-color-row");
        }
        finally
        {
            settings.TaskbarBackgroundMode=background;settings.TaskbarOpacity=opacity;nativeReady=ready;Save();
        }
    }
}
