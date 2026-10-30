using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed class PinManagerWindow:Window
{
    private sealed record Entry(PinModel Model,string Name);
    private readonly DesktopToolsController tools;
    private readonly ComboBox groups=new(){MinWidth=180};
    private readonly ListView images=new(){DisplayMemberPath="Name",SelectionMode=ListViewSelectionMode.Single};
    private readonly TextBlock state=ToolUi.Label("");
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(200)};
    private bool refreshing,dirty;
    public PinManagerWindow(DesktopToolsController tools)
    {
        this.tools=tools;var root=new Grid {Padding=new(18),RowSpacing=12};
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        root.Children.Add(ToolUi.Label(L.T("Pinned images & groups"),24));
        var header=new CommandBar {DefaultLabelPosition=CommandBarDefaultLabelPosition.Right};header.Content=groups;
        groups.SelectionChanged+=(_,_)=>{if(!refreshing && groups.SelectedIndex>=0 && groups.SelectedIndex<tools.Pins.Groups.Count)tools.Pins.SwitchGroup(tools.Pins.Groups[groups.SelectedIndex]);};
        void Command(CommandBar bar,string label,Symbol icon,Func<Task> action,bool secondary=false)
        {var button=new AppBarButton {Label=L.T(label),Icon=new SymbolIcon(icon),IsCompact=true};button.Click+=async(_,_)=>{try{await action();}catch(Exception ex){await ToolUi.ErrorAsync(this,ex);}};if(secondary)bar.SecondaryCommands.Add(button);else bar.PrimaryCommands.Add(button);}
        Command(header,"New group",Symbol.Add,()=>GroupDialogAsync(false));Command(header,"Rename group",Symbol.Rename,()=>GroupDialogAsync(true));
        Command(header,"Close group",Symbol.Cancel,CloseGroupAsync,true);
        Command(header,"Show / hide all",Symbol.View,()=>{tools.Pins.ToggleAll();return Task.CompletedTask;});
        Command(header,"Open image files",Symbol.OpenFile,()=>tools.OpenFilesAsync(this));Grid.SetRow(header,1);root.Children.Add(header);
        Grid.SetRow(images,2);root.Children.Add(images);
        var bottom=new StackPanel {Spacing=8};var actions=new CommandBar {DefaultLabelPosition=CommandBarDefaultLabelPosition.Right};
        PinModel? Selected()=>(images.SelectedItem as Entry)?.Model;
        Command(actions,"Show image",Symbol.View,()=>{if(Selected() is {} p)tools.Pins.Show(p);return Task.CompletedTask;});
        Command(actions,"Edit annotations",Symbol.Edit,()=>{Selected()?.Window.OpenEditor();return Task.CompletedTask;});
        Command(actions,"Close",Symbol.Cancel,()=>{if(Selected() is {} p)tools.Pins.Close(p);return Task.CompletedTask;});
        Command(actions,"Destroy",Symbol.Delete,()=>{if(Selected() is {} p)tools.Pins.Destroy(p);return Task.CompletedTask;});
        bottom.Children.Add(actions);bottom.Children.Add(state);Grid.SetRow(bottom,3);root.Children.Add(bottom);Content=root;ToolUi.Configure(this,tools,"Manage images",900,650);
        tools.Pins.Changed+=Invalidate;Closed+=(_,_)=>{tools.Pins.Changed-=Invalidate;timer.Stop();};timer.Tick+=(_,_)=>{if(dirty){dirty=false;Refresh();}};timer.Start();Refresh();
    }
    private void Invalidate()=>dirty=true;
    private void Refresh()
    {
        string? selected=(images.SelectedItem as Entry)?.Model.Record.Id;refreshing=true;
        groups.ItemsSource=tools.Pins.Groups.Select(x=>x=="Default"?L.T("Default group"):x).ToArray();groups.SelectedIndex=tools.Pins.Groups.IndexOf(tools.Pins.ActiveGroup);
        images.ItemsSource=tools.Pins.Images.Select((pin,index)=>new Entry(pin,$"{index+1}. {L.T(pin.Content.Kind)} · {pin.Document.Source.Width} × {pin.Document.Source.Height} · {(pin.Record.Group=="Default"?L.T("Default group"):pin.Record.Group)} · {L.T(pin.Record.State)}")).ToArray();
        images.SelectedItem=images.Items.OfType<Entry>().FirstOrDefault(x=>x.Model.Record.Id==selected);state.Text=L.F("{0} images · {1} groups",tools.Pins.Images.Count,tools.Pins.Groups.Count);refreshing=false;
    }
    private async Task GroupDialogAsync(bool rename)
    {
        var input=new TextBox {Text=rename?tools.Pins.ActiveGroup:"",MaxLength=60,MinWidth=240};
        var dialog=new ContentDialog {Title=L.T(rename?"Rename group":"New group"),Content=input,PrimaryButtonText=L.T("Save"),CloseButtonText=L.T("Cancel"),DefaultButton=ContentDialogButton.Primary,XamlRoot=((FrameworkElement)Content).XamlRoot};
        if(await dialog.ShowAsync()==ContentDialogResult.Primary)
        {bool result=rename?tools.Pins.RenameGroup(tools.Pins.ActiveGroup,input.Text):tools.Pins.AddGroup(input.Text);if(!result)state.Text=L.T("Choose a unique group name. The default group cannot be renamed.");}
    }
    private async Task CloseGroupAsync()
    {
        if(tools.Pins.ActiveGroup=="Default")return;
        var dialog=new ContentDialog {Title=L.T("Close group"),Content=L.T("Close this group and destroy its images?"),PrimaryButtonText=L.T("Close group"),CloseButtonText=L.T("Cancel"),DefaultButton=ContentDialogButton.Close,XamlRoot=((FrameworkElement)Content).XamlRoot};
        if(await dialog.ShowAsync()==ContentDialogResult.Primary)tools.Pins.CloseGroup(tools.Pins.ActiveGroup);
    }
}
