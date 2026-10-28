using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Graphics;
using Windows.UI;

namespace ClariBar.App;

public sealed partial class MainWindow : Window
{
    private readonly SettingsStore store;
    private readonly AppSettings settings;
    private BrokerClient? broker;
    private SensorSnapshot snapshot = SensorSnapshot.Empty("connecting");
    private readonly Native.TrayCallback trayCallback;
    private readonly DispatcherTimer freshnessTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly ObservableCollection<SensorRow> sensorRows = [];
    private readonly Dictionary<string, SensorRow> rowIndex = [];
    private string page = "overview", query = "", category = "全部类别", sensorKind = "常用指标", deviceId = "";
    private string connection = "正在连接采集进程…";
    private bool nativeReady, quitting, reloading, synchronizingSelection,showUnavailable;
    private BrokerPhase connectionPhase=BrokerPhase.Idle;
    private string? accessFallbackNotice;
    private readonly List<BrokerPhase> connectionTransitions=[];
    private Button? hardwareAction;
    private TextBlock? hardwareState;
    private ProgressRing? hardwareProgress;
    private Grid? sensorTitleRow,sensorFilters;
    private StackPanel? sensorFilterSummary;
    private CheckBox? unavailableFilter;
    private object? sensorControlsVerification;
    private DateTimeOffset lastHealthExport=DateTimeOffset.MinValue;
    private readonly bool smokeTest;
    private readonly nint hwnd;
    private ListView? sensorList;
    private TextBlock? statusText, countText, cpuValue, gpuValue, temperatureValue, fanValue;
    private TextBlock? emptySensors;
    private InfoBar? accessInfo,networkInfo;
    private InfoBar? taskbarInfo;
    private readonly TaskbarController taskbar = new();
    private ComboBox? deviceFilter;
    private ComboBox? renameSelector;
    private ComboBox? metricColorSelector;
    private TaskbarPreview? taskbarPreview,expandedPreview;
    private ColorScope paletteScope=ColorScope.Monitor;
    private string[]? fontFamilies;
    private NumberBox? fontSizeEditor;
    private bool updatingFontSize;

    public MainWindow()
    {
        smokeTest = Environment.GetCommandLineArgs().Contains("--smoke-test");
        store = new SettingsStore(smokeTest ? Path.Combine(AppPaths.Data, "smoke-session") : AppPaths.Data);
        settings = store.Load();
        if(NetworkMetrics.Migrate(settings))Save();
        InitializeComponent();
        Root.AddHandler(UIElement.PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler(OnPagePointerPressed),true);
        Activated+=(_,args)=>{if(args.WindowActivationState==WindowActivationState.Deactivated)CommitFontSize(true);};
        Title = "澄栏 · ClariBar";
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.Resize(new SizeInt32(1220, 860));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "ClariBar.ico"));
        InitializeShell();ApplyTheme();
        trayCallback = action => DispatcherQueue.TryEnqueue(() =>
        {
            if (action == 1) ShowSettings();
            else if (action == 2) _ = QuitAsync();
            else if (action == 3) { settings.MiniMonitorEnabled = !settings.MiniMonitorEnabled; Save(); UpdateMonitor(); if (page == "taskbar") RenderPage(); }
            else if(action==4){settings.TaskbarMonitorEnabled=!settings.TaskbarMonitorEnabled;TaskbarChanged();if(page=="taskbar")RenderPage();}
        });
        try { nativeReady = Native.CB_Start(trayCallback) == 1; }
        catch (Exception ex) { connection = $"原生模块未加载：{ex.Message}"; }
        AppWindow.Closing += (sender, e) =>
        {
            CommitFontSize(true);
            if (quitting) return;
            e.Cancel = true;
            if (settings.CloseToTray && nativeReady && !smokeTest) Native.ShowWindow(hwnd, 0);
            else _ = QuitAsync();
        };
        string? requestedPage=Environment.GetCommandLineArgs().FirstOrDefault(arg=>arg.StartsWith("--page="))?[7..];
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item=>(string)item.Tag==requestedPage)
            ?? Nav.MenuItems.OfType<NavigationViewItem>().First();
        freshnessTimer.Tick += (_, _) => UpdateLive();
        freshnessTimer.Start();
        _ = ConnectAsync(Environment.GetCommandLineArgs().Contains("--elevated") || (!smokeTest && settings.HardwareAccessEnabled));
        if (!smokeTest && TaskbarController.Needed(settings)) _ = EnsureTaskbarAsync();
        if (smokeTest) _ = SmokeTestAsync();
    }

    private async Task ConnectAsync(bool elevated,bool fallback=false)
    {
        if(reloading || quitting)return;
        reloading=true;
        if(!fallback)accessFallbackNotice=null;
        snapshot=SensorSnapshot.Empty("connecting");
        SetConnectionState(elevated?BrokerPhase.Authorizing:BrokerPhase.Connecting,
            elevated?"正在等待 Windows 硬件访问授权…":"正在连接采集进程…");
        UpdateLive();
        try
        {
            var previous=broker;broker=null;
            if(previous is not null)await previous.DisposeAsync();
            snapshot = SensorSnapshot.Empty("connecting");
            var next = new BrokerClient { IntervalMs = settings.IntervalMs };
            broker = next;
            next.Sample += data => DispatcherQueue.TryEnqueue(() =>
            {
                if (quitting || broker != next) return;
                snapshot = data;
                if(data.Elevated && !settings.HardwareAccessEnabled){settings.HardwareAccessEnabled=true;Save();}
                SetConnectionState(BrokerPhase.Connected,data.Errors.Length>0?$"采集部分可用 · {data.Errors[0]}":data.Elevated?"硬件访问已授权":"基础监控已连接");
                UpdateLive();
                if(Environment.GetCommandLineArgs().Contains("--diagnostics") && DateTimeOffset.Now-lastHealthExport>TimeSpan.FromSeconds(5))
                {
                    lastHealthExport=DateTimeOffset.Now;
                    Directory.CreateDirectory(AppPaths.Logs);
                    File.WriteAllText(Path.Combine(AppPaths.Logs,"claribar-current-sensors.json"),JsonSerializer.Serialize(data,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
                    File.WriteAllText(Path.Combine(AppPaths.Logs,"claribar-connection-state.json"),JsonSerializer.Serialize(ConnectionUiSnapshot(),new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
                }

            });
            next.StateChanged += state => DispatcherQueue.TryEnqueue(() =>
            {
                if (broker != next || quitting) return;
                SetConnectionState(state.Phase,state.Message);
                if(state.Phase is BrokerPhase.Cancelled or BrokerPhase.Failed && elevated)accessFallbackNotice=state.Message;
                // Do not keep showing old valid-looking values after disconnect.
                snapshot = snapshot with { Status = "disconnected", CapturedAt = DateTimeOffset.MinValue };
                UpdateLive();
            });
            next.Failed+=cancelled=>DispatcherQueue.TryEnqueue(()=>
            {
                if(broker!=next || quitting || !elevated)return;
                settings.HardwareAccessEnabled=false;Save();
                _=ConnectAsync(false,true);
            });
            next.Start(elevated);
        }
        finally { reloading=false;UpdateHardwareAction(); }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        { CommitFontSize(false);page = tag; RenderPage(); }
    }
    private void Search_Submitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        query = args.QueryText.Trim();
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == "sensors");
        if (page == "sensors") RenderPage();
    }

    private void RenderPage()
    {
        if (PageContent is null) return;
        sensorList = null; statusText = null; accessInfo = null; networkInfo=null; taskbarInfo = null; countText = null; emptySensors = null;
        cpuValue = gpuValue = temperatureValue = fanValue = null;
        deviceFilter = null; renameSelector = null; metricColorSelector = null; taskbarPreview = null;
        hardwareAction=null;hardwareState=null;hardwareProgress=null;sensorTitleRow=null;sensorFilters=null;sensorFilterSummary=null;unavailableFilter=null;
        fontSizeEditor=null;
        responsiveLayouts.Clear();layoutControls.Clear();
        PageContent.Children.Clear();
        PageScroll.ChangeView(null, 0, null);
        string title = page switch
        {
            "taskbar" => "任务栏外观", "sensors" => "资源监控", "colors" => "文字与图标",
            "general" => "通用设置", "about" => "关于澄栏", _ => "概览"
        };
        PageContent.Children.Add(new TextBlock { Text = "澄栏  ›  " + title, Style = SecondaryStyle, Margin = new Thickness(0, 0, 0, 2) });
        var pageTitle=new TextBlock {Text=title,FontSize=28,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
        pageHeader=new Grid {ColumnSpacing=18,Margin=new Thickness(0,0,0,12)};
        pageHeader.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        pageHeader.ColumnDefinitions.Add(new(){Width=GridLength.Auto});pageHeader.Children.Add(pageTitle);
        pageTitle.TextWrapping=TextWrapping.Wrap;PageContent.Children.Add(pageHeader);
        if(page=="sensors")
        {
            sensorTitleRow=pageHeader;var action=HardwareActionControl();Grid.SetColumn(action,1);pageHeader.Children.Add(action);
            ResponsiveHeader(pageHeader,action);
        }
        else if(page=="about")
        {
            var version=Description($"{AppVersion} · Windows 11 任务栏与实时监控");
            version.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(version,1);pageHeader.Children.Add(version);
            ResponsiveHeader(pageHeader,version,760);
        }
        switch (page)
        {
            case "taskbar": BuildTaskbar(); break;
            case "sensors": BuildSensors(); break;
            case "colors": BuildColors(); break;
            case "general": BuildGeneral(); break;
            case "about": BuildAbout(); break;
            default: BuildOverview(); break;
        }
        UpdateLive();UpdateResponsiveLayouts();AnimatePage();
    }

    private Style SecondaryStyle => (Style)Application.Current.Resources["SecondaryText"];
    private Border Card(UIElement child) => new()
    { Style = (Style)Application.Current.Resources["SettingsCard"], Child = child };
    private TextBlock Description(string text) => new() { Text = text, Style = SecondaryStyle };
    private Button Button(string text, Action clicked, bool accent = false)
    {
        var button = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
        if (accent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        button.Click += (_, _) => clicked();
        return button;
    }
    private void Heading(string title) => PageContent.Children.Add(new TextBlock
    { Text = title, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 2) });
    private void Row(string title, string subtitle, UIElement control, string glyph = "\uE713")
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var icon = new FontIcon { Glyph = glyph, FontSize = 20, Width = 24, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(icon);
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(Description(subtitle)); Grid.SetColumn(text, 1); grid.Children.Add(text);
        Grid.SetColumn((FrameworkElement)control, 2); grid.Children.Add(control);
        layoutControls.Add((FrameworkElement)control);
        PageContent.Children.Add(Card(grid));ResponsiveRow(grid,(FrameworkElement)control);
    }
    private ToggleSwitch Toggle(bool current, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsOn = current, OnContent = "开", OffContent = "关", VerticalAlignment = VerticalAlignment.Center };
        toggle.Toggled += (_, _) => changed(toggle.IsOn);
        return toggle;
    }
    private InfoBar Notice(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        var info = new InfoBar { IsOpen = true, IsClosable = false, Title = title, Message = message, Severity = severity };
        PageContent.Children.Add(info); return info;
    }

    private void BuildOverview()
    {
        PageContent.Children.Add(Description("清晰掌握系统状态，让桌面保持轻盈。"));
        var tiles = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 8, 0, 6) };
        for (int i = 0; i < 4; i++) tiles.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var labels = new[] { "CPU 占用", "CPU 温度", "GPU · 3D", "风扇通道" };
        var values = new TextBlock[4];
        for (int i = 0; i < labels.Length; i++)
        {
            var stack = new StackPanel { Spacing = 12, MinHeight = 88 };
            stack.Children.Add(Description(labels[i]));
            values[i] = new TextBlock { Text = "—", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            stack.Children.Add(values[i]); var card = Card(stack); Grid.SetColumn(card, i); tiles.Children.Add(card);
        }
        cpuValue = values[0]; temperatureValue = values[1]; gpuValue = values[2]; fanValue = values[3];
        ResponsiveTiles(tiles,4);PageContent.Children.Add(tiles);
        accessInfo = Notice("硬件访问", "正在连接采集进程…");
        Row("读取温度与风扇转速", "授权独立采集进程访问硬件，设置界面保持普通权限。",
            HardwareActionControl(), "\uEA18");
        Row("监控窗", "显示已固定的指标，默认鼠标穿透；关闭穿透后可拖动。",
            Toggle(settings.MiniMonitorEnabled, value => { settings.MiniMonitorEnabled = value; Save(); UpdateMonitor(); }), "\uE9D9");
        Row("选择监控指标", "CPU、GPU、磁盘、网络和主板传感器；支持固定与重命名。",
            Button("打开资源监控", () => Nav.SelectedItem = Nav.MenuItems[2]), "\uE9D9");
        Heading("采集状态");
        statusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = SecondaryStyle };
        PageContent.Children.Add(Card(statusText));
        if (store.LoadWarning is not null) Notice("配置恢复", store.LoadWarning, InfoBarSeverity.Warning);
    }

    private void BuildTaskbar()
    {
        AddTaskbarPreview();
        taskbarInfo = Notice("Explorer 任务栏适配", "连接后会核对本机任务栏控件与应用状态。");
        Row("任务栏连接", "系统或显示器设置更新后，可重新检查连接；关闭功能会恢复原有外观。",
            Button("连接 / 重新检查", () => _ = EnsureTaskbarAsync(true), true), "\uE7F4");
        Heading("任务栏背景");
        var mode = new ComboBox { ItemsSource = new[] { "跟随系统", "全透明", "半透明着色" },
            SelectedIndex = settings.TaskbarBackgroundMode switch { "Transparent" => 1, "Tinted" => 2, _ => 0 }, MinWidth = 160 };
        mode.SelectionChanged += (_, _) => { settings.TaskbarBackgroundMode = mode.SelectedIndex switch {1=>"Transparent",2=>"Tinted",_=>"System"}; TaskbarChanged(); };
        Row("背景样式", "调整任务栏背景；选择跟随系统可恢复原有外观。", mode, "\uE790");
        AddColorPicker("任务栏背景颜色", settings.TaskbarBackgroundColor, hex => settings.TaskbarBackgroundColor = hex);
        var opacity = new Slider { Minimum = 0, Maximum = 100, Value = settings.TaskbarOpacity, StepFrequency = 1, Width = 200 };
        opacity.ValueChanged += (_, args) => { settings.TaskbarOpacity = (int)args.NewValue; TaskbarChanged(); };
        Row("背景不透明度", "仅半透明着色模式使用，0% 完全透明，100% 实色。", opacity, "\uE790");
        Heading("内嵌资源监控");
        Row("嵌入 Windows 任务栏", "作为托盘布局中的真实子项显示，各任务栏分别参与空间分配。",
            Toggle(settings.TaskbarMonitorEnabled, value => {settings.TaskbarMonitorEnabled=value;TaskbarChanged();}), "\uE9D9");
        AddMonitorLayoutControls();
        Row("选择监控指标", "最多 8 项；默认显示 CPU、温度与当前最活跃网卡的上下行。",
            Button("选择指标", () => Nav.SelectedItem = Nav.MenuItems[2]), "\uE8FD");
        Row("逐项文字颜色", "固定指标可分别指定颜色，未单独设置时跟随默认文字颜色。",
            Button("设置颜色", () => Nav.SelectedItem = Nav.MenuItems[3]), "\uE790");
        Heading("独立监控窗");
        Row("额外显示独立监控窗", "与任务栏内嵌显示独立开关；关闭鼠标穿透后可拖动位置。",
            Toggle(settings.MiniMonitorEnabled, value => {settings.MiniMonitorEnabled=value;Save();UpdateMonitor();}), "\uE9D9");
        Heading("还原");
        Row("还原任务栏修改", "关闭背景、系统前景色与内嵌显示，保留你的颜色数值和指标选择。", Button("全部还原", () =>
        {
            settings.TaskbarBackgroundMode="System"; settings.TaskbarMonitorEnabled=false;
            settings.TimeColorEnabled=settings.DateColorEnabled=settings.TrayColorEnabled=false;
            settings.NetworkColorEnabled=settings.VolumeColorEnabled=settings.InputColorEnabled=settings.BatteryColorEnabled=false;
            TaskbarChanged(); RenderPage();
        }), "\uE777");
    }

    private void AddMonitorLayoutControls()
    {
        var layout = new ComboBox { ItemsSource = new[] { "单行", "双栏（两列）", "双行（两行）" },
            SelectedIndex = settings.MonitorLayout switch {"SingleLine"=>0,"TwoRows"=>2,_=>1}, MinWidth = 160 };
        layout.SelectionChanged += (_, _) => {settings.MonitorLayout=layout.SelectedIndex switch{0=>"SingleLine",2=>"TwoRows",_=>"TwoColumns"};TaskbarChanged();};
        Row("显示布局", "双栏按两列排列；双行按两行排列，空间不足时文字省略。", layout, "\uE8FD");
        var size=new NumberBox {Minimum=8,Maximum=24,Value=settings.MonitorFontSize,SmallChange=1,LargeChange=2,
            SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Inline,Width=160,
            NumberFormatter=new Windows.Globalization.NumberFormatting.DecimalFormatter {FractionDigits=0,IntegerDigits=1}};
        fontSizeEditor=size;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(size,"整体文字大小");
        size.ValueChanged+=(_,args)=>{if(!updatingFontSize && double.IsFinite(args.NewValue))SetFontSize(args.NewValue);};
        size.AddHandler(UIElement.KeyDownEvent,new Microsoft.UI.Xaml.Input.KeyEventHandler((_,args)=>
        {
            if(args.Key==Windows.System.VirtualKey.Enter){CommitFontSize(true);args.Handled=true;}
            else if(args.Key==Windows.System.VirtualKey.Escape){SetFontSize(settings.MonitorFontSize);PageScroll.Focus(FocusState.Programmatic);args.Handled=true;}
        }),true);
        size.LostFocus+=(_,_)=>{if(!Within(FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject,size))CommitFontSize(false);};
        Row("整体文字大小","输入后按回车，或点击空白处保存；上下按钮可逐级调整。",size,"\uE8D2");
        fontFamilies??=Native.FontFamilies();
        var fonts=new ComboBox {ItemsSource=fontFamilies,SelectedItem=settings.MonitorFontFamily,IsEditable=true,Text=settings.MonitorFontFamily,Width=240,MaxDropDownHeight=320};
        void SetFont(){string name=fonts.Text.Trim();if(name.Length>0 && name!=settings.MonitorFontFamily){settings.MonitorFontFamily=name;TaskbarChanged();}}
        fonts.SelectionChanged+=(_,_)=>{if(fonts.SelectedItem is string name){settings.MonitorFontFamily=name;TaskbarChanged();}};
        fonts.LostFocus+=(_,_)=>SetFont();
        Row("监控字体", "从本机已安装字体中选择，也可输入字体名称。",fonts,"\uE8D2");
        Row("加粗文字", "统一加粗所有监控指标。",Toggle(settings.MonitorBold,value=>{settings.MonitorBold=value;TaskbarChanged();}),"\uE8DD");
        var width = new Slider { Minimum = 100, Maximum = 900, Value = settings.MonitorWidth, StepFrequency = 10, Width = 200,IsEnabled=!settings.MonitorAutoWidth };
        Row("自动适应内容宽度", "默认开启。各列按文字、字体与字号计算宽度，内容过长时保留任务栏空间。",
            Toggle(settings.MonitorAutoWidth,value=>{settings.MonitorAutoWidth=value;width.IsEnabled=!value;TaskbarChanged();}),"\uE7F4");
        width.ValueChanged += (_, args) => {settings.MonitorWidth=(int)args.NewValue;TaskbarChanged();};
        Row("手动区域宽度", "关闭自动宽度后使用，单位为逻辑像素。", width, "\uE7F4");
        Row("鼠标穿透", "默认开启。关闭后独立监控窗可拖动；内嵌区域可接收鼠标。",
            Toggle(settings.MonitorClickThrough, value => {settings.MonitorClickThrough=value;TaskbarChanged();}), "\uE962");
    }

    private static bool Within(DependencyObject? node,DependencyObject container)
    {
        while(node is not null){if(ReferenceEquals(node,container))return true;node=VisualTreeHelper.GetParent(node);}
        return false;
    }
    private void SetFontSize(double value)
    {
        if(fontSizeEditor is null || updatingFontSize)return;
        int normalized=(int)Math.Clamp(Math.Round(value),8,24);
        updatingFontSize=true;
        try
        {
            fontSizeEditor.Value=normalized;
            fontSizeEditor.Text=normalized.ToString(CultureInfo.CurrentCulture);
            if(settings.MonitorFontSize!=normalized){settings.MonitorFontSize=normalized;TaskbarChanged();}
        }
        finally{updatingFontSize=false;}
    }
    private void CommitFontSize(bool releaseFocus)
    {
        if(fontSizeEditor is null || updatingFontSize)return;
        var editor=fontSizeEditor;
        bool focused=Within(FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject,editor);
        double value=double.TryParse(editor.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out double entered) && double.IsFinite(entered)?entered:settings.MonitorFontSize;
        SetFontSize(value);
        if(releaseFocus && focused)PageScroll.Focus(FocusState.Programmatic);
    }
    private void OnPagePointerPressed(object sender,Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if(fontSizeEditor is not null && !Within(args.OriginalSource as DependencyObject,fontSizeEditor))CommitFontSize(true);
    }

    private void TaskbarChanged()
    {
        Save();UpdateMonitor();
        if(TaskbarController.Needed(settings) && !taskbar.Attached && !taskbar.IsConnecting)_=EnsureTaskbarAsync();
    }
    private async Task EnsureTaskbarAsync(bool force=false)
    {
        if(!nativeReady || taskbar.IsConnecting || quitting)return;
        if(!taskbar.Attached || force)await taskbar.ConnectAsync();
        UpdateMonitor();UpdateTaskbarInfo();
    }
    private void UpdateTaskbarInfo()
    {
        if(taskbarInfo is null)return;
        taskbar.Refresh();var state=taskbar.Status;
        taskbarInfo.Title=taskbar.IsConnecting?"正在连接 Explorer":state.Frames>0?"任务栏已连接":"任务栏适配状态";
        taskbarInfo.Message=taskbar.IsConnecting?"正在枚举本机 XAML 布局…":taskbar.Error ??
            (state.Connected==0?"尚未连接；启用任务栏功能时会自动连接。":
            $"已连接 {state.Frames} 个任务栏 · 内嵌监控 {state.Panels} 处 · 时间与日期 {(state.Times>0 && state.Dates>0?"可用":"待检查")} · 系统图标 {(state.SystemIcons>0?"可用":"待检查")}");
        if(state.LastError!=0)taskbarInfo.Message+=$"；适配错误 0x{state.LastError:X8}";
        if(state.AttachResult<0)taskbarInfo.Message+=$"；连接错误 0x{state.AttachResult:X8}，请手动重新检查。";
        taskbarInfo.Severity=taskbar.Error is not null || state.LastError!=0 || state.AttachResult<0?InfoBarSeverity.Warning:InfoBarSeverity.Informational;
    }

    private void SetConnectionState(BrokerPhase phase,string message)
    {
        connectionPhase=phase;connection=message;
        if(connectionTransitions.Count==0 || connectionTransitions[^1]!=phase)connectionTransitions.Add(phase);
        UpdateHardwareAction();
    }
    private bool ConnectingHardware=>reloading || connectionPhase is BrokerPhase.Authorizing or BrokerPhase.Connecting or BrokerPhase.Reading;
    private string HardwareStatusText()=>connectionPhase switch
    {
        BrokerPhase.Authorizing=>"等待硬件授权", BrokerPhase.Connecting=>"正在连接采集器",BrokerPhase.Reading=>"正在读取硬件数据",
        BrokerPhase.Cancelled=>"硬件授权已取消",BrokerPhase.Failed=>"采集连接失败",
        BrokerPhase.Connected when !snapshot.IsFresh=>"采集连接已中断",
        BrokerPhase.Connected when snapshot.Elevated=>"硬件访问已授权",
        BrokerPhase.Connected=>"基础监控已连接",_=>"等待采集"
    };
    private FrameworkElement HardwareActionControl()
    {
        var controls=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10,VerticalAlignment=VerticalAlignment.Center};
        hardwareProgress=new ProgressRing {Width=16,Height=16,Visibility=Visibility.Collapsed};controls.Children.Add(hardwareProgress);
        hardwareState=new TextBlock {Style=SecondaryStyle,VerticalAlignment=VerticalAlignment.Center};controls.Children.Add(hardwareState);
        hardwareAction=Button("授权硬件访问",()=>_=ConnectAsync((snapshot.IsFresh && snapshot.Sensors.Length>0) || settings.HardwareAccessEnabled),true);
        // An existing basic session upgrades; an authorized session reconnects
        // with its current privilege. Only live sample data confirms authorization.
        controls.Children.Add(hardwareAction);UpdateHardwareAction();return controls;
    }
    private void UpdateHardwareAction()
    {
        if(hardwareAction is null)return;
        bool live=connectionPhase==BrokerPhase.Connected && snapshot.IsFresh && snapshot.Sensors.Length>0;
        hardwareAction.Content=connectionPhase switch
        {
            BrokerPhase.Authorizing=>"等待授权…",BrokerPhase.Connecting=>"连接中…",BrokerPhase.Reading=>"读取中…",
            _ when live && snapshot.Elevated=>"重新连接",_ when live=>"授权硬件访问",_=>"重新连接"
        };
        hardwareAction.IsEnabled=!ConnectingHardware && !quitting;
        if(hardwareState is not null)hardwareState.Text=HardwareStatusText();
        if(hardwareProgress is not null){hardwareProgress.IsActive=ConnectingHardware;hardwareProgress.Visibility=ConnectingHardware?Visibility.Visible:Visibility.Collapsed;}
    }

    private object ConnectionUiSnapshot()=>new
    {
        capturedAt=DateTimeOffset.Now,phase=connectionPhase.ToString(),status=HardwareStatusText(),
        button=hardwareAction?.Content?.ToString(),enabled=hardwareAction?.IsEnabled,busy=ConnectingHardware,
        elevated=snapshot.Elevated,fresh=snapshot.IsFresh,sensors=snapshot.Sensors.Length,
        transitions=connectionTransitions.Select(phase=>phase.ToString()),fallback=accessFallbackNotice,
        category=category,hardware=(deviceFilter?.SelectedItem as DeviceOption)?.Name
    };
    private void CheckSensorControls()
    {
        if(sensorTitleRow is null || sensorFilters is null || sensorFilterSummary is null || hardwareAction is null || deviceFilter is null)
            throw new IOException("资源监控标题或筛选控件缺失。");
        var action=hardwareAction.TransformToVisual(sensorTitleRow).TransformPoint(new Windows.Foundation.Point());
        var device=deviceFilter.TransformToVisual(sensorFilters).TransformPoint(new Windows.Foundation.Point());
        var summary=sensorFilterSummary.TransformToVisual(sensorFilters).TransformPoint(new Windows.Foundation.Point());
        bool stacked=Grid.GetRow((FrameworkElement)VisualTreeHelper.GetParent(hardwareAction))>0;
        bool titleAction=(stacked || action.X>sensorTitleRow.ActualWidth*.5) && action.X+hardwareAction.ActualWidth<=sensorTitleRow.ActualWidth+1 && action.Y>=-1;
        bool summaryAfterDevice=(summary.Y>=device.Y+deviceFilter.ActualHeight-1 || summary.X>=device.X+deviceFilter.ActualWidth-1) && summary.X+sensorFilterSummary.ActualWidth<=sensorFilters.ActualWidth+1;
        bool distinctFilterNames=sensorFilters.Children.OfType<ComboBox>().First().SelectedItem?.ToString()=="全部类别" &&
            (deviceFilter.SelectedItem as DeviceOption)?.Name=="全部硬件";
        bool actualConnection=hardwareAction.Content?.ToString()==(snapshot.Elevated?"重新连接":"授权硬件访问") && hardwareAction.IsEnabled && !ConnectingHardware;
        bool progressObserved=connectionTransitions.Contains(BrokerPhase.Connecting) && connectionTransitions.Contains(BrokerPhase.Reading) && connectionTransitions.Contains(BrokerPhase.Connected);
        sensorControlsVerification=new {passed=titleAction && summaryAfterDevice && distinctFilterNames && actualConnection && progressObserved,
            titleAction,summaryAfterDevice,distinctFilterNames,actualConnection,progressObserved,actionX=action.X,deviceX=device.X,summaryX=summary.X,
            state=ConnectionUiSnapshot()};
        File.WriteAllText(Path.Combine(AppPaths.Logs,"claribar-sensor-controls-check.json"),JsonSerializer.Serialize(sensorControlsVerification,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
        if(!titleAction || !summaryAfterDevice || !distinctFilterNames || !actualConnection || !progressObserved)
            throw new IOException("资源监控按钮状态或筛选布局检查失败。");
    }

    private void BuildSensors()
    {
        accessInfo = Notice("实时传感器", "连接后显示实际读数，不可用项目显示 —。");
        networkInfo=Notice("默认物理出口","正在检查默认路由…");
        var filters=new Grid {ColumnSpacing=8};sensorFilters=filters;
        filters.ColumnDefinitions.Add(new(){Width=new GridLength(140)});
        filters.ColumnDefinitions.Add(new(){Width=new GridLength(140)});
        filters.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=110});
        filters.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var categories = new ComboBox { ItemsSource = new[] { "全部类别", "CPU", "GPU", "主板与风扇", "磁盘", "网络", "内存" }, SelectedItem = category, HorizontalAlignment = HorizontalAlignment.Stretch };
        categories.SelectionChanged += (_, _) => { category = (string)categories.SelectedItem; deviceId = ""; PopulateDevices(); RefreshSensorRows(); };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(categories,"设备类别");
        ToolTipService.SetToolTip(categories,"按设备类别筛选");filters.Children.Add(categories);
        var kinds = new ComboBox { ItemsSource = new[] { "常用指标", "全部指标", "已固定", "仅温度", "仅风扇", "仅占用", "仅速率" }, SelectedItem = sensorKind, HorizontalAlignment = HorizontalAlignment.Stretch };
        kinds.SelectionChanged += (_, _) => { sensorKind = (string)kinds.SelectedItem;PopulateDevices();RefreshSensorRows(); };
        Grid.SetColumn(kinds, 1); filters.Children.Add(kinds);
        deviceFilter = new ComboBox { PlaceholderText = "全部硬件", DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
        deviceFilter.SelectionChanged += (_, _) =>
        { if (!synchronizingSelection) { deviceId = (deviceFilter.SelectedItem as DeviceOption)?.Id ?? ""; RefreshSensorRows(); } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(deviceFilter,"具体硬件");
        ToolTipService.SetToolTip(deviceFilter,"按具体硬件筛选");Grid.SetColumn(deviceFilter,2);filters.Children.Add(deviceFilter);
        sensorFilterSummary=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};
        countText=Description("");countText.VerticalAlignment=VerticalAlignment.Center;sensorFilterSummary.Children.Add(countText);
        unavailableFilter=new CheckBox {Content="显示无读数通道",IsChecked=showUnavailable,VerticalAlignment=VerticalAlignment.Center};
        unavailableFilter.Checked+=(_,_)=>{showUnavailable=true;PopulateDevices();RefreshSensorRows();};
        unavailableFilter.Unchecked+=(_,_)=>{showUnavailable=false;PopulateDevices();RefreshSensorRows();};sensorFilterSummary.Children.Add(unavailableFilter);
        Grid.SetColumn(sensorFilterSummary,3);filters.Children.Add(sensorFilterSummary);PageContent.Children.Add(filters);
        var search = new TextBox { PlaceholderText = "筛选名称、设备或传感器标识", Text = query };
        search.TextChanged += (_, _) => { query = search.Text.Trim(); RefreshSensorRows(); };
        PageContent.Children.Add(search);
        PageContent.Children.Add(Description("常用指标只保留整机占用、CPU/GPU 温度、各磁盘活动与读写、物理出口上下行。勾选可固定到任务栏（最多 8 项）。"));
        var header = new Grid { ColumnSpacing = 12, Padding = new Thickness(46, 6, 18, 4) };
        PageContent.Children.Add(header);
        sensorList = new ListView { ItemsSource = sensorRows, SelectionMode = ListViewSelectionMode.Multiple,
            IsMultiSelectCheckBoxEnabled = true, MaxHeight = 460, MinHeight = 180, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        sensorList.ItemTemplate = (DataTemplate)XamlReader.Load("""
          <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
           <Grid ColumnSpacing="12" Padding="0,9" HorizontalAlignment="Stretch">
            <Grid.ColumnDefinitions><ColumnDefinition Width="2*"/><ColumnDefinition Width="1.8*"/><ColumnDefinition Width="*"/><ColumnDefinition Width="1.2*"/><ColumnDefinition Width="0.9*"/></Grid.ColumnDefinitions>
            <TextBlock Text="{Binding Name}" TextTrimming="CharacterEllipsis" ToolTipService.ToolTip="{Binding Details}" VerticalAlignment="Center"/>
            <TextBlock Grid.Column="1" Text="{Binding Device}" FontSize="12" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
            <TextBlock Grid.Column="2" Text="{Binding Kind}" FontSize="12" VerticalAlignment="Center"/>
            <TextBlock Grid.Column="3" Text="{Binding Value}" FontWeight="SemiBold" VerticalAlignment="Center"/>
            <TextBlock Grid.Column="4" Text="{Binding Status}" FontSize="12" VerticalAlignment="Center"/>
           </Grid>
          </DataTemplate>
          """);
        ResponsiveSensors(filters,categories,kinds,deviceFilter!,sensorFilterSummary!,header,sensorList,sensorList.ItemTemplate);
        sensorList.SelectionChanged += (_, args) =>
        {
            if (synchronizingSelection) return;
            var selected = settings.PinnedSensors.ToHashSet();
            foreach (var row in args.RemovedItems.OfType<SensorRow>()) selected.Remove(row.Id);
            foreach (var row in args.AddedItems.OfType<SensorRow>()) selected.Add(row.Id);
            if (selected.Count > 8)
            {
                SynchronizePinned();
                _ = MessageAsync("最多固定 8 个指标", "请先取消一个指标，再选择新的指标。"); return;
            }
            settings.PinnedSensors = selected.ToArray(); Save(); UpdateMonitor();
        };
        PageContent.Children.Add(Card(sensorList));
        emptySensors = Description("暂无匹配的传感器。温度和主板风扇可尝试授权硬件访问；也可调整设备与指标筛选。");
        PageContent.Children.Add(emptySensors);
        var names = new StackPanel { Spacing = 10 };
        names.Children.Add(Description("传感器名称可以自定义；不会改变原始设备标识，也不会设置风扇转速。"));
        var channel = new ComboBox { PlaceholderText = "选择已枚举的传感器", DisplayMemberPath = "DisplayName", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = CustomizableSensors() };
        renameSelector = channel;
        var rename = new TextBox { PlaceholderText = "输入显示名称，例如 CPU 风扇（留空还原）" };
        channel.SelectionChanged += (_, _) => { if (channel.SelectedItem is SensorReading reading) rename.Text = settings.SensorNames.GetValueOrDefault(reading.Id, ""); };
        names.Children.Add(channel); names.Children.Add(rename);
        names.Children.Add(Button("保存名称", () =>
        {
            if (channel.SelectedItem is not SensorReading reading) return;
            string value = rename.Text.Trim();
            if (value.Length == 0) settings.SensorNames.Remove(reading.Id); else settings.SensorNames[reading.Id] = value[..Math.Min(value.Length, 48)];
            Save(); RefreshSensorRows(); UpdateMonitor();
        }));
        PageContent.Children.Add(new Expander { Header = "自定义指标名称", Content = names, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        PopulateDevices();
    }

    private sealed record DeviceOption(string Id, string Name);
    private void PopulateDevices()
    {
        if (deviceFilter is null) return;
        var devices = SensorCatalog.Filter(snapshot,sensorKind,settings.PinnedSensors,showUnavailable).Where(s => category == "全部类别" || s.Category == category)
            .GroupBy(s => s.HardwareId).Select(g => new DeviceOption(g.Key, g.First().Hardware)).OrderBy(d => d.Name).ToList();
        devices.Insert(0,new("","全部硬件"));
        if ((deviceFilter.ItemsSource as List<DeviceOption>)?.SequenceEqual(devices) == true) return;
        synchronizingSelection = true;
        try
        {
            deviceFilter.ItemsSource = devices;
            var selected=devices.FirstOrDefault(d=>d.Id==deviceId)??devices[0];deviceId=selected.Id;deviceFilter.SelectedItem=selected;
        }
        finally { synchronizingSelection = false; }
    }

    private void RefreshSensorRows()
    {
        if (sensorList is null) return;
        var desired = SensorCatalog.Filter(snapshot,sensorKind,settings.PinnedSensors,showUnavailable).Where(s=>
            (category=="全部类别" || s.Category==category) && (deviceId.Length==0 || s.HardwareId==deviceId) &&
            (query.Length==0 || $"{s.Name} {s.DisplayName} {s.Hardware} {s.Id} {settings.SensorNames.GetValueOrDefault(s.Id)}".Contains(query,StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s=>s.Category switch {"CPU"=>0,"GPU"=>1,"内存"=>2,"磁盘"=>3,"网络"=>4,_=>5})
            .ThenBy(s=>s.Hardware).ThenBy(s=>s.Kind).ThenBy(s=>s.Id).ToArray();
        synchronizingSelection = true;
        try
        {
            if (!sensorRows.Select(r => r.Id).SequenceEqual(desired.Select(r => r.Id)))
            {
                sensorRows.Clear(); rowIndex.Clear();
                foreach (var sensor in desired)
                {
                    var row = new SensorRow(sensor, settings.SensorNames.GetValueOrDefault(sensor.Id, ""), snapshot.IsFresh);
                    sensorRows.Add(row); rowIndex[sensor.Id] = row;
                }
            }
            else foreach (var sensor in desired) rowIndex[sensor.Id].Update(sensor, settings.SensorNames.GetValueOrDefault(sensor.Id, ""), snapshot.IsFresh);
        }
        finally { synchronizingSelection = false; }
        SynchronizePinned();
        if (countText is not null) countText.Text = $"{desired.Length} / {snapshot.Sensors.Length} 项 · 已固定 {settings.PinnedSensors.Length} 项";
        if (emptySensors is not null) emptySensors.Visibility = desired.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SynchronizePinned()
    {
        if (sensorList is null) return;
        synchronizingSelection = true;
        try
        {
            foreach (var row in sensorRows)
            {
                bool wanted = settings.PinnedSensors.Contains(row.Id), selected = sensorList.SelectedItems.Contains(row);
                if (wanted && !selected) sensorList.SelectedItems.Add(row);
                if (!wanted && selected) sensorList.SelectedItems.Remove(row);
            }
        }
        finally { synchronizingSelection = false; }
    }

    private void AddTaskbarPreview()
    {
        var content=new StackPanel {Spacing=12};
        var header=new Grid {ColumnSpacing=12};header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        header.Children.Add(new TextBlock {Text="任务栏预览",FontSize=16,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8};
        var desktop=new ComboBox {ItemsSource=new[]{"深色桌面","浅色桌面"},SelectedIndex=0,MinWidth=130};
        actions.Children.Add(desktop);actions.Children.Add(Button("放大预览",()=>_=ShowTaskbarPreviewAsync()));Grid.SetColumn(actions,1);header.Children.Add(actions);
        taskbarPreview=new TaskbarPreview();desktop.SelectionChanged+=(_,_)=>taskbarPreview?.SetDesktop(desktop.SelectedIndex==0);
        ResponsiveHeader(header,actions,560);
        content.Children.Add(header);content.Children.Add(taskbarPreview);content.Children.Add(Description("监控使用实时数据；预览同步当前布局、字体和配色，系统图标为示意。"));
        PageContent.Children.Add(Card(content));
    }
    private async Task ShowTaskbarPreviewAsync()
    {
        var preview=new TaskbarPreview {MinWidth=Math.Min(720,Math.Max(240,Root.ActualWidth-80)),MinHeight=230};expandedPreview=preview;
        var content=new StackPanel {Spacing=12};
        var desktop=new ComboBox {ItemsSource=new[]{"深色桌面","浅色桌面"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Left};
        desktop.SelectionChanged+=(_,_)=>preview.SetDesktop(desktop.SelectedIndex==0);content.Children.Add(desktop);content.Children.Add(preview);
        var dialog=new ContentDialog {Title="任务栏实时预览",Content=content,CloseButtonText="关闭",XamlRoot=Root.XamlRoot};
        dialog.Resources["ContentDialogMaxWidth"]=1000d;
        UpdateMonitor();
        try
        {
            var shown=dialog.ShowAsync();
            if(smokeTest)
            {
                await Task.Delay(600);
                if(preview.MetricCount==0 || preview.MetricTexts.Any(text=>text.IsTextTrimmed || text.FontFamily.Source!=settings.MonitorFontFamily || text.FontWeight.Weight!=(settings.MonitorBold?700:400)))throw new IOException("放大预览中的指标为空、被截断或字体样式不匹配。");
                await RenderCheckImageAsync("taskbar-preview-large",preview);
                preview.SetDesktop(false);await Task.Delay(150);await RenderCheckImageAsync("taskbar-preview-light",preview);dialog.Hide();
            }
            await shown;
        }
        finally{dialog.Hide();expandedPreview=null;}
    }
    private void AddColorSchemes()
    {
        Heading("快速配色");
        var scope=new ComboBox {ItemsSource=new[]{"监控指标","时间与系统图标","全部文字"},SelectedIndex=paletteScope==ColorScope.All?2:paletteScope==ColorScope.System?1:0,MinWidth=180};
        scope.SelectionChanged+=(_,_)=>paletteScope=scope.SelectedIndex==2?ColorScope.All:scope.SelectedIndex==1?ColorScope.System:ColorScope.Monitor;
        Row("配色范围","系统配色会同时开启对应的固定颜色开关。",scope,"\uE790");
        Row("一键随机颜色","为所选范围生成一组不同的颜色。",Button("随机颜色",()=>ApplyColorScheme(ColorSchemes.Random()),true),"\uE8B1");
        var schemes=new Grid {ColumnSpacing=10};
        for(int index=0;index<ColorSchemes.Presets.Count;index++)
        {
            schemes.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var scheme=ColorSchemes.Presets[index];var content=new StackPanel {Spacing=10};
            var swatches=new StackPanel {Orientation=Orientation.Horizontal,Spacing=4};
            foreach(string color in scheme.Colors.Take(4))swatches.Children.Add(new Border {Width=18,Height=18,CornerRadius=new CornerRadius(5),Background=new SolidColorBrush(ParseColor(color))});
            content.Children.Add(swatches);content.Children.Add(new TextBlock {Text=scheme.Name});
            var button=new Button {Content=content,Padding=new Thickness(14,12,14,12),HorizontalAlignment=HorizontalAlignment.Stretch};
            button.Click+=(_,_)=>ApplyColorScheme(scheme);Grid.SetColumn(button,index);schemes.Children.Add(button);
        }
        ResponsiveTiles(schemes,4,660);PageContent.Children.Add(schemes);
    }
    private void ApplyColorScheme(ColorScheme scheme)
    {
        IEnumerable<string> ids=settings.PinnedSensors.Length>0?settings.PinnedSensors:MonitorSensors().Select(sensor=>sensor.Id);
        ColorSchemes.Apply(settings,ids,scheme,paletteScope);TaskbarChanged();RenderPage();
    }

    private void BuildColors()
    {
        AddTaskbarPreview();
        AddColorSchemes();
        Heading("监控文字");
        AddColorPicker("默认文字颜色", settings.MonitorColor, hex => settings.MonitorColor = hex);
        var selection = new ComboBox { DisplayMemberPath="DisplayName", PlaceholderText="选择指标", HorizontalAlignment=HorizontalAlignment.Stretch,
            ItemsSource=CustomizableSensors() };
        metricColorSelector=selection;
        var picker=new ColorPicker { IsAlphaEnabled=false,Color=ParseColor(settings.MonitorColor) };
        bool updating=false;
        selection.SelectionChanged+=(_,_)=>
        {
            if(selection.SelectedItem is SensorReading reading){updating=true;picker.Color=ParseColor(settings.SensorColors.GetValueOrDefault(reading.Id,settings.MonitorColor));updating=false;}
        };
        picker.ColorChanged+=(_,args)=>
        {
            if(updating || selection.SelectedItem is not SensorReading reading)return;
            settings.SensorColors[reading.Id]=$"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";TaskbarChanged();
        };
        var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8};
        actions.Children.Add(new Button {Content="选择颜色",Flyout=new Flyout{Content=picker}});
        actions.Children.Add(Button("跟随默认",()=>{if(selection.SelectedItem is SensorReading reading){settings.SensorColors.Remove(reading.Id);TaskbarChanged();updating=true;picker.Color=ParseColor(settings.MonitorColor);updating=false;}}));
        var perMetric=new StackPanel {Spacing=10};perMetric.Children.Add(selection);perMetric.Children.Add(actions);
        Row("每个指标的文字颜色", "颜色按传感器标识保存，同一指标在内嵌区域与独立窗中使用相同颜色。", new TextBlock(), "\uE790");
        PageContent.Children.Add(Card(perMetric));
        AddMonitorLayoutControls();
        Heading("独立监控窗背景");
        AddColorPicker("独立窗背景颜色", settings.MonitorBackground, hex => settings.MonitorBackground = hex);
        var opacity = new Slider { Minimum = 30, Maximum = 100, Value = settings.MonitorOpacity, StepFrequency = 1, Width = 200 };
        opacity.ValueChanged += (_, args) => { settings.MonitorOpacity = (int)args.NewValue; Save(); UpdateMonitor(); };
        Row("独立窗不透明度", "仅用于独立监控窗，任务栏背景请在任务栏外观页面设置。", opacity, "\uE790");
        Heading("系统任务栏文字与图标");
        taskbarInfo=Notice("系统前景色", "支持时间、日期和系统单色图标；第三方程序的彩色托盘图标保留原样。");
        SystemColorRow("时间","\uE917",settings.TimeColor,settings.TimeColorEnabled,v=>settings.TimeColorEnabled=v,v=>settings.TimeColor=v);
        SystemColorRow("日期","\uE787",settings.DateColor,settings.DateColorEnabled,v=>settings.DateColorEnabled=v,v=>settings.DateColor=v);
        SystemColorRow("系统托盘字形","\uE971",settings.TrayColor,settings.TrayColorEnabled,v=>settings.TrayColorEnabled=v,v=>settings.TrayColor=v);
        SystemColorRow("网络图标","\uE839",settings.NetworkColor,settings.NetworkColorEnabled,v=>settings.NetworkColorEnabled=v,v=>settings.NetworkColor=v);
        SystemColorRow("声音图标","\uE767",settings.VolumeColor,settings.VolumeColorEnabled,v=>settings.VolumeColorEnabled=v,v=>settings.VolumeColor=v);
        SystemColorRow("输入法与语言指示","\uE765",settings.InputColor,settings.InputColorEnabled,v=>settings.InputColorEnabled=v,v=>settings.InputColor=v);
        SystemColorRow("电池图标","\uE83F",settings.BatteryColor,settings.BatteryColorEnabled,v=>settings.BatteryColorEnabled=v,v=>settings.BatteryColor=v);
    }
    private void SystemColorRow(string title,string glyph,string color,bool enabled,Action<bool> toggle,Action<string> change)
    {
        var picker=new ColorPicker { Color=ParseColor(color),IsAlphaEnabled=false };
        var label=new TextBlock {Text=color};
        picker.ColorChanged+=(_,args)=>{string hex=$"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";label.Text=hex;change(hex);TaskbarChanged();};
        var controls=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};
        controls.Children.Add(new Button {Content=label,Flyout=new Flyout {Content=picker},VerticalAlignment=VerticalAlignment.Center});
        controls.Children.Add(Toggle(enabled,value=>{toggle(value);TaskbarChanged();}));
        Row(title,"开启后使用固定颜色，关闭后跟随系统。单项颜色优先于通用颜色。",controls,glyph);
    }
    private void AddColorPicker(string title, string value, Action<string> changed)
    {
        var picker = new ColorPicker { Color = ParseColor(value), IsAlphaEnabled = false, IsColorSliderVisible = true,
            IsColorChannelTextInputVisible = true, IsHexInputVisible = true, MinWidth = 300 };
        var hexText = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center };
        picker.ColorChanged += (_, args) =>
        {
            string hex = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
            changed(hex); hexText.Text = hex; TaskbarChanged();
        };
        var button = new Button { Content = hexText, Flyout = new Flyout { Content = picker } };
        Row(title, "点击打开颜色选择器，实时预览。", button, "\uE790");
    }
    private static Color ParseColor(string hex) => Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
    private void BuildGeneral()
    {
        var theme = new ComboBox { ItemsSource = new[] { "跟随系统", "浅色", "深色" }, SelectedIndex = settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 }, MinWidth = 150 };
        theme.SelectionChanged += (_, _) => { settings.Theme = theme.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" }; ApplyTheme(); Save(); };
        Row("应用主题", "使用 Windows 原生控件，支持系统浅色和深色。", theme, "\uE790");
        var interval = new ComboBox { ItemsSource = new[] { "0.5 秒", "1 秒", "2 秒", "5 秒" }, SelectedIndex = settings.IntervalMs switch { 500 => 0, 2000 => 2, 5000 => 3, _ => 1 }, MinWidth = 150 };
        interval.SelectionChanged += (_, _) => { settings.IntervalMs = new[] { 500, 1000, 2000, 5000 }[interval.SelectedIndex]; if (broker is not null) broker.IntervalMs = settings.IntervalMs; Save(); };
        Row("启动时请求硬件访问","仅独立采集器需要管理员权限，Windows 会显示授权提示；关闭后使用基础采集。",
            Toggle(settings.HardwareAccessEnabled,value=>{settings.HardwareAccessEnabled=value;Save();_=ConnectAsync(value);}),"\uEA18");
        Row("采样间隔", "采集在独立进程执行；温度、转速等随硬件更新。", interval, "\uE916");
        Row("关闭窗口时保留监控", "点击关闭后隐藏到系统托盘；可从托盘菜单彻底退出。",
            Toggle(settings.CloseToTray, value => { settings.CloseToTray = value; Save(); }), "\uE73F");
        Heading("文件与数据");
        Row("配置与运行数据", AppPaths.Data, Button("打开目录", () => OpenFolder(AppPaths.Data)), "\uE8B7");
        Row("诊断记录", "导出当前采集状态与传感器快照，保存在 E 盘日志目录。", Button("导出诊断", ExportDiagnostics), "\uE9D9");
        Row("彻底退出", "关闭监控窗、撤销任务栏试验并断开采集进程。", Button("退出澄栏", () => _ = QuitAsync()), "\uE8BB");
    }
    private void BuildAbout()
    {
        BuildProjectAbout();Heading("技术与开源依赖");
        Row("技术组成", "WinUI + .NET 设置界面，C++ 原生托盘与监控窗，独立 LibreHardwareMonitor 采集进程。", new TextBlock(), "\uE943");
        Row("硬件数据", "温度和风扇可能需要管理员权限。核显没有独立风扇时不显示 GPU 风扇；不以 CPU 温度代替 GPU 温度。", new TextBlock(), "\uE9D9");
        Row("风扇通道", "0 RPM 可能表示停转或未连接；分线器可能只回传一个转速。当前只读，不控制风扇转速。", new TextBlock(), "\uE9CE");
        Row("开源依赖", "LibreHardwareMonitor：MPL-2.0；Windows App SDK、.NET 等依赖许可随构建输出保留。", Button("查看软件目录",()=>OpenFolder(AppContext.BaseDirectory)), "\uE8A5");
    }

    private void ApplyTheme() => Root.RequestedTheme = settings.Theme switch
    { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
    private void Save()
    {
        try { store.Save(settings); }
        catch (Exception ex) { connection = $"配置未保存：{ex.Message}"; if (statusText is not null) statusText.Text = connection; }
    }
    private SensorReading? Find(Func<SensorReading, bool> predicate) => snapshot.Sensors.FirstOrDefault(s => s.IsValid && predicate(s));
    private SensorReading[] CustomizableSensors()=>SensorCatalog.Common(snapshot).Concat(snapshot.Sensors.Where(sensor=>settings.PinnedSensors.Contains(sensor.Id)))
        .DistinctBy(sensor=>sensor.Id).OrderBy(sensor=>settings.PinnedSensors.Contains(sensor.Id)?0:1).ThenBy(sensor=>sensor.Hardware).ThenBy(sensor=>sensor.DisplayName).ToArray();
    private void UpdateLive()
    {
        if (quitting) return;
        bool fresh=snapshot.IsFresh;
        UpdateHardwareAction();
        if (statusText is not null)
            statusText.Text = $"{connection}\n{(snapshot.Sensors.Length > 0 ? $"{snapshot.Sensors.Length} 个传感器条目 · 最近采样 {snapshot.CapturedAt.ToLocalTime():HH:mm:ss}" : "尚未收到硬件数据")}\n{(fresh ? "" : "读数未就绪或已过期。")}";
        if (cpuValue is not null) cpuValue.Text = SensorFormatting.Value(Find(s => s.HardwareType == "Cpu" && s.Kind == "Load" && s.Name.Contains("Total")), fresh);
        if (temperatureValue is not null) temperatureValue.Text = SensorFormatting.Value(Find(s => s.HardwareType == "Cpu" && s.Kind == "Temperature" && s.Name == "CPU Package"), fresh);
        if (gpuValue is not null) gpuValue.Text = SensorFormatting.Value(Find(s => s.Category == "GPU" && s.Kind == "Load" && s.Name.Contains("3D")), fresh);
        if (fanValue is not null)
        {
            var fans = snapshot.Sensors.Where(s => s.Kind == "Fan" && s.IsValid).ToArray();
            fanValue.Text = fresh && fans.Length > 0 ? $"{fans.Count(s => s.Value > 0)} / {fans.Length}" : "—";
        }
        if (accessInfo is not null)
        {
            accessInfo.Title=HardwareStatusText();
            accessInfo.Message = !fresh || snapshot.Sensors.Length == 0 ? connection
                : snapshot.Errors.Length > 0 ? string.Join("；", snapshot.Errors.Take(2))
                : snapshot.Elevated ? "CPU 温度、主板风扇等按实际支持情况显示；不可用指标保留 —。"
                : "CPU、GPU、内存和物理出口网速已可用；授权后可读取 CPU 温度、核心频率和磁盘详细数据。";
            if(accessFallbackNotice is not null)accessInfo.Message=accessFallbackNotice+"\n"+accessInfo.Message;
            accessInfo.Severity=snapshot.Errors.Length>0 || accessFallbackNotice is not null || connectionPhase is BrokerPhase.Failed or BrokerPhase.Cancelled?InfoBarSeverity.Warning:
                connectionPhase==BrokerPhase.Connected && fresh && snapshot.Elevated?InfoBarSeverity.Success:InfoBarSeverity.Informational;
        }
        if(accessInfo is not null && fresh)
        {
            var issues=snapshot.Capabilities.Where(capability=>capability.Status!="valid" && capability.Label.EndsWith("温度")).ToArray();
            if(issues.Length>0)accessInfo.Message+="\n"+string.Join("；",issues.Select(capability=>$"{capability.Label}：{capability.Explanation}"));
        }
        if(networkInfo is not null)
        {
            var route=snapshot.Network;
            networkInfo.Title=route is {InterfaceIndex:>0}?$"物理出口：{route.Name}":"默认物理出口";
            networkInfo.Message=route is null?"尚未收到物理出口数据。":route.InterfaceIndex==0?route.Message:
                $"{route.Description} · 接口 {route.InterfaceIndex} · 已排除 {route.ExcludedAdapters} 个虚拟或非物理接口。上下行统计物理出口的全部流量。";
            networkInfo.Severity=route is {InterfaceIndex:>0}?InfoBarSeverity.Informational:InfoBarSeverity.Warning;
        }
        if (page == "sensors")
        {
            PopulateDevices(); RefreshSensorRows();
            if (renameSelector is not null && renameSelector.Items.Count != CustomizableSensors().Length)
                renameSelector.ItemsSource = CustomizableSensors();
        }
        if(page=="colors" && metricColorSelector is not null && metricColorSelector.Items.Count!=CustomizableSensors().Length)metricColorSelector.ItemsSource=CustomizableSensors();
        UpdateMonitor();
        UpdateTaskbarInfo();
    }
    private SensorReading[] MonitorSensors()
    {
        if(settings.PinnedSensors.Length>0)return settings.PinnedSensors.Select(id=>snapshot.Sensors.FirstOrDefault(sensor=>sensor.Id==id))
            .Where(sensor=>sensor is not null).Cast<SensorReading>().ToArray();
        return SensorCatalog.Defaults(snapshot);
    }
    private TaskbarConfig MonitorConfig()=>TaskbarController.CreateConfig(settings,MonitorSensors().Select(sensor=>(sensor,
        $"{settings.SensorNames.GetValueOrDefault(sensor.Id,SensorLabels.Compact(sensor))} {SensorFormatting.Value(sensor,snapshot.IsFresh)}")));
    private void UpdateMonitor()
    {
        var config=MonitorConfig();
        taskbarPreview?.Update(settings,config);expandedPreview?.Update(settings,config);
        if(!nativeReady)return;
        Native.CB_XamlConfigure(ref config);
        string text=config.MetricCount==0?"澄栏 · 等待监控数据":string.Join("    ",config.Metrics.Take((int)config.MetricCount).Select(metric=>metric.Text));
        Native.CB_SetMonitor(text,Native.ColorRef(settings.MonitorColor),Native.ColorRef(settings.MonitorBackground),settings.MonitorOpacity,settings.MiniMonitorEnabled?1:0);
    }
    private void ExportDiagnostics()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            var path = Path.Combine(AppPaths.Logs, $"claribar-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            taskbar.Refresh();
            File.WriteAllText(Path.ChangeExtension(path,"tree.txt"),Native.ReadTaskbarTree());
            File.WriteAllText(path, JsonSerializer.Serialize(new
            { version = "0.6.1", system = Environment.OSVersion.VersionString, connection, snapshot, taskbar=taskbar.Status, pinnedSensors = settings.PinnedSensors },
                new JsonSerializerOptions(Wire.Json) { WriteIndented = true }));
            _ = MessageAsync("诊断已导出", path);
        }
        catch (Exception ex) { _ = MessageAsync("无法导出诊断", ex.Message); }
    }
    private static void OpenFolder(string path)
    { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
    private async Task MessageAsync(string title, string message)
    {
        var dialog = new ContentDialog { Title = title, Content = message, CloseButtonText = "确定", XamlRoot = Root.XamlRoot };
        try { await dialog.ShowAsync(); } catch (InvalidOperationException) { }
    }
    private void ShowSettings() { Native.ShowWindow(hwnd, 9); Activate(); Native.SetForegroundWindow(hwnd); }
    private async Task QuitAsync()
    {
        if (quitting) return;
        CommitFontSize(false);
        quitting = true; freshnessTimer.Stop();
        if (nativeReady) { Native.CB_Stop(); nativeReady = false; }
        if (broker is not null) await broker.DisposeAsync();
        Close();
    }

    private async Task CheckFontSizeEditingAsync()
    {
        int original=settings.MonitorFontSize;
        var results=new List<object>();
        foreach(int index in new[]{1,3})
        {
            Nav.SelectedItem=Nav.MenuItems[index];await Task.Delay(250);
            var editor=fontSizeEditor??throw new IOException("字号编辑控件缺失。");
            PageScroll.ChangeView(null,editor.TransformToVisual(PageContent).TransformPoint(new Windows.Foundation.Point()).Y-100,null,true);
            await Task.Delay(200);
            if(!editor.Focus(FocusState.Programmatic))throw new IOException("字号输入框无法获得焦点。");
            editor.Text="13";CommitFontSize(true);await Task.Delay(100);
            bool enter=settings.MonitorFontSize==13 && store.Load().MonitorFontSize==13 && !Within(FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject,editor);
            editor.Focus(FocusState.Programmatic);editor.Text="14";
            PageScroll.Focus(FocusState.Programmatic);await Task.Delay(100);
            bool blur=settings.MonitorFontSize==14 && store.Load().MonitorFontSize==14;
            editor.Focus(FocusState.Programmatic);editor.Text="invalid";CommitFontSize(true);await Task.Delay(80);
            bool invalid=settings.MonitorFontSize==14 && editor.Value==14;
            editor.Focus(FocusState.Programmatic);editor.Text="99";CommitFontSize(true);await Task.Delay(80);
            bool range=settings.MonitorFontSize==24 && editor.Value==24;
            bool inline=editor.SpinButtonPlacementMode==NumberBoxSpinButtonPlacementMode.Inline &&
                !VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Any(popup=>popup.IsOpen);
            SetFontSize(original);PageScroll.Focus(FocusState.Programmatic);
            results.Add(new{page=index==1?"taskbar":"colors",enter,blur,invalid,range,inline});
            if(!enter || !blur || !invalid || !range || !inline)throw new IOException("字号确认/失焦或浮窗检查失败："+JsonSerializer.Serialize(results));
            if(index==3)await RenderCheckImageAsync("font-size-inline");
        }
        File.WriteAllText(Path.Combine(AppPaths.Logs,"claribar-font-size-check.json"),JsonSerializer.Serialize(new{passed=true,results},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
    }

    private async Task CaptureMonitorAlignmentAsync()
    {
        var preview=new TaskbarPreview {Width=1040,Height=180};
        PageContent.Children.Add(preview);
        var sample=new AppSettings {MonitorFontFamily="Microsoft YaHei UI",MonitorBold=true};
        var config=TaskbarConfig.Empty();config.Layout=2;config.MetricCount=6;config.BackgroundMode=1;
        string[] labels=["CPU 4.16 %","CPU 核心温度 38 °C","内存 23.86 %","上传 9 KiB/s","下载 97.5 KiB/s","磁盘占用 0.98 %"];
        for(int i=0;i<labels.Length;i++)config.Metrics[i]=new(){Text=labels[i],Color=0xff70d8ee};
        var checks=new List<object>();
        foreach(int size in new[]{8,15,24})
        {
            sample.MonitorFontSize=size;preview.Update(sample,config);await Task.Delay(250);
            await RenderCheckImageAsync($"monitor-alignment-{size}",preview);
            checks.Add(new{font=size,height=preview.TaskbarHeight,up=preview.UpShift,image=$"monitor-alignment-{size}.png"});
        }
        config.Metrics[3].Text="上传 gjpq Â 9 KiB/s";sample.MonitorFontSize=24;
        preview.Update(sample,config);await Task.Delay(250);await RenderCheckImageAsync("monitor-alignment-descenders",preview);
        checks.Add(new{font=24,height=preview.TaskbarHeight,up=preview.UpShift,image="monitor-alignment-descenders.png"});
        File.WriteAllText(Path.Combine(AppPaths.Logs,"claribar-preview-layout.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
        PageContent.Children.Remove(preview);
    }

    private async Task SmokeTestAsync()
    {
        var visited = new List<string>(); string? error = null;
        try
        {
            for (int i = 0; i < 45 && snapshot.Sensors.Length == 0; i++) await Task.Delay(1000);
            foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
            {
                Nav.SelectedItem = item; await Task.Delay(400);
                if (PageContent.Children.Count < 4) throw new InvalidOperationException("页面未创建：" + item.Tag);
                visited.Add((string)item.Tag);
                if((string)item.Tag=="sensors")CheckSensorControls();
                if ((string)item.Tag is "overview" or "sensors" or "colors") await RenderCheckImageAsync((string)item.Tag);
            }
            await CheckFontSizeEditingAsync();
            PageScroll.ChangeView(null,PageScroll.ScrollableHeight,null,true);await Task.Delay(250);
            await RenderCheckImageAsync("system-color-icons");
            await CaptureMonitorAlignmentAsync();
            await CheckShellAsync();
            await CheckProjectAboutAsync();
            settings.Theme = "Dark"; ApplyTheme(); Nav.SelectedItem = Nav.MenuItems[0]; await Task.Delay(500);
            await RenderCheckImageAsync("overview-dark");
            settings.Theme = "Light"; ApplyTheme(); await Task.Delay(300);
            if (snapshot.Sensors.Length == 0) throw new IOException("未收到实际传感器数据：" + connection);
            if (!nativeReady) throw new IOException("原生模块未就绪");
            if(fontFamilies is null || fontFamilies.Length==0)throw new IOException("未枚举到本机字体。");
            TaskbarStatus? checkedTaskbar=null;
            if(Environment.GetCommandLineArgs().Contains("--taskbar-check"))
            {
                settings.TaskbarMonitorEnabled=true;settings.TaskbarBackgroundMode="Transparent";
                settings.MonitorAutoWidth=true;settings.MonitorFontFamily="Microsoft YaHei UI";settings.MonitorBold=true;
                settings.TimeColorEnabled=settings.DateColorEnabled=settings.TrayColorEnabled=true;
                settings.TimeColor="#66E0FF";settings.DateColor="#FFC77A";settings.TrayColor="#A4E6C3";
                settings.PinnedSensors=SensorCatalog.Common(snapshot).Where(s=>s.IsValid).Take(4).Select(s=>s.Id).ToArray();
                var colors=new[]{"#70D8EE","#FFB86C","#A5E6A1","#E7B4FA"};
                for(int i=0;i<settings.PinnedSensors.Length;i++)settings.SensorColors[settings.PinnedSensors[i]]=colors[i];
                UpdateMonitor();await EnsureTaskbarAsync();await Task.Delay(1800);taskbar.Refresh();checkedTaskbar=taskbar.Status;
                if(checkedTaskbar.Frames==0 || checkedTaskbar.Panels==0 || checkedTaskbar.PatchedProperties==0 || checkedTaskbar.LastError!=0)
                    throw new IOException("实际任务栏应用检查失败："+JsonSerializer.Serialize(checkedTaskbar));
                File.WriteAllText(Path.Combine(AppPaths.Logs,"claribar-app-taskbar-check.json"),JsonSerializer.Serialize(checkedTaskbar,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
                Nav.SelectedItem=Nav.MenuItems[1];await Task.Delay(300);await RenderCheckImageAsync("taskbar-settings");
                if(taskbarPreview?.MetricCount!=settings.PinnedSensors.Length)throw new IOException("任务栏预览指标数量不匹配。");
                await ShowTaskbarPreviewAsync();
            }
            if (Environment.GetCommandLineArgs().Contains("--elevated") && !snapshot.Elevated) throw new IOException("管理员采集会话未建立");
            Native.CB_SetMonitor("澄栏 · 监控窗检查", Native.ColorRef("#FFFFFF"), Native.ColorRef("#202020"), 92, 1);
            await Task.Delay(300);
            Native.CB_SetMonitor("澄栏 · 监控窗检查", Native.ColorRef("#FFFFFF"), Native.ColorRef("#202020"), 92, 0);
        }
        catch (Exception ex) { error = ex.ToString(); }
        Directory.CreateDirectory(AppPaths.Logs);
        File.WriteAllText(Path.Combine(AppPaths.Logs, "claribar-ui-smoke.json"), JsonSerializer.Serialize(new
        {
            capturedAt = DateTimeOffset.Now, passed = error is null, error, visited,
            sensorCount = snapshot.Sensors.Length, nativeReady, brokerElevated = snapshot.Elevated,
            commonSensors=SensorCatalog.Common(snapshot),network=snapshot.Network,capabilities=snapshot.Capabilities,sensorControls=sensorControlsVerification,
            taskbarCheck=Environment.GetCommandLineArgs().Contains("--taskbar-check"),taskbar=taskbar.Status,
            installedFonts=fontFamilies?.Length,adaptiveWidth=settings.MonitorAutoWidth,fontFamily=settings.MonitorFontFamily,bold=settings.MonitorBold,
            sourceRoot = @"E:\haoyue\myproject\home\ClariBar", configPath = store.FilePath
        }, new JsonSerializerOptions(Wire.Json) { WriteIndented = true }));
        await QuitAsync();
    }

    private async Task RenderCheckImageAsync(string name,FrameworkElement? target=null)
    {
        // Render this application's own XAML tree; no desktop capture or external UI automation.
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(target??Root);
        var pixels = await bitmap.GetPixelsAsync();
        string directory = Path.Combine(AppPaths.Cache, "artifacts", "ClariBar", "screenshots");
        Directory.CreateDirectory(directory);
        var folder = await StorageFolder.GetFolderFromPathAsync(directory);
        var file = await folder.CreateFileAsync(name + ".png", CreationCollisionOption.ReplaceExisting);
        using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }
}
