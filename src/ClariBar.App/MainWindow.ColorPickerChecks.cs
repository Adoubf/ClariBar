using System.Reflection;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private async Task CheckColorPickerRefinementsAsync(Action<bool,string> require)
    {
        string[] historyBefore=settings.ColorHistory;var formatsBefore=settings.ColorFormats;string defaultBefore=settings.ColorCopyFormat;
        var sample=new ScreenColor(80,190,210);bool cancel=settings.CaptureCancelOnDeactivate;settings.CaptureCancelOnDeactivate=false;
        static T Field<T>(object source,string name)=>(T)source.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(source)!;
        static async Task InvokeAsync(Button button)
        {var invoke=(IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke);invoke.Invoke();await Task.Delay(60);}
        ColorEditorWindow? editor=null;
        try
        {
            settings.ColorHistory=["#FFFFFF",sample.Hex,"#FF5555"];settings.ColorFormats=ColorFormats.Defaults();settings.ColorCopyFormat="HEX";
            string? selected=null;editor=new(desktopTools,sample,value=>selected=value);desktopTools.Track(editor);editor.Activate();await Task.Delay(200);
            var root=(FrameworkElement)editor.Content;var rows=Field<StackPanel>(editor,"formats");var shades=Field<Grid>(editor,"shades");var history=Field<StackPanel>(editor,"history");
            require(rows.Children.Count==4 && shades.Children.Count==5 && history.Children.Count==3,"PowerToys-style color editor shows four format cards, five shades and a horizontal history");
            require(!CaptureContainsColorSpectrum(root),"The full color spectrum no longer occupies the main color editor");
            double scale=DesktopWindowDpi.For(WinRT.Interop.WindowNative.GetWindowHandle(editor))/96d;
            foreach(int width in new[]{360,560})
            {
                editor.AppWindow.Resize(new((int)(width*scale),(int)(420*scale)));await Task.Delay(80);
                require(rows.Children.OfType<Border>().All(card=>{var button=(Button)((Grid)card.Child).Children[2];var at=button.TransformToVisual(root).TransformPoint(new());return button.ActualWidth>=30 && at.X>=0 && at.X+button.ActualWidth<=root.ActualWidth+1;}),$"Color format copy buttons stay inside the editor at width {width}");
            }
            await InvokeAsync((Button)((Grid)((Border)rows.Children[1]).Child).Children[2]);
            require(await Clipboard.GetContent().GetTextAsync()==sample.Rgb,"RGB card copies its exact displayed value into the real clipboard");
            var tune=Field<Flyout>(editor,"fineTune");tune.ShowAt((Button)shades.Children[2]);await Task.Delay(100);
            var red=Field<NumberBox>(editor,"red");red.Value=123;
            require(Field<ScreenColor>(editor,"draft")==new ScreenColor(123,190,210) && Field<TextBox>(editor,"hex").Text=="#7BBED2","Fine-tune RGB input updates the HSV draft and HEX field together");
            var fineTuneImage=await RenderOwnElementAsync((FrameworkElement)tune.Content);
            int backdrop=root.ActualTheme==ElementTheme.Dark?32:251;
            for(int i=0;i<fineTuneImage.Pixels.Length;i+=4){int alpha=fineTuneImage.Pixels[i+3];for(int channel=0;channel<3;channel++)fineTuneImage.Pixels[i+channel]=(byte)Math.Min(255,fineTuneImage.Pixels[i+channel]+(backdrop*(255-alpha)+127)/255);fineTuneImage.Pixels[i+3]=255;}
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-color-fine-tune.png"),await fineTuneImage.PngAsync());
            await InvokeAsync(((StackPanel)tune.Content).Children.OfType<Button>().Last());
            require(selected=="#7BBED2" && settings.ColorHistory[0]==selected,"Selecting a fine-tuned color updates the caller and records its history");
            await Task.Delay(80);tune.ShowAt((Button)shades.Children[2]);await Task.Delay(70);red.Value=42;tune.Hide();await Task.Delay(80);
            require(selected=="#7BBED2" && Field<ScreenColor>(editor,"color")==new ScreenColor(123,190,210),"Dismissing fine-tune discards the draft without applying it to the caller");
            var preferences=Field<Flyout>(editor,"preferences");var header=(Grid)Field<Grid>(editor,"layout").Children[0];preferences.ShowAt((Button)header.Children[2]);await Task.Delay(100);
            var options=Field<StackPanel>(editor,"preferenceContent");var choices=options.Children.OfType<ComboBox>().ToArray();
            require(choices.Length==5,"Color picker preferences expose activation, default format and all three mouse buttons");
            choices[1].SelectedIndex=1;require(settings.ColorCopyFormat=="RGB","Default copy format changes through the editor's settings");
            preferences.Hide();await Task.Delay(70);await RenderCheckImageAsync(L.LanguageCode+"-color-editor",root);
            var white=history.Children.OfType<Button>().First(button=>Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)=="#FFFFFF");
            var context=(MenuFlyout)white.ContextFlyout;context.ShowAt(white);await Task.Delay(70);
            var remove=(IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement((MenuFlyoutItem)context.Items[0])!.GetPattern(PatternInterface.Invoke);remove.Invoke();context.Hide();await Task.Delay(70);
            require(!settings.ColorHistory.Contains("#FFFFFF"),"History context menu removes the chosen color");editor.Close();editor=null;
            require(!settings.ColorHistory.Contains("#FFFFFF"),"Closing the editor does not reinsert deleted history colors");

            var bounds=DesktopCapture.DesktopBounds;byte[] pixels=new byte[120*100*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=210;pixels[i+1]=190;pixels[i+2]=80;pixels[i+3]=255;}
            byte[] gradient=new byte[8*8*4];for(int y=0;y<8;y++)for(int x=0;x<8;x++){int at=(y*8+x)*4;gradient[at]=(byte)(y*30);gradient[at+2]=(byte)(x*30);gradient[at+3]=255;}
            var original=new PixelFrame(8,8,gradient);var enlarged=original.Magnify(4,4,64,8);
            require(Enumerable.Range(28,8).All(x=>enlarged.ColorAt(x,32)==original.ColorAt(4,4)) && enlarged.ColorAt(27,32)==original.ColorAt(3,4) && enlarged.ColorAt(36,32)==original.ColorAt(5,4),"Magnifier centers the chosen pixel in an exact eight-pixel block with distinct neighbors");
            await CheckLiveColorPickerAsync(require);
        }
        finally{editor?.Close();settings.ColorHistory=historyBefore;settings.ColorFormats=formatsBefore;settings.ColorCopyFormat=defaultBefore;settings.CaptureCancelOnDeactivate=cancel;}
    }
    private static bool CaptureContainsColorSpectrum(DependencyObject node)
    {if(node is ColorPicker)return true;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(CaptureContainsColorSpectrum(VisualTreeHelper.GetChild(node,i)))return true;return false;}
}
