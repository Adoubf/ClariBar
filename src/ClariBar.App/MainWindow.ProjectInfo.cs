using L = ClariBar.Core.Localizer;
using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private const string ProjectAuthor="Haoyue";
    private const string ProjectEmail="mosaryalex@gmail.com";
    private const string ProjectRepository="https://github.com/Adoubf/ClariBar";
    private readonly List<Button> projectLinks=[];
    private bool projectLicenseVerified;

    private Button ProjectLink(string label,string address)
    {
        var uri=new Uri(address);
        var button=Button(label,()=>_=OpenProjectLinkAsync(uri));
        button.Tag=uri;ToolTipService.SetToolTip(button,address);projectLinks.Add(button);
        return button;
    }
    private async Task OpenProjectLinkAsync(Uri uri)
    {
        try
        {
            if(!await Windows.System.Launcher.LaunchUriAsync(uri))
                await MessageAsync(L.T("Could not open the link"),uri.AbsoluteUri);
        }
        catch(Exception ex){await MessageAsync(L.T("Could not open the link"),ex.Message+"\n"+uri.AbsoluteUri);}
    }
    private void BuildProjectAbout()
    {
        projectLinks.Clear();
        Heading(L.T("Author & project"));
        Row(L.T("Author & contact"),ProjectAuthor+"\n"+ProjectEmail,
            ProjectLink(L.T("Contact author"),"mailto:"+ProjectEmail+"?subject="+Uri.EscapeDataString(L.T("ClariBar questions and feedback"))),"\uE77B");
        Row("GitHub",ProjectRepository,ProjectLink(L.T("View source"),ProjectRepository),"\uE943");
        Row(L.T("MIT License"),L.T("Original ClariBar code and artwork use the MIT License. Third-party dependencies retain their own licenses."),
            Button(L.T("View license"),()=>_=ShowProjectLicenseAsync()),"\uE8A5");
        Heading(L.T("Sponsorship & thanks"));
        Row(L.T("Thank you for your support"),L.T("Thank you to everyone who uses, reports issues or contributes. Contact the author by email about sponsorship. Include a publicly displayable name if you would like an acknowledgement."),
            ProjectLink(L.T("Discuss sponsorship"),"mailto:"+ProjectEmail+"?subject="+Uri.EscapeDataString(L.T("ClariBar sponsorship and support"))),"\uEB51");
    }
    private async Task ShowProjectLicenseAsync()
    {
        string path=Path.Combine(AppContext.BaseDirectory,"LICENSE");
        if(!File.Exists(path)){await MessageAsync(L.T("License"),L.T("The local license file is missing. Use the complete installer."));return;}
        string license=File.ReadAllText(path);
        var text=new TextBlock {Text=license,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,FontSize=12,MaxWidth=520};
        var dialog=new ContentDialog {Title=L.T("MIT License"),CloseButtonText=L.T("Close"),XamlRoot=Root.XamlRoot,
            Content=new ScrollViewer {Content=text,MaxHeight=Math.Max(180,Root.ActualHeight-220)}};
        var shown=dialog.ShowAsync();
        if(smokeTest)
        {
            await Task.Delay(250);
            projectLicenseVerified=dialog.ActualWidth>200 && license.Contains("MIT License") && license.Contains("2026 Haoyue");
            await RenderCheckImageAsync("project-license",dialog);dialog.Hide();
        }
        await shown;
    }
    private async Task CheckProjectAboutAsync()
    {
        NavigateTo("about");await Task.Delay(350);
        var texts=new List<string>();
        void Read(DependencyObject node)
        {
            if(node is TextBlock text)texts.Add(text.Text);
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Read(VisualTreeHelper.GetChild(node,i));
        }
        Read(PageContent);
        bool author=texts.Any(text=>text.Contains(ProjectAuthor));
        bool email=texts.Any(text=>text.Contains(ProjectEmail));
        bool github=projectLinks.Any(button=>button.Tag is Uri uri && uri.AbsoluteUri==ProjectRepository);
        bool contacts=projectLinks.Count(button=>button.Tag is Uri uri && uri.OriginalString.StartsWith("mailto:"+ProjectEmail,StringComparison.Ordinal))==2;
        bool sponsor=texts.Any(text=>text.Contains(Localizer.IsChinese?"公开展示的名称":"publicly displayable name"));
        await RenderCheckImageAsync("about-project");
        await ShowProjectLicenseAsync();
        var result=new{capturedAt=DateTimeOffset.Now,passed=author && email && github && contacts && sponsor && projectLicenseVerified,
            author,email,github,contacts,sponsor,license=projectLicenseVerified};
        File.WriteAllText(Path.Combine(AppPaths.Logs,"claribar-project-info-check.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
        if(!result.passed)throw new IOException("关于页、许可证或联系入口检查失败。");
    }
}
