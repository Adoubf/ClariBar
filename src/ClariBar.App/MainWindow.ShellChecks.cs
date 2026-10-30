using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private bool helpVerified;
    private readonly List<FrameworkElement> layoutControls=[];
    private async Task CheckShellAsync()
    {
        var checks=new List<object>();var original=AppWindow.Size;
        bool preference=settings.NavigationCollapsed;
        try
        {
            settings.Theme="Light";ApplyTheme();settings.NavigationCollapsed=false;wideNavigation=null;
            AppWindow.Resize(new SizeInt32(1220,860));await Task.Delay(400);ApplyShellLayout();
            Nav.SelectedItem=Nav.MenuItems[0];await Task.Delay(300);
            double expanded=PageSurface.ActualWidth;
            ToggleNavigation();await Task.Delay(35);
            bool paneAnimation=!uiPreferences.AnimationsEnabled || paneMotion?.GetCurrentState()==ClockState.Active;
            await Task.Delay(1000);
            File.WriteAllText(Path.Combine(AppPaths.Logs,"claribar-navigation-debug.json"),JsonSerializer.Serialize(new{mode=Nav.PaneDisplayMode.ToString(),display=Nav.DisplayMode.ToString(),open=Nav.IsPaneOpen,visible=Nav.IsPaneVisible,items=Nav.MenuItems.OfType<NavigationViewItem>().Select(item=>new{title=item.Content?.ToString(),width=item.ActualWidth,height=item.ActualHeight,opacity=item.Opacity,visibility=item.Visibility.ToString(),iconWidth=item.Icon?.ActualWidth,iconHeight=item.Icon?.ActualHeight,iconX=item.Icon?.TransformToVisual(Root).TransformPoint(new()).X})},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
            bool compact=!Nav.IsPaneOpen && PageSurface.ActualWidth-expanded>120 && store.Load().NavigationCollapsed;
            await RenderCheckImageAsync("shell-compact");
            ToggleNavigation();await Task.Delay(320);
            bool restored=Nav.IsPaneOpen && !store.Load().NavigationCollapsed;
            if(!paneAnimation || !compact || !restored)throw new IOException("导航折叠、动画或设置保存检查失败。");
            Nav.SelectedItem=Nav.MenuItems[1];await Task.Delay(35);
            bool pageAnimation=!uiPreferences.AnimationsEnabled || pageMotion?.GetCurrentState()==ClockState.Active;
            await Task.Delay(260);
            await ShowHelpAsync();
            if(!helpVerified || !pageAnimation)throw new IOException("帮助窗口或页面切换动画检查失败。");
            foreach(int width in new[]{600,900,1220,1600})
            {
                AppWindow.Resize(new SizeInt32(width,width==600?700:860));await Task.Delay(350);
                foreach(var item in Nav.MenuItems.OfType<NavigationViewItem>())
                {
                    Nav.SelectedItem=item;await Task.Delay(260);
                    CheckShortcutTooltips();
                    var failures=new List<string>();
                    foreach(var element in layoutControls.Concat(PageContent.Children.OfType<FrameworkElement>()))
                    {
                        if(element.Visibility!=Visibility.Visible || element.ActualWidth<1)continue;
                        var position=element.TransformToVisual(PageContent).TransformPoint(new());
                        if(position.X< -2 || position.X+element.ActualWidth>PageContent.ActualWidth+2)
                            failures.Add($"{element.GetType().Name} {position.X:F1}+{element.ActualWidth:F1}/{PageContent.ActualWidth:F1}");
                    }
                    var searchAt=SearchBox.TransformToVisual(Root).TransformPoint(new());
                    double searchCenterDelta=Math.Abs(searchAt.X+SearchBox.ActualWidth/2-Root.ActualWidth/2);
                    double searchVerticalDelta=Math.Abs(searchAt.Y+SearchBox.ActualHeight/2-ShellTitleBar.ActualHeight/2);
                    bool search=SearchBox.ActualWidth>=100 && searchAt.X+SearchBox.ActualWidth<Root.ActualWidth-120 && searchCenterDelta<=1.25 && searchVerticalDelta<=1.25;
                    bool passed=failures.Count==0 && search && PageSurface.ActualWidth>=300;
                    checks.Add(new{width,page=(string)item.Tag,contentWidth=PageContent.ActualWidth,paneOpen=Nav.IsPaneOpen,search,searchCenterDelta,searchVerticalDelta,searchHostWidth=TitleSearchHost.ActualWidth,searchHostX=TitleSearchHost.TransformToVisual(Root).TransformPoint(new()).X,searchWidth=SearchBox.ActualWidth,searchMargin=SearchBox.Margin.Left,failures,passed});
                    if(!passed)throw new IOException("自适应布局检查失败："+JsonSerializer.Serialize(checks[^1]));
                    if((width==600 && (string)item.Tag is "sensors" or "colors" or "about") ||
                        (width==1220 && (string)item.Tag is "overview" or "about"))
                        await RenderCheckImageAsync($"shell-{width}-{item.Tag}");
                }
            }
            var report=new{capturedAt=DateTimeOffset.Now,passed=true,paneAnimation,compact,restored,pageAnimation,help=helpVerified,
                animationsEnabled=uiPreferences.AnimationsEnabled,pageAnimationCount,titleBar=ExtendsContentIntoTitleBar,
                toolbarColor=ShellTitleBar.Background is SolidColorBrush brush?brush.Color.ToString():"",
                checks};
            File.WriteAllText(Path.Combine(AppPaths.Logs,"claribar-shell-check.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
        }
        finally
        {
            settings.NavigationCollapsed=preference;AppWindow.Resize(original);wideNavigation=null;
            ApplyShellLayout();Save();
        }
    }
}
