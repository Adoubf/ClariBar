using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed class AiOcrSettingsPanel:UserControl
{
    private readonly AppSettings settings;
    private readonly Action save;
    private readonly StackPanel fields=new(){Spacing=12};
    private readonly ContentControl configuration=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private CancellationTokenSource? testing;
    internal Expander Expander {get;}
    internal TextBox Endpoint {get;}=new(){Header=L.T("Server address or API endpoint"),PlaceholderText="http://192.168.1.10:8000/v1"};
    internal TextBox Model {get;}=new(){Header=L.T("OCR model ID"),PlaceholderText=L.T("Use the exact model name served by your deployment")};
    internal TextBox TranslationModel {get;}=new(){Header=L.T("Translation model ID"),PlaceholderText=L.T("A text or multimodal model on the same service")};
    internal TextBox TranslationPrompt {get;}=new(){Header=L.T("Translation style (optional)"),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=64,MaxLength=8192};
    internal ComboBox TestPurpose {get;}=new(){Header=L.T("Test model"),ItemsSource=new[]{L.T("AI text recognition"),L.T("AI translation")},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
    internal ComboBox ApiType {get;}=new(){Header=L.T("Endpoint type"),HorizontalAlignment=HorizontalAlignment.Stretch};
    internal ComboBox ModelType {get;}=new(){Header=L.T("Model type"),HorizontalAlignment=HorizontalAlignment.Stretch};
    internal TextBox Prompt {get;}=new(){Header=L.T("Custom OCR prompt (optional)"),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=80,MaxLength=8192,PlaceholderText=L.T("Leave blank to use the model's default OCR prompt.")};
    internal NumberBox Timeout {get;}=new(){Header=L.T("Request timeout (seconds)"),Minimum=5,Maximum=1800,SmallChange=5,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Inline,Width=180,HorizontalAlignment=HorizontalAlignment.Left};
    internal PasswordBox Key {get;}=new(){Header=L.T("API key")};
    internal TextBlock ResolvedAddress {get;}=ToolUi.Label("",12);
    internal InfoBar Status {get;}=new(){IsOpen=false,IsClosable=true};
    internal TextBox TestOutput {get;}=new(){Header=L.T("Test output"),IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxHeight=150,Visibility=Visibility.Collapsed};
    internal Button TestButton {get;}
    internal Button SaveButton {get;}
    internal Button RemoveKeyButton {get;}
    internal Button CancelButton {get;}
    internal Task LastTest {get;private set;}=Task.CompletedTask;
    internal AiOcrSettingsPanel(AppSettings settings,Action save)
    {
        this.settings=settings;this.save=save;
        Endpoint.Text=settings.AiOcrEndpoint;Model.Text=settings.AiOcrModel;Prompt.Text=settings.AiOcrPrompt;Timeout.Value=settings.AiOcrTimeoutSeconds;
        TranslationModel.Text=settings.AiTranslationModel;TranslationPrompt.Text=settings.AiTranslationPrompt;
        ApiType.ItemsSource=new[]{L.T("Auto detect (default)"),"OpenAI (/v1/chat/completions)","OpenAI Responses (/v1/responses)","Anthropic (/v1/messages)","Gemini (/v1beta/models/{model}:generateContent)"};
        ApiType.SelectedIndex=Math.Max(0,Array.IndexOf(AiOcrProtocol.ApiTypes,settings.AiOcrApiType));
        ModelType.ItemsSource=new[]{L.T("Automatic model type"),L.T("OCR model"),L.T("General multimodal model"),L.T("Vision-language (VL) model")};
        ModelType.SelectedIndex=Math.Max(0,Array.IndexOf(AiOcrProtocol.ModelTypes,settings.AiOcrModelType));
        UpdateKeyHint();
        fields.Children.Add(ToolUi.Label(L.T("OCR and translation share this server address, API format, key and timeout. Add a translation model ID to reuse your existing AI connection. HTTP and HTTPS support local, LAN and remote services.")));
        fields.Children.Add(ApiType);fields.Children.Add(Endpoint);fields.Children.Add(ResolvedAddress);
        fields.Children.Add(ToolUi.Label(L.T("Enter a server root, /v1, /v1beta or a complete endpoint. Auto detect uses the API suffix and known service host."),12));
        fields.Children.Add(ModelType);fields.Children.Add(Model);
        fields.Children.Add(ToolUi.Label(L.T("OCR models such as PaddleOCR-VL and DeepSeek-OCR, VL models and general multimodal models can use these APIs. Model IDs are not restricted to a fixed list."),12));
        fields.Children.Add(Prompt);fields.Children.Add(Timeout);fields.Children.Add(Key);
        fields.Children.Add(TranslationModel);fields.Children.Add(TranslationPrompt);
        fields.Children.Add(ToolUi.Label(L.T("The API key is encrypted for your Windows account and excluded from diagnostics."),12));
        configuration.Content=fields;var panel=new StackPanel{Spacing=12};panel.Children.Add(configuration);
        panel.Children.Add(TestPurpose);
        panel.Children.Add(ToolUi.Label(L.T("Test the selected model with a built-in image or short text using the current fields. Your screenshots and recognition history are not sent."),12));
        var buttons=new Grid{ColumnSpacing=8,RowSpacing=8};
        buttons.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});buttons.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        buttons.RowDefinitions.Add(new(){Height=GridLength.Auto});buttons.RowDefinitions.Add(new(){Height=GridLength.Auto});
        SaveButton=ToolUi.Button("Save AI settings",SaveCurrent);TestButton=ToolUi.Button("Test connection",()=>{});
        TestButton.Click+=async(_,_)=>{LastTest=TestAsync();await LastTest;};
        RemoveKeyButton=ToolUi.Button("Remove API key",()=>{OcrCredential.Clear();Key.Password="";UpdateKeyHint();Status.Severity=InfoBarSeverity.Informational;Status.Title=L.T("API key removed");Status.Message="";Status.IsOpen=true;});
        CancelButton=ToolUi.Button("Cancel test",()=>testing?.Cancel());CancelButton.Visibility=Visibility.Collapsed;
        foreach(var (button,column,row) in new[]{(SaveButton,0,0),(TestButton,1,0),(RemoveKeyButton,0,1),(CancelButton,1,1)})
        {button.HorizontalAlignment=HorizontalAlignment.Stretch;Grid.SetColumn(button,column);Grid.SetRow(button,row);buttons.Children.Add(button);}
        panel.Children.Add(buttons);panel.Children.Add(Status);panel.Children.Add(TestOutput);
        Expander=SettingsIcons.Section(L.T("AI configuration"),panel);Content=Expander;
        Endpoint.TextChanging+=(_,_)=>InputChanged();Model.TextChanging+=(_,_)=>InputChanged();Prompt.TextChanging+=(_,_)=>InputChanged();Key.PasswordChanged+=(_,_)=>InputChanged();
        ApiType.SelectionChanged+=(_,_)=>InputChanged();ModelType.SelectionChanged+=(_,_)=>InputChanged();Timeout.ValueChanged+=(_,_)=>InputChanged();
        TranslationModel.TextChanging+=(_,_)=>InputChanged();TranslationPrompt.TextChanging+=(_,_)=>InputChanged();TestPurpose.SelectionChanged+=(_,_)=>InputChanged();
        Unloaded+=(_,_)=>testing?.Cancel();UpdatePreview();
    }
    private AiOcrOptions Options()=>new(Endpoint.Text.Trim(),Model.Text.Trim(),AiOcrProtocol.ApiTypes[Math.Max(0,ApiType.SelectedIndex)],
        AiOcrProtocol.ModelTypes[Math.Max(0,ModelType.SelectedIndex)],Prompt.Text.Trim(),double.IsFinite(Timeout.Value)?(int)Math.Clamp(Timeout.Value,5,1800):120);
    private void UpdateKeyHint()=>Key.PlaceholderText=OcrCredential.Exists?L.T("A key is saved. Leave blank to keep it."):L.T("Optional for a local model");
    private void InputChanged(){if(testing is null){Status.IsOpen=false;TestOutput.Visibility=Visibility.Collapsed;}UpdatePreview();}
    private void UpdatePreview()
    {
        if(string.IsNullOrWhiteSpace(Endpoint.Text)){ResolvedAddress.Text="";return;}
        try{var options=Options();var target=AiOcrProtocol.Resolve(TestPurpose.SelectedIndex==1?options with {Model=TranslationModel.Text.Trim()}:options);ResolvedAddress.Text=L.F("Request address: {0}",target.Endpoint.GetLeftPart(UriPartial.Path));}
        catch(Exception ex)when(ex is InvalidOperationException or UriFormatException){ResolvedAddress.Text=ex.Message;}
    }
    private void SaveCurrent()
    {
        try
        {
            var options=Options();if(options.Endpoint.Length>0)AiOcrProtocol.Resolve(options.Model.Length>0?options:options with {Model=TranslationModel.Text.Trim()});
            if(Key.Password.Length>0)OcrCredential.Save(Key.Password.Trim());
            settings.AiOcrEndpoint=options.Endpoint;settings.AiOcrModel=options.Model;settings.AiOcrApiType=options.ApiType;
            settings.AiOcrModelType=options.ModelType;settings.AiOcrPrompt=options.Prompt;settings.AiOcrTimeoutSeconds=options.TimeoutSeconds;
            settings.AiTranslationModel=TranslationModel.Text.Trim();settings.AiTranslationPrompt=TranslationPrompt.Text.Trim();
            save();Key.Password="";UpdateKeyHint();Status.Severity=InfoBarSeverity.Success;Status.Title=L.T("AI settings saved");Status.Message="";Status.IsOpen=true;
        }
        catch(Exception ex){Status.Severity=InfoBarSeverity.Error;Status.Title=L.T("AI settings could not be saved");Status.Message=ex.Message;Status.IsOpen=true;}
    }
    private async Task TestAsync()
    {
        if(testing is not null)return;
        using var cancellation=new CancellationTokenSource();testing=cancellation;
        configuration.IsEnabled=SaveButton.IsEnabled=TestButton.IsEnabled=RemoveKeyButton.IsEnabled=false;CancelButton.Visibility=Visibility.Visible;
        TestPurpose.IsEnabled=false;
        Status.Title=L.T("Testing connection…");Status.Message=L.T(TestPurpose.SelectedIndex==1?"Checking the API, authentication and translation…":"Checking the API, authentication and image recognition…");Status.Severity=InfoBarSeverity.Informational;Status.IsOpen=true;TestOutput.Visibility=Visibility.Collapsed;
        try
        {
            var options=Options();string key=Key.Password.Length>0?Key.Password.Trim():OcrCredential.Read();
            if(TestPurpose.SelectedIndex==1)
            {
                var sampleSettings=new AppSettings{AiOcrEndpoint=options.Endpoint,AiOcrApiType=options.ApiType,AiOcrTimeoutSeconds=options.TimeoutSeconds,AiTranslationModel=TranslationModel.Text.Trim(),AiTranslationPrompt=TranslationPrompt.Text.Trim()};
                var watch=System.Diagnostics.Stopwatch.StartNew();string[] output=await TranslationService.TranslateLinesAsync(["Hello world."],"en","zh","Ai",sampleSettings,cancellation.Token,key:key);
                var endpoint=AiOcrProtocol.Resolve(options with{Model=sampleSettings.AiTranslationModel});
                Status.Severity=InfoBarSeverity.Success;Status.Title=L.T("Connection and translation succeeded");Status.Message=L.F("{0} · {1} · {2:0.00} s",AiOcrProtocol.ApiLabel(endpoint.ApiType),sampleSettings.AiTranslationModel,watch.ElapsedMilliseconds/1000d);
                TestOutput.Text=output[0];TestOutput.Visibility=Visibility.Visible;return;
            }
            var result=await OcrService.TestConnectionAsync(options,key,cancellation.Token);
            Status.Severity=result.SampleRecognized?InfoBarSeverity.Success:InfoBarSeverity.Warning;
            Status.Title=L.T(result.SampleRecognized?"Connection and image recognition succeeded":"Connected; check the recognition result");
            Status.Message=L.F("{0} · {1} · {2:0.00} s",AiOcrProtocol.ApiLabel(result.Target.ApiType),result.Model,result.ElapsedMilliseconds/1000d);
            if(!result.SampleRecognized)Status.Message+="\n"+L.T("The server returned text, but the expected sample text was not found. Check the model and OCR prompt.");
            TestOutput.Text=result.Text.Length>2000?result.Text[..2000]+"…":result.Text;TestOutput.Visibility=Visibility.Visible;
        }
        catch(OperationCanceledException){Status.Title=L.T("Connection test cancelled");Status.Message="";Status.Severity=InfoBarSeverity.Informational;}
        catch(Exception ex){Status.Title=L.T("Connection test failed");Status.Message=ex.Message;Status.Severity=InfoBarSeverity.Error;}
        finally{testing=null;configuration.IsEnabled=SaveButton.IsEnabled=TestButton.IsEnabled=RemoveKeyButton.IsEnabled=TestPurpose.IsEnabled=true;CancelButton.Visibility=Visibility.Collapsed;}
    }
}
