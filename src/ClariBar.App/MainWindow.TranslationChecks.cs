using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassName(nint window,StringBuilder name,int capacity);
    private async Task CheckTranslationAsync(Action<bool,string> require)
    {
        var baseline=JsonSerializer.Serialize(settings,Wire.Json);Window? sampleWindow=null;
        static void Invoke(Button button)=>((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
        try
        {
            require(settings.TranslationEngine=="Free" && !settings.OfflineTranslationEnabled && !Directory.Exists(TranslationService.OfflineDirectory),"Translation starts with the free API and never installs offline models on launch");
            NavigateTo("tools");await Task.Delay(100);
            require(translationEngine is {Items.Count:3,SelectedIndex:0} && translationDisplay is {Items.Count:2,SelectedIndex:0},"Desktop tools exposes three engines and two display modes with free/bilingual defaults");
            require(translationTarget is {SelectedIndex:0} && TranslationLanguages.Read(translationTarget,false)=="Auto" && translationTarget.Text==L.T("Automatic (Chinese ↔ English)"),"Target language defaults to the localized automatic Chinese-English option in settings");
            translationDisplay!.SelectedIndex=1;require(store.Load().TranslationDisplay=="Overlay","Changing the translation display persists immediately");translationDisplay.SelectedIndex=0;
            translationEngine!.SelectedIndex=1;require(store.Load().TranslationEngine=="Local" && !Directory.Exists(TranslationService.OfflineDirectory),"Selecting the offline engine alone does not download models; the separate opt-in is required");translationEngine.SelectedIndex=0;
            translationSource!.SelectedIndex=1;translationTarget!.SelectedIndex=1;
            require(store.Load().TranslationSourceLanguage=="en" && store.Load().TranslationTargetLanguage=="en","Choosing source and target languages commits immediately before using the global capture shortcut");translationSource.SelectedIndex=0;translationTarget.SelectedIndex=0;
            require(store.Load().TranslationTargetLanguage=="Auto","Returning to automatic translation persists immediately instead of remembering the previous fixed target");
            using(var handler=new FreeTranslationCheckHandler())
            {
                string result=await TranslationService.FreeAsync(string.Concat(Enumerable.Repeat("Hello 你好 🌏 ",80)),"en","zh",handler:handler);
                require(handler.Requests>1 && handler.Valid && result.Contains("翻译"),"The free transport splits long UTF-8 text into valid no-key requests and decodes provider output");
            }
            using(var handler=new FreeTranslationCheckHandler(quota:true))
            {
                bool failed=false;try{await TranslationService.FreeAsync("Hello","en","zh",handler:handler);}catch(IOException ex){failed=ex.Message.Contains(L.IsChinese?"额度":"quota");}
                require(failed,"Quota exhaustion is displayed as a provider error rather than a translation");
            }
            if(Environment.GetCommandLineArgs().Contains("--translation-live-check"))
            {
                string online=await TranslationService.FreeAsync("Hello world.","en","zh");
                require(online.Any(character=>character is >= '\u3400' and <= '\u9fff'),"The real free API translates the built-in English sample to Chinese without a key");
                online=await TranslationService.FreeAsync("你好，世界。","zh","en");
                require(online.Any(char.IsAsciiLetter),"The real free API translates the built-in Chinese sample to English without a key");
            }
            using(var handler=new AutomaticTranslationCheckHandler())
            {
                string[] english=await TranslationService.TranslateLinesAsync(["Hello world."],"Auto","Auto","Free",settings,handler:handler);
                string[] chinese=await TranslationService.TranslateLinesAsync(["你好，世界。"],"Auto","Auto","Free",settings,handler:handler);
                require(english[0]=="你好，世界。" && chinese[0]=="Hello world." && handler.Pairs.SequenceEqual(new[]{"en|zh-CN","zh-CN|en"}),"Consecutive free translations reverse direction from the recognized text without changing target settings");
            }
            bool unavailable=false;try{await TranslationService.TranslateLinesAsync(["Hello"],"en","zh","Local",settings);}catch(InvalidOperationException){unavailable=true;}
            require(unavailable && !Directory.Exists(TranslationService.OfflineDirectory),"A missing optional model produces guidance without downloading or silently switching engines");
            foreach(bool tls in new[]{false,true})foreach(string api in AiOcrProtocol.ApiTypes.Skip(1))
            {
                await using var server=new AiOcrCheckServer(AiTestResponse(api,"[\"你好\",\"世界\"]"),tls:tls);
                using var handler=tls?server.TrustedTestHandler():null;
                settings.AiOcrEndpoint=server.Address;settings.AiOcrApiType=api;settings.AiTranslationModel="text-only-model";
                string[] output=await TranslationService.TranslateLinesAsync(["Hello","world"],"en","zh","Ai",settings,handler:handler);
                var request=await server.Request.WaitAsync(TimeSpan.FromSeconds(5));
                require(output.SequenceEqual(new[]{"你好","世界"}) && !request.Body.Contains("image_url") && !request.Body.Contains("inlineData") && !request.Body.Contains("input_image"),(tls?"HTTPS":"HTTP")+" / "+api+": translation uses the shared transport with text-only input and preserves block order");
            }
            settings.AiOcrEndpoint="http://127.0.0.1:8000/v1";settings.AiOcrApiType=AiOcrProtocol.Chat;settings.AiTranslationModel="hy-mt2-7b";
            using(var handler=new TranslationCompatibilityHandler(prompt=>prompt.Contains("into English.",StringComparison.Ordinal)?"Hello world.":"你好，世界。"))
            {
                string[] chinese=await TranslationService.TranslateLinesAsync(["你好，世界。"],"Auto","Auto","Ai",settings,handler:handler,key:"");
                string[] english=await TranslationService.TranslateLinesAsync(["Hello world."],"Auto","Auto","Ai",settings,handler:handler,key:"");
                require(chinese[0]=="Hello world." && english[0]=="你好，世界。" && handler.PlainRequests==2,"HY-MT receives a real target language for both directions without a remembered target or an Auto prompt");
            }
            using(var handler=new TranslationCompatibilityHandler(prompt=>prompt.EndsWith("Hello",StringComparison.Ordinal)?"你好":"世界"))
            {
                string[] output=await TranslationService.TranslateLinesAsync(["Hello","world"],"en","zh","Ai",settings,handler:handler,key:"");
                require(output.SequenceEqual(new[]{"你好","世界"}) && handler.PlainRequests==2 && handler.BatchRequests==0 && !handler.Disposed,"HY-MT uses one plain request per paragraph and keeps result positions stable across concurrent replies");
            }
            settings.AiTranslationModel="generic-model";
            using(var handler=new TranslationCompatibilityHandler(prompt=>prompt.Contains("Input JSON:",StringComparison.Ordinal)?"[\"merged paragraphs\"]":prompt.EndsWith("Hello",StringComparison.Ordinal)?"你好":"世界"))
            {
                string[] output=await TranslationService.TranslateLinesAsync(["Hello","world"],"en","zh","Ai",settings,handler:handler,key:"");
                require(output.SequenceEqual(new[]{"你好","世界"}) && handler.BatchRequests==1 && handler.PlainRequests==2,"A general model that merges JSON blocks is retried by paragraph without shifting translations onto the wrong source rectangles");
            }
            settings.AiOcrEndpoint="http://127.0.0.1:8000/v1";settings.AiOcrApiType=AiOcrProtocol.Chat;settings.AiOcrModel="existing-vision";settings.AiTranslationModel="";Save();RenderPage();
            var panel=PageContent.Children.OfType<AiOcrSettingsPanel>().Single();panel.Expander.IsExpanded=true;await Task.Delay(80);
            require(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(panel.Expander)==L.T("AI configuration"),"AI configuration is one shared section with a semantic icon");
            await using(var server=new AiOcrCheckServer(AiTestResponse(AiOcrProtocol.Chat,"[\"你好，世界。\"]")))
            {
                panel.Endpoint.Text=server.Address;panel.TranslationModel.Text="translation-model";panel.TestPurpose.SelectedIndex=1;Invoke(panel.TestButton);await panel.LastTest;
                require(panel.Status.Severity==InfoBarSeverity.Success && panel.TestOutput.Text=="你好，世界。","The shared AI configuration tests the translation model without saving or sending screenshot data");
                Invoke(panel.SaveButton);var saved=store.Load();
                require(saved.AiOcrModel=="existing-vision" && saved.AiTranslationModel=="translation-model" && saved.AiOcrEndpoint==server.Address,"Adding a translation model retains the existing OCR model and reuses the connection");
            }
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-shared-ai-configuration.png"),await (await RenderOwnElementAsync(panel)).PngAsync());
            // Verify archive extraction independently, using tiny synthetic archives.
            string zip=Path.Combine(AppPaths.Logs,"translation-model-test.zip"),folder=Path.Combine(AppPaths.Logs,"translation-extracted");
            using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){var entry=archive.CreateEntry("model-root/model/model.bin");using var writer=new StreamWriter(entry.Open());writer.Write("synthetic test model");}
            OfflineTranslationInstaller.Extract(zip,folder,true,CancellationToken.None);
            require(File.Exists(Path.Combine(folder,"model","model.bin")),"Optional model archives strip their package root into the expected inference layout");
            using(var archive=ZipFile.Open(zip+".bad",ZipArchiveMode.Create)){var entry=archive.CreateEntry("model-root/../../escape.bin");using var writer=new StreamWriter(entry.Open());writer.Write("test");}
            bool rejected=false;try{OfflineTranslationInstaller.Extract(zip+".bad",folder,true,CancellationToken.None);}catch(IOException){rejected=true;}
            require(rejected && !File.Exists(Path.Combine(AppPaths.Logs,"escape.bin")),"Optional model extraction rejects directory traversal");
            await CheckTranslationCaptureFlowAsync(require);
            require(!Directory.Exists(TranslationService.OfflineDirectory) && !settings.OfflineTranslationEnabled,"All translation UI and transport checks finish without opting into a local model download");
        }
        finally
        {
            sampleWindow?.Close();foreach(var window in InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<TranslationWindow>().ToArray())window.Close();
            var original=JsonSerializer.Deserialize<AppSettings>(baseline,Wire.Json)!;foreach(var property in typeof(AppSettings).GetProperties().Where(property=>property.CanWrite))property.SetValue(settings,property.GetValue(original));Save();ShowSettings();
        }
    }
    private async Task CheckTranslationCaptureFlowAsync(Action<bool,string> require)
    {
        var reference=new Window{Content=new Grid{Background=ToolUi.Brush("#FFFFFF"),Children={new TextBlock{Text="Hello world.",FontSize=32,Foreground=ToolUi.Brush("#101010"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}}}};
        try
        {
            // Keep the owned sample visible even when Windows denies foreground
            // activation to a test launched from a background terminal.
            ToolUi.Configure(reference,desktopTools,"Translation sample",560,210,true);reference.AppWindow.Move(new(220,180));reference.Activate();await Task.Delay(300);
            var sample=ToolWindowGeometry.ClientBounds(reference);require(sample.Width>200,"The translation reference window has real physical client bounds");
            settings.TranslationEngine="Ai";settings.TranslationDisplay="Bilingual";settings.TranslationSourceLanguage="en";settings.TranslationTargetLanguage="Auto";settings.AiOcrApiType=AiOcrProtocol.Chat;settings.AiTranslationModel="translation-model";settings.CaptureCancelOnDeactivate=false;
            await using var server=new AiOcrCheckServer(AiTestResponse(AiOcrProtocol.Chat,"[\"你好，世界。\"]"));settings.AiOcrEndpoint=server.Address;
            nint host=0;DesktopCapture.EnumWindows((window,_)=>{DesktopWindowState.Process(window,out uint process);var name=new StringBuilder(120);GetClassName(window,name,name.Capacity);if(process==Environment.ProcessId && name.ToString()=="ClariBar.Host")host=window;return true;},0);
            require(host!=0 && Native.CB_SetToolHotkey(7,3,0x41)==0,"The native eighth shortcut slot registers Ctrl+Alt+A independently");
            Native.PostMessage(host,0x0312,17,0); // Exercise the actual WM_HOTKEY → command 22 route.
            CaptureOverlay? overlay=null;
            for(int attempt=0;attempt<100;attempt++){overlay=InteractionField<CaptureOverlay?>(desktopTools,"overlay");if(overlay is not null)break;await Task.Delay(30);}
            require(overlay is not null && !DesktopCapture.IsWindowVisible(hwnd),"Ctrl+Alt+A reaches capture selection and hides the settings window");
            await Task.Delay(150);var desktop=DesktopCapture.DesktopBounds;var area=sample with{X=sample.X-desktop.X,Y=sample.Y-desktop.Y};
            Native.ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(reference),0);
            typeof(CaptureOverlay).GetMethod("SetArea",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(overlay,[area,false,null]);
            await (Task)typeof(CaptureOverlay).GetMethod("OutputAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(overlay,["OCR"])!;
            TranslationWindow? result=null;
            for(int attempt=0;attempt<100;attempt++){result=InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<TranslationWindow>().SingleOrDefault();if(result is not null && InteractionField<bool>(result,"started"))break;await Task.Delay(30);}
            require(result is not null,"Completing the selection opens the translation result through the shared desktop tool controller");
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,"translation-captured-source.png"),await InteractionField<PixelFrame>(result!,"frame").PngAsync());
            await result!.InitialTranslation.WaitAsync(TimeSpan.FromSeconds(20));
            await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"translation-flow-result.json"),JsonSerializer.Serialize(new{document=result.Result,status=InteractionField<TextBlock>(result,"status").Text},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
            require(result.Result?.SourceText.Contains("Hello",StringComparison.OrdinalIgnoreCase)==true && result.Result.TargetText=="你好，世界。","A real captured region completes Windows OCR and translates its recognized text");
            require(!DesktopCapture.IsWindowVisible(hwnd),"A successful translation opens only its result and leaves settings hidden");
            var document=result.Result!;require(document.Lines.All(line=>line.Bounds.X>=0 && line.Bounds.Y>=0 && line.Bounds.Right<=sample.Width && line.Bounds.Bottom<=sample.Height),"Local OCR returns text positions mapped back to the captured image pixels");
            require(TranslationLanguages.Read(InteractionField<ComboBox>(result,"source"),true)=="en" && TranslationLanguages.Read(InteractionField<ComboBox>(result,"target"),false)=="Auto" && document.TargetLanguage=="zh" && settings.TranslationTargetLanguage=="Auto","The bilingual selector stays automatic while its result records the actual detected translation direction");
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-translation-bilingual.png"),await (await RenderOwnElementAsync((FrameworkElement)result.Content)).PngAsync());
            result.AppWindow.Resize(new(620,720));await Task.Delay(120);
            var comparison=InteractionField<Grid>(result,"comparison");require(Grid.GetRow(InteractionField<TextBox>(result,"translated"))==1 && comparison.ColumnDefinitions[1].Width.Value==0,"A narrow bilingual window stacks source and translation instead of squeezing the two columns");
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-translation-bilingual-narrow.png"),await (await RenderOwnElementAsync((FrameworkElement)result.Content)).PngAsync());
            typeof(TranslationWindow).GetMethod("SwitchMode",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(result,null);await Task.Delay(200);
            var replacement=InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<TranslationWindow>().Single();
            require(replacement.IsOverlay && replacement.Result==document && ToolWindowGeometry.ClientBounds(replacement)==sample,"Switching to overlay reuses the result and covers the exact physical selection without a second AI call");
            require(replacement.TranslationMasks.Children.Count==document.Lines.Length,"The overlay places a translated mask over each recognized text block");
            var bar=InteractionField<TranslationToolbarWindow>(replacement,"toolbar");var barBounds=ToolWindowGeometry.ClientBounds(bar);
            require(barBounds.Y>=sample.Bottom && barBounds.Y-sample.Bottom<=10,"The overlay toolbar floats directly below the captured region");
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-translation-overlay.png"),await DesktopCapture.Capture(new(sample.X,sample.Y,Math.Max(sample.Width,barBounds.Width),sample.Height+barBounds.Height+10)).PngAsync());
            string previousTheme=settings.Theme;settings.Theme="Dark";ApplyTheme();await Task.Delay(120);
            require(bar.Content is Border {ActualTheme:ElementTheme.Dark,Background:SolidColorBrush {Color.R:32}},"The translation overlay toolbar follows dark theme with a visible, matching surface");
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-translation-overlay-dark.png"),await DesktopCapture.Capture(new(sample.X,sample.Y,Math.Max(sample.Width,barBounds.Width),sample.Height+barBounds.Height+10)).PngAsync());settings.Theme=previousTheme;ApplyTheme();
            typeof(TranslationWindow).GetMethod("SwitchMode",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(replacement,null);await Task.Delay(120);
            var bilingual=InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<TranslationWindow>().Single();require(!bilingual.IsOverlay && bilingual.Result==document,"The overlay can return to bilingual comparison without repeating recognition or translation");
            var captured=InteractionField<PixelFrame>(bilingual,"frame");bilingual.Close();
            settings.TranslationDisplay="Overlay";settings.AiTranslationModel="";desktopTools.OpenTranslation(captured,sample);
            var missing=InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<TranslationWindow>().Single();
            for(int attempt=0;attempt<100 && !InteractionField<bool>(missing,"started");attempt++)await Task.Delay(30);await missing.InitialTranslation;
            require(missing.IsOverlay && missing.Result is null && InteractionField<TextBlock>(missing,"status").Text==L.T("Set a translation model ID in AI configuration first."),"Opening directly in overlay mode keeps missing-model guidance visible without a settings popup");
            typeof(TranslationWindow).GetMethod("SwitchMode",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(missing,null);await Task.Delay(150);
            var retry=InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<TranslationWindow>().Single();
            require(!retry.IsOverlay && retry.Result is null && InteractionField<ComboBox>(retry,"engines").IsEnabled && InteractionField<TextBlock>(retry,"status").Text==L.T("Set a translation model ID in AI configuration first."),"An overlay error can switch to the bilingual window to choose another engine without automatically retrying a failed request");
            require(InteractionField<TextBox>(retry,"original").Text.Contains("Hello",StringComparison.OrdinalIgnoreCase),"Recognized source text survives an AI error and switching from the overlay to the bilingual window");retry.Close();
        }
        finally{Native.CB_SetToolHotkey(7,0,0);reference.Close();}
    }
}

internal sealed class TranslationCompatibilityHandler(Func<string,string> translate):HttpMessageHandler
{
    private int plain,batch;
    internal int PlainRequests=>plain;
    internal int BatchRequests=>batch;
    internal bool Disposed {get;private set;}
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(Disposed,this);
        using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));string prompt=json.RootElement.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("text").GetString()!;
        if(prompt.Contains("Input JSON:",StringComparison.Ordinal))Interlocked.Increment(ref batch);else Interlocked.Increment(ref plain);
        if(prompt.EndsWith("Hello",StringComparison.Ordinal))await Task.Delay(30,cancellation);
        string body=JsonSerializer.Serialize(new{choices=new[]{new{message=new{content=translate(prompt)}}}});
        return new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};
    }
    protected override void Dispose(bool disposing){Disposed=true;base.Dispose(disposing);}
}

internal sealed class FreeTranslationCheckHandler(bool quota=false):HttpMessageHandler
{
    internal int Requests {get;private set;}
    internal bool Valid {get;private set;}=true;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
    {
        Requests++;var query=request.RequestUri!.Query.TrimStart('?').Split('&').Select(item=>item.Split('=',2)).ToDictionary(pair=>pair[0],pair=>Uri.UnescapeDataString(pair[1]));
        Valid&=request.RequestUri.Host=="api.mymemory.translated.net" && request.Headers.Authorization is null && query["langpair"]=="en|zh-CN" && Encoding.UTF8.GetByteCount(query["q"])<=500;
        string body=quota?"{\"responseStatus\":429,\"quotaFinished\":true}":"{\"responseStatus\":200,\"quotaFinished\":false,\"responseData\":{\"translatedText\":\"翻译 &amp; 测试\"}}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")});
    }
}

internal sealed class AutomaticTranslationCheckHandler:HttpMessageHandler
{
    internal List<string> Pairs {get;}=[];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
    {
        var query=request.RequestUri!.Query.TrimStart('?').Split('&').Select(item=>item.Split('=',2)).ToDictionary(pair=>pair[0],pair=>Uri.UnescapeDataString(pair[1]));
        string pair=query["langpair"];Pairs.Add(pair);
        string text=pair=="en|zh-CN"?"你好，世界。":pair=="zh-CN|en"?"Hello world.":throw new IOException("Unexpected automatic language pair: "+pair);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{responseStatus=200,responseData=new{translatedText=text}}),Encoding.UTF8,"application/json")});
    }
}
