using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private async Task CheckDefaultOcrEngineAsync(Action<bool,string> require)
    {
        NavigateTo("tools");await Task.Delay(80);
        string engineBefore=settings.OcrDefaultEngine,endpointBefore=settings.AiOcrEndpoint,modelBefore=settings.AiOcrModel,apiBefore=settings.AiOcrApiType;
        OcrResultWindow? window=null;
        byte[] pixels=new byte[600*160*4];Array.Fill(pixels,(byte)255);
        var image=new PixelFrame(600,160,pixels).Render([new("Text",[new(24,24),new(25,25)],"#101010",3,"ClariBar OCR 2026"){FontSize=38}]);
        async Task<OcrResultWindow> OpenAsync()
        {
            desktopTools.OpenOcr(image);var opened=InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<OcrResultWindow>().Single();window=opened;
            for(int attempt=0;attempt<100 && !InteractionField<bool>(opened,"initialStarted");attempt++)await Task.Delay(30);
            await opened.InitialRecognition.WaitAsync(TimeSpan.FromSeconds(15));return opened;
        }
        try
        {
            require(defaultOcrEngine is {Items.Count:2} && defaultOcrEngine.SelectedIndex==(engineBefore=="Ai"?1:0),"Screenshots & OCR offers local and AI defaults with the saved selection");
            defaultOcrEngine!.SelectedIndex=0;settings.OcrDefaultEngine="Local";
            await using(var server=new AiOcrCheckServer(AiTestResponse(AiOcrProtocol.Chat,"DEFAULT AI RESULT")))
            {
                settings.AiOcrEndpoint=server.Address;settings.AiOcrModel="test-model";settings.AiOcrApiType=AiOcrProtocol.Chat;
                var local=await OpenAsync();string text=InteractionField<TextBox>(local,"result").Text;
                require(text.Contains("2026") && !server.Request.IsCompleted,"The local default recognizes the image on Windows without calling the configured AI service");
                local.Close();window=null;
                defaultOcrEngine.SelectedIndex=1;
                require(new SettingsStore(store.DirectoryPath).Load().OcrDefaultEngine=="Ai","Choosing the AI default persists immediately and survives settings reload");
                var ai=await OpenAsync();var request=await server.Request.WaitAsync(TimeSpan.FromSeconds(5));
                require(InteractionField<TextBox>(ai,"result").Text=="DEFAULT AI RESULT" && request.Path=="/v1/chat/completions","The shared OCR entry point automatically uses AI when AI is the default");
                require(InteractionField<TextBlock>(ai,"status").Text==L.T("AI OCR complete"),"Automatic AI recognition finishes directly with a visible result");
                ai.Close();window=null;
            }
            settings.AiOcrEndpoint="";settings.AiOcrModel="";var missing=await OpenAsync();
            require(InteractionField<TextBox>(missing,"result").Text.Length==0 && InteractionField<TextBlock>(missing,"status").Text==L.T("Configure an AI endpoint and model on Desktop tools first."),"An unconfigured AI default shows configuration guidance without silently switching to local OCR");
            missing.Close();window=null;
            defaultOcrEngine.SelectedIndex=0;require(new SettingsStore(store.DirectoryPath).Load().OcrDefaultEngine=="Local","Switching back to local recognition is saved");
        }
        finally
        {
            window?.Close();settings.OcrDefaultEngine=engineBefore;settings.AiOcrEndpoint=endpointBefore;settings.AiOcrModel=modelBefore;settings.AiOcrApiType=apiBefore;Save();
        }
    }
    private async Task ExportSettingsIconAuditAsync()
    {
        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for(int index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)
            {var child=VisualTreeHelper.GetChild(parent,index);yield return child;foreach(var nested in Descendants(child))yield return nested;}
        }
        var pages=new List<object>();
        foreach(string pageName in new[]{"overview","taskbar","sensors","colors","tools","general","about"})
        {
            NavigateTo(pageName);await Task.Delay(120);
            var icons=Descendants(PageContent).OfType<IconElement>().Where(icon=>icon.Tag is string).ToArray();
            pages.Add(new{page=pageName,icons=icons.Select(icon=>new{label=icon.Tag,kind=icon.GetType().Name,glyph=icon is FontIcon font?font.Glyph:null,icon.ActualWidth,icon.ActualHeight}).ToArray()});
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-settings-icons-"+pageName+".png"),await (await RenderOwnElementAsync(PageContent)).PngAsync());
        }
        await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"settings-icon-audit.json"),JsonSerializer.Serialize(new{language=L.LanguageCode,pages},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
        NavigateTo("tools");
    }
}
