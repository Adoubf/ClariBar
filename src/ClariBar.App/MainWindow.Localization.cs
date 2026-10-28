using System.Globalization;
using System.Runtime.InteropServices;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ClariBar.App;

internal static class AppLocalization
{
    [DllImport("kernel32.dll")] private static extern ushort GetUserDefaultUILanguage();
    public static string SystemLanguage {get;}=ReadSystemLanguage();
    public static string? TestOverride {get;private set;}
    private static string ReadSystemLanguage()
    {
        try{return CultureInfo.GetCultureInfo(GetUserDefaultUILanguage()).Name;}
        catch(CultureNotFoundException){return CultureInfo.CurrentUICulture.Name;}
    }
    public static void Initialize()
    {
        bool smoke=Environment.GetCommandLineArgs().Contains("--smoke-test");
        var directory=smoke?Path.Combine(AppPaths.Data,"smoke-session"):AppPaths.Data;
        string preference=new SettingsStore(directory).Load().Language;
        if(smoke)TestOverride=Environment.GetCommandLineArgs().FirstOrDefault(arg=>arg.StartsWith("--language=",StringComparison.Ordinal))?[11..];
        Apply(TestOverride??preference);
    }
    public static void Apply(string preference)
    {
        Localizer.Configure(preference,SystemLanguage);
        var culture=CultureInfo.GetCultureInfo(Localizer.LanguageCode);
        CultureInfo.CurrentUICulture=culture;
        CultureInfo.CurrentCulture=culture;
        CultureInfo.DefaultThreadCurrentUICulture=culture;
        CultureInfo.DefaultThreadCurrentCulture=culture;
        Environment.SetEnvironmentVariable("CLARIBAR_LANGUAGE",Localizer.LanguageCode);
        try{Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride=Localizer.LanguageCode;}
        catch(COMException){/* Root.Language and application resources still provide the selected locale. */}
    }
}

public sealed partial class MainWindow
{
    private ComboBox? languageSelector;
    private void LocalizeShell()
    {
        Root.Language=Localizer.LanguageCode;
        Root.KeyboardAcceleratorPlacementMode=Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
        if(ToolTipService.GetToolTip(Root) is ToolTip tooltip)tooltip.IsOpen=false;
        ToolTipService.SetToolTip(Root,null);
        Title=Localizer.T("ClariBar");
        HelpButton.Content=Localizer.T("Help");
        AutomationProperties.SetName(HelpButton,Localizer.T("Help"));
        ToolTipService.SetToolTip(HelpButton,Localizer.T("Help and shortcuts (F1)"));
        HelpMenuItem.Text=Localizer.T("Help");
        ShortcutsMenuItem.Text=Localizer.T("Keyboard shortcuts");
        AboutMenuItem.Text=Localizer.T("About ClariBar");
        SearchBox.PlaceholderText=Localizer.T("Find sensors");
        AutomationProperties.SetName(SearchBox,Localizer.T("Find sensors"));
        PaneTitle.Text=Localizer.T("ClariBar");
        AutomationProperties.SetName(PaneIcon,Localizer.T("ClariBar icon"));
        MottoTitle.Text=Localizer.T("Your taskbar, your rhythm");
        MottoSubtitle.Text=Localizer.T("Style and system status, together");
        var names=new[]{"Overview","Taskbar","Resource monitor","Text & icons","General","About"};
        for(int i=0;i<Nav.MenuItems.Count;i++)
        {
            if(Nav.MenuItems[i] is not NavigationViewItem item)continue;
            item.Content=Localizer.T(names[i]);
            ToolTipService.SetToolTip(item,item.Content);
            AutomationProperties.SetName(item,item.Content.ToString()!);
        }
        ToolTipService.SetToolTip(FooterVersion,Localizer.T("ClariBar")+" "+AppVersion);
    }
    private void AddLanguageControl()
    {
        string automatic=Localizer.ResolveLanguage("System",AppLocalization.SystemLanguage)=="zh-CN"?"简体中文":"English";
        var selector=new ComboBox
        {
            ItemsSource=new[]{Localizer.F("Follow system ({0})",automatic),"English","简体中文"},
            SelectedIndex=settings.Language switch{"en-US"=>1,"zh-CN"=>2,_=>0},
            MinWidth=190
        };
        languageSelector=selector;
        AutomationProperties.SetName(selector,Localizer.T("Language"));
        selector.SelectionChanged+=(_,_)=>DispatcherQueue.TryEnqueue(()=>
        {
            if(selector.SelectedIndex<0)return;
            ChangeLanguage(selector.SelectedIndex switch{1=>"en-US",2=>"zh-CN",_=>"System"});
        });
        Row(Localizer.T("Language"),Localizer.T("Choose a display language. Changes apply immediately."),selector,"\uE774");
    }
    private void ChangeLanguage(string preference)
    {
        preference=Localizer.NormalizePreference(preference);
        if(settings.Language==preference && Root.Language==Localizer.ResolveLanguage(preference,AppLocalization.SystemLanguage))return;
        CommitFontSize(true);
        settings.Language=preference;
        AppLocalization.Apply(preference);
        category=Localizer.T(category);sensorKind=Localizer.T(sensorKind);connection=Localizer.T(connection);
        if(accessFallbackNotice is not null)accessFallbackNotice=Localizer.T(accessFallbackNotice);
        LocalizeShell();
        if(nativeReady)Native.CB_SetLanguage(Localizer.IsChinese?1:0);
        sensorRows.Clear();rowIndex.Clear();
        Save();renderedPage="";RenderPage();UpdatePaneAppearance();UpdateMonitor();
    }
}
