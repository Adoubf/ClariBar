using ClariBar.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed class DesktopToolsController
{
    private readonly MainWindow owner;
    private readonly AppSettings settings;
    private readonly Action save;
    private readonly HashSet<Window> windows=[];
    private CaptureOverlay? overlay;
    private LiveColorPicker? livePicker;
    private PixelFrame? latest;
    private bool busy,disposed;
    public ToolSessionStore Sessions {get;}
    public PinManager Pins {get;}
    public ElementTheme Theme=>settings.Theme switch {"Light"=>ElementTheme.Light,"Dark"=>ElementTheme.Dark,_=>ElementTheme.Default};
    public AppSettings Settings=>settings;
    public string ScreenshotDirectory=>string.IsNullOrWhiteSpace(settings.ScreenshotDirectory)?Path.Combine(AppPaths.Data,"screenshots"):settings.ScreenshotDirectory;
    public DesktopToolsController(MainWindow owner,AppSettings settings,Action save)
    {
        this.owner=owner;this.settings=settings;this.save=save;
        Sessions=new(Path.Combine(owner.ToolDataDirectory,"desktop-tools"));Sessions.LoadHistory();Pins=new(this,Sessions);
    }
    public void Save()=>save();
    public void ShowSettings()=>owner.RestoreFromTool();
    public void Track(Window window)
    {windows.Add(window);window.Closed+=(_,_)=>windows.Remove(window);window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"ClariBar.ico"));}
    public void RefreshAppearance()
    {
        foreach(var window in windows.ToArray())if(window is not CaptureOverlay && window.Content is FrameworkElement root)
        {root.RequestedTheme=Theme;root.Language=L.LanguageCode;ToolUi.Translate(root);if(window.Title.StartsWith("ClariBar · "))window.Title="ClariBar · "+L.T(window.Title[11..]);}
    }
    public async Task RestoreAsync(){if(!disposed && settings.PinAutoRestore)await Pins.RestoreAsync();}
    public async Task BeginAsync(bool color=false,bool ocr=false,Action<string>? chosen=null,string mode="Region",PixelRect? customArea=null,bool returnToSettings=true,bool translate=false)
    {
        if(disposed)return;if(busy){overlay?.Activate();return;}
        busy=true;bool settingsVisible=DesktopCapture.IsWindowVisible(owner.Handle),restore=settingsVisible && returnToSettings;
        try
        {
            if(settingsVisible)owner.HideForTool();
            if(color)
            {
                await Task.Delay(120);using var picker=new LiveColorPicker(owner,this,chosen is null && settings.ColorPickerActivation=="PickOnly"?"Copy":null);livePicker=picker;
                var selectedColor=await picker.SelectAsync();livePicker=null;if(disposed || selectedColor is null)return;
                if(selectedColor.Color is {} picked)
                {
                    Remember(picked);await ToolUi.CopyTextAsync(ColorFormats.DefaultText(picked,settings));chosen?.Invoke(picked.Hex);
                    if(chosen is null){if(selectedColor.Action=="Edit")OpenColor(picked);restore=false;}
                }
                return;
            }
            if(mode=="Delayed" && !await CountdownAsync(settings.CaptureDelaySeconds))return;
            await Task.Delay(160);DesktopCapture.DwmFlush();var bounds=DesktopCapture.DesktopBounds;
            var candidates=DesktopCapture.Windows(bounds,owner.Handle);
            PixelFrame frame;
            if(mode=="Whiteboard"){byte[] white=new byte[checked(bounds.Width*bounds.Height*4)];Array.Fill(white,(byte)255);frame=new(bounds.Width,bounds.Height,white);}
            else frame=DesktopCapture.Capture(bounds);
            PixelRect? selected=null;
            if(mode is "FullScreen" or "Whiteboard")selected=frame.Bounds;
            else if(mode=="Screen")
            {DesktopCapture.GetCursorPos(out var cursor);var screen=DisplayArea.GetFromPoint(new PointInt32(cursor.X,cursor.Y),DisplayAreaFallback.Nearest).OuterBounds;selected=new(screen.X-bounds.X,screen.Y-bounds.Y,screen.Width,screen.Height);}
            else if(mode=="Last" && Sessions.LastArea is {} last)selected=last with{X=last.X-bounds.X,Y=last.Y-bounds.Y};
            else if(customArea is {} custom)selected=custom with{X=custom.X-bounds.X,Y=custom.Y-bounds.Y};
            if(disposed)return;overlay=new(this,frame,bounds,candidates,false,ocr,selected);Track(overlay);
            var result=await overlay.SelectAsync();overlay=null;if(disposed)return;
            if(translate && result is not null)restore=false;
            if(restore){owner.RestoreFromTool();restore=false;}
            if(result is null)return;
            if(result.Image is {} image)
            {
                latest=image;
                if(translate)
                {
                    var origin=result.Desktop??bounds;var a=result.Area??image.Bounds;
                    OpenTranslation(image,new(origin.X+a.X,origin.Y+a.Y,a.Width,a.Height));
                }
                else if(result.Action=="OCR")OpenOcr(image);
                else if(result.Action=="Pin")
                {
                    var content=new ImageContent(result.Source??image);
                    var origin=result.Desktop??bounds;
                    PixelRect? position=result.Area is {} a?new(origin.X+a.X,origin.Y+a.Y,a.Width,a.Height):null;
                    Pins.Create(content,result.Marks,position);
                }
                else if(result.Action=="Edit")OpenEditor(image);
            }
        }
        catch(Exception ex){if(!disposed){if(restore){owner.RestoreFromTool();restore=false;}await owner.ToolErrorAsync(ex.Message);}}
        finally{livePicker=null;if(restore && !disposed)owner.RestoreFromTool();busy=false;}
    }
    private async Task<bool> CountdownAsync(int seconds)
    {
        if(seconds<=0)return true;bool cancelled=false;
        var window=new Window {Title=L.T("Delayed capture")};
        var label=ToolUi.Label("",25);var panel=new StackPanel {Spacing=12,Padding=new(18)};panel.Children.Add(label);
        panel.Children.Add(ToolUi.Button("Cancel",()=>{cancelled=true;window.Close();}));window.Content=panel;
        ToolUi.Configure(window,this,"Delayed capture",260,170,true);Track(window);
        window.Closed+=(_,_)=>cancelled=true;window.Activate();
        for(int remaining=seconds;remaining>0 && !cancelled;remaining--){label.Text=L.F("Capture in {0}…",remaining);await Task.Delay(1000);}
        bool result=!cancelled;window.Close();return result;
    }
    public async Task CompletedCaptureAsync(PixelFrame scene,PixelRect bounds,PixelRect area,IEnumerable<CaptureMark> marks,PixelFrame output,string action)
    {
        await Sessions.RecordAsync(scene,bounds,area,marks,settings.CaptureHistoryLimit);latest=output;
        if(settings.CaptureAutoSave && action!="QuickSave")await ImageExport.QuickAsync(output,settings,Path.Combine(ScreenshotDirectory,"AutoSave"));
        if(settings.CaptureSound && action is "Copy" or "Save" or "QuickSave")DesktopCapture.MessageBeep(0);
    }
    public void Remember(ScreenColor color){settings.ColorHistory=ScreenColor.Remember(settings.ColorHistory,color.Hex);save();}
    public Task ActivateColorAsync()
    {if(settings.ColorPickerActivation=="Editor"){OpenColor();return Task.CompletedTask;}return BeginAsync(color:true);}
    public void OpenColor(ScreenColor? color=null,Action<string>? changed=null)
    {
        if(disposed)return;if(color is null && ScreenColor.TryParse(settings.ColorHistory.FirstOrDefault(),out var last))color=last;
        var window=new ColorEditorWindow(this,color??new ScreenColor(80,190,210),changed);Track(window);window.Activate();
    }
    public void OpenEditor(PixelFrame frame){if(disposed)return;latest=frame;var window=new CaptureEditorWindow(this,frame);Track(window);window.Activate();}
    public void Pin(PixelFrame frame){if(!disposed){latest=frame;Pins.Create(new(frame));}}
    public async Task PinClipboardAsync()
    {try{await Pins.PasteAsync();}catch(Exception ex){await owner.ToolErrorAsync(ex.Message);}}
    public void OpenOcr(PixelFrame frame){if(disposed)return;var window=new OcrResultWindow(this,frame);Track(window);window.Activate();}
    public Task BeginTranslationAsync()=>BeginAsync(ocr:true,translate:true);
    public void OpenTranslation(PixelFrame frame,PixelRect position)
    {if(disposed)return;var window=new TranslationWindow(this,frame,position,settings.TranslationDisplay=="Overlay");Track(window);window.Activate();}
    public Task<string?> SaveImageAsync(PixelFrame frame,Window parent)=>QuickSaveAsync(frame);
    private async Task<string?> QuickSaveAsync(PixelFrame frame)=>await ImageExport.QuickAsync(frame,settings,ScreenshotDirectory);
    public Task<string?> SaveAsImageAsync(PixelFrame frame,Window parent)=>ImageExport.SaveAsAsync(frame,parent,settings);
    public async Task OpenFilesAsync(Window? parent=null)
    {
        var picker=new Windows.Storage.Pickers.FileOpenPicker {ViewMode=Windows.Storage.Pickers.PickerViewMode.Thumbnail};
        foreach(string type in new[]{".png",".jpg",".jpeg",".bmp",".tif",".tiff",".gif",".webp",".ico",".tga"})picker.FileTypeFilter.Add(type);
        WinRT.Interop.InitializeWithWindow.Initialize(picker,parent is null?owner.Handle:WinRT.Interop.WindowNative.GetWindowHandle(parent));
        foreach(var file in await picker.PickMultipleFilesAsync())Pins.Create(await ImageContent.FromFileAsync(file.Path));
    }
    public async Task OpenPinsAsync()
    {var window=new PinManagerWindow(this);Track(window);window.Activate();await Task.CompletedTask;}
    public async Task ShowErrorAsync(Exception ex)=>await owner.ToolErrorAsync(ex.Message);
    public async Task ShutdownAsync(){if(disposed)return;await Pins.PersistAsync();CloseAll();}
    public void CloseAll()
    {disposed=true;livePicker?.Dispose();Pins.Shutdown();foreach(var window in windows.ToArray())window.Close();windows.Clear();latest=null;}
}

internal static class ToolUi
{
    public static TextBlock Label(string text, double size = 14) => new() { Text = text, Tag = "ClariBar.Translatable", FontSize = size, TextWrapping = TextWrapping.Wrap };
    public static Button Button(string text, Action action, string? tip = null)
    {
        var button = new Button { Content = L.T(text), VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, L.T(text));
        if (tip is not null) ToolTipService.SetToolTip(button, L.T(tip));
        button.Click += (_, _) => action(); return button;
    }
    public static Button AsyncButton(string text, Func<Task> action)
    {
        var button = Button(text, () => { });
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception ex)
            {
                var dialog = new ContentDialog { Title = L.T("Desktop tools"), Content = ex.Message, CloseButtonText = L.T("Close"), XamlRoot = button.XamlRoot };
                try { await dialog.ShowAsync(); } catch (InvalidOperationException) { }
            }
            finally { button.IsEnabled = true; }
        };
        return button;
    }
    public static SolidColorBrush Brush(string hex) => new(Windows.UI.Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16)));
    public static void Configure(Window window, DesktopToolsController tools, string title, int width, int height, bool topmost = false)
    {
        window.Title = "ClariBar · " + L.T(title); window.AppWindow.Resize(new SizeInt32(width, height));
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = tools.Theme; root.Language = L.LanguageCode;
            void Surface()
            {
                var fill = window is PinnedImageWindow or PinEditWindow or AnnotationToolbarWindow?new SolidColorBrush(Microsoft.UI.Colors.Transparent):Brush(root.ActualTheme == ElementTheme.Dark ? "#202020" : "#F4F4F4");
                if (root is Panel panel) panel.Background = fill; else if (root is Control control) control.Background = fill;
            }
            root.ActualThemeChanged += (_, _) => Surface(); root.Loaded += (_, _) => Surface(); Surface();
        }
        if (window.AppWindow.Presenter is OverlappedPresenter presenter) { presenter.IsAlwaysOnTop = topmost; presenter.PreferredMinimumWidth = 320; presenter.PreferredMinimumHeight = 240; }
    }
    public static void Translate(DependencyObject node)
    {
        if (node is FrameworkElement { ContextFlyout: MenuFlyout menu })
            foreach (var item in menu.Items.OfType<MenuFlyoutItem>()) item.Text = L.T(item.Text);
        if (node is CommandBar bar)
            foreach (var item in bar.PrimaryCommands.Concat(bar.SecondaryCommands).OfType<AppBarButton>()) item.Label = L.T(item.Label);
        if (node is TextBox text)
        {
            if (text.Header is string header) text.Header = L.T(header);
            text.PlaceholderText = L.T(text.PlaceholderText); return;
        }
        if (node is TextBlock { Tag: "ClariBar.Translatable" } label) label.Text = L.T(label.Text);
        if (node is AppBarButton command) command.Label = L.T(command.Label);
        if (node is Button { Content: string value } button) button.Content = L.T(value);
        if (node is ComboBox { ItemsSource: string[] items } combo)
        {
            int selected = combo.SelectedIndex; string[] translated = items.Select(L.T).ToArray();
            if (!items.SequenceEqual(translated)) { combo.ItemsSource = translated; combo.SelectedIndex = selected; }
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Translate(VisualTreeHelper.GetChild(node, i));
    }
    public static async Task CopyTextAsync(string value) { var package = new DataPackage(); package.SetText(value); await ClipboardTransfer.SetAsync(package); }
    public static async Task ErrorAsync(Window window, Exception error)
    {
        var dialog = new ContentDialog { Title = L.T("Desktop tools"), Content = error.Message, CloseButtonText = L.T("Close"), XamlRoot = (window.Content as FrameworkElement)?.XamlRoot };
        try { await dialog.ShowAsync(); } catch (InvalidOperationException) { }
    }
}
