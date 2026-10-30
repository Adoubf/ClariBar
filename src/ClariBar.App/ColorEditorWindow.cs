using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed partial class ColorEditorWindow:Window
{
    private readonly DesktopToolsController tools;
    private readonly Action<string>? changed;
    private ScreenColor color,draft;
    private bool syncing,tuning;
    private readonly ScrollViewer root=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    private readonly Grid layout=new();
    private readonly StackPanel history=new(){Orientation=Orientation.Horizontal,Spacing=6};
    private readonly Grid shades=new();
    private readonly StackPanel formats=new(){Spacing=8};
    private readonly Border body;
    private readonly TextBlock state=ToolUi.Label("",12);
    private readonly Flyout fineTune=new(){Placement=FlyoutPlacementMode.RightEdgeAlignedTop};
    private readonly Flyout preferences=new(){Placement=FlyoutPlacementMode.BottomEdgeAlignedRight};
    private readonly Slider hue=new(){Header="H",Minimum=0,Maximum=359.9,StepFrequency=.1};
    private readonly Slider saturation=new(){Header="S",Minimum=0,Maximum=100,StepFrequency=.1};
    private readonly Slider value=new(){Header="V",Minimum=0,Maximum=100,StepFrequency=.1};
    private readonly Border saturationTrack=new(){Height=6,CornerRadius=new(3),Margin=new(12,0,12,0)};
    private readonly Border valueTrack=new(){Height=6,CornerRadius=new(3),Margin=new(12,0,12,0)};
    private readonly NumberBox red=Channel("R"),green=Channel("G"),blue=Channel("B");
    private readonly TextBox hex=new(){Header="HEX",MaxLength=7};
    private readonly StackPanel preferenceContent=new(){Spacing=12,Width=380};
    private readonly List<(Border Card,TextBlock Label)> cards=[];

    public ColorEditorWindow(DesktopToolsController tools,ScreenColor color,Action<string>? changed=null)
    {
        this.tools=tools;this.changed=changed;this.color=color;ColorFormats.Normalize(tools.Settings);
        for(int i=0;i<3;i++)layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new Grid{Padding=new(16,12,16,12),ColumnSpacing=12};
        header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var pick=ToolUi.AsyncButton("Pick from screen",PickAsync);var pickContent=new StackPanel{Orientation=Orientation.Horizontal,Spacing=7};pickContent.Children.Add(new FontIcon{Glyph="\uEF3C",FontSize=16});pickContent.Children.Add(ToolUi.Label(L.T("Pick"),14));pick.Content=pickContent;header.Children.Add(pick);
        var historyViewport=new ScrollViewer{Content=history,HorizontalScrollMode=ScrollMode.Enabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalAlignment=VerticalAlignment.Center};
        Grid.SetColumn(historyViewport,1);header.Children.Add(historyViewport);
        var options=IconButton("\uE713","Color picker settings");options.Flyout=preferences;Grid.SetColumn(options,2);header.Children.Add(options);layout.Children.Add(header);
        var main=new Grid{ColumnSpacing=12,Padding=new(16)};main.ColumnDefinitions.Add(new(){Width=new(24)});main.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        shades.MinHeight=232;main.Children.Add(shades);Grid.SetColumn(formats,1);main.Children.Add(formats);
        body=new Border{Child=main,BorderThickness=new(0,1,0,0)};Grid.SetRow(body,1);layout.Children.Add(body);
        state.Margin=new(16,0,16,8);Grid.SetRow(state,2);layout.Children.Add(state);root.Content=layout;Content=root;
        BuildFineTune();preferences.Content=new ScrollViewer{Content=preferenceContent,MaxHeight=540,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};preferences.Opening+=(_,_)=>BuildPreferences();
        root.ActualThemeChanged+=(_,_)=>PaintCards();root.KeyDown+=KeyDown;
        double scale=DesktopWindowDpi.For(WinRT.Interop.WindowNative.GetWindowHandle(this))/96d;
        ToolUi.Configure(this,tools,"Color editor",(int)(560*scale),(int)(420*scale),true);
        if(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter){presenter.PreferredMinimumWidth=(int)(360*scale);presenter.PreferredMinimumHeight=(int)(280*scale);}
        layout.RowDefinitions[1].Height=new(1,GridUnitType.Star);formats.VerticalAlignment=VerticalAlignment.Top;
        root.SizeChanged+=(_,_)=>{preferenceContent.Width=Math.Max(260,Math.Min(380,root.ActualWidth-64));layout.MinHeight=root.ActualHeight;};
        Set(color,false);RefreshHistory();
    }
    private static NumberBox Channel(string name)=>new(){Header=name,Minimum=0,Maximum=255,SmallChange=1,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Hidden,HorizontalAlignment=HorizontalAlignment.Stretch};
    private static Button IconButton(string glyph,string label)
    {
        var button=new Button{Content=new FontIcon{Glyph=glyph,FontSize=16},Width=34,Height=34,MinWidth=0,Padding=new(6),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),BorderThickness=new(0)};
        AutomationProperties.SetName(button,L.T(label));ToolTipService.SetToolTip(button,L.T(label));return button;
    }
    private async Task PickAsync()
    {
        Native.ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(this),0);
        try{await tools.BeginAsync(color:true,chosen:hexColor=>{if(ScreenColor.TryParse(hexColor,out var picked))Set(picked);},returnToSettings:false);}
        finally{Activate();RefreshHistory();}
    }
    private void Set(ScreenColor selected,bool notify=true)
    {color=selected;RenderColor(color);RefreshHistory();if(notify)changed?.Invoke(color.Hex);}
    private void Remember(ScreenColor selected)
    {tools.Remember(selected);Set(selected);}
    private void RenderColor(ScreenColor selected)
    {
        shades.Children.Clear();shades.RowDefinitions.Clear();cards.Clear();formats.Children.Clear();
        int index=0;
        foreach(double delta in new[]{25d,12,0,-12,-25})
        {
            shades.RowDefinitions.Add(new(){Height=new(delta==0?2:1,GridUnitType.Star)});
            var shade=selected.Shade(delta);var button=new Button{MinWidth=0,MinHeight=0,Padding=new(0),HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,
                Content=new Border{Background=ToolUi.Brush(shade.Hex),HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch},HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,BorderThickness=new(0),CornerRadius=new(index==0?4:0,index==0?4:0,index==4?4:0,index==4?4:0)};
            AutomationProperties.SetName(button,delta==0?L.T("Fine-tune color"):shade.Hex);ToolTipService.SetToolTip(button,delta==0?L.T("Fine-tune color"):shade.Hex);
            if(delta==0)button.Flyout=fineTune;else button.Click+=(_,_)=>Remember(shade);
            Grid.SetRow(button,index++);shades.Children.Add(button);
        }
        foreach(var format in tools.Settings.ColorFormats.Where(item=>item.Enabled))
        {
            string text=ColorFormats.Format(selected,format.Format);
            var row=new Grid{MinHeight=48,ColumnSpacing=8,Padding=new(12,5,6,5)};row.ColumnDefinitions.Add(new(){Width=new(46)});row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var name=ToolUi.Label(format.Name,12);name.VerticalAlignment=VerticalAlignment.Center;name.TextTrimming=TextTrimming.CharacterEllipsis;ToolTipService.SetToolTip(name,format.Name);row.Children.Add(name);
            var valueText=new TextBlock{Text=text,FontSize=14,IsTextSelectionEnabled=true,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};ToolTipService.SetToolTip(valueText,text);Grid.SetColumn(valueText,1);row.Children.Add(valueText);
            var copy=IconButton("\uE8C8",L.F("Copy {0}",format.Name));copy.Click+=async(_,_)=>
            {try{await ToolUi.CopyTextAsync(ColorFormats.Format(tuning?draft:color,format.Format));tools.Remember(tuning?draft:color);RefreshHistory();state.Text=L.T("Copied to clipboard");}catch(Exception ex){await ToolUi.ErrorAsync(this,ex);}};
            Grid.SetColumn(copy,2);row.Children.Add(copy);var card=new Border{CornerRadius=new(5),BorderThickness=new(1),Child=row};formats.Children.Add(card);cards.Add((card,name));
        }
        PaintCards();
    }
    private void PaintCards()
    {
        bool dark=root.ActualTheme==ElementTheme.Dark;body.Background=ToolUi.Brush(dark?"#252525":"#FBFCFD");body.BorderBrush=ToolUi.Brush(dark?"#3E3E3E":"#DDE1E6");
        foreach(var (card,label) in cards){card.Background=ToolUi.Brush(dark?"#303030":"#FFFFFF");card.BorderBrush=ToolUi.Brush(dark?"#464646":"#E0E3E7");label.Foreground=ToolUi.Brush(dark?"#BFBFBF":"#72777D");}
    }
    private void BuildFineTune()
    {
        var panel=new StackPanel{Width=320,Spacing=9};panel.Children.Add(ToolUi.Label(L.T("Fine-tune color"),16));
        void Track(string name,Slider slider,Border track)
        {
            var row=new Grid{ColumnSpacing=10};row.ColumnDefinitions.Add(new(){Width=new(15)});row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
            var label=ToolUi.Label(name,12);label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);
            slider.Header=null;var transparent=new SolidColorBrush(Microsoft.UI.Colors.Transparent);slider.Resources["SliderTrackFill"]=transparent;slider.Resources["SliderTrackValueFill"]=transparent;
            var layers=new Grid();layers.Children.Add(track);layers.Children.Add(slider);Grid.SetColumn(layers,1);row.Children.Add(layers);panel.Children.Add(row);
        }
        var hueTrack=new Border{Height=6,CornerRadius=new(3),Margin=new(12,0,12,0)};
        var rainbow=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,0)};for(int i=0;i<=6;i++)rainbow.GradientStops.Add(new(){Offset=i/6d,Color=ToolUi.Brush(ColorFormats.FromHsv(i*60,100,100).Hex).Color});hueTrack.Background=rainbow;
        Track("H",hue,hueTrack);Track("S",saturation,saturationTrack);Track("V",value,valueTrack);
        var channels=new Grid{ColumnSpacing=8};for(int i=0;i<3;i++)channels.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        foreach(var (channel,column) in new[]{(red,0),(green,1),(blue,2)}){Grid.SetColumn(channel,column);channels.Children.Add(channel);}panel.Children.Add(channels);panel.Children.Add(hex);
        void HsvChanged(object sender,RangeBaseValueChangedEventArgs args){if(!syncing){draft=ColorFormats.FromHsv(hue.Value,saturation.Value,value.Value);UpdateFineTune();}}
        hue.ValueChanged+=HsvChanged;saturation.ValueChanged+=HsvChanged;value.ValueChanged+=HsvChanged;
        foreach(var channel in new[]{red,green,blue})channel.ValueChanged+=(_,_)=>{if(!syncing && new[]{red.Value,green.Value,blue.Value}.All(double.IsFinite)){draft=new((byte)red.Value,(byte)green.Value,(byte)blue.Value);UpdateFineTune();}};
        void CommitHex(){if(!syncing && ScreenColor.TryParse(hex.Text,out var selected)){draft=selected;UpdateFineTune();}else if(!syncing)hex.Text=draft.Hex;}
        hex.LostFocus+=(_,_)=>CommitHex();hex.KeyDown+=(_,e)=>{if(e.Key==VirtualKey.Enter){CommitHex();e.Handled=true;}};
        var select=ToolUi.Button("Select",()=>{CommitHex();fineTune.Hide();Remember(draft);});select.HorizontalAlignment=HorizontalAlignment.Stretch;panel.Children.Add(select);
        fineTune.Content=panel;fineTune.Opening+=(_,_)=>{tuning=true;draft=color;UpdateFineTune();};fineTune.Closed+=(_,_)=>{tuning=false;RenderColor(color);};
    }
    private void UpdateFineTune()
    {
        syncing=true;try{var hsv=ColorFormats.Hsv(draft);hue.Value=hsv.H;saturation.Value=hsv.S;value.Value=hsv.V;red.Value=draft.R;green.Value=draft.G;blue.Value=draft.B;hex.Text=draft.Hex;}finally{syncing=false;}
        LinearGradientBrush Gradient(ScreenColor first,ScreenColor last)
        {var brush=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,0)};brush.GradientStops.Add(new(){Offset=0,Color=ToolUi.Brush(first.Hex).Color});brush.GradientStops.Add(new(){Offset=1,Color=ToolUi.Brush(last.Hex).Color});return brush;}
        var currentHsv=ColorFormats.Hsv(draft);saturationTrack.Background=Gradient(ColorFormats.FromHsv(currentHsv.H,0,currentHsv.V),ColorFormats.FromHsv(currentHsv.H,100,currentHsv.V));valueTrack.Background=Gradient(new(0,0,0),ColorFormats.FromHsv(currentHsv.H,currentHsv.S,100));
        foreach(var (format,card) in tools.Settings.ColorFormats.Where(item=>item.Enabled).Zip(formats.Children.OfType<Border>()))
            if(card.Child is Grid row && row.Children[1] is TextBlock text){text.Text=ColorFormats.Format(draft,format.Format);ToolTipService.SetToolTip(text,text.Text);}
    }
    private void RefreshHistory()
    {
        history.Children.Clear();
        foreach(string hexColor in tools.Settings.ColorHistory)
        {
            var button=new Button{Width=30,Height=30,MinWidth=0,MinHeight=0,Padding=new(4),CornerRadius=new(15),BorderThickness=new(hexColor==color.Hex?2:0),BorderBrush=ToolUi.Brush(hexColor),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),Content=new Border{Width=18,Height=18,CornerRadius=new(9),Background=ToolUi.Brush(hexColor)}};
            AutomationProperties.SetName(button,hexColor);ToolTipService.SetToolTip(button,hexColor);button.Click+=(_,_)=>{if(ScreenColor.TryParse(hexColor,out var selected))Set(selected);};
            var menu=new MenuFlyout();var remove=new MenuFlyoutItem{Text=L.T("Remove color"),Icon=new FontIcon{Glyph="\uE74D"}};
            remove.Click+=(_,_)=>{tools.Settings.ColorHistory=tools.Settings.ColorHistory.Where(item=>item!=hexColor).ToArray();tools.Save();RefreshHistory();};menu.Items.Add(remove);
            var export=new MenuFlyoutSubItem{Text=L.T("Export colors")};
            foreach(bool byFormat in new[]{false,true}){var item=new MenuFlyoutItem{Text=L.T(byFormat?"Group by format":"Group by color")};item.Click+=async(_,_)=>await ExportAsync(byFormat);export.Items.Add(item);}menu.Items.Add(export);button.ContextFlyout=menu;history.Children.Add(button);
        }
    }
    private async Task ExportAsync(bool byFormat)
    {
        try
        {
            var picker=new FileSavePicker{SuggestedFileName="ClariBar-colors"};picker.FileTypeChoices.Add("JSON",new[]{".json"});picker.FileTypeChoices.Add(L.T("Text"),new[]{".txt"});WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
            if(await picker.PickSaveFileAsync() is {} file){await File.WriteAllTextAsync(file.Path,ColorFormats.Export(tools.Settings.ColorHistory,tools.Settings.ColorFormats,byFormat,file.FileType.Equals(".json",StringComparison.OrdinalIgnoreCase)));state.Text=L.F("Saved: {0}",file.Path);}
        }
        catch(Exception ex){await ToolUi.ErrorAsync(this,ex);}
    }
    private async void KeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(FocusManager.GetFocusedElement(root.XamlRoot) is TextBox)return;
        try
        {
            if(e.Key==VirtualKey.C && AnnotationSurface.Down(VirtualKey.Control)){await ToolUi.CopyTextAsync(ColorFormats.DefaultText(color,tools.Settings));tools.Remember(color);RefreshHistory();state.Text=L.T("Copied to clipboard");e.Handled=true;}
            else if(e.Key==VirtualKey.Escape){Close();e.Handled=true;}
        }
        catch(Exception ex){await ToolUi.ErrorAsync(this,ex);}
    }
}
