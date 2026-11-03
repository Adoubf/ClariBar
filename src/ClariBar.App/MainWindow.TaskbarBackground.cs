using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using L = ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private void BuildTaskbarBackground()
    {
        bool opacityMode=settings.TaskbarBackgroundMode=="Tinted";
        var style=new ComboBox
        {
            ItemsSource=new[]{L.T("Follow system"),L.T("Transparent")},
            SelectedIndex=settings.TaskbarBackgroundMode=="Transparent"?1:0,MinWidth=160,IsEnabled=!opacityMode
        };
        taskbarStyleSelector=style;
        var useStyle=new RadioButton {Content=L.T("Use style"),GroupName="ClariBarTaskbarBackground",IsChecked=!opacityMode,VerticalAlignment=VerticalAlignment.Center};
        var useOpacity=new RadioButton {Content=L.T("Use opacity"),GroupName="ClariBarTaskbarBackground",IsChecked=opacityMode,VerticalAlignment=VerticalAlignment.Center};
        taskbarStyleOption=useStyle;taskbarOpacityOption=useOpacity;
        var styleControls=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};
        styleControls.Children.Add(style);styleControls.Children.Add(useStyle);
        Row(L.T("Background style"),L.T("Choose the Windows background or full transparency. This option and background opacity are mutually exclusive."),styleControls,"\uE790");
        var opacity=new Slider {Minimum=0,Maximum=100,Value=settings.TaskbarOpacity,StepFrequency=1,Width=200,IsEnabled=opacityMode};
        taskbarOpacitySlider=opacity;
        var opacityControls=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};
        opacityControls.Children.Add(opacity);opacityControls.Children.Add(useOpacity);
        Row(L.T("Background opacity"),L.T("Use a custom background color and opacity instead of a background style. 0% is transparent; 100% is solid."),opacityControls,"\uE790");
        var color=AddColorPicker(L.T("Taskbar background color"),settings.TaskbarBackgroundColor,hex=>settings.TaskbarBackgroundColor=hex);
        color.IsEnabled=opacityMode;
        void RefreshControls()
        {
            bool tinted=settings.TaskbarBackgroundMode=="Tinted";
            style.IsEnabled=!tinted;opacity.IsEnabled=tinted;color.IsEnabled=tinted;
        }
        useStyle.Checked+=(_,_)=>
        {
            settings.TaskbarBackgroundMode=style.SelectedIndex==1?"Transparent":"System";
            RefreshControls();TaskbarChanged();
        };
        useOpacity.Checked+=(_,_)=>
        {
            settings.TaskbarBackgroundMode="Tinted";
            RefreshControls();TaskbarChanged();
        };
        style.SelectionChanged+=(_,_)=>
        {
            if(useStyle.IsChecked!=true)return;
            settings.TaskbarBackgroundMode=style.SelectedIndex==1?"Transparent":"System";TaskbarChanged();
        };
        opacity.ValueChanged+=(_,args)=>
        {
            if(useOpacity.IsChecked!=true)return;
            settings.TaskbarOpacity=(int)args.NewValue;TaskbarChanged();
        };
        Responsive(width=>
        {
            bool compact=width<480;
            styleControls.Orientation=opacityControls.Orientation=compact?Orientation.Vertical:Orientation.Horizontal;
        });
    }
}
