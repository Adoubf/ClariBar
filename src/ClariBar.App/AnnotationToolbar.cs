using ClariBar.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed class AnnotationStyle
{
    public string Tool {get;set;}="";
    public string Color {get;set;}="#FF5555";
    public int Width {get;set;}=3;
    public int Alpha {get;set;}=255;
    public int MarkerAlpha {get;set;}=100;
    public string Fill {get;set;}="Outline";
    public string BrushMode {get;set;}="Brush";
    public string FontFamily {get;set;}="Microsoft YaHei UI";
    public float FontSize {get;set;}=22;
    public bool Bold {get;set;}
    public bool Italic {get;set;}
    public float Rotation {get;set;}
    public void Apply(CaptureMark mark)
    {
        Tool=mark.Tool;Color=mark.Color;Width=mark.Width;
        if(mark.Tool=="Marker")MarkerAlpha=mark.Alpha;else Alpha=mark.Alpha;
        if(mark.Tool is "Marker" or "Mosaic" or "Blur" or "Eraser")BrushMode=mark.Fill;else Fill=mark.Fill;
        FontFamily=mark.FontFamily;FontSize=mark.FontSize;Bold=mark.Bold;Italic=mark.Italic;Rotation=mark.Rotation;
    }
    public CaptureMark Mark(Windows.Foundation.Point[] points,string text="") => new(Tool,points,Color,Width,text)
    {Alpha=Tool=="Marker"?MarkerAlpha:Alpha,Fill=Tool is "Mosaic" or "Blur" or "Eraser" or "Marker"?BrushMode:Fill,FontFamily=FontFamily,FontSize=FontSize,Bold=Bold,Italic=Italic,Rotation=Rotation};
}

internal sealed class AnnotationToolbar:UserControl
{
    internal const double PreferredWidth=620;
    internal const double PreferredHeight=112;
    internal const double CompactWidth=600;
    private readonly AnnotationSurface surface;
    private readonly Func<string,Task> action;
    private readonly Grid layout=new();
    private readonly StackPanel toolsRow=new(){Orientation=Orientation.Horizontal,Spacing=2};
    private readonly StackPanel actionsRow=new(){Orientation=Orientation.Horizontal,Spacing=2};
    private readonly StackPanel properties=new(){Orientation=Orientation.Horizontal,Spacing=7};
    private readonly StackPanel variants=new(){Orientation=Orientation.Horizontal,Spacing=2};
    private readonly Grid propertyRow=new(){ColumnSpacing=6,Margin=new(0,6,0,0)};
    private readonly Grid palette=new(){ColumnSpacing=3,RowSpacing=3,VerticalAlignment=VerticalAlignment.Center};
    private readonly Dictionary<string,ToggleButton> groups=[];
    private readonly Dictionary<string,ToggleButton> variantButtons=[];
    private readonly Dictionary<string,string> remembered=new(){["Shapes"]="Rectangle",["Lines"]="Arrow",["Effects"]="Mosaic"};
    private readonly List<(string Color,Button Button)> swatches=[];
    private readonly Border colorChip=new(){Width=18,Height=18,CornerRadius=new(4),BorderBrush=ToolUi.Brush("#DDE5EB"),BorderThickness=new(.75)};
    private readonly TextBlock widthLabel=ToolUi.Label("3 px",12);
    private readonly TextBlock fillLabel=ToolUi.Label("",12);
    private readonly Button widthButton,fillButton,styleButton,undoButton,redoButton;
    private readonly ColorPicker picker;
    private readonly Slider alpha,rotation;
    private readonly NumberBox textSize;
    private readonly ComboBox fonts;
    private readonly ToggleButton bold,italic;
    private readonly StackPanel textOptions;
    private readonly TextBlock opacityLabel=ToolUi.Label("100%",12);
    private readonly Style buttonStyle,toggleStyle;
    private bool syncing;
    internal IReadOnlyList<Button> PrimaryActions {get;}
    internal bool IsCompact {get;private set;}
    internal bool IsPinned {get;private set;}

    public AnnotationToolbar(AnnotationSurface surface,Func<string,Task> action,bool close=true,bool pinned=false)
    {
        this.surface=surface;this.action=action;RequestedTheme=ElementTheme.Dark;MaxWidth=PreferredWidth;
        ProtectedCursor=InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        buttonStyle=CreateStyle("Button");toggleStyle=CreateStyle("ToggleButton");
        layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        for(int i=0;i<3;i++)layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var card=new Border {Background=ToolUi.Brush("#232A32"),BorderBrush=ToolUi.Brush("#4A5663"),BorderThickness=new(1),CornerRadius=new(11),Padding=new(8),Child=layout};
        var shell=new Grid {Padding=new(3,3,3,6)};
        shell.Children.Add(new Border {Background=new SolidColorBrush(Windows.UI.Color.FromArgb(90,0,0,0)),CornerRadius=new(13),Margin=new(-2,3,-2,-3)});
        shell.Children.Add(card);Content=shell;
        var toolViewport=new ScrollViewer {Content=toolsRow,HorizontalScrollMode=ScrollMode.Enabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};layout.Children.Add(toolViewport);
        foreach(string group in new[]{"Move","Shapes","Lines","Pen","Marker","Effects","Text","Eraser"})
        {
            var button=new ToggleButton {Style=toggleStyle,Content=Icon(DefaultTool(group)),IsChecked=group=="Move"};
            string label=GroupLabel(group);Describe(button,label);button.Click+=(_,_)=>SelectTool(group=="Move" || groups[group].IsChecked!=true?"":DefaultTool(group));
            toolsRow.Children.Add(button);groups[group]=button;
        }
        actionsRow.Children.Add(Separator());
        undoButton=ActionButton("Undo","Undo","Ctrl+Z");redoButton=ActionButton("Redo","Redo","Ctrl+Y");
        actionsRow.Children.Add(undoButton);actionsRow.Children.Add(redoButton);actionsRow.Children.Add(Separator());
        var primary=new List<Button>();
        if(close){var cancel=ActionButton("Close","Close","Esc");cancel.Style=CreateStyle("Button","#B54150","#8F3040");actionsRow.Children.Add(cancel);primary.Add(cancel);}
        foreach(var (name,key,shortcut) in new[]{("Pin image","Pin","Ctrl+T"),("Save","Save","Ctrl+S"),("Copy","Copy","Enter / Ctrl+C"),("OCR","OCR","")})
        {
            var button=ActionButton(name,key,shortcut);
            actionsRow.Children.Add(button);primary.Add(button);
        }
        var more=new Button {Style=buttonStyle,Content=Icon("More")};Describe(more,"More actions");var extra=new MenuFlyout {Placement=FlyoutPlacementMode.BottomEdgeAlignedRight,ShouldConstrainToRootBounds=false};
        foreach(var (text,key,shortcut) in new[]{("Quick save","QuickSave","Ctrl+Shift+S"),("Print","Print","Ctrl+P"),("Clear annotations","Clear","Ctrl+Shift+Z")})
        {var entry=new MenuFlyoutItem {Text=L.T(text),KeyboardAcceleratorTextOverride=shortcut};entry.Click+=async(_,_)=>await RunAsync(key);extra.Items.Add(entry);}more.Flyout=extra;actionsRow.Children.Add(more);
        Grid.SetColumn(actionsRow,1);layout.Children.Add(actionsRow);PrimaryActions=primary;SetPinned(pinned);

        propertyRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var propertyViewport=new ScrollViewer {Content=properties,HorizontalScrollMode=ScrollMode.Enabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};propertyRow.Children.Add(propertyViewport);
        Grid.SetRow(propertyRow,1);Grid.SetColumnSpan(propertyRow,2);layout.Children.Add(propertyRow);
        properties.Children.Add(variants);
        foreach(string tool in new[]{"Rectangle","Ellipse","Line","Polyline","Arrow","Mosaic","Blur"})
        {var button=new ToggleButton {Style=toggleStyle,Width=28,Height=28,Content=Icon(tool)};Describe(button,tool);button.Click+=(_,_)=>SelectTool(tool);variants.Children.Add(button);variantButtons[tool]=button;}
        widthButton=SmallMenu(widthLabel,"Stroke width");
        var widths=new MenuFlyout {Placement=FlyoutPlacementMode.Bottom,ShouldConstrainToRootBounds=false};foreach(int pixels in new[]{1,2,3,4,6,8,12,16,24,32})
        {var item=new MenuFlyoutItem {Text=pixels+" px"};item.Click+=(_,_)=>{surface.Ink.Width=pixels;surface.RestyleActive();};widths.Items.Add(item);}widthButton.Flyout=widths;properties.Children.Add(widthButton);
        fillButton=SmallMenu(fillLabel,"Fill style");properties.Children.Add(fillButton);
        properties.Children.Add(Separator());
        picker=new ColorPicker {Color=Windows.UI.Color.FromArgb(255,255,85,85),Width=280,IsAlphaEnabled=false,IsColorChannelTextInputVisible=true};
        picker.ColorChanged+=(_,e)=>{if(syncing)return;surface.Ink.Color=$"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";surface.RestyleActive();};
        var color=new Button {Style=buttonStyle,Width=30,Height=30,Content=colorChip,Flyout=new Flyout {Content=picker,Placement=FlyoutPlacementMode.Bottom,ShouldConstrainToRootBounds=false}};Describe(color,"Custom color");properties.Children.Add(color);
        string[] colors=["#FF5555","#FF9D50","#FFD35B","#67D89C","#56CDD9","#5AA8FF","#B095F5","#FFFFFF","#AD374B","#A8643E","#A88B29","#34845E","#237E8B","#3568AD","#7755A6","#20262E"];
        for(int i=0;i<8;i++)palette.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        for(int i=0;i<2;i++)palette.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var swatchStyle=CreateStyle("Button",swatch:true);
        for(int i=0;i<colors.Length;i++)
        {
            string hex=colors[i];var button=new Button {Style=swatchStyle,Width=15,Height=13,MinWidth=0,MinHeight=0,CornerRadius=new(2),Padding=new(1),Background=ToolUi.Brush(hex),BorderBrush=ToolUi.Brush("#596572"),BorderThickness=new(.5),Content=null};
            AutomationProperties.SetName(button,L.T("Color")+" "+hex);ToolTipService.SetToolTip(button,hex);button.Click+=(_,_)=>{surface.Ink.Color=hex;surface.RestyleActive();};
            Grid.SetRow(button,i/8);Grid.SetColumn(button,i%8);palette.Children.Add(button);swatches.Add((hex,button));
        }
        properties.Children.Add(palette);
        var style=new StackPanel {Spacing=12,Width=270};
        var opacityHeader=new Grid();opacityHeader.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});opacityHeader.ColumnDefinitions.Add(new(){Width=GridLength.Auto});opacityHeader.Children.Add(ToolUi.Label(L.T("Opacity"),12));Grid.SetColumn(opacityLabel,1);opacityHeader.Children.Add(opacityLabel);
        alpha=new Slider {Header=opacityHeader,Minimum=0,Maximum=100,Value=100,StepFrequency=1};alpha.ValueChanged+=(_,e)=>{if(syncing)return;int value=(int)Math.Round(e.NewValue*255/100);if(surface.Ink.Tool=="Marker")surface.Ink.MarkerAlpha=value;else surface.Ink.Alpha=value;surface.RestyleActive();};style.Children.Add(alpha);
        textOptions=new StackPanel {Spacing=10};string[] families;try{families=Native.FontFamilies();}catch{families=["Microsoft YaHei UI","Segoe UI","Arial","Consolas"];}
        fonts=new ComboBox {Header=L.T("Font"),ItemsSource=families,HorizontalAlignment=HorizontalAlignment.Stretch};fonts.SelectionChanged+=(_,_)=>{if(!syncing && fonts.SelectedItem is string family){surface.Ink.FontFamily=family;surface.RestyleActive();}};textOptions.Children.Add(fonts);
        textSize=new NumberBox {Header=L.T("Text size"),Minimum=6,Maximum=300,Value=22,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Inline};textSize.ValueChanged+=(_,e)=>{if(!syncing && double.IsFinite(e.NewValue)){surface.Ink.FontSize=(float)e.NewValue;surface.RestyleActive();}};textOptions.Children.Add(textSize);
        var emphasis=new StackPanel {Orientation=Orientation.Horizontal,Spacing=6};bold=new ToggleButton {Style=toggleStyle,Content=new TextBlock {Text="B",FontWeight=Microsoft.UI.Text.FontWeights.Bold,FontSize=16}};italic=new ToggleButton {Style=toggleStyle,Content=new TextBlock {Text="I",FontStyle=Windows.UI.Text.FontStyle.Italic,FontSize=16}};Describe(bold,"Bold");Describe(italic,"Italic");
        bold.Click+=(_,_)=>{surface.Ink.Bold=bold.IsChecked==true;surface.RestyleActive();};italic.Click+=(_,_)=>{surface.Ink.Italic=italic.IsChecked==true;surface.RestyleActive();};emphasis.Children.Add(bold);emphasis.Children.Add(italic);textOptions.Children.Add(emphasis);
        rotation=new Slider {Header=L.T("Text rotation"),Minimum=-180,Maximum=180,StepFrequency=1};rotation.ValueChanged+=(_,e)=>{if(!syncing){surface.Ink.Rotation=(float)e.NewValue;surface.RestyleActive();}};textOptions.Children.Add(rotation);style.Children.Add(textOptions);
        styleButton=new Button {Style=buttonStyle,Width=28,Height=28,Content=Icon("Style"),Flyout=new Flyout {Content=style,Placement=FlyoutPlacementMode.Bottom,ShouldConstrainToRootBounds=false}};Describe(styleButton,"Annotation style");properties.Children.Add(styleButton);
        surface.InkChanged+=Sync;surface.Document.Changed+=UpdateActions;SizeChanged+=(_,_)=>Reflow();Loaded+=(_,_)=>Reflow();Sync();
    }
    private string DefaultTool(string group)=>group switch {"Move"=>"", "Shapes" or "Lines" or "Effects"=>remembered[group],_=>group};
    private static string GroupFor(string tool)=>tool switch {""=>"Move","Rectangle" or "Ellipse"=>"Shapes","Line" or "Polyline" or "Arrow"=>"Lines","Mosaic" or "Blur"=>"Effects",_=>tool};
    private static string GroupLabel(string group)=>group switch {"Move"=>"Move","Shapes"=>"Shapes","Lines"=>"Lines and arrows","Effects"=>"Mosaic and blur","Marker"=>"Marker pen",_=>group};
    private static void Describe(ButtonBase button,string label,string shortcut="")
    {AutomationProperties.SetName(button,L.T(label));ToolTipService.SetToolTip(button,L.T(label)+(shortcut.Length>0?"\n"+shortcut:""));}
    private Border Separator()=>new(){Width=1,Height=18,Margin=new(4,0,4,0),Background=ToolUi.Brush("#46515E"),VerticalAlignment=VerticalAlignment.Center};
    private Button SmallMenu(TextBlock label,string name)
    {
        label.Foreground=ToolUi.Brush("#DCE5ED");label.VerticalAlignment=VerticalAlignment.Center;
        var panel=new StackPanel {Orientation=Orientation.Horizontal,Spacing=6};panel.Children.Add(label);panel.Children.Add(new FontIcon {Glyph="\uE70D",FontSize=8});
        var button=new Button {Style=buttonStyle,Width=double.NaN,Height=28,Padding=new(7,3,7,3),Content=panel,Background=ToolUi.Brush("#303A45")};Describe(button,name);return button;
    }
    private Button ActionButton(string label,string key,string shortcut)
    {
        var button=new Button {Style=buttonStyle,Content=Icon(key),Tag=key};Describe(button,label,shortcut);
        button.Click+=async(_,_)=>await RunAsync(key=="Save" && AnnotationSurface.Down(Windows.System.VirtualKey.Shift)?"QuickSave":key);return button;
    }
    internal void SetPinned(bool pinned)
    {
        IsPinned=pinned;var button=PrimaryActions.Single(button=>(string?)button.Tag=="Pin");
        button.Style=pinned?CreateStyle("Button","#427E8B","#2B5964"):buttonStyle;
        button.Background=pinned?ToolUi.Brush("#376C78"):new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.BorderBrush=pinned?ToolUi.Brush("#6DCED8"):new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AutomationProperties.SetItemStatus(button,L.T(pinned?"Pinned":"Not pinned"));
    }
    private async Task RunAsync(string key)
    {try{surface.FinishText();if(key=="Undo")surface.Undo();else if(key=="Redo")surface.Redo();else await action(key);}catch(Exception ex){await surface.ShowErrorAsync(ex);}}
    private static Style CreateStyle(string type,string hover="#3B4855",string pressed="#485A69",bool swatch=false)
    {
        // XamlReader's deferred visual states must not depend on resources in a later host's namescope.
        string hoverSetter=swatch?"<Setter Target='Chrome.Opacity' Value='.8'/>":$"<Setter Target='Chrome.Background' Value='{hover}'/>";
        string pressedSetter=swatch?"<Setter Target='Chrome.Opacity' Value='.65'/>":$"<Setter Target='Chrome.Background' Value='{pressed}'/>";
        string checkedStates=type=="ToggleButton"?"<VisualState x:Name='Checked'><VisualState.Setters><Setter Target='Chrome.Background' Value='#376C78'/><Setter Target='Chrome.BorderBrush' Value='#6DCED8'/></VisualState.Setters></VisualState><VisualState x:Name='CheckedPointerOver'><VisualState.Setters><Setter Target='Chrome.Background' Value='#427E8B'/><Setter Target='Chrome.BorderBrush' Value='#8ADFE6'/></VisualState.Setters></VisualState><VisualState x:Name='CheckedPressed'><VisualState.Setters><Setter Target='Chrome.Background' Value='#2B5964'/></VisualState.Setters></VisualState><VisualState x:Name='CheckedDisabled'><VisualState.Setters><Setter Target='Chrome.Opacity' Value='.35'/></VisualState.Setters></VisualState>":"";
        return (Style)XamlReader.Load("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='"+type+"'><Setter Property='Width' Value='32'/><Setter Property='Height' Value='32'/><Setter Property='MinWidth' Value='0'/><Setter Property='MinHeight' Value='0'/><Setter Property='Padding' Value='6'/><Setter Property='Foreground' Value='#DEE7EF'/><Setter Property='Background' Value='Transparent'/><Setter Property='BorderBrush' Value='Transparent'/><Setter Property='BorderThickness' Value='1'/><Setter Property='CornerRadius' Value='6'/><Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='UseSystemFocusVisuals' Value='True'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='"+type+"'><Border x:Name='Chrome' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='{TemplateBinding CornerRadius}'><VisualStateManager.VisualStateGroups><VisualStateGroup x:Name='CommonStates'><VisualState x:Name='Normal'/><VisualState x:Name='PointerOver'><VisualState.Setters>"+hoverSetter+"</VisualState.Setters></VisualState><VisualState x:Name='Pressed'><VisualState.Setters>"+pressedSetter+"</VisualState.Setters></VisualState><VisualState x:Name='Disabled'><VisualState.Setters><Setter Target='Chrome.Opacity' Value='.3'/></VisualState.Setters></VisualState>"+checkedStates+"</VisualStateGroup></VisualStateManager.VisualStateGroups><ContentPresenter Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' Padding='{TemplateBinding Padding}' Foreground='{TemplateBinding Foreground}' HorizontalContentAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalContentAlignment='{TemplateBinding VerticalContentAlignment}'/></Border></ControlTemplate></Setter.Value></Setter></Style>");
    }
    private static FrameworkElement Icon(string tool)
    {
        string path=tool switch
        {
            ""=>"M10,0 L14,4 H11 V9 H16 V6 L20,10 L16,14 V11 H11 V16 H14 L10,20 L6,16 H9 V11 H4 V14 L0,10 L4,6 V9 H9 V4 H6Z",
            "Rectangle"=>"F0 M1,3 H19 V17 H1Z M3,5 V15 H17 V5Z",
            "Ellipse"=>"F0 M10,1 A9,9 0 1 1 9.99,1Z M10,3 A7,7 0 1 0 10.01,3Z",
            "Line"=>"M2,16 L16,2 L18,4 L4,18Z",
            "Polyline"=>"M1,14 L6,3 L12,12 L17,3 L19,4 L12,17 L6,8 L3,15Z",
            "Arrow"=>"M2,16 L14,4 H8 V2 H18 V12 H16 V6 L4,18Z",
            "Pen"=>"F0 M2,14 L14,2 L18,6 L6,18 L1,19Z M4,14 L6,16 L15,7 L13,5Z",
            "Marker"=>"M1,17 L4,14 L6,16 L3,19Z M4,11 L13,2 L19,8 L10,17Z",
            "Text"=>"M2,2 H18 V4 H11 V18 H9 V4 H2Z",
            "Mosaic"=>"M1,1 H6 V6 H1Z M8,1 H13 V6 H8Z M15,1 H20 V6 H15Z M1,8 H6 V13 H1Z M15,8 H20 V13 H15Z M8,15 H13 V20 H8Z M15,15 H20 V20 H15Z M1,15 H6 V20 H1Z",
            "Blur"=>"M1,2 H3 V4 H1Z M5,2 H7 V4 H5Z M9,2 H19 V4 H9Z M1,9 H5 V11 H1Z M7,9 H9 V11 H7Z M11,9 H19 V11 H11Z M1,16 H3 V18 H1Z M5,16 H9 V18 H5Z M11,16 H19 V18 H11Z",
            "Eraser"=>"F0 M1,12 L12,1 L20,9 L10,19 H7Z M4,12 L9,17 L13,13 L8,8Z",
            "Copy"=>"F0 M6,1 H19 V15 H16 V19 H1 V5 H6Z M3,7 V17 H14 V7Z M8,3 V5 H16 V13 H17 V3Z",
            "Save"=>"F0 M2,1 H15 L19,5 V19 H1 V1Z M4,3 V8 H14 V3Z M5,12 V17 H15 V12Z",
            "Pin"=>"M7,1 H17 V3 H15 L13,9 L16,12 L14,14 L10,12 L4,19 L3,18 L8,10 L5,7 L8,5 L9,3 H7Z",
            "Close"=>"M3,2 L10,8 L17,2 L19,4 L12,10 L19,17 L17,19 L10,12 L3,19 L1,17 L8,10 L1,4Z",
            "Undo"=>"M1,7 L7,1 V5 H12 Q20,5 20,13 V17 H18 V13 Q18,7 12,7 H7 V11Z",
            "Redo"=>"M19,7 L13,1 V5 H8 Q0,5 0,13 V17 H2 V13 Q2,7 8,7 H13 V11Z",
            "OCR"=>"M1,1 H7 V3 H3 V7 H1Z M13,1 H19 V7 H17 V3 H13Z M1,13 H3 V17 H7 V19 H1Z M17,13 H19 V19 H13 V17 H17Z M6,5 H14 V7 H11 V15 H9 V7 H6Z",
            "Style"=>"M1,4 H6 V6 H1Z M10,4 H19 V6 H10Z M6,2 H10 V8 H6Z M1,14 H12 V16 H1Z M16,14 H19 V16 H16Z M12,12 H16 V18 H12Z",
            _=>"M2,8 H5 V11 H2Z M8,8 H11 V11 H8Z M14,8 H17 V11 H14Z"
        };
        var icon=(PathIcon)XamlReader.Load("<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='"+path+"'/>");icon.Width=icon.Height=20;
        return new Viewbox {Width=16,Height=16,Child=icon,Stretch=Stretch.Uniform};
    }
    private void Reflow()
    {
        double width=ActualWidth>0?ActualWidth:PreferredWidth;bool compact=width<CompactWidth;IsCompact=compact;
        Grid.SetRow(actionsRow,compact?2:0);Grid.SetColumn(actionsRow,compact?0:1);Grid.SetColumnSpan(actionsRow,compact?2:1);actionsRow.HorizontalAlignment=HorizontalAlignment.Right;
        actionsRow.Margin=compact?new Thickness(0,6,0,0):new Thickness(0);
        palette.Visibility=width>=480?Visibility.Visible:Visibility.Collapsed;
    }
    private void UpdateActions(){undoButton.IsEnabled=surface.Document.CanUndo;redoButton.IsEnabled=surface.Document.CanRedo;}
    private void Sync()
    {
        if(syncing)return;syncing=true;
        try
        {
            string group=GroupFor(surface.Ink.Tool);foreach(var pair in groups)pair.Value.IsChecked=pair.Key==group;
            foreach(var pair in variantButtons){pair.Value.Visibility=GroupFor(pair.Key)==group?Visibility.Visible:Visibility.Collapsed;pair.Value.IsChecked=pair.Key==surface.Ink.Tool;}
            variants.Visibility=group is "Shapes" or "Lines" or "Effects"?Visibility.Visible:Visibility.Collapsed;
            propertyRow.Visibility=surface.Ink.Tool==""?Visibility.Collapsed:Visibility.Visible;
            widthLabel.Text=surface.Ink.Width+" px";widthButton.Visibility=surface.Ink.Tool=="Text"?Visibility.Collapsed:Visibility.Visible;
            bool effects=surface.Ink.Tool is "Marker" or "Mosaic" or "Blur" or "Eraser";
            fillButton.Visibility=effects || group=="Shapes"?Visibility.Visible:Visibility.Collapsed;
            fillLabel.Text=L.T(effects?(surface.Ink.BrushMode=="Rectangle"?"Region":"Brush"):surface.Ink.Fill switch{"Filled"=>"Filled","Bordered"=>"Fill with stroke",_=>"Outline"});
            var fill=new MenuFlyout {Placement=FlyoutPlacementMode.Bottom,ShouldConstrainToRootBounds=false};string[] labels=effects?["Brush","Region"]:["Outline","Filled","Fill with stroke"];
            for(int i=0;i<labels.Length;i++){int index=i;var entry=new MenuFlyoutItem {Text=L.T(labels[i])};entry.Click+=(_,_)=>{if(effects)surface.Ink.BrushMode=index==0?"Brush":"Rectangle";else surface.Ink.Fill=new[]{"Outline","Filled","Bordered"}[index];surface.RestyleActive();};fill.Items.Add(entry);}fillButton.Flyout=fill;
            colorChip.Background=ToolUi.Brush(surface.Ink.Color);picker.Color=((SolidColorBrush)colorChip.Background).Color;
            foreach(var (color,button) in swatches){button.BorderThickness=new(color.Equals(surface.Ink.Color,StringComparison.OrdinalIgnoreCase)?2:.5);button.BorderBrush=ToolUi.Brush(color.Equals(surface.Ink.Color,StringComparison.OrdinalIgnoreCase)?"#FFFFFF":"#596572");}
            alpha.Value=(surface.Ink.Tool=="Marker"?surface.Ink.MarkerAlpha:surface.Ink.Alpha)*100d/255;opacityLabel.Text=Math.Round(alpha.Value)+"%";
            textOptions.Visibility=surface.Ink.Tool=="Text"?Visibility.Visible:Visibility.Collapsed;fonts.SelectedItem=surface.Ink.FontFamily;textSize.Value=surface.Ink.FontSize;bold.IsChecked=surface.Ink.Bold;italic.IsChecked=surface.Ink.Italic;rotation.Value=surface.Ink.Rotation;
            UpdateActions();
        }
        finally{syncing=false;}
        Reflow();
    }
    public void SelectTool(string tool)
    {surface.FinishText();surface.EndEditing();surface.CancelInput();surface.Ink.Tool=tool;string group=GroupFor(tool);if(remembered.ContainsKey(group))remembered[group]=tool;Sync();surface.ToolChanged?.Invoke();}
    public void ChangeWidth(int direction){surface.Ink.Width=Math.Clamp(surface.Ink.Width+direction,1,100);surface.RestyleActive();}
}
