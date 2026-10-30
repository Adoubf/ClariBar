using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private DropDownButton CaptureModeButton()
    {
        var flyout=new MenuFlyout {Placement=FlyoutPlacementMode.BottomEdgeAlignedLeft};
        foreach(var (text,mode) in new[]{("Region capture","Region"),("Capture current screen","Screen"),("Capture entire desktop","FullScreen"),("Repeat last region","Last"),("Delayed capture","Delayed"),("Whiteboard","Whiteboard")})
        {var item=new MenuFlyoutItem {Text=L.T(text)};item.Click+=(_,_)=>_=desktopTools.BeginAsync(mode:mode);flyout.Items.Add(item);}
        var custom=new MenuFlyoutItem {Text=L.T("Custom capture")};custom.Click+=(_,_)=>_=ShowCustomCaptureAsync();flyout.Items.Add(custom);
        return new DropDownButton {Content=L.T("Capture"),Flyout=flyout};
    }
    private async Task ShowCustomCaptureAsync()
    {
        var desktop=DesktopCapture.DesktopBounds;DesktopCapture.GetCursorPos(out var cursor);
        var content=new StackPanel {Spacing=10};
        NumberBox Input(string text,double value,double min,double max)
        {var box=new NumberBox {Header=L.T(text),Value=value,Minimum=min,Maximum=max,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Inline,Width=270};content.Children.Add(box);return box;}
        var x=Input("Left (physical pixels)",Math.Max(desktop.X,cursor.X-settings.CapturePresetWidth/2),desktop.X,desktop.Right-1);
        var y=Input("Top (physical pixels)",Math.Max(desktop.Y,cursor.Y-settings.CapturePresetHeight/2),desktop.Y,desktop.Bottom-1);
        NumberBox width=Input("Width",settings.CapturePresetWidth,1,desktop.Width),height=Input("Height",settings.CapturePresetHeight,1,desktop.Height);
        var delay=Input("Capture delay (seconds)",settings.CaptureDelaySeconds,0,30);
        var dialog=new ContentDialog {Title=L.T("Custom capture"),Content=content,PrimaryButtonText=L.T("Capture"),CloseButtonText=L.T("Cancel"),DefaultButton=ContentDialogButton.Primary,XamlRoot=Root.XamlRoot};
        if(await dialog.ShowAsync()==ContentDialogResult.Primary && new[]{x.Value,y.Value,width.Value,height.Value,delay.Value}.All(double.IsFinite))
        {settings.CapturePresetWidth=(int)width.Value;settings.CapturePresetHeight=(int)height.Value;settings.CaptureDelaySeconds=(int)delay.Value;Save();await desktopTools.BeginAsync(mode:delay.Value>0?"Delayed":"Region",customArea:new PixelRect((int)x.Value,(int)y.Value,(int)width.Value,(int)height.Value));}
    }
    private void BuildCapturePreferences()
    {
        var body=new StackPanel {Spacing=12};
        void ToggleOption(string label,bool value,Action<bool> changed)
        {var control=new ToggleSwitch {Header=L.T(label),IsOn=value,OnContent=L.T("On"),OffContent=L.T("Off")};control.Toggled+=(_,_)=>{changed(control.IsOn);Save();};body.Children.Add(control);}
        void NumberOption(string label,int value,int min,int max,Action<int> changed)
        {var number=new NumberBox {Header=L.T(label),Minimum=min,Maximum=max,Value=value,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Inline,HorizontalAlignment=HorizontalAlignment.Left,Width=200};number.ValueChanged+=(_,e)=>{if(double.IsFinite(e.NewValue)){changed((int)Math.Clamp(e.NewValue,min,max));Save();}};body.Children.Add(number);}
        ToggleOption("Detect UI elements",settings.CaptureDetectElements,v=>settings.CaptureDetectElements=v);
        ToggleOption("Capture the cursor",settings.CaptureCursor,v=>settings.CaptureCursor=v);
        ToggleOption("Cancel capture when another app is activated",settings.CaptureCancelOnDeactivate,v=>settings.CaptureCancelOnDeactivate=v);
        ToggleOption("Automatically save completed captures",settings.CaptureAutoSave,v=>settings.CaptureAutoSave=v);
        ToggleOption("Play a sound after saving",settings.CaptureSound,v=>settings.CaptureSound=v);
        NumberOption("Capture delay (seconds)",settings.CaptureDelaySeconds,0,30,v=>settings.CaptureDelaySeconds=v);
        NumberOption("Capture history limit",settings.CaptureHistoryLimit,0,100,v=>{settings.CaptureHistoryLimit=v;desktopTools.Sessions.TrimHistory(v);});
        var formats=new[]{"png","jpg","bmp","webp"};var format=new ComboBox {Header=L.T("Quick-save format"),ItemsSource=formats.Select(x=>x.ToUpperInvariant()).ToArray(),SelectedIndex=Math.Max(0,Array.IndexOf(formats,settings.CaptureFormat)),MinWidth=150};
        format.SelectionChanged+=(_,_)=>{if(format.SelectedIndex>=0){settings.CaptureFormat=formats[format.SelectedIndex];Save();}};body.Children.Add(format);
        NumberOption("JPEG / WebP quality",settings.CaptureQuality,1,100,v=>settings.CaptureQuality=v);
        var naming=new TextBox {Header=L.T("File name pattern"),Text=settings.CaptureNamePattern,MaxLength=100,PlaceholderText="ClariBar-{date}-{time}",MaxWidth=420,HorizontalAlignment=HorizontalAlignment.Stretch};
        naming.LostFocus+=(_,_)=>{settings.CaptureNamePattern=naming.Text;Save();};body.Children.Add(naming);
        body.Children.Add(Description(L.T("Use {date}, {time} and {counter}. Ctrl+S opens Save As; Ctrl+Shift+S saves to the selected folder.")));
        body.Children.Add(Button(L.T("Clear capture history"),()=>desktopTools.Sessions.ClearHistory()));
        PageContent.Children.Add(SettingsIcons.Section(L.T("Capture preferences"),body));
        var pin=new StackPanel {Spacing=12};
        void PinToggle(string label,bool current,Action<bool> changed)
        {var toggle=new ToggleSwitch {Header=L.T(label),IsOn=current,OnContent=L.T("On"),OffContent=L.T("Off")};toggle.Toggled+=(_,_)=>{changed(toggle.IsOn);Save();};pin.Children.Add(toggle);}
        PinToggle("Restore images and groups at startup",settings.PinAutoRestore,v=>settings.PinAutoRestore=v);
        PinToggle("Preserve HTML formatting",!settings.PinIgnoreHtml,v=>settings.PinIgnoreHtml=!v);
        PinToggle("Convert copied file paths to text images",settings.PinFilePathsAsText,v=>settings.PinFilePathsAsText=v);
        PinToggle("Pin screenshots at their original position",settings.PinUseCapturePosition,v=>settings.PinUseCapturePosition=v);
        void PinNumber(string label,int value,int min,int max,Action<int> changed)
        {var number=new NumberBox {Header=L.T(label),Minimum=min,Maximum=max,Value=value,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Inline,HorizontalAlignment=HorizontalAlignment.Left,Width=200};number.ValueChanged+=(_,e)=>{if(double.IsFinite(e.NewValue)){changed((int)e.NewValue);Save();}};pin.Children.Add(number);}
        PinNumber("Recoverable closed images",settings.PinRecoverableCount,0,20,v=>settings.PinRecoverableCount=v);
        PinNumber("New image opacity (%)",settings.PinDefaultOpacity,10,100,v=>settings.PinDefaultOpacity=v);
        PinNumber("Thumbnail size (pixels)",settings.PinThumbnailSize,32,500,v=>settings.PinThumbnailSize=v);
        PageContent.Children.Add(SettingsIcons.Section(L.T("Pinned image preferences"),pin));
    }
}
