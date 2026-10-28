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
                await MessageAsync("无法打开链接",uri.AbsoluteUri);
        }
        catch(Exception ex){await MessageAsync("无法打开链接",ex.Message+"\n"+uri.AbsoluteUri);}
    }
    private void BuildProjectAbout()
    {
        projectLinks.Clear();
        Heading("作者与项目");
        Row("作者与联系",ProjectAuthor+"\n"+ProjectEmail,
            ProjectLink("联系作者","mailto:"+ProjectEmail+"?subject="+Uri.EscapeDataString("ClariBar 交流与反馈")),"\uE77B");
        Row("GitHub",ProjectRepository,ProjectLink("查看源码",ProjectRepository),"\uE943");
        Row("MIT 许可证","本项目原创代码与品牌素材采用 MIT 许可证。第三方依赖保留各自的许可。",
            Button("查看许可证",()=>_=ShowProjectLicenseAsync()),"\uE8A5");
        Heading("赞助与感谢");
        Row("谢谢每一份支持","感谢每一位使用、反馈和贡献的朋友。赞助事宜可通过邮箱联系；如希望署名致谢，请在邮件中注明可公开展示的名称。",
            ProjectLink("赞助联系","mailto:"+ProjectEmail+"?subject="+Uri.EscapeDataString("ClariBar 赞助与支持")),"\uEB51");
    }
    private async Task ShowProjectLicenseAsync()
    {
        string path=Path.Combine(AppContext.BaseDirectory,"LICENSE");
        if(!File.Exists(path)){await MessageAsync("许可证","未找到本地许可证文件，请使用完整安装包。");return;}
        string license=File.ReadAllText(path);
        var text=new TextBlock {Text=license,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,FontSize=12,MaxWidth=520};
        var dialog=new ContentDialog {Title="MIT 许可证",CloseButtonText="关闭",XamlRoot=Root.XamlRoot,
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
        Nav.SelectedItem=Nav.MenuItems[5];await Task.Delay(350);
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
        bool sponsor=texts.Any(text=>text.Contains("公开展示的名称"));
        await RenderCheckImageAsync("about-project");
        await ShowProjectLicenseAsync();
        var result=new{capturedAt=DateTimeOffset.Now,passed=author && email && github && contacts && sponsor && projectLicenseVerified,
            author,email,github,contacts,sponsor,license=projectLicenseVerified};
        File.WriteAllText(Path.Combine(AppPaths.Logs,"claribar-project-info-check.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
        if(!result.passed)throw new IOException("关于页、许可证或联系入口检查失败。");
    }
}
