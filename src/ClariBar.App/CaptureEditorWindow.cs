using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed class CaptureEditorWindow:Window
{
    private readonly DesktopToolsController tools;
    internal AnnotationDocument Document {get;}
    private readonly AnnotationSurface surface;
    private readonly AnnotationToolbar toolbar;
    private readonly ScrollViewer viewport;
    private readonly TextBlock status=ToolUi.Label("");
    public CaptureEditorWindow(DesktopToolsController tools,PixelFrame frame)
    {
        this.tools=tools;Document=new(frame);surface=new(Document);
        var root=new Grid {RowSpacing=6};root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        toolbar=new(surface,OutputAsync){HorizontalAlignment=HorizontalAlignment.Center,Margin=new(8,6,8,0)};root.Children.Add(toolbar);
        viewport=new ScrollViewer {Content=surface,ZoomMode=ZoomMode.Enabled,MinZoomFactor=.1f,MaxZoomFactor=8,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new(16)};
        viewport.Loaded+=(_,_)=>{float zoom=(float)Math.Clamp(Math.Min((viewport.ActualWidth-32)/frame.Width,(viewport.ActualHeight-32)/frame.Height),.1,1);viewport.ChangeView(0,0,zoom,true);toolbar.SelectTool("Rectangle");};
        surface.ToolChanged=()=>{viewport.HorizontalScrollMode=viewport.VerticalScrollMode=surface.Ink.Tool==""?ScrollMode.Enabled:ScrollMode.Disabled;};
        Grid.SetRow(viewport,1);root.Children.Add(viewport);status.Text=$"{frame.Width} × {frame.Height} px · "+L.T("Drag to annotate · Ctrl+Z undo · Ctrl+C copy · Ctrl+S save");status.Margin=new(16,8,16,12);Grid.SetRow(status,2);root.Children.Add(status);
        root.KeyDown+=KeyDown;Content=root;ToolUi.Configure(this,tools,"Screenshot editor",1180,820);AppWindow.Closing+=(_,_)=>surface.FinishText();
    }
    private async Task OutputAsync(string action)
    {
        var frame=surface.Snapshot();if(action=="Close"){Close();return;}if(action=="Clear"){surface.Clear();return;}
        if(action=="Copy"){await frame.CopyAsync();status.Text=L.T("Copied to clipboard");}
        else if(action=="Save"){string? path=await tools.SaveAsImageAsync(frame,this);if(path is not null)status.Text=L.F("Saved: {0}",path);}
        else if(action=="QuickSave")status.Text=L.F("Saved: {0}",await tools.SaveImageAsync(frame,this));
        else if(action=="Pin")tools.Pin(frame);else if(action=="OCR")tools.OpenOcr(frame);else if(action=="Print")ImagePrinter.Print(frame,this);
    }
    private async void KeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(FocusManager.GetFocusedElement(((FrameworkElement)Content).XamlRoot) is TextBox)return;
        bool ctrl=AnnotationSurface.Down(VirtualKey.Control),shift=AnnotationSurface.Down(VirtualKey.Shift);
        try
        {
            if(e.Key==VirtualKey.Escape){if(!surface.FinishCurrent())Close();}
            else if(ctrl && e.Key==VirtualKey.Z){if(shift)surface.Clear();else surface.Undo();}
            else if(ctrl && e.Key==VirtualKey.Y)surface.Redo();
            else if(ctrl && e.Key==VirtualKey.C || e.Key==VirtualKey.Enter)await OutputAsync("Copy");
            else if(ctrl && e.Key==VirtualKey.S)await OutputAsync(shift?"QuickSave":"Save");
            else if(ctrl && e.Key==VirtualKey.T)await OutputAsync("Pin");
            else if(ctrl && e.Key==VirtualKey.P)await OutputAsync("Print");
            else if(e.Key==VirtualKey.Tab && surface.Ink.Tool is "Line" or "Arrow")toolbar.SelectTool(surface.Ink.Tool=="Line"?"Arrow":"Line");
            else if((int)e.Key is 49 or 50 or 219 or 221)toolbar.ChangeWidth((int)e.Key is 49 or 219?-1:1);
            else return;e.Handled=true;
        }
        catch(Exception ex){await ToolUi.ErrorAsync(this,ex);}
    }
}
