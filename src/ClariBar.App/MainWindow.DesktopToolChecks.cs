using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using L = ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    [DllImport("ole32.dll")] private static extern int OleGetClipboard(out nint data);
    [DllImport("ole32.dll")] private static extern int OleSetClipboard(nint data);
    [DllImport("ole32.dll")] private static extern int OleFlushClipboard();
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(nint window, out uint key, out byte alpha, out uint flags);
    private async Task CheckDesktopToolsAsync()
    {
        var checks = new List<object>(); string? failure = null; ClipboardSnapshot? clipboard = null;
        void Require(bool condition, string name)
        {
            checks.Add(new { name, passed = condition });
            Directory.CreateDirectory(AppPaths.Logs);
            File.WriteAllText(Path.Combine(AppPaths.Logs,"desktop-tools-progress.json"),JsonSerializer.Serialize(new {last=name,passed=condition,checks=checks.Count},Wire.Json));
            if (!condition) throw new IOException(name);
        }
        try
        {
            string? expectedRoot = Environment.GetEnvironmentVariable("CLARIBAR_DESKTOP_CHECK_ROOT");
            if (expectedRoot is null || !string.Equals(Path.GetFullPath(expectedRoot).TrimEnd('\\'), AppPaths.Cache.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Desktop tool checks require an explicitly isolated CLARIBAR_DESKTOP_CHECK_ROOT matching the runtime root.");
            await Task.Delay(900); Directory.CreateDirectory(AppPaths.Logs);
            if(Environment.GetCommandLineArgs().Contains("--caption-hover-check")){await CheckCaptionHoverAsync(Require);return;}
            if(Environment.GetCommandLineArgs().Contains("--configured-translation-check")){await CheckConfiguredTranslationAsync(Require);return;}
            if(Environment.GetCommandLineArgs().Contains("--translation-check")){await CheckTranslationAsync(Require);return;}
            if(Environment.GetCommandLineArgs().Contains("--ai-ocr-check")){await CheckAiOcrProtocolsAsync(Require);await CheckAiOcrSettingsAsync(Require);await CheckDefaultOcrEngineAsync(Require);await ExportSettingsIconAuditAsync();return;}
            if(Environment.GetCommandLineArgs().Contains("--pin-editor-check")){await CheckPinnedEditorAsync(Require);return;}
            if(Environment.GetCommandLineArgs().Contains("--interaction-check")){await CheckAnnotationGesturesAsync(Require);await CheckLiveColorPickerAsync(Require);await CheckColorPickerWindowFlowAsync(Require);return;}
            NavigateTo("tools"); await Task.Delay(350);
            await RenderCheckImageAsync(L.LanguageCode + "-tools");
            Require(PageContent.Children.Count > 12, "Desktop tools page is populated");
            HelpButton.Flyout.ShowAt(HelpButton); await Task.Delay(300);
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).First(p => p.IsOpen && p.Child is MenuFlyoutPresenter);
            var presenter = (FrameworkElement)popup.Child;
            var buttonAt = HelpButton.TransformToVisual(Root).TransformPoint(new());
            var popupAt = presenter.TransformToVisual(Root).TransformPoint(new());
            Require(popupAt.Y >= buttonAt.Y + HelpButton.ActualHeight - 2, $"Help opens below its button ({popupAt.Y:0.0} >= {buttonAt.Y + HelpButton.ActualHeight:0.0})");
            Require(HelpButton.Content?.ToString() == (L.IsChinese ? "帮助" : "Help"), "Help uses the concise localized label");
            HelpButton.Flyout.Hide();

            var white = new byte[960 * 540 * 4]; Array.Fill(white, (byte)255);
            var sample = new PixelFrame(960, 540, white).Render([
                new("Text", [new(40, 40), new(41, 41)], "#202020", 6, "ClariBar OCR 2026"),
                new("Text", [new(40, 105), new(41, 106)], "#202020", 5, "Hello Windows 11"),
                new("Rectangle", [new(40, 210), new(310, 420)], "#50BED2", 5),
                new("Ellipse", [new(360, 210), new(630, 420)], "#FF8855", 5),
                new("Arrow", [new(700, 400), new(880, 230)], "#9060DD", 6)
            ]);
            var overlay = new CaptureOverlay(desktopTools, sample, DesktopCapture.DesktopBounds, [], false);
            desktopTools.Track(overlay); var selection = overlay.SelectAsync(); await Task.Delay(350);
            var overlayRoot = (Grid)((ContentControl)overlay.Content).Content;
            var mapped = (Point)typeof(CaptureOverlay).GetMethod("Pixel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [new Point(overlayRoot.ActualWidth * .75, overlayRoot.ActualHeight * .5)])!;
            Require(Math.Abs(mapped.X - 720) < 1 && Math.Abs(mapped.Y - 270) < 1, "Selection maps actual virtual-desktop window coordinates to source pixels");
            Require(overlay.AppWindow.Size.Width == DesktopCapture.DesktopBounds.Width && overlay.AppWindow.Size.Height == DesktopCapture.DesktopBounds.Height, "Capture overlay covers the complete physical virtual desktop");
            overlay.Close(); Require(await selection is null, "Closing an overlay cancels selection without producing an image");
            var crop = sample.Crop(new(-10, -10, 30, 30)); Require(crop.Width == 20 && crop.Height == 20 && crop.ColorAt(0, 0) == new ScreenColor(255, 255, 255), "Native frame cropping clips safely");
            string imagePath = Path.Combine(AppPaths.Logs, "desktop-tools-synthetic.png"); await File.WriteAllBytesAsync(imagePath, await sample.PngAsync());
            Require(sample.Render([]).Pixels.SequenceEqual(sample.Pixels), "Empty annotation replay preserves every pixel");
            var grid = new byte[120 * 120 * 4];
            for (int y = 0; y < 120; y++) for (int x = 0; x < 120; x++) { int i = (y * 120 + x) * 4; grid[i] = (byte)(x * 2); grid[i + 1] = (byte)(y * 2); grid[i + 2] = 180; grid[i + 3] = 255; }
            var mosaic = new PixelFrame(120, 120, grid).Render([new("Mosaic", [new(12, 12), new(108, 108)], "#000000", 3)]);
            Require(mosaic.ColorAt(12, 12) == mosaic.ColorAt(23, 23) && mosaic.ColorAt(0, 0) == new ScreenColor(180, 0, 0), "Mosaic changes only the selected region");
            clipboard=ClipboardSnapshot.Capture(hwnd);
            var pasted=await CheckClipboardRoundTripAsync(sample,checks);
            Require(pasted is not null && pasted.Width == sample.Width && pasted.Height == sample.Height && pasted.Pixels.SequenceEqual(sample.Pixels), "PNG clipboard round trip preserves dimensions and pixels");
            string? saved = await desktopTools.SaveImageAsync(sample, this); Require(saved is not null && File.Exists(saved) && new FileInfo(saved).Length > 100, "Screenshot saves as a real PNG in the configured directory");

            var editor = new CaptureEditorWindow(desktopTools, sample); desktopTools.Track(editor); editor.Activate(); await Task.Delay(400);
            await RenderCheckImageAsync(L.LanguageCode + "-capture-editor", (FrameworkElement)editor.Content);
            editor.Document.Add(new("Pen", [new(20, 480), new(300, 475), new(700, 490)], "#FF0000", 4));
            byte[] annotated = editor.Document.Render().Pixels;
            Require(!annotated.SequenceEqual(sample.Pixels), "Editor applies a real freehand annotation");
            editor.Document.Undo(); Require(editor.Document.Render().Pixels.SequenceEqual(sample.Pixels), "Editor undo restores the original image");
            editor.Document.Redo(); Require(editor.Document.Render().Pixels.SequenceEqual(annotated), "Editor redo restores the annotated image");
            editor.Close();

            var colorEditor = new ColorEditorWindow(desktopTools, new(80, 190, 210)); desktopTools.Track(colorEditor); colorEditor.Activate(); await Task.Delay(350);
            var colorRoot = (FrameworkElement)colorEditor.Content;
            await RenderCheckImageAsync(L.LanguageCode + "-color-editor", colorRoot);
            string locale = settings.Language, theme = settings.Theme;
            settings.Theme = "Dark"; ApplyTheme(); await Task.Delay(200);
            Require(colorRoot.ActualTheme == ElementTheme.Dark && ((ScrollViewer)colorRoot).Background is SolidColorBrush { Color.R: 32 }, "Open color editors follow dark theme with a visible surface");
            await RenderCheckImageAsync(L.LanguageCode + "-color-editor-dark", colorRoot);
            ChangeLanguage(L.IsChinese ? "en-US" : "zh-CN"); await Task.Delay(100);
            Require(colorEditor.Title.EndsWith(L.T("Color editor")), "Open tool windows update their localized title");
            ChangeLanguage(locale); settings.Theme = theme; ApplyTheme(); colorEditor.Close();
            var pin = new PinnedImageWindow(desktopTools, sample); desktopTools.Track(pin); pin.Activate(); await Task.Delay(350);
            var before = pin.AppWindow.Size;
            typeof(PinnedImageWindow).GetMethod("SetZoom", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pin, [.5]);
            Require(pin.AppWindow.Size.Width == 480 && pin.AppWindow.Size.Height == 270 && before.Width > 0, "Pinned image zoom resizes the physical image");
            var detected = DesktopCapture.Windows(DesktopCapture.DesktopBounds);
            nint pinHandle = WinRT.Interop.WindowNative.GetWindowHandle(pin);
            typeof(PinnedImageWindow).GetMethod("SetOpacity", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pin, [50]);
            Require(GetLayeredWindowAttributes(pinHandle, out _, out byte alpha, out _) && alpha == 127, "Pinned-image opacity is applied to the native window");
            Require(detected.Any(window => window.Handle == pinHandle), "Automatic window detection finds a visible pinned window");
            Native.ShowWindow(pinHandle, 0); Require(!DesktopCapture.Windows(DesktopCapture.DesktopBounds).Any(window => window.Handle == pinHandle), "Automatic window detection ignores a hidden window"); pin.Close();

            var ocr = await OcrService.LocalAsync(sample, "Auto");
            Require(ocr.Text.Contains("2026") && ocr.Text.Contains("Windows", StringComparison.OrdinalIgnoreCase), "Installed Windows OCR recognizes controlled English text");
            checks.Add(new { name = "local-ocr", language = ocr.Language, text = ocr.Text, installedLanguages = OcrService.Languages });
            var ocrWindow = new OcrResultWindow(desktopTools, sample); desktopTools.Track(ocrWindow); ocrWindow.Activate(); await Task.Delay(1100);
            await RenderCheckImageAsync(L.LanguageCode + "-ocr", (FrameworkElement)ocrWindow.Content); ocrWindow.Close();
            string transient = "claribar-test-" + Guid.NewGuid().ToString("N"); OcrCredential.Save(transient);
            Require(OcrCredential.Read() == transient && !Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(AppPaths.Data, "ocr-credential.bin"))).Contains(transient), "AI API key is round-tripped through current-user DPAPI"); OcrCredential.Clear();
            await CheckAiOcrAsync(sample, Require);
            try
            {
                Require(RegisterHotKey(hwnd, 302, 3, 0x87), "Test hotkey reservation succeeds");
                Require(Native.CB_SetToolHotkey(0, 3, 0x87) == 1409, "Native global hotkeys report an existing shortcut collision");
                UnregisterHotKey(hwnd, 302);
                Require(Native.CB_SetToolHotkey(0, 3, 0x87) == 0, "Native global hotkeys can be registered after the conflict is removed");
            }
            finally { UnregisterHotKey(hwnd, 302); Native.CB_SetToolHotkey(0, 0, 0); }

            await CheckCaptionHoverAsync(Require);
            if(Environment.GetCommandLineArgs().Contains("--capture-ui-check")){await CheckCaptureRefinementsAsync(Require);await CheckColorPickerRefinementsAsync(Require);}
            if(Environment.GetCommandLineArgs().Contains("--snipaste-parity-check"))await CheckSnipasteParityAsync(Require);
            var desktop = DesktopCapture.DesktopBounds;
            checks.Add(new { name = "virtual-desktop", desktop });
        }
        catch (Exception ex) { failure = ex.ToString(); }
        finally
        {
            Directory.CreateDirectory(AppPaths.Logs);
            File.WriteAllText(Path.Combine(AppPaths.Logs, "claribar-desktop-tools-check.json"), JsonSerializer.Serialize(new { passed = failure is null, error = failure, checks }, new JsonSerializerOptions(Wire.Json) { WriteIndented = true }));
            clipboard?.Restore(hwnd);
            await QuitAsync();
        }
    }
    private static async Task<PixelFrame?> CheckClipboardRoundTripAsync(PixelFrame sample,List<object> checks)
    {
        for(int attempt=0;;attempt++)
        {
            await sample.CopyAsync();uint written=DesktopCapture.GetClipboardSequenceNumber();
            var pasted=await PixelFrame.FromClipboardAsync();var package=await ClipboardTransfer.ReadAsync();uint read=DesktopCapture.GetClipboardSequenceNumber();
            if(pasted is not null && pasted.Width==sample.Width && pasted.Height==sample.Height && pasted.Pixels.SequenceEqual(sample.Pixels))return pasted;
            checks.Add(new {name="clipboard-interference-diagnostic",attempt,written,read,width=pasted?.Width,height=pasted?.Height,formats=package.AvailableFormats.ToArray()});
            if(written==read || attempt>=2)return pasted;
            await Task.Delay(150);
        }
    }
    private static async Task CheckAiOcrAsync(PixelFrame image, Action<bool, string> require)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(); using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 8192, true);
            var headers = new List<string>(); string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync())) headers.Add(line);
            int length = int.Parse(headers.First(x => x.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1].Trim());
            char[] body = new char[length]; int offset = 0;
            while (offset < length) { int count = await reader.ReadAsync(body.AsMemory(offset)); if (count == 0) throw new IOException("Incomplete AI request"); offset += count; }
            using var document = JsonDocument.Parse(new string(body));
            var content = document.RootElement.GetProperty("messages")[0].GetProperty("content");
            bool selectedOnly = content.GetArrayLength() == 2 && content[1].GetProperty("image_url").GetProperty("url").GetString()!.StartsWith("data:image/png;base64,");
            bool model = document.RootElement.GetProperty("model").GetString() == "test-vision";
            bool auth = headers.Contains("Authorization: Bearer synthetic-test-key");
            byte[] response = Encoding.UTF8.GetBytes("{\"choices\":[{\"message\":{\"content\":\"ClariBar AI OCR test\"}}]}");
            byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {response.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head); await stream.WriteAsync(response); return selectedOnly && model && auth;
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        string text = await OcrService.AiAsync(image, $"http://127.0.0.1:{port}/v1/chat/completions", "test-vision", "synthetic-test-key", timeout.Token);
        require(text == "ClariBar AI OCR test" && await serving, "Optional AI OCR works against a local mock with only the selected image");
    }
}
