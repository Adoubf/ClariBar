using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
using Windows.UI.ViewManagement;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private const string AppVersion="0.6.2";
    private readonly List<Action<double>> responsiveLayouts=[];
    private readonly UISettings uiPreferences=new();
    private Storyboard? pageMotion,paneMotion,themeMotion;
    private readonly List<Storyboard> navigationMotions=[];
    private bool shellReady;
    private bool? wideNavigation;
    private string renderedPage="";
    private int pageAnimationCount;
    private Grid? pageHeader;

    private void InitializeShell()
    {
        ExtendsContentIntoTitleBar=true;SetTitleBar(ShellTitleBar);
        AppWindow.TitleBar.PreferredHeightOption=TitleBarHeightOption.Tall;
        AppWindow.TitleBar.IconShowOptions=IconShowOptions.HideIconAndSystemMenu;
        if(AppWindow.Presenter is OverlappedPresenter presenter)
        {presenter.PreferredMinimumWidth=600;presenter.PreferredMinimumHeight=480;}
        Root.Loaded+=(_,_)=>{shellReady=true;ApplyChromeColors();ApplyShellLayout();};
        Root.SizeChanged+=(_,_)=>ApplyShellLayout();
        Root.ActualThemeChanged+=(_,_)=>{ApplyChromeColors();Animate(Root,ref themeMotion,0,0,.82,160);};
        PageContent.SizeChanged+=(_,_)=>UpdateResponsiveLayouts();
        Nav.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty,(_,_)=>UpdatePaneAppearance());
        foreach(var item in Nav.MenuItems.OfType<NavigationViewItem>())
        {ToolTipService.SetToolTip(item,item.Content);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item,item.Content?.ToString()??"");item.Transitions=new TransitionCollection {new RepositionThemeTransition()};}
        AddShortcut(VirtualKey.F1,VirtualKeyModifiers.None,()=>_=ShowHelpAsync());
        AddShortcut(VirtualKey.F,VirtualKeyModifiers.Control,()=>SearchBox.Focus(FocusState.Keyboard));
        AddShortcut(VirtualKey.B,VirtualKeyModifiers.Control,ToggleNavigation);
    }
    private void AddShortcut(VirtualKey key,VirtualKeyModifiers modifiers,Action action)
    {
        var shortcut=new KeyboardAccelerator {Key=key,Modifiers=modifiers};
        shortcut.Invoked+=(_,args)=>{action();args.Handled=true;};Root.KeyboardAccelerators.Add(shortcut);
    }
    private void ApplyChromeColors()
    {
        bool dark=Root.ActualTheme==ElementTheme.Dark;
        var fill=ParseColor(dark?"#202020":"#ECF6F9");var ink=dark?Colors.White:Colors.Black;
        var title=AppWindow.TitleBar;
        title.BackgroundColor=title.InactiveBackgroundColor=fill;
        title.ButtonBackgroundColor=title.ButtonInactiveBackgroundColor=fill;
        title.ForegroundColor=title.ButtonForegroundColor=ink;
        title.InactiveForegroundColor=title.ButtonInactiveForegroundColor=dark?ParseColor("#BBBBBB"):ParseColor("#555555");
        title.ButtonHoverBackgroundColor=ParseColor(dark?"#353535":"#DEE9EC");
        title.ButtonPressedBackgroundColor=ParseColor(dark?"#404040":"#D3E0E4");
        title.ButtonHoverForegroundColor=title.ButtonPressedForegroundColor=ink;
    }
    private void ApplyShellLayout()
    {
        if(!shellReady)return;
        bool wide=Root.ActualWidth>=1000;
        if(wideNavigation!=wide)
        {
            wideNavigation=wide;
            Nav.PaneDisplayMode=wide && !settings.NavigationCollapsed?NavigationViewPaneDisplayMode.Left:NavigationViewPaneDisplayMode.LeftCompact;
            Nav.IsPaneOpen=wide && !settings.NavigationCollapsed;
        }
        bool small=Root.ActualWidth<760;
        BodySurface.Margin=small?new Thickness(4,0,4,4):new Thickness(8,0,8,8);
        BodySurface.CornerRadius=new CornerRadius(small?12:16);
        PageScroll.Padding=small?new Thickness(16,18,16,24):new Thickness(28,22,28,32);
        SearchBox.Margin=small?new Thickness(8,0,8,0):new Thickness(24,0,24,0);
        UpdatePaneAppearance();UpdateResponsiveLayouts();
    }
    private void UpdatePaneAppearance()
    {
        PaneIntro.Visibility=Nav.IsPaneOpen?Visibility.Visible:Visibility.Collapsed;
        FooterVersion.Text=Nav.IsPaneOpen?$"版本 {AppVersion}":AppVersion;
        FooterVersion.FontSize=Nav.IsPaneOpen?12:10;
        FooterVersion.Margin=new Thickness(0);
        PaneFooterContent.Margin=Nav.IsPaneOpen?new Thickness(16,12,8,20):new Thickness(8,12,0,20);
        PaneMotto.Visibility=Nav.IsPaneOpen?Visibility.Visible:Visibility.Collapsed;
    }
    private void TitleBar_PaneToggleRequested(TitleBar sender,object args)=>ToggleNavigation();
    private void ToggleNavigation()
    {
        CommitFontSize(true);
        bool open=!Nav.IsPaneOpen;settings.NavigationCollapsed=!open;
        if(Root.ActualWidth>=1000)Nav.PaneDisplayMode=open?NavigationViewPaneDisplayMode.Left:NavigationViewPaneDisplayMode.LeftCompact;
        Nav.IsPaneOpen=open;UpdatePaneAppearance();Save();
        Animate(PageSurface,ref paneMotion,open?10:-10,0,.82,200);
        foreach(var motion in navigationMotions)motion.Stop();navigationMotions.Clear();
        foreach(var item in Nav.MenuItems.OfType<NavigationViewItem>())
        {
            Storyboard? motion=null;Animate(item,ref motion,open?6:-6,0,.4,200);
            if(motion is not null)navigationMotions.Add(motion);
        }
    }
    private void Animate(FrameworkElement target,ref Storyboard? previous,double x,double y,double opacity,int milliseconds)
    {
        previous?.Stop();target.Opacity=1;
        var offset=new TranslateTransform();target.RenderTransform=offset;
        if(!shellReady || !uiPreferences.AnimationsEnabled)return;
        var storyboard=new Storyboard();
        void Add(DependencyObject element,string property,double from,double to)
        {
            var animation=new DoubleAnimation {From=from,To=to,Duration=TimeSpan.FromMilliseconds(milliseconds),
                EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}};
            Storyboard.SetTarget(animation,element);Storyboard.SetTargetProperty(animation,property);storyboard.Children.Add(animation);
        }
        Add(target,"Opacity",opacity,1);
        if(x!=0)Add(offset,"X",x,0);if(y!=0)Add(offset,"Y",y,0);
        previous=storyboard;storyboard.Begin();
    }
    private void AnimatePage()
    {
        if(renderedPage==page)return;renderedPage=page;
        Animate(PageContent,ref pageMotion,0,10,0,200);
        if(shellReady && uiPreferences.AnimationsEnabled)pageAnimationCount++;
    }
    private double ContentWidth=>PageContent.ActualWidth>0?PageContent.ActualWidth:880;
    private void Responsive(Action<double> layout)
    {responsiveLayouts.Add(layout);layout(ContentWidth);}
    private void UpdateResponsiveLayouts()
    {foreach(var layout in responsiveLayouts.ToArray())layout(ContentWidth);}
    private void ResponsiveHeader(Grid grid,FrameworkElement trailing,double threshold=660)
    {
        grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        bool? previous=null;
        Responsive(width=>
        {
            bool narrow=width<threshold;if(previous==narrow)return;previous=narrow;
            Grid.SetRow(trailing,narrow?1:0);Grid.SetColumn(trailing,narrow?0:1);Grid.SetColumnSpan(trailing,narrow?2:1);
            trailing.HorizontalAlignment=narrow?HorizontalAlignment.Left:HorizontalAlignment.Right;grid.RowSpacing=narrow?8:0;
        });
    }
    private void ResponsiveTiles(Grid grid,int count,double threshold=720)
    {
        int previous=0;
        grid.ChildrenTransitions=new TransitionCollection {new RepositionThemeTransition()};
        Responsive(width=>
        {
            int columns=width<threshold?(width<380?1:2):count;if(columns==previous)return;previous=columns;
            grid.ColumnDefinitions.Clear();grid.RowDefinitions.Clear();grid.RowSpacing=10;
            for(int i=0;i<columns;i++)grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            for(int i=0;i<(count+columns-1)/columns;i++)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
            for(int i=0;i<grid.Children.Count;i++){Grid.SetColumn((FrameworkElement)grid.Children[i],i%columns);Grid.SetRow((FrameworkElement)grid.Children[i],i/columns);}
        });
    }
    private void ResponsiveRow(Grid grid,FrameworkElement control)
    {
        grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var text=(FrameworkElement)grid.Children[1];var alignment=control.HorizontalAlignment;
        bool? previous=null;
        Responsive(width=>
        {
            bool stack=width<640;if(previous==stack)return;previous=stack;
            grid.ColumnDefinitions[2].Width=stack?new GridLength(0):GridLength.Auto;
            Grid.SetColumnSpan(text,stack?2:1);Grid.SetRow(control,stack?1:0);Grid.SetColumn(control,stack?1:2);Grid.SetColumnSpan(control,stack?2:1);
            control.HorizontalAlignment=stack?HorizontalAlignment.Left:alignment;grid.RowSpacing=stack?12:0;
        });
    }
    private void Help_Click(object sender,RoutedEventArgs args)=>_=ShowHelpAsync();
    private void Shortcuts_Click(object sender,RoutedEventArgs args)=>_=ShowHelpAsync(true);
    private void About_Click(object sender,RoutedEventArgs args)=>Nav.SelectedItem=Nav.MenuItems[5];
    private async Task ShowHelpAsync(bool shortcuts=false)
    {
        var content=new StackPanel {Spacing=16,MaxWidth=480};
        void Section(string title,string body)
        {content.Children.Add(new TextBlock {Text=title,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});content.Children.Add(new TextBlock {Text=body,TextWrapping=TextWrapping.Wrap});}
        if(!shortcuts)
        {
            Section("开始使用","在“任务栏外观”选择透明样式并开启内嵌监控；在“资源监控”勾选需要显示的指标，最多 8 项。");
            Section("温度与风扇","点击“授权硬件访问”连接硬件采集；连接状态会显示在概览和资源监控页。硬件未提供的传感器会说明原因。");
            Section("文字与配色","在“文字与图标”调整字体、字号、布局和颜色。字号输入后按回车或点击空白处保存。");
            Section("导航与窗口","顶栏左侧按钮可折叠为图标导航。窗口缩小时内容自动换行；悬停导航图标可查看页面名称。");
            Section("关闭与退出","关闭窗口默认保留任务栏监控；从系统托盘菜单可彻底退出并恢复任务栏。");
        }
        Section("键盘快捷键","F1    打开使用帮助\nCtrl + F    查找传感器\nCtrl + B    展开或折叠导航\nEnter    确认字号\nEsc    取消当前字号输入");
        var dialog=new ContentDialog {Title=shortcuts?"键盘快捷键":"使用帮助",Content=new ScrollViewer {Content=content,MaxHeight=Math.Max(180,Root.ActualHeight-200)},CloseButtonText="关闭",XamlRoot=Root.XamlRoot};
        try
        {
            var shown=dialog.ShowAsync();
            if(smokeTest){await Task.Delay(250);helpVerified=dialog.ActualWidth>200;await RenderCheckImageAsync("help",dialog);dialog.Hide();}
            await shown;
        }
        catch(InvalidOperationException) { }
    }

    private void ResponsiveSensors(Grid filters,ComboBox categories,ComboBox kinds,ComboBox device,StackPanel summary,Grid header,ListView list,DataTemplate wideTemplate)
    {
        for(int i=0;i<3;i++)filters.RowDefinitions.Add(new(){Height=GridLength.Auto});
        filters.RowSpacing=8;
        var compactTemplate=(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
          <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
           <Grid ColumnSpacing="12" Padding="0,9" HorizontalAlignment="Stretch">
            <Grid.ColumnDefinitions><ColumnDefinition Width="2*"/><ColumnDefinition Width="1.2*"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
            <StackPanel Spacing="3" VerticalAlignment="Center">
             <TextBlock Text="{Binding Name}" TextTrimming="CharacterEllipsis" ToolTipService.ToolTip="{Binding Details}"/>
             <TextBlock Text="{Binding Device}" FontSize="11" TextTrimming="CharacterEllipsis" Foreground="{ThemeResource TextFillColorSecondaryBrush}" ToolTipService.ToolTip="{Binding Device}"/>
            </StackPanel>
            <TextBlock Grid.Column="1" Text="{Binding Value}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" ToolTipService.ToolTip="{Binding Value}" VerticalAlignment="Center"/>
            <TextBlock Grid.Column="2" Text="{Binding Status}" FontSize="12" TextTrimming="CharacterEllipsis" ToolTipService.ToolTip="{Binding Status}" VerticalAlignment="Center"/>
           </Grid>
          </DataTemplate>
          """);
        int previous=-1;bool? compactBefore=null;
        Responsive(width=>
        {
            int mode=width>=860?0:width>=560?1:width>=420?2:3;
            if(mode!=previous)
            {
                previous=mode;
                filters.ColumnDefinitions[0].Width=mode>=2?new GridLength(1,GridUnitType.Star):new GridLength(140);
                filters.ColumnDefinitions[1].Width=mode>=2?new GridLength(1,GridUnitType.Star):new GridLength(140);
                filters.ColumnDefinitions[2].MinWidth=0;
                filters.ColumnDefinitions[2].Width=mode>=2?new GridLength(0):new GridLength(1,GridUnitType.Star);
                filters.ColumnDefinitions[3].Width=mode==0?GridLength.Auto:new GridLength(0);
                Grid.SetRow(device,mode>=2?1:0);Grid.SetColumn(device,mode>=2?0:2);Grid.SetColumnSpan(device,mode>=2?2:1);
                Grid.SetRow(summary,mode==0?0:mode==1?1:2);Grid.SetColumn(summary,mode==0?3:0);Grid.SetColumnSpan(summary,mode==0?1:mode==1?3:2);
                summary.Margin=mode==0?new Thickness(8,0,0,0):new Thickness(0);
                summary.Orientation=mode==3?Orientation.Vertical:Orientation.Horizontal;
            }
            bool compact=width<660;if(compactBefore==compact)return;compactBefore=compact;
            list.ItemTemplate=compact?compactTemplate:wideTemplate;header.Children.Clear();header.ColumnDefinitions.Clear();
            var weights=compact?new[]{2d,1.2,1}:new[]{2d,1.8,1,1.2,.9};
            var labels=compact?new[]{"指标与设备","当前值","状态"}:new[]{"指标","设备","类型","当前值","状态"};
            for(int i=0;i<labels.Length;i++)
            {
                header.ColumnDefinitions.Add(new(){Width=new GridLength(weights[i],GridUnitType.Star)});
                var text=Description(labels[i]);Grid.SetColumn(text,i);header.Children.Add(text);
            }
        });
    }
}
