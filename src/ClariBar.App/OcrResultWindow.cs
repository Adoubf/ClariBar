using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using L = ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed class OcrResultWindow : Window
{
    private readonly DesktopToolsController tools;
    private readonly PixelFrame frame;
    private readonly CancellationTokenSource cancellation = new();
    private readonly TextBox result = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, PlaceholderText = L.T("Recognized text appears here. You can edit it before copying."), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock status = ToolUi.Label("");
    private readonly ProgressRing progress = new() { Width = 22, Height = 22, Visibility = Visibility.Collapsed };
    private readonly ComboBox languages;
    private bool running, closed,initialStarted;
    internal Task InitialRecognition {get;private set;}=Task.CompletedTask;
    public OcrResultWindow(DesktopToolsController tools, PixelFrame frame)
    {
        this.tools = tools; this.frame = frame;
        var root = new Grid { Padding = new(20), RowSpacing = 12, Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(ToolUi.Label(L.T("Extract text"), 24));
        string[] available = ["Auto", .. OcrService.Languages];
        languages = new ComboBox { ItemsSource = available.Select(x => x == "Auto" ? L.T("Automatic language") : x).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(available, tools.Settings.OcrLanguage)), MinWidth = 160 };
        languages.SelectionChanged += (_, _) => { tools.Settings.OcrLanguage = available[Math.Max(0, languages.SelectedIndex)]; tools.Save(); };
        var toolbar = new CommandBar { DefaultLabelPosition = CommandBarDefaultLabelPosition.Right };
        toolbar.Content = languages;
        void Command(string label, Microsoft.UI.Xaml.Controls.Symbol icon, Func<Task> action)
        {
            var button = new AppBarButton { Label = L.T(label), Icon = new SymbolIcon(icon), IsCompact = true };
            button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { if (!closed) status.Text = ex.Message; } };
            toolbar.PrimaryCommands.Add(button);
        }
        Command("Recognize locally", Symbol.Play, () => RecognizeAsync(false));
        Command("Recognize with AI", Symbol.World, ConfirmAiAsync);
        Command("Copy text", Symbol.Copy, async () => { await ToolUi.CopyTextAsync(result.Text); status.Text = L.T("Copied to clipboard"); });
        Command("Save text", Symbol.Save, async () =>
        {
            Directory.CreateDirectory(tools.ScreenshotDirectory); string path = Path.Combine(tools.ScreenshotDirectory, $"ClariBar-OCR-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            await File.WriteAllTextAsync(path, result.Text); status.Text = L.F("Saved: {0}", path);
        });
        Grid.SetRow(toolbar, 1); root.Children.Add(toolbar); Grid.SetRow(result, 2); root.Children.Add(result);
        var bottom = new Grid { ColumnSpacing = 10 }; bottom.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); bottom.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        bottom.Children.Add(progress); Grid.SetColumn(status, 1); bottom.Children.Add(status); Grid.SetRow(bottom, 3); root.Children.Add(bottom);
        Content = root; ToolUi.Configure(this, tools, "OCR", 800, 650);
        root.Loaded += async (_, _) =>
        {
            if(initialStarted)return;initialStarted=true;
            InitialRecognition=RecognizeAsync(tools.Settings.OcrDefaultEngine=="Ai");await InitialRecognition;
        };
        Closed += (_, _) => { closed = true; cancellation.Cancel(); };
    }
    private async Task ConfirmAiAsync()
    {
        if (running || closed) return;
        if(tools.Settings.OcrDefaultEngine=="Ai"){await RecognizeAsync(true);return;}
        if (string.IsNullOrWhiteSpace(tools.Settings.AiOcrEndpoint) || string.IsNullOrWhiteSpace(tools.Settings.AiOcrModel)) { status.Text = L.T("Configure an AI endpoint and model on Desktop tools first."); return; }
        Uri endpoint;
        try { endpoint = ClariBar.Core.AiOcrProtocol.Resolve(ClariBar.Core.AiOcrOptions.From(tools.Settings)).Endpoint; } catch (Exception ex) { status.Text = ex.Message; return; }
        var dialog = new ContentDialog { Title = L.T("Recognize with AI"), Content = L.F("Send this selected image to {0} using model {1}?", endpoint.Host, tools.Settings.AiOcrModel), PrimaryButtonText = L.T("Send selected image"), CloseButtonText = L.T("Cancel"), DefaultButton = ContentDialogButton.Close, XamlRoot = ((FrameworkElement)Content).XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await RecognizeAsync(true);
    }
    private async Task RecognizeAsync(bool useAi)
    {
        if (running || closed) return;
        if(useAi && (string.IsNullOrWhiteSpace(tools.Settings.AiOcrEndpoint) || string.IsNullOrWhiteSpace(tools.Settings.AiOcrModel)))
        {status.Text=L.T("Configure an AI endpoint and model on Desktop tools first.");return;}
        running = true; progress.IsActive = true; progress.Visibility = Visibility.Visible; languages.IsEnabled = false;
        status.Text = L.T(useAi ? "Recognizing with AI…" : "Recognizing locally…");
        try
        {
            if (useAi) { result.Text = await OcrService.AiAsync(frame, ClariBar.Core.AiOcrOptions.From(tools.Settings), OcrCredential.Read(), cancellation.Token); status.Text = L.T("AI OCR complete"); }
            else { var output = await OcrService.LocalAsync(frame, tools.Settings.OcrLanguage, cancellation.Token); result.Text = output.Text; status.Text = L.F("Local OCR · {0}", output.Language); }
            if (string.IsNullOrWhiteSpace(result.Text)) status.Text = L.T("No text was found. Try a larger region or another language.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed) status.Text = ex.Message; }
        finally { running = false; if (!closed) { progress.IsActive = false; progress.Visibility = Visibility.Collapsed; languages.IsEnabled = true; } }
    }
}
