using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace ClariBar.App;

internal sealed partial class CaptureOverlay
{
    private readonly Border pickerSwatch=new(){Width=48,Height=48,CornerRadius=new(5)};
    private readonly TextBlock pickerValue=new(){FontSize=16,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,MaxWidth=250,TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly TextBlock pickerName=new(){FontSize=12};
    private readonly string? colorActionOverride;
    private string pickerAction="Edit";
    private int pickerZoom;
    private void BuildColorPickerInfo(StackPanel labels)
    {
        bool dark=tools.Theme==ElementTheme.Dark || tools.Theme==ElementTheme.Default && Application.Current.RequestedTheme==ApplicationTheme.Dark;
        info.Background=ToolUi.Brush(dark?"#272727":"#FCFCFC");info.BorderBrush=ToolUi.Brush(dark?"#686868":"#AAB0B8");info.BorderThickness=new(1);info.CornerRadius=new(8);info.Padding=new(7);
        pickerValue.Foreground=ToolUi.Brush(dark?"#F4F4F4":"#222222");pickerName.Foreground=ToolUi.Brush(dark?"#B7B7B7":"#6C7178");
        var row=new Grid{ColumnSpacing=10};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.Children.Add(pickerSwatch);
        var text=new StackPanel{Spacing=3,VerticalAlignment=VerticalAlignment.Center};text.Children.Add(pickerValue);text.Children.Add(pickerName);Grid.SetColumn(text,1);row.Children.Add(text);
        labels.Children.Remove(details);labels.Children.Add(row);zoomPanel.Visibility=Visibility.Collapsed;
        zoomPanel.Width=zoomPanel.Height=zoom.Width=zoom.Height=192;
        root.PointerWheelChanged+=(_,e)=>{pickerZoom=Math.Clamp(pickerZoom+(e.GetCurrentPoint(root).Properties.MouseWheelDelta>0?1:-1),0,5);UpdatePickerInfo(last);e.Handled=true;};
    }
    private string PickerText(ScreenColor selected)=>(colorFormat%4) switch
    {1=>selected.Rgb,2=>selected.Hsl,3=>ColorFormats.Format(selected,"hsv(%Hu, %Sb%, %Va%)"),_=>ColorFormats.DefaultText(selected,tools.Settings)};
    private void UpdatePickerInfo(Point p)
    {
        int px=(int)p.X,py=(int)p.Y;var selected=frame.ColorAt(px,py);pickerSwatch.Background=ToolUi.Brush(selected.Hex);pickerValue.Text=PickerText(selected);
        pickerName.Text=ColorFormats.Name(selected);pickerName.Visibility=tools.Settings.ColorShowName?Visibility.Visible:Visibility.Collapsed;
        zoomPanel.Visibility=pickerZoom>0?Visibility.Visible:Visibility.Collapsed;
        if(pickerZoom>0)
        {
            int factor=new[]{4,8,12,16,24}[pickerZoom-1];zoom.Source=frame.Magnify(px,py,192,factor).Bitmap();
            if(zoomPanel.Children[1] is Border center)center.Width=center.Height=factor;
        }
        info.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        double xPos=p.X/ScaleX+22,yPos=p.Y/ScaleY+22;
        if(xPos+info.DesiredSize.Width>root.ActualWidth-8)xPos=p.X/ScaleX-info.DesiredSize.Width-22;
        if(yPos+info.DesiredSize.Height>root.ActualHeight-8)yPos=p.Y/ScaleY-info.DesiredSize.Height-22;
        Canvas.SetLeft(info,Math.Max(8,xPos));Canvas.SetTop(info,Math.Max(8,yPos));
    }
    private void PickerPressed(PointerRoutedEventArgs e)
    {
        var buttons=e.GetCurrentPoint(root).Properties;
        string? action=buttons.IsRightButtonPressed?tools.Settings.ColorRightAction:buttons.IsMiddleButtonPressed?tools.Settings.ColorMiddleAction:buttons.IsLeftButtonPressed?colorActionOverride??tools.Settings.ColorLeftAction:null;
        if(action is null)return;
        if(action=="Cancel"){Finish(null);e.Handled=true;return;}
        pickerAction=action;dragging=true;root.CapturePointer(e.Pointer);e.Handled=true;
    }
    private void CompletePicker(string action)
    {if(action=="Cancel")Finish(null);else Finish(new(Color:frame.ColorAt((int)last.X,(int)last.Y),Action:action));}
}
