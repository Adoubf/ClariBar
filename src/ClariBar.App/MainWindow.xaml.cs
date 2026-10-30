using L = ClariBar.Core.Localizer;
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
    private string page = "overview", query = "", category = L.T("All categories"), sensorKind = L.T("Common metrics"), deviceId = "";
    private string connection = L.T("Connecting to sensors…");
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
        if(smokeTest && AppLocalization.TestOverride is {} languageOverride)settings.Language=Localizer.NormalizePreference(languageOverride);
        if(NetworkMetrics.Migrate(settings) | DesktopShortcuts.Migrate(settings) | TranslationProtocol.MigrateLanguages(settings))Save();
        InitializeComponent();
        Root.AddHandler(UIElement.PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler(OnPagePointerPressed),true);
        Activated+=(_,args)=>{if(args.WindowActivationState==WindowActivationState.Deactivated)CommitFontSize(true);};
        Title = L.T("ClariBar");
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        desktopTools = new DesktopToolsController(this, settings, Save);
        AppWindow.Resize(new SizeInt32(1220, 860));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "ClariBar.ico"));
        InitializeShell();ApplyTheme();
        trayCallback = action => DispatcherQueue.TryEnqueue(() =>
        {
            if (action == 1) ShowSettings();
            else if (action == 2) _ = QuitAsync();
            else if (action == 3) { settings.MiniMonitorEnabled = !settings.MiniMonitorEnabled; Save(); UpdateMonitor(); if (page == "taskbar") RenderPage(); }
            else if(action==4){settings.TaskbarMonitorEnabled=!settings.TaskbarMonitorEnabled;TaskbarChanged();if(page=="taskbar")RenderPage();}
            else if(action is >=10 and <=22)_=ExecuteDesktopCommandAsync(action);
        });
        try { Native.CB_SetLanguage(Localizer.IsChinese?1:0);nativeReady = Native.CB_Start(trayCallback) == 1; }
        catch (Exception ex) { connection = L.F("Native module unavailable: {0}", ex.Message); }
        ConfigureToolHotkeys();
        AppWindow.Closing += (sender, e) =>
        {
            CommitFontSize(true);
            if (quitting) return;
            e.Cancel = true;
            if (settings.CloseToTray && nativeReady && !smokeTest) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, HideForTool);
            else _ = QuitAsync();
        };
        string? requestedPage=Environment.GetCommandLineArgs().FirstOrDefault(arg=>arg.StartsWith("--page="))?[7..];
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item=>(string)item.Tag==requestedPage)
            ?? Nav.MenuItems.OfType<NavigationViewItem>().First();
        freshnessTimer.Tick += (_, _) => UpdateLive();
        freshnessTimer.Start();
        _ = ConnectAsync(Environment.GetCommandLineArgs().Contains("--elevated") || (!smokeTest && settings.HardwareAccessEnabled));
        if (!smokeTest && TaskbarController.Needed(settings)) _ = EnsureTaskbarAsync();
        if(!smokeTest)desktopRestore=desktopTools.RestoreAsync();
        if (smokeTest) { if (Environment.GetCommandLineArgs().Contains("--desktop-tools-check")) _ = CheckDesktopToolsAsync(); else _ = SmokeTestAsync(); }
    }

    private async Task ConnectAsync(bool elevated,bool fallback=false)
    {
        if(reloading || quitting)return;
        reloading=true;
        if(!fallback)accessFallbackNotice=null;
        snapshot=SensorSnapshot.Empty("connecting");
        SetConnectionState(elevated?BrokerPhase.Authorizing:BrokerPhase.Connecting,
            elevated?L.T("Waiting for Windows hardware authorization…"):L.T("Connecting to sensors…"));
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
                SetConnectionState(BrokerPhase.Connected,data.Errors.Length>0?L.F("Some readings are unavailable · {0}", L.T(data.Errors[0])):data.Elevated?L.T("Hardware access authorized"):L.T("Basic monitoring connected"));
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
                if(cancelled){settings.HardwareAccessEnabled=false;Save();}
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
        fontSizeEditor=null;languageSelector=null;
        responsiveLayouts.Clear();layoutControls.Clear();
        PageContent.Children.Clear();
        PageScroll.ChangeView(null, 0, null);
        string title = page switch
        {
            "taskbar" => L.T("Taskbar"), "sensors" => L.T("Resource monitor"), "colors" => L.T("Text & icons"),
            "tools" => L.T("Desktop tools"), "general" => L.T("General"), "about" => L.T("About ClariBar"), _ => L.T("Overview")
        };
        PageContent.Children.Add(new TextBlock { Text = L.T("ClariBar  ›  ") + title, Style = SecondaryStyle, Margin = new Thickness(0, 0, 0, 2) });
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
            var version=Description(L.F("{0} · Windows 11 taskbar and live monitoring", AppVersion));
            version.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(version,1);pageHeader.Children.Add(version);
            ResponsiveHeader(pageHeader,version,760);
        }
        switch (page)
        {
            case "taskbar": BuildTaskbar(); break;
            case "sensors": BuildSensors(); break;
            case "colors": BuildColors(); break;
            case "tools": BuildDesktopTools(); break;
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
        var icon = SettingsIcons.Create(title,glyph);
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
        var toggle = new ToggleSwitch { IsOn = current, OnContent = L.T("On"), OffContent = L.T("Off"), VerticalAlignment = VerticalAlignment.Center };
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
        PageContent.Children.Add(Description(L.T("Keep an eye on your system while keeping your desktop clear.")));
        var tiles = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 8, 0, 6) };
        for (int i = 0; i < 4; i++) tiles.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var labels = new[] { L.T("CPU usage"), L.T("CPU temperature"), "GPU · 3D", L.T("Fan channels") };
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
        accessInfo = Notice(L.T("Hardware access"), L.T("Connecting to sensors…"));
        Row(L.T("Read temperatures and fan speeds"), L.T("Authorize the separate sensor process to access hardware while settings run with standard permissions."),
            HardwareActionControl(), "\uEA18");
        Row(L.T("Monitor window"), L.T("Show pinned metrics in a floating window. Click-through is enabled by default; disable it to drag."),
            Toggle(settings.MiniMonitorEnabled, value => { settings.MiniMonitorEnabled = value; Save(); UpdateMonitor(); }), "\uE9D9");
        Row(L.T("Choose monitoring metrics"), L.T("CPU, GPU, disk, network and motherboard sensors, with pinning and custom display names."),
            Button(L.T("Open resource monitor"), () => Nav.SelectedItem = Nav.MenuItems[2]), "\uE9D9");
        Heading(L.T("Monitoring status"));
        statusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = SecondaryStyle };
        PageContent.Children.Add(Card(statusText));
        if (store.LoadWarning is not null) Notice(L.T("Settings recovery"), L.T(store.LoadWarning), InfoBarSeverity.Warning);
    }

    private void BuildTaskbar()
    {
        AddTaskbarPreview();
        taskbarInfo = Notice(L.T("Explorer taskbar integration"), L.T("Checks the taskbar controls and applied state after connecting."));
        Row(L.T("Taskbar connection"), L.T("Reconnect after Windows or display changes. Disabling taskbar features restores the original appearance."),
            Button(L.T("Connect / check again"), () => _ = EnsureTaskbarAsync(true), true), "\uE7F4");
        Heading(L.T("Taskbar background"));
        var mode = new ComboBox { ItemsSource = new[] { L.T("Follow system"), L.T("Transparent"), L.T("Tinted") },
            SelectedIndex = settings.TaskbarBackgroundMode switch { "Transparent" => 1, "Tinted" => 2, _ => 0 }, MinWidth = 160 };
        mode.SelectionChanged += (_, _) => { settings.TaskbarBackgroundMode = mode.SelectedIndex switch {1=>"Transparent",2=>"Tinted",_=>"System"}; TaskbarChanged(); };
        Row(L.T("Background style"), L.T("Customize the taskbar background. Follow system restores the original appearance."), mode, "\uE790");
        AddColorPicker(L.T("Taskbar background color"), settings.TaskbarBackgroundColor, hex => settings.TaskbarBackgroundColor = hex);
        var opacity = new Slider { Minimum = 0, Maximum = 100, Value = settings.TaskbarOpacity, StepFrequency = 1, Width = 200 };
        opacity.ValueChanged += (_, args) => { settings.TaskbarOpacity = (int)args.NewValue; TaskbarChanged(); };
        Row(L.T("Background opacity"), L.T("Used only for tinted backgrounds: 0% is transparent and 100% is solid."), opacity, "\uE790");
        Heading(L.T("Embedded resource monitor"));
        Row(L.T("Embed in the Windows taskbar"), L.T("Displays as an actual item in the system tray layout, reserving space independently on each taskbar."),
            Toggle(settings.TaskbarMonitorEnabled, value => {settings.TaskbarMonitorEnabled=value;TaskbarChanged();}), "\uE9D9");
        AddMonitorLayoutControls();
        Row(L.T("Choose monitoring metrics"), L.T("Up to 8 metrics. The default selection includes CPU, temperature and physical uplink traffic."),
            Button(L.T("Select a metric"), () => Nav.SelectedItem = Nav.MenuItems[2]), "\uE8FD");
        Row(L.T("Individual text colors"), L.T("Set colors per pinned metric. Unconfigured metrics use the default text color."),
            Button(L.T("Configure colors"), () => Nav.SelectedItem = Nav.MenuItems[3]), "\uE790");
        Heading(L.T("Floating monitor"));
        Row(L.T("Show a floating monitor"), L.T("Controlled separately from the embedded taskbar display. Disable click-through to drag the floating window."),
            Toggle(settings.MiniMonitorEnabled, value => {settings.MiniMonitorEnabled=value;Save();UpdateMonitor();}), "\uE9D9");
        Heading(L.T("Restore"));
        Row(L.T("Restore taskbar changes"), L.T("Disable taskbar styling and embedded monitoring while preserving color values and selected metrics."), Button(L.T("Restore all"), () =>
        {
            settings.TaskbarBackgroundMode="System"; settings.TaskbarMonitorEnabled=false;
            settings.TimeColorEnabled=settings.DateColorEnabled=settings.TrayColorEnabled=false;
            settings.NetworkColorEnabled=settings.VolumeColorEnabled=settings.InputColorEnabled=settings.BatteryColorEnabled=false;
            TaskbarChanged(); RenderPage();
        }), "\uE777");
    }

    private void AddMonitorLayoutControls()
    {
        var layout = new ComboBox { ItemsSource = new[] { L.T("Single line"), L.T("Two columns"), L.T("Two rows") },
            SelectedIndex = settings.MonitorLayout switch {"SingleLine"=>0,"TwoRows"=>2,_=>1}, MinWidth = 160 };
        layout.SelectionChanged += (_, _) => {settings.MonitorLayout=layout.SelectedIndex switch{0=>"SingleLine",2=>"TwoRows",_=>"TwoColumns"};TaskbarChanged();};
        Row(L.T("Monitor layout"), L.T("Choose two columns or two rows. Text is trimmed when space is limited."), layout, "\uE8FD");
        var size=new NumberBox {Minimum=8,Maximum=24,Value=settings.MonitorFontSize,SmallChange=1,LargeChange=2,
            SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Inline,Width=160,
            NumberFormatter=new Windows.Globalization.NumberFormatting.DecimalFormatter {FractionDigits=0,IntegerDigits=1}};
        fontSizeEditor=size;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(size,L.T("Text size"));
        size.ValueChanged+=(_,args)=>{if(!updatingFontSize && double.IsFinite(args.NewValue))SetFontSize(args.NewValue);};
        size.AddHandler(UIElement.KeyDownEvent,new Microsoft.UI.Xaml.Input.KeyEventHandler((_,args)=>
        {
            if(args.Key==Windows.System.VirtualKey.Enter){CommitFontSize(true);args.Handled=true;}
            else if(args.Key==Windows.System.VirtualKey.Escape){SetFontSize(settings.MonitorFontSize);PageScroll.Focus(FocusState.Programmatic);args.Handled=true;}
        }),true);
        size.LostFocus+=(_,_)=>{if(!Within(FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject,size))CommitFontSize(false);};
        Row(L.T("Text size"),L.T("Press Enter or click outside to save. Use the arrow buttons to adjust one step at a time."),size,"\uE8D2");
        fontFamilies??=Native.FontFamilies();
        var fonts=new ComboBox {ItemsSource=fontFamilies,SelectedItem=settings.MonitorFontFamily,IsEditable=true,Text=settings.MonitorFontFamily,Width=240,MaxDropDownHeight=320};
        void SetFont(){string name=fonts.Text.Trim();if(name.Length>0 && name!=settings.MonitorFontFamily){settings.MonitorFontFamily=name;TaskbarChanged();}}
        fonts.SelectionChanged+=(_,_)=>{if(fonts.SelectedItem is string name){settings.MonitorFontFamily=name;TaskbarChanged();}};
        fonts.LostFocus+=(_,_)=>SetFont();
        Row(L.T("Monitor font"), L.T("Select an installed font or enter its name."),fonts,"\uE8D2");
        Row(L.T("Bold text"), L.T("Use bold text for all monitoring metrics."),Toggle(settings.MonitorBold,value=>{settings.MonitorBold=value;TaskbarChanged();}),"\uE8DD");
        var width = new Slider { Minimum = 100, Maximum = 900, Value = settings.MonitorWidth, StepFrequency = 10, Width = 200,IsEnabled=!settings.MonitorAutoWidth };
        Row(L.T("Automatic content width"), L.T("Enabled by default. Each column adapts to its text, font and size while preserving space on the taskbar."),
            Toggle(settings.MonitorAutoWidth,value=>{settings.MonitorAutoWidth=value;width.IsEnabled=!value;TaskbarChanged();}),"\uE7F4");
        width.ValueChanged += (_, args) => {settings.MonitorWidth=(int)args.NewValue;TaskbarChanged();};
        Row(L.T("Manual width"), L.T("Used when automatic width is disabled. Values are in logical pixels."), width, "\uE7F4");
        Row(L.T("Click-through"), L.T("Enabled by default. Disable to drag the floating window and allow the embedded monitor to receive pointer input."),
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
        taskbarInfo.Title=taskbar.IsConnecting?L.T("Connecting to Explorer"):state.Frames>0?L.T("Taskbar connected"):L.T("Taskbar integration status");
        taskbarInfo.Message=taskbar.IsConnecting?L.T("Inspecting the local XAML layout…"):taskbar.Error ??
            (state.Connected==0?L.T("Not connected. Taskbar features connect automatically when enabled."):
            L.F("{0} taskbars connected · {1} embedded monitors · Time and date: {2} · System icons: {3}", state.Frames, state.Panels, (state.Times>0 && state.Dates>0?L.T("Available"):L.T("Not checked")), (state.SystemIcons>0?L.T("Available"):L.T("Not checked"))));
        if(state.LastError!=0)taskbarInfo.Message+=L.F("; taskbar error 0x{0:X8}", state.LastError);
        if(state.AttachResult<0)taskbarInfo.Message+=L.F("; connection error 0x{0:X8}. Choose Connect / check again.", state.AttachResult);
        taskbarInfo.Message=L.T(taskbarInfo.Message);
        taskbarInfo.Severity=taskbar.Error is not null || state.LastError!=0 || state.AttachResult<0?InfoBarSeverity.Warning:InfoBarSeverity.Informational;
    }

    private void SetConnectionState(BrokerPhase phase,string message)
    {
        connectionPhase=phase;connection=L.T(message);
        if(connectionTransitions.Count==0 || connectionTransitions[^1]!=phase)connectionTransitions.Add(phase);
        UpdateHardwareAction();
    }
    private bool ConnectingHardware=>reloading || connectionPhase is BrokerPhase.Authorizing or BrokerPhase.Connecting or BrokerPhase.Reading;
    private string HardwareStatusText()=>connectionPhase switch
    {
        BrokerPhase.Authorizing=>L.T("Waiting for hardware authorization"), BrokerPhase.Connecting=>L.T("Connecting to the sensor process"),BrokerPhase.Reading=>L.T("Reading hardware data"),
        BrokerPhase.Cancelled=>L.T("Hardware authorization cancelled"),BrokerPhase.Failed=>L.T("Sensor connection failed"),
        BrokerPhase.Connected when !snapshot.IsFresh=>L.T("Sensor connection lost"),
        BrokerPhase.Connected when snapshot.Elevated=>L.T("Hardware access authorized"),
        BrokerPhase.Connected=>L.T("Basic monitoring connected"),_=>L.T("Waiting for sensors")
    };
    private FrameworkElement HardwareActionControl()
    {
        var controls=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10,VerticalAlignment=VerticalAlignment.Center};
        hardwareProgress=new ProgressRing {Width=16,Height=16,Visibility=Visibility.Collapsed};controls.Children.Add(hardwareProgress);
        hardwareState=new TextBlock {Style=SecondaryStyle,VerticalAlignment=VerticalAlignment.Center};controls.Children.Add(hardwareState);
        hardwareAction=Button(L.T("Authorize hardware access"),()=>_=ConnectAsync((snapshot.IsFresh && snapshot.Sensors.Length>0) || settings.HardwareAccessEnabled),true);
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
            BrokerPhase.Authorizing=>L.T("Waiting for authorization…"),BrokerPhase.Connecting=>L.T("Connecting…"),BrokerPhase.Reading=>L.T("Reading…"),
            _ when live && snapshot.Elevated=>L.T("Reconnect"),_ when live=>L.T("Authorize hardware access"),_=>L.T("Reconnect")
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
        bool distinctFilterNames=sensorFilters.Children.OfType<ComboBox>().First().SelectedItem?.ToString()==L.T("All categories") &&
            (deviceFilter.SelectedItem as DeviceOption)?.Name==L.T("All hardware");
        bool actualConnection=hardwareAction.Content?.ToString()==(snapshot.Elevated?L.T("Reconnect"):L.T("Authorize hardware access")) && hardwareAction.IsEnabled && !ConnectingHardware;
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
        accessInfo = Notice(L.T("Live sensors"), L.T("Shows actual readings when connected. Unavailable readings appear as —."));
        networkInfo=Notice(L.T("Default physical uplink"),L.T("Checking the default route…"));
        var filters=new Grid {ColumnSpacing=8};sensorFilters=filters;
        filters.ColumnDefinitions.Add(new(){Width=new GridLength(160)});
        filters.ColumnDefinitions.Add(new(){Width=new GridLength(160)});
        filters.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=110});
        filters.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var categories = new ComboBox { ItemsSource = new[] { L.T("All categories"), "CPU", "GPU", L.T("Motherboard & fans"), L.T("Disk"), L.T("Network"), L.T("Memory") }, SelectedItem = category, HorizontalAlignment = HorizontalAlignment.Stretch };
        categories.SelectionChanged += (_, _) => { category = (string)categories.SelectedItem; deviceId = ""; PopulateDevices(); RefreshSensorRows(); };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(categories,L.T("Device category"));
        ToolTipService.SetToolTip(categories,L.T("Filter by device category"));filters.Children.Add(categories);
        var kinds = new ComboBox { ItemsSource = new[] { L.T("Common metrics"), L.T("All metrics"), L.T("Pinned"), L.T("Temperatures only"), L.T("Fans only"), L.T("Usage only"), L.T("Transfer rates only") }, SelectedItem = sensorKind, HorizontalAlignment = HorizontalAlignment.Stretch };
        kinds.SelectionChanged += (_, _) => { sensorKind = (string)kinds.SelectedItem;PopulateDevices();RefreshSensorRows(); };
        Grid.SetColumn(kinds, 1); filters.Children.Add(kinds);
        deviceFilter = new ComboBox { PlaceholderText = L.T("All hardware"), DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
        deviceFilter.SelectionChanged += (_, _) =>
        { if (!synchronizingSelection) { deviceId = (deviceFilter.SelectedItem as DeviceOption)?.Id ?? ""; RefreshSensorRows(); } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(deviceFilter,L.T("Hardware device"));
        ToolTipService.SetToolTip(deviceFilter,L.T("Filter by hardware device"));Grid.SetColumn(deviceFilter,2);filters.Children.Add(deviceFilter);
        sensorFilterSummary=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};
        countText=Description("");countText.VerticalAlignment=VerticalAlignment.Center;sensorFilterSummary.Children.Add(countText);
        unavailableFilter=new CheckBox {Content=L.T("Show sensors without readings"),IsChecked=showUnavailable,VerticalAlignment=VerticalAlignment.Center};
        unavailableFilter.Checked+=(_,_)=>{showUnavailable=true;PopulateDevices();RefreshSensorRows();};
        unavailableFilter.Unchecked+=(_,_)=>{showUnavailable=false;PopulateDevices();RefreshSensorRows();};sensorFilterSummary.Children.Add(unavailableFilter);
        Grid.SetColumn(sensorFilterSummary,3);filters.Children.Add(sensorFilterSummary);PageContent.Children.Add(filters);
        var search = new TextBox { PlaceholderText = L.T("Filter by name, device or sensor identifier"), Text = query };
        search.TextChanged += (_, _) => { query = search.Text.Trim(); RefreshSensorRows(); };
        PageContent.Children.Add(search);
        PageContent.Children.Add(Description(L.T("Common metrics include overall usage, CPU/GPU temperatures, disk activity and transfers, and physical uplink traffic. Select up to 8 to pin.")));
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
                _ = MessageAsync(L.T("Up to 8 metrics can be pinned"), L.T("Unpin an existing metric before selecting another.")); return;
            }
            settings.PinnedSensors = selected.ToArray(); Save(); UpdateMonitor();
        };
        PageContent.Children.Add(Card(sensorList));
        emptySensors = Description(L.T("No sensors match these filters. Authorize hardware access for temperatures and motherboard fans, or adjust the filters."));
        PageContent.Children.Add(emptySensors);
        var names = new StackPanel { Spacing = 10 };
        names.Children.Add(Description(L.T("Custom display names do not change sensor identifiers or control fan speeds.")));
        var channel = new ComboBox { PlaceholderText = L.T("Select a detected sensor"), DisplayMemberPath = "DisplayName", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = CustomizableSensors() };
        renameSelector = channel;
        var rename = new TextBox { PlaceholderText = L.T("Enter a display name, e.g. CPU fan (leave blank to reset)") };
        channel.SelectionChanged += (_, _) => { if (channel.SelectedItem is SensorReading reading) rename.Text = settings.SensorNames.GetValueOrDefault(reading.Id, ""); };
        names.Children.Add(channel); names.Children.Add(rename);
        names.Children.Add(Button(L.T("Save name"), () =>
        {
            if (channel.SelectedItem is not SensorReading reading) return;
            string value = rename.Text.Trim();
            if (value.Length == 0) settings.SensorNames.Remove(reading.Id); else settings.SensorNames[reading.Id] = value[..Math.Min(value.Length, 48)];
            Save(); RefreshSensorRows(); UpdateMonitor();
        }));
        PageContent.Children.Add(SettingsIcons.Section(L.T("Custom metric names"),names));
        PopulateDevices();
    }

    private sealed record DeviceOption(string Id, string Name);
    private void PopulateDevices()
    {
        if (deviceFilter is null) return;
        var devices = SensorCatalog.Filter(snapshot,sensorKind,settings.PinnedSensors,showUnavailable).Where(s => category == L.T("All categories") || s.Category == category)
            .GroupBy(s => s.HardwareId).Select(g => new DeviceOption(g.Key, g.First().Hardware)).OrderBy(d => d.Name).ToList();
        devices.Insert(0,new("",L.T("All hardware")));
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
            (category==L.T("All categories") || s.Category==category) && (deviceId.Length==0 || s.HardwareId==deviceId) &&
            (query.Length==0 || $"{s.Name} {s.DisplayName} {s.Hardware} {s.Id} {settings.SensorNames.GetValueOrDefault(s.Id)}".Contains(query,StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s=>s.Category switch {"CPU"=>0,"GPU"=>1,_ when s.Category == L.T("Memory") =>2,_ when s.Category == L.T("Disk") =>3,_ when s.Category == L.T("Network") =>4,_=>5})
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
        if (countText is not null) countText.Text = L.F("{0} / {1} readings · {2} pinned", desired.Length, snapshot.Sensors.Length, settings.PinnedSensors.Length);
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
        header.Children.Add(new TextBlock {Text=L.T("Taskbar preview"),FontSize=16,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8};
        var desktop=new ComboBox {ItemsSource=new[]{L.T("Dark desktop"),L.T("Light desktop")},SelectedIndex=0,MinWidth=130};
        actions.Children.Add(desktop);actions.Children.Add(Button(L.T("Expand preview"),()=>_=ShowTaskbarPreviewAsync()));Grid.SetColumn(actions,1);header.Children.Add(actions);
        taskbarPreview=new TaskbarPreview();desktop.SelectionChanged+=(_,_)=>taskbarPreview?.SetDesktop(desktop.SelectedIndex==0);
        ResponsiveHeader(header,actions,560);
        content.Children.Add(header);content.Children.Add(taskbarPreview);content.Children.Add(Description(L.T("Monitoring uses live data. The preview follows the current layout, font and colors; system icons are illustrative.")));
        PageContent.Children.Add(Card(content));
    }
    private async Task ShowTaskbarPreviewAsync()
    {
        var preview=new TaskbarPreview {MinWidth=Math.Min(720,Math.Max(240,Root.ActualWidth-80)),MinHeight=230};expandedPreview=preview;
        var content=new StackPanel {Spacing=12};
        var desktop=new ComboBox {ItemsSource=new[]{L.T("Dark desktop"),L.T("Light desktop")},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Left};
        desktop.SelectionChanged+=(_,_)=>preview.SetDesktop(desktop.SelectedIndex==0);content.Children.Add(desktop);content.Children.Add(preview);
        var dialog=new ContentDialog {Title=L.T("Live taskbar preview"),Content=content,CloseButtonText=L.T("Close"),XamlRoot=Root.XamlRoot};
        dialog.Resources["ContentDialogMaxWidth"]=1000d;
        UpdateMonitor();
        try
        {
            var shown=dialog.ShowAsync();
            if(smokeTest)
            {
                await Task.Delay(600);
                if(preview.MetricCount==0 || preview.MetricTexts.Any(text=>text.IsTextTrimmed || text.FontFamily.Source!=settings.MonitorFontFamily || text.FontWeight.Weight!=(settings.MonitorBold?700:400)))throw new IOException(L.T("Expanded preview metrics are missing, trimmed, or use an incorrect font style."));
                await RenderCheckImageAsync("taskbar-preview-large",preview);
                preview.SetDesktop(false);await Task.Delay(150);await RenderCheckImageAsync("taskbar-preview-light",preview);dialog.Hide();
            }
            await shown;
        }
        finally{dialog.Hide();expandedPreview=null;}
    }
    private void AddColorSchemes()
    {
        Heading(L.T("Color presets"));
        var scope=new ComboBox {ItemsSource=new[]{L.T("Monitor metrics"),L.T("Time & system icons"),L.T("All text")},SelectedIndex=paletteScope==ColorScope.All?2:paletteScope==ColorScope.System?1:0,MinWidth=180};
        scope.SelectionChanged+=(_,_)=>paletteScope=scope.SelectedIndex==2?ColorScope.All:scope.SelectedIndex==1?ColorScope.System:ColorScope.Monitor;
        Row(L.T("Color scope"),L.T("Applying system colors also enables the corresponding fixed-color switches."),scope,"\uE790");
        Row(L.T("Random colors"),L.T("Generate a different color for each item in the selected scope."),Button(L.T("Randomize"),()=>ApplyColorScheme(ColorSchemes.Random()),true),"\uE8B1");
        var schemes=new Grid {ColumnSpacing=10};
        for(int index=0;index<ColorSchemes.Presets.Count;index++)
        {
            schemes.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var scheme=ColorSchemes.Presets[index];var content=new StackPanel {Spacing=10};
            var swatches=new StackPanel {Orientation=Orientation.Horizontal,Spacing=4};
            foreach(string color in scheme.Colors.Take(4))swatches.Children.Add(new Border {Width=18,Height=18,CornerRadius=new CornerRadius(5),Background=new SolidColorBrush(ParseColor(color))});
            content.Children.Add(swatches);content.Children.Add(new TextBlock {Text=L.T(scheme.Name)});
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
        Heading(L.T("Monitor text"));
        AddColorPicker(L.T("Default text color"), settings.MonitorColor, hex => settings.MonitorColor = hex);
        var selection = new ComboBox { DisplayMemberPath="DisplayName", PlaceholderText=L.T("Select a metric"), HorizontalAlignment=HorizontalAlignment.Stretch,
            ItemsSource=CustomizableSensors() };
        metricColorSelector=selection;
        var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8};
        actions.Children.Add(Button(L.T("Choose color"),()=>
        {
            if(selection.SelectedItem is not SensorReading reading)return;
            ScreenColor.TryParse(settings.SensorColors.GetValueOrDefault(reading.Id,settings.MonitorColor),out var color);
            desktopTools.OpenColor(color,hex=>{settings.SensorColors[reading.Id]=hex;TaskbarChanged();});
        }));
        actions.Children.Add(Button(L.T("Use default color"),()=>{if(selection.SelectedItem is SensorReading reading){settings.SensorColors.Remove(reading.Id);TaskbarChanged();}}));
        var perMetric=new StackPanel {Spacing=10};perMetric.Children.Add(selection);perMetric.Children.Add(actions);
        Row(L.T("Per-metric text colors"), L.T("Colors are stored by sensor identifier and shared by embedded and floating monitors."), new TextBlock(), "\uE790");
        PageContent.Children.Add(Card(perMetric));
        AddMonitorLayoutControls();
        Heading(L.T("Floating monitor background"));
        AddColorPicker(L.T("Floating window background color"), settings.MonitorBackground, hex => settings.MonitorBackground = hex);
        var opacity = new Slider { Minimum = 30, Maximum = 100, Value = settings.MonitorOpacity, StepFrequency = 1, Width = 200 };
        opacity.ValueChanged += (_, args) => { settings.MonitorOpacity = (int)args.NewValue; Save(); UpdateMonitor(); };
        Row(L.T("Floating monitor opacity"), L.T("Applies only to the floating monitor. Configure the taskbar background on the Taskbar page."), opacity, "\uE790");
        Heading(L.T("System taskbar text & icons"));
        taskbarInfo=Notice(L.T("System foreground colors"), L.T("Supports time, date and monochrome system icons. Third-party color tray bitmaps retain their original appearance."));
        SystemColorRow(L.T("Time"),"\uE917",settings.TimeColor,settings.TimeColorEnabled,v=>settings.TimeColorEnabled=v,v=>settings.TimeColor=v);
        SystemColorRow(L.T("Date"),"\uE787",settings.DateColor,settings.DateColorEnabled,v=>settings.DateColorEnabled=v,v=>settings.DateColor=v);
        SystemColorRow(L.T("System tray glyphs"),"\uE971",settings.TrayColor,settings.TrayColorEnabled,v=>settings.TrayColorEnabled=v,v=>settings.TrayColor=v);
        SystemColorRow(L.T("Network icon"),"\uE839",settings.NetworkColor,settings.NetworkColorEnabled,v=>settings.NetworkColorEnabled=v,v=>settings.NetworkColor=v);
        SystemColorRow(L.T("Volume icon"),"\uE767",settings.VolumeColor,settings.VolumeColorEnabled,v=>settings.VolumeColorEnabled=v,v=>settings.VolumeColor=v);
        SystemColorRow(L.T("Input method & language"),"\uE765",settings.InputColor,settings.InputColorEnabled,v=>settings.InputColorEnabled=v,v=>settings.InputColor=v);
        SystemColorRow(L.T("Battery icon"),"\uE83F",settings.BatteryColor,settings.BatteryColorEnabled,v=>settings.BatteryColorEnabled=v,v=>settings.BatteryColor=v);
    }
    private void SystemColorRow(string title,string glyph,string color,bool enabled,Action<bool> toggle,Action<string> change)
    {
        var label=new TextBlock {Text=color};
        var controls=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};
        var choose=new Button {Content=label,VerticalAlignment=VerticalAlignment.Center};
        choose.Click+=(_,_)=>{ScreenColor.TryParse(label.Text,out var current);desktopTools.OpenColor(current,hex=>{label.Text=hex;change(hex);TaskbarChanged();});};
        controls.Children.Add(choose);
        controls.Children.Add(Toggle(enabled,value=>{toggle(value);TaskbarChanged();}));
        Row(title,L.T("Enable a fixed color or disable it to follow Windows. Individual colors override the general tray color."),controls,glyph);
    }
    private void AddColorPicker(string title, string value, Action<string> changed)
    {
        var hexText = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center };
        var button = new Button { Content = hexText };
        button.Click += (_, _) =>
        {
            ScreenColor.TryParse(hexText.Text, out var current);
            desktopTools.OpenColor(current, hex => { changed(hex); hexText.Text = hex; TaskbarChanged(); });
        };
        Row(title, L.T("Open the color picker to preview changes immediately."), button, "\uE790");
    }
    private static Color ParseColor(string hex) => Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
    private void BuildGeneral()
    {
        AddLanguageControl();
        var theme = new ComboBox { ItemsSource = new[] { L.T("Follow system"), L.T("Light"), L.T("Dark") }, SelectedIndex = settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 }, MinWidth = 150 };
        theme.SelectionChanged += (_, _) => { settings.Theme = theme.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" }; ApplyTheme(); Save(); };
        Row(L.T("App theme"), L.T("Native Windows controls with light and dark themes."), theme, "\uE790");
        var interval = new ComboBox { ItemsSource = new[] { L.T("0.5 seconds"), L.T("1 second"), L.T("2 seconds"), L.T("5 seconds") }, SelectedIndex = settings.IntervalMs switch { 500 => 0, 2000 => 2, 5000 => 3, _ => 1 }, MinWidth = 150 };
        interval.SelectionChanged += (_, _) => { settings.IntervalMs = new[] { 500, 1000, 2000, 5000 }[interval.SelectedIndex]; if (broker is not null) broker.IntervalMs = settings.IntervalMs; Save(); };
        Row(L.T("Request hardware access at startup"),L.T("Only the sensor process requires administrator access. Windows will ask for authorization. Disable this option to use basic monitoring."),
            Toggle(settings.HardwareAccessEnabled,value=>{settings.HardwareAccessEnabled=value;Save();_=ConnectAsync(value);}),"\uEA18");
        Row(L.T("Sampling interval"), L.T("Samples run in a separate process. Temperatures and fan speeds update according to the hardware."), interval, "\uE916");
        Row(L.T("Keep monitoring when the window closes"), L.T("Closing hides the window in the system tray. Use the tray menu to quit completely."),
            Toggle(settings.CloseToTray, value => { settings.CloseToTray = value; Save(); }), "\uE73F");
        Heading(L.T("Files & data"));
        Row(L.T("Settings & runtime data"), AppPaths.Data, Button(L.T("Open folder"), () => OpenFolder(AppPaths.Data)), "\uE8B7");
        Row(L.T("Diagnostic records"), L.T("Export the current monitoring state and sensor snapshot to the logs directory."), Button(L.T("Export diagnostics"), ExportDiagnostics), "\uE9D9");
        Row(L.T("Quit application"), L.T("Close monitors, restore the taskbar and disconnect the sensor process."), Button(L.T("Quit ClariBar"), () => _ = QuitAsync()), "\uE8BB");
    }
    private void BuildAbout()
    {
        BuildProjectAbout();Heading(L.T("Technology & dependencies"));
        Row(L.T("Technology"), L.T("WinUI and .NET settings, native C++ tray and monitor windows, and an independent LibreHardwareMonitor sensor process."), new TextBlock(), "\uE943");
        Row(L.T("Hardware readings"), L.T("Some temperatures and fans require administrator access. Integrated GPUs without a fan do not report GPU fan speed. CPU temperature never substitutes for GPU temperature."), new TextBlock(), "\uE9D9");
        Row(L.T("Fan channels"), L.T("0 RPM can mean a stopped or disconnected fan. Splitters may expose only one speed channel. Monitoring is read-only and does not control fan speeds."), new TextBlock(), "\uE9CE");
        Row(L.T("Open-source dependencies"), L.T("LibreHardwareMonitor: MPL-2.0. Windows App SDK, .NET and other dependency licenses are included with the application."), Button(L.T("Open application folder"),()=>OpenFolder(AppContext.BaseDirectory)), "\uE8A5");
    }

    private void ApplyTheme()
    { Root.RequestedTheme = settings.Theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default }; desktopTools?.RefreshAppearance(); }
    private void Save()
    {
        try { store.Save(settings); }
        catch (Exception ex) { connection = L.F("Settings could not be saved: {0}", ex.Message); if (statusText is not null) statusText.Text = connection; }
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
            statusText.Text = $"{connection}\n{(snapshot.Sensors.Length > 0 ? L.F("{0} sensor readings · Last sample {1:HH:mm:ss}", snapshot.Sensors.Length, snapshot.CapturedAt.ToLocalTime()) : L.T("Waiting for hardware data"))}\n{(fresh ? "" : L.T("Readings are not ready or are stale."))}";
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
                : snapshot.Errors.Length > 0 ? string.Join(Localizer.IsChinese?"；":"; ", snapshot.Errors.Take(2).Select(L.T))
                : snapshot.Elevated ? L.T("CPU temperatures and motherboard fans depend on hardware support. Unavailable readings remain —.")
                : L.T("CPU, GPU, memory and physical network traffic are available. Authorize hardware access for CPU temperatures, core clocks and detailed disk readings.");
            if(accessFallbackNotice is not null)accessInfo.Message=accessFallbackNotice+"\n"+accessInfo.Message;
            accessInfo.Severity=snapshot.Errors.Length>0 || accessFallbackNotice is not null || connectionPhase is BrokerPhase.Failed or BrokerPhase.Cancelled?InfoBarSeverity.Warning:
                connectionPhase==BrokerPhase.Connected && fresh && snapshot.Elevated?InfoBarSeverity.Success:InfoBarSeverity.Informational;
        }
        if(accessInfo is not null && fresh)
        {
            var issues=snapshot.Capabilities.Where(capability=>capability.Status!="valid" && capability.Key.EndsWith("/Temperature",StringComparison.Ordinal)).ToArray();
            if(issues.Length>0)accessInfo.Message+="\n"+string.Join(Localizer.IsChinese?"；":"; ",issues.Select(capability=>L.F("{0}: {1}",capability.DisplayLabel,L.T(capability.Explanation))));
        }
        if(networkInfo is not null)
        {
            var route=snapshot.Network;
            networkInfo.Title=route is {InterfaceIndex:>0}?L.F("Physical uplink: {0}", route.Name):L.T("Default physical uplink");
            networkInfo.Message=route is null?L.T("Physical uplink data has not arrived."):route.InterfaceIndex==0?L.T(route.Message):
                L.F("{0} · Interface {1} · {2} virtual or nonphysical interfaces excluded. Traffic includes all transfers through the physical uplink.", route.Description, route.InterfaceIndex, route.ExcludedAdapters);
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
        string text=config.MetricCount==0?L.T("ClariBar · Waiting for monitoring data"):string.Join("    ",config.Metrics.Take((int)config.MetricCount).Select(metric=>metric.Text));
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
            { version = AppVersion, system = Environment.OSVersion.VersionString, connection, snapshot, taskbar=taskbar.Status, pinnedSensors = settings.PinnedSensors },
                new JsonSerializerOptions(Wire.Json) { WriteIndented = true }));
            _ = MessageAsync(L.T("Diagnostics exported"), path);
        }
        catch (Exception ex) { _ = MessageAsync(L.T("Could not export diagnostics"), ex.Message); }
    }
    private static void OpenFolder(string path)
    { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
    private async Task MessageAsync(string title, string message)
    {
        var dialog = new ContentDialog { Title = title, Content = message, CloseButtonText = L.T("OK"), XamlRoot = Root.XamlRoot };
        try { await dialog.ShowAsync(); } catch (InvalidOperationException) { }
    }
    private void ShowSettings()
    {
        SynchronizeTranslationPreferences();
        Native.ClearCaptionHover(hwnd); Native.ShowWindow(hwnd, 9); Activate(); Native.SetForegroundWindow(hwnd);
        PageScroll.Focus(FocusState.Programmatic);
    }
    private async Task QuitAsync()
    {
        if (quitting) return;
        CommitFontSize(false);
        quitting = true; freshnessTimer.Stop();
        await desktopTools.ShutdownAsync();
        if (nativeReady) { Native.CB_Stop(); nativeReady = false; }
        if (broker is not null) await broker.DisposeAsync();
        Close();
        Application.Current.Exit();
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
        string[] labels=["CPU 4.16 %",L.T("CPU core temperature 38 °C"),L.T("Memory 23.86 %"),L.T("Upload 9 KiB/s"),L.T("Download 97.5 KiB/s"),L.T("Disk usage 0.98 %")];
        for(int i=0;i<labels.Length;i++)config.Metrics[i]=new(){Text=labels[i],Color=0xff70d8ee};
        var checks=new List<object>();
        foreach(int size in new[]{8,15,24})
        {
            sample.MonitorFontSize=size;preview.Update(sample,config);await Task.Delay(250);
            await RenderCheckImageAsync($"monitor-alignment-{size}",preview);
            checks.Add(new{font=size,height=preview.TaskbarHeight,up=preview.UpShift,image=$"monitor-alignment-{size}.png"});
        }
        config.Metrics[3].Text=L.T("Upload gjpq Â 9 KiB/s");sample.MonitorFontSize=24;
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
            await CheckLocalizationAsync();
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
            repository = ProjectRepository, configPath = store.FilePath
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
