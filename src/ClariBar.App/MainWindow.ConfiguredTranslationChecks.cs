using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private async Task CheckConfiguredTranslationAsync(Action<bool,string> require)
    {
        string samplePath=Environment.GetEnvironmentVariable("CLARIBAR_TRANSLATION_SAMPLE")??throw new InvalidOperationException("An explicit selected image is required.");
        var input=(await ImageContent.FromFileAsync(samplePath)).Frame;var recognized=await OcrService.LayoutAsync(input,settings.OcrLanguage);
        require(TranslationProtocol.IsHunyuan(settings.AiTranslationModel) && recognized.Lines.Length>=10,"The live check uses the saved HY-MT model and the reported multiline screenshot, not a mock response");
        var paragraphs=TranslationProtocol.Paragraphs(recognized.Lines);
        require(paragraphs.Length<recognized.Lines.Length && paragraphs.Any(line=>line.Source.Contains("by your hardware.")),"Wrapped visual lines are reassembled into complete source paragraphs before translation");
        require(settings.TranslationTargetLanguage=="Auto","The existing Chinese target has migrated to automatic Chinese-English translation");
        settings.TranslationEngine="Ai";settings.TranslationDisplay="Bilingual";HideForTool();
        desktopTools.OpenTranslation(input,new(220,180,input.Width,input.Height));
        var window=InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<TranslationWindow>().Single();
        if(window.AppWindow.Presenter is OverlappedPresenter presenter)presenter.IsAlwaysOnTop=true;
        try
        {
            for(int attempt=0;attempt<500 && !InteractionField<bool>(window,"started");attempt++)await Task.Delay(20);
            for(int attempt=0;attempt<500 && InteractionField<TextBox>(window,"original").Text.Length==0 && !window.InitialTranslation.IsCompleted;attempt++)await Task.Delay(20);
            require(InteractionField<TextBox>(window,"original").Text.Contains("Features"),"Recognized source text is visible independently of AI response parsing");
            await window.InitialTranslation.WaitAsync(TimeSpan.FromMinutes(3));
            require(window.Result is {Lines.Length:>0} && window.Result.Lines.Length==paragraphs.Length,"The actual configured model completes one translation for every source paragraph");
            var document=window.Result!;
            require(document.SourceLanguage=="en" && document.TargetLanguage=="zh" && settings.TranslationTargetLanguage=="Auto","The real English screenshot translates into Chinese without saving a fixed target language");
            require(document.Lines.All(line=>line.Target.Any(character=>character is >= '\u3400' and <= '\u9fff')),"All returned blocks contain real Chinese translations rather than a JSON-format error");
            require(document.SourceText.Contains("bold text") && document.SourceText.Contains("clocks") && !document.SourceText.Contains("cl oc ks"),"Automatic OCR selects the installed English recognizer for Latin source text so split words do not distort the translation");
            require(!DesktopCapture.IsWindowVisible(hwnd),"The live translation result leaves the settings window hidden");
            await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"configured-translation-result.json"),JsonSerializer.Serialize(new{model=settings.AiTranslationModel,visualLines=recognized.Lines.Length,paragraphs=paragraphs.Length,document},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
            DesktopCapture.DwmFlush();
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-configured-translation-bilingual.png"),await DesktopCapture.Capture(ToolWindowGeometry.ClientBounds(window)).PngAsync());
            typeof(TranslationWindow).GetMethod("SwitchMode",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,null);await Task.Delay(200);
            window=InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<TranslationWindow>().Single();
            require(window.IsOverlay && window.Result==document && window.TranslationMasks.Children.Count==paragraphs.Length,"The actual result switches to the screenshot overlay with paragraph positions preserved and no extra AI request");
            var toolbar=InteractionField<TranslationToolbarWindow>(window,"toolbar");var imageBounds=ToolWindowGeometry.ClientBounds(window);var bar=ToolWindowGeometry.ClientBounds(toolbar);
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-configured-translation-overlay.png"),await DesktopCapture.Capture(new(imageBounds.X,imageBounds.Y,Math.Max(imageBounds.Width,bar.Width),imageBounds.Height+bar.Height+8)).PngAsync());window.Close();
            byte[] white=new byte[840*190*4];Array.Fill(white,(byte)255);
            var chineseImage=new PixelFrame(840,190,white).Render([
                new("Text",[new(30,30),new(31,31)],"#101010",6,"截图翻译会自动识别中文和英文。"){FontSize=30},
                new("Text",[new(30,100),new(31,101)],"#101010",6,"无需手动切换目标语言。"){FontSize=30}
            ]);
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,"automatic-chinese-source.png"),await chineseImage.PngAsync());
            settings.TranslationDisplay="Bilingual";desktopTools.OpenTranslation(chineseImage,new(220,180,840,190));
            window=InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<TranslationWindow>().Single();
            if(window.AppWindow.Presenter is OverlappedPresenter chinesePresenter)chinesePresenter.IsAlwaysOnTop=true;
            for(int attempt=0;attempt<500 && !InteractionField<bool>(window,"started");attempt++)await Task.Delay(20);
            await window.InitialTranslation.WaitAsync(TimeSpan.FromMinutes(3));
            var chineseDocument=window.Result;
            require(chineseDocument is {SourceLanguage:"zh",TargetLanguage:"en"} && chineseDocument.Lines.All(line=>line.Target.Any(char.IsAsciiLetter) && !line.Target.Any(character=>character is >= '\u3400' and <= '\u9fff')),"The same real model automatically reverses from the English screenshot to Chinese-to-English OCR and translation");
            require(settings.TranslationTargetLanguage=="Auto" && TranslationLanguages.Read(InteractionField<ComboBox>(window,"target"),false)=="Auto","Two consecutive directions leave the target selector in automatic mode");
            await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"configured-auto-directions.json"),JsonSerializer.Serialize(new{model=settings.AiTranslationModel,english=document,chinese=chineseDocument},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
            DesktopCapture.DwmFlush();await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-configured-translation-chinese-to-english.png"),await DesktopCapture.Capture(ToolWindowGeometry.ClientBounds(window)).PngAsync());window.Close();
            ShowSettings();NavigateTo("tools");await Task.Delay(120);var panel=PageContent.Children.OfType<AiOcrSettingsPanel>().Single();panel.Expander.IsExpanded=true;panel.TestPurpose.SelectedIndex=1;
            if(AppWindow.Presenter is OverlappedPresenter mainPresenter)mainPresenter.IsAlwaysOnTop=true;
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(panel.TestButton)!.GetPattern(PatternInterface.Invoke)).Invoke();await panel.LastTest;
            require(panel.Status.Severity==InfoBarSeverity.Success && panel.TestOutput.Text.Contains("世界"),"The AI settings connection-test button also succeeds with the configured HY-MT model's plain-text reply");
            DesktopCapture.DwmFlush();
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-configured-ai-test.png"),await DesktopCapture.Capture(ToolWindowGeometry.ClientBounds(this)).PngAsync());
            NavigateTo("overview");await Task.Delay(150);
        }
        finally{if(InteractionField<HashSet<Window>>(desktopTools,"windows").Contains(window))window.Close();}
    }
}
