using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private CancellationTokenSource? offlineDownload;
    private ComboBox? translationEngine,translationDisplay,translationSource,translationTarget;
    private void SynchronizeTranslationPreferences()
    {
        if(page!="tools")return;
        if(translationEngine is not null)translationEngine.SelectedIndex=settings.TranslationEngine switch{"Local"=>1,"Ai"=>2,_=>0};
        if(translationDisplay is not null)translationDisplay.SelectedIndex=settings.TranslationDisplay=="Overlay"?1:0;
        if(translationSource is not null)TranslationLanguages.Set(translationSource,settings.TranslationSourceLanguage,true);
        if(translationTarget is not null)TranslationLanguages.Set(translationTarget,settings.TranslationTargetLanguage,false);
    }
    private void BuildTranslationSettings()
    {
        Heading(L.T("Screenshot translation"));
        translationEngine=new(){ItemsSource=new[]{L.T("Free online (MyMemory)"),L.T("Offline Chinese ↔ English"),L.T("AI translation")},SelectedIndex=settings.TranslationEngine switch{"Local"=>1,"Ai"=>2,_=>0},MinWidth=210};
        translationEngine.SelectionChanged+=(_,_)=>{settings.TranslationEngine=translationEngine.SelectedIndex switch{1=>"Local",2=>"Ai",_=>"Free"};Save();};
        Row(L.T("Translation engine"),L.T("Free online is the default and needs no API key. It sends recognized text to MyMemory (5,000 characters/day under its current free policy). AI uses the shared configuration below."),translationEngine);
        translationDisplay=new(){ItemsSource=new[]{L.T("Bilingual comparison"),L.T("Show over captured region")},SelectedIndex=settings.TranslationDisplay=="Overlay"?1:0,MinWidth=210};
        translationDisplay.SelectionChanged+=(_,_)=>{settings.TranslationDisplay=translationDisplay.SelectedIndex==1?"Overlay":"Bilingual";Save();};
        Row(L.T("Translation display"),L.T("Use Ctrl + Alt + A to select a region. Switch between bilingual comparison and translated text over the original captured area."),translationDisplay);
        var source=TranslationLanguages.Create(settings.TranslationSourceLanguage,true);
        var target=TranslationLanguages.Create(settings.TranslationTargetLanguage,false,230);
        translationSource=source;translationTarget=target;
        TranslationLanguages.CommitChanges(source,true,value=>{settings.TranslationSourceLanguage=value;Save();});
        TranslationLanguages.CommitChanges(target,false,value=>{settings.TranslationTargetLanguage=value;Save();});
        Row(L.T("Source language"),L.T("Auto detects Chinese or English for free / offline translation. AI also accepts other language codes or names."),source);
        Row(L.T("Target language"),L.T("Automatically translates Chinese to English and English to Chinese. Choose a target language to override this; AI also accepts language codes or names."),target);
        var check=new CheckBox{Content=new TextBlock{Text=L.T("Enable and download offline Chinese ↔ English models"),TextWrapping=TextWrapping.Wrap},IsChecked=settings.OfflineTranslationEnabled};
        var status=Description(TranslationService.OfflineReady?L.T("Offline models installed. Translation works without an internet connection."):L.T("Optional, not downloaded by default. Checking this box downloads about 190 MB of models and an isolated runtime. Unchecking keeps downloaded files for reuse."));
        var progress=new ProgressBar{IsIndeterminate=true,Visibility=Visibility.Collapsed};var cancel=Button(L.T("Cancel download"),()=>offlineDownload?.Cancel());cancel.Visibility=Visibility.Collapsed;
        var panel=new StackPanel{Spacing=8,MaxWidth=450};panel.Children.Add(check);panel.Children.Add(status);panel.Children.Add(progress);panel.Children.Add(cancel);
        check.Checked+=async(_,_)=>
        {
            if(TranslationService.OfflineReady){settings.OfflineTranslationEnabled=true;Save();return;}
            if(offlineDownload is not null)return;
            using var cancellation=new CancellationTokenSource();offlineDownload=cancellation;check.IsEnabled=false;cancel.Visibility=progress.Visibility=Visibility.Visible;
            try
            {
                await OfflineTranslationInstaller.InstallAsync(text=>status.Text=text,cancellation.Token);settings.OfflineTranslationEnabled=true;Save();status.Text=L.T("Offline models installed. Translation works without an internet connection.");
            }
            catch(OperationCanceledException){check.IsChecked=false;status.Text=L.T("Offline download cancelled. Free online translation remains available.");}
            catch(Exception ex){check.IsChecked=false;status.Text=ex.Message;}
            finally{offlineDownload=null;check.IsEnabled=true;cancel.Visibility=progress.Visibility=Visibility.Collapsed;}
        };
        check.Unchecked+=(_,_)=>{settings.OfflineTranslationEnabled=false;Save();};
        panel.Unloaded+=(_,_)=>offlineDownload?.Cancel();
        Row(L.T("Optional offline models"),"",panel);
    }
}
