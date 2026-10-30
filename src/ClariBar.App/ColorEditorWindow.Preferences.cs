using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed partial class ColorEditorWindow
{
    private void BuildPreferences()
    {
        preferenceContent.Children.Clear();preferenceContent.Children.Add(ToolUi.Label(L.T("Color picker settings"),18));
        void Choice(string label,string[] labels,string[] values,string current,Action<string> save)
        {
            var combo=new ComboBox{Header=L.T(label),ItemsSource=labels.Select(L.T).ToArray(),SelectedIndex=Math.Max(0,Array.IndexOf(values,current)),HorizontalAlignment=HorizontalAlignment.Stretch};
            combo.SelectionChanged+=(_,_)=>{if(combo.SelectedIndex>=0){save(values[combo.SelectedIndex]);tools.Save();}};preferenceContent.Children.Add(combo);
        }
        Choice("Activation behavior",["Pick a color and open editor","Open editor","Only pick a color"],["PickAndEdit","Editor","PickOnly"],tools.Settings.ColorPickerActivation,selected=>tools.Settings.ColorPickerActivation=selected);
        string[] names=tools.Settings.ColorFormats.Select(item=>item.Name).ToArray();Choice("Default color format",names,names,tools.Settings.ColorCopyFormat,selected=>tools.Settings.ColorCopyFormat=selected);
        var showName=new ToggleSwitch{Header=L.T("Show color name"),IsOn=tools.Settings.ColorShowName};showName.Toggled+=(_,_)=>{tools.Settings.ColorShowName=showName.IsOn;tools.Save();};preferenceContent.Children.Add(showName);
        foreach(var (label,current,save) in new (string,string,Action<string>)[]{("Left mouse button",tools.Settings.ColorLeftAction,selected=>tools.Settings.ColorLeftAction=selected),("Middle mouse button",tools.Settings.ColorMiddleAction,selected=>tools.Settings.ColorMiddleAction=selected),("Right mouse button",tools.Settings.ColorRightAction,selected=>tools.Settings.ColorRightAction=selected)})
            Choice(label,["Pick a color and open editor","Copy color and close","Cancel"],["Edit","Copy","Cancel"],current,save);
        preferenceContent.Children.Add(ToolUi.Label(L.T("Color formats"),16));
        void SaveFormats(){tools.Save();RenderColor(color);BuildPreferences();}
        foreach(var format in tools.Settings.ColorFormats.ToArray())
        {
            var row=new Grid{ColumnSpacing=4};row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});for(int i=0;i<4;i++)row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var enabled=new CheckBox{Content=format.Name,IsChecked=format.Enabled};enabled.Click+=(_,_)=>{format.Enabled=enabled.IsChecked==true;SaveFormats();};row.Children.Add(enabled);
            void Move(int direction){int at=tools.Settings.ColorFormats.IndexOf(format),to=at+direction;if(to<0 || to>=tools.Settings.ColorFormats.Count)return;(tools.Settings.ColorFormats[at],tools.Settings.ColorFormats[to])=(tools.Settings.ColorFormats[to],tools.Settings.ColorFormats[at]);SaveFormats();}
            var up=IconButton("\uE70E","Move up");up.IsEnabled=tools.Settings.ColorFormats.IndexOf(format)>0;up.Click+=(_,_)=>Move(-1);Grid.SetColumn(up,1);row.Children.Add(up);
            var down=IconButton("\uE70D","Move down");down.IsEnabled=tools.Settings.ColorFormats.IndexOf(format)<tools.Settings.ColorFormats.Count-1;down.Click+=(_,_)=>Move(1);Grid.SetColumn(down,2);row.Children.Add(down);
            var edit=IconButton("\uE70F","Edit format");edit.Click+=async(_,_)=>await EditFormatAsync(format);Grid.SetColumn(edit,3);row.Children.Add(edit);
            var remove=IconButton("\uE74D","Remove format");remove.IsEnabled=tools.Settings.ColorFormats.Count>1;remove.Click+=(_,_)=>{tools.Settings.ColorFormats.Remove(format);SaveFormats();};Grid.SetColumn(remove,4);row.Children.Add(remove);preferenceContent.Children.Add(row);
        }
        preferenceContent.Children.Add(ToolUi.AsyncButton("Add custom color format",AddFormatAsync));
    }
    private Task AddFormatAsync()=>EditFormatAsync(null);
    private async Task EditFormatAsync(ColorFormatDefinition? editing)
    {
        preferences.Hide();var name=new TextBox{Header=L.T("Name"),Text=editing?.Name??"",MaxLength=24};var template=new TextBox{Header=L.T("Format"),Text=editing?.Format??"#%ReX%GrX%BlX",MaxLength=200};var preview=ToolUi.Label(color.Hex);var error=ToolUi.Label("");
        var panel=new StackPanel{Spacing=10};panel.Children.Add(name);panel.Children.Add(template);panel.Children.Add(preview);panel.Children.Add(ToolUi.Label(L.T("Tokens: %Re / %Gr / %Bl (RGB), %Hu / %Sl / %Ll (HSL), %Sb / %Va (HSV). Add X for two-digit HEX, for example #%ReX%GrX%BlX."),12));panel.Children.Add(error);
        template.TextChanged+=(_,_)=>preview.Text=ColorFormats.Format(color,template.Text);
        var dialog=new ContentDialog{Title=L.T(editing is null?"Add custom color format":"Edit format"),Content=panel,PrimaryButtonText=L.T("Save"),CloseButtonText=L.T("Cancel"),XamlRoot=root.XamlRoot};
        dialog.PrimaryButtonClick+=(_,e)=>{if(string.IsNullOrWhiteSpace(name.Text) || tools.Settings.ColorFormats.Any(item=>item!=editing && item.Name.Equals(name.Text.Trim(),StringComparison.OrdinalIgnoreCase)) || !ColorFormats.ValidTemplate(template.Text)){e.Cancel=true;error.Text=L.T("Use a unique name and supported color tokens.");}};
        if(await dialog.ShowAsync()==ContentDialogResult.Primary){if(editing is null)tools.Settings.ColorFormats.Add(new(){Name=name.Text.Trim(),Format=template.Text});else{string previous=editing.Name;editing.Name=name.Text.Trim();editing.Format=template.Text;if(tools.Settings.ColorCopyFormat==previous)tools.Settings.ColorCopyFormat=editing.Name;}tools.Save();RenderColor(color);}
    }
}
