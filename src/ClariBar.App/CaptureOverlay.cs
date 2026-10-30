using ClariBar.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed record CaptureSelection(PixelRect? Area=null,ScreenColor? Color=null,PixelFrame? Image=null,string Action="Edit",PixelFrame? Source=null,CaptureMark[]? Marks=null,PixelRect? Desktop=null);

internal sealed partial class CaptureOverlay:Window
{
    private readonly DesktopToolsController tools;
    private PixelFrame frame;
    private PixelFrame? cursorFrame;
    private PixelRect desktop;
    private readonly PixelRect windowBounds;
    private List<DetectedWindow> candidates;
    private readonly bool eyedropper,ocrMode;
    private readonly TaskCompletionSource<CaptureSelection?> completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class CrosshairGrid:Grid {public CrosshairGrid()=>ProtectedCursor=InputSystemCursor.Create(InputSystemCursorShape.Cross);}
    private readonly Grid root=new CrosshairGrid();
    private readonly ContentControl focus=new(){IsTabStop=true,UseSystemFocusVisuals=false,KeyboardAcceleratorPlacementMode=KeyboardAcceleratorPlacementMode.Hidden};
    private readonly Image background=new(){Stretch=Stretch.Fill};
    private readonly Canvas adorners=new();
    private readonly Canvas anchors=new();
    private readonly Rectangle selectionHalo=new(){Stroke=new SolidColorBrush(Windows.UI.Color.FromArgb(210,8,16,24)),StrokeThickness=5,IsHitTestVisible=false};
    private readonly Rectangle selection=new(){Stroke=ToolUi.Brush("#58CBE3"),StrokeThickness=3,IsHitTestVisible=false};
    private readonly Rectangle[] shade=[new(),new(),new(),new()];
    private readonly Border info=new(){CornerRadius=new(9),Background=ToolUi.Brush("#20252D"),Padding=new(10),IsHitTestVisible=false};
    private readonly Image zoom=new(){Width=144,Height=144,Stretch=Stretch.Fill};
    private readonly Grid zoomPanel=new(){Width=144,Height=144};
    private readonly TextBlock details=new(){Foreground=ToolUi.Brush("#FFFFFF"),FontSize=12,TextWrapping=TextWrapping.Wrap,MaxWidth=350};
    private readonly ElementDetector detector=new();
    private readonly DesktopCapture.CapturedCursor capturedCursor=new();
    private AnnotationSurface? surface;
    private AnnotationToolbar? toolbar;
    private PixelRect? area,hoverArea;
    private Point start,last;
    private PixelRect beforeMove;
    private bool dragging,moving,completed,exporting,handleDrag,detectElements,cursorIncluded,spaceDown,loaded,toolbarShown=true;
    private int historyIndex=-1,colorFormat;
    private long hoverVersion;
    private double ScaleX=>frame.Width/Math.Max(1,root.ActualWidth);
    private double ScaleY=>frame.Height/Math.Max(1,root.ActualHeight);
    private PixelFrame VisibleFrame=>cursorIncluded?(cursorFrame??=DesktopCapture.WithCursor(frame,desktop,capturedCursor)):frame;
    public AnnotationDocument? Document=>surface?.Document;

    public CaptureOverlay(DesktopToolsController tools,PixelFrame frame,PixelRect desktop,List<DetectedWindow> candidates,bool eyedropper,bool ocrMode=false,PixelRect? preset=null,string? colorAction=null)
    {
        this.tools=tools;this.frame=frame;this.desktop=desktop;windowBounds=desktop;this.candidates=candidates;this.eyedropper=eyedropper;this.ocrMode=ocrMode;
        colorActionOverride=colorAction;
        detectElements=tools.Settings.CaptureDetectElements;cursorIncluded=tools.Settings.CaptureCursor && !eyedropper;
        Title="ClariBar · "+L.T(eyedropper?"Screen color picker":"Screenshot");
        root.Background=ToolUi.Brush("#000000");background.Source=VisibleFrame.Bitmap();root.Children.Add(background);root.Children.Add(adorners);
        foreach(var rectangle in shade){rectangle.Fill=new SolidColorBrush(Windows.UI.Color.FromArgb(eyedropper?(byte)0:(byte)175,0,0,0));rectangle.IsHitTestVisible=false;adorners.Children.Add(rectangle);}
        adorners.Children.Add(selectionHalo);adorners.Children.Add(selection);selectionHalo.Visibility=selection.Visibility=Visibility.Collapsed;
        adorners.Children.Add(anchors);
        zoomPanel.Children.Add(zoom);zoomPanel.Children.Add(new Border{Width=16,Height=16,BorderBrush=ToolUi.Brush("#FFFFFF"),BorderThickness=new(1),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
        var labels=new StackPanel {Spacing=7};labels.Children.Add(zoomPanel);labels.Children.Add(details);info.Child=labels;adorners.Children.Add(info);
        if(eyedropper)BuildColorPickerInfo(labels);
        details.Text=L.T(eyedropper?"Click to pick a color · Esc to cancel":"Click a window or drag a region · Esc to cancel");
        Canvas.SetLeft(info,20);Canvas.SetTop(info,20);
        focus.Content=root;Content=focus;
        root.PointerPressed+=Pressed;root.PointerMoved+=Moved;root.PointerReleased+=Released;
        root.PointerCaptureLost+=(_,_)=>{dragging=false;moving=false;};
        root.DoubleTapped+=async(_,e)=>{if(!eyedropper && area is not null && surface?.IsEditing!=true){e.Handled=true;await OutputAsync("Copy");}};
        focus.KeyDown+=KeyDown;focus.KeyUp+=(_,e)=>
        {
            if(e.Key==VirtualKey.Space && spaceDown)
            {
                spaceDown=false;
                if(dragging && !moving && area is {} box)
                    start=new(last.X>=start.X?box.X:box.Right,last.Y>=start.Y?box.Y:box.Bottom);
            }
            if(e.Key==VirtualKey.Menu)UpdateInfo(last);
        };
        root.SizeChanged+=(_,_)=>DrawSelection();
        Closed+=(_,_)=>{completed=true;capturedCursor.Dispose();completion.TrySetResult(null);};
        Activated+=async(_,e)=>
        {
            if(e.WindowActivationState!=WindowActivationState.Deactivated || !loaded || completed || exporting || !tools.Settings.CaptureCancelOnDeactivate)return;
            await Task.Delay(80);if(completed || exporting || !DesktopCapture.IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(this)))return;
            var foreground=DesktopWindowState.Foreground;DesktopWindowState.Process(foreground,out uint pid);
            if(pid!=Environment.ProcessId && !VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Any(x=>x.IsOpen))Finish(null);
        };
        if(AppWindow.Presenter is OverlappedPresenter presenter){presenter.SetBorderAndTitleBar(false,false);presenter.IsAlwaysOnTop=true;presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;}
        AppWindow.MoveAndResize(new RectInt32(desktop.X,desktop.Y,desktop.Width,desktop.Height));
        if(preset is {} selected)SetArea(selected.Intersect(frame.Bounds),false);
    }
    public Task<CaptureSelection?> SelectAsync()
    {
        root.Loaded+=(_,_)=>
        {
            AppWindow.MoveAndResize(new RectInt32(windowBounds.X,windowBounds.Y,windowBounds.Width,windowBounds.Height));
            DesktopCapture.GetCursorPos(out var cursor);
            last=Pixel(new((cursor.X-windowBounds.X)*root.ActualWidth/Math.Max(1,windowBounds.Width),(cursor.Y-windowBounds.Y)*root.ActualHeight/Math.Max(1,windowBounds.Height)));
            focus.Focus(FocusState.Programmatic);loaded=true;DrawSelection();
        };
        Activate();return completion.Task;
    }
    private Point Pixel(Point p)=>new(Math.Clamp(p.X*ScaleX,0,frame.Width-1),Math.Clamp(p.Y*ScaleY,0,frame.Height-1));
    private void SetArea(PixelRect selected,bool preserve=true,CaptureMark[]? marks=null)
    {
        selected=selected.Intersect(frame.Bounds);if(selected.IsEmpty)return;area=selected;hoverArea=null;selection.Tag=null;
        if(surface is null || !preserve)
        {
            if(surface is not null)adorners.Children.Remove(surface);if(toolbar is not null)adorners.Children.Remove(toolbar);
            var document=new AnnotationDocument(VisibleFrame.Crop(selected));if(marks is not null)document.Marks.AddRange(marks);
            surface=new(document);surface.ToolChanged=()=>{focus.Focus(FocusState.Programmatic);DrawSelection();};
            adorners.Children.Insert(4,surface);
            toolbar=new(surface,OutputAsync);adorners.Children.Add(toolbar);
        }
        else surface.ReplaceSource(VisibleFrame.Crop(selected));
        DrawSelection();
    }
    private async void Pressed(object sender,PointerRoutedEventArgs e)
    {
        if(exporting || completed || IsOnToolbar(e.OriginalSource as DependencyObject) || e.OriginalSource is Thumb)return;
        var current=e.GetCurrentPoint(root);last=Pixel(current.Position);
        if(eyedropper){PickerPressed(e);return;}
        if(current.Properties.IsRightButtonPressed)
        {
            if(surface?.FinishCurrent()==true){e.Handled=true;return;}
            if(await MarkElementAsync(last)){e.Handled=true;return;}
            if(area is not null){ClearArea();e.Handled=true;}else Finish(null);return;
        }
        if(current.Properties.IsMiddleButtonPressed && area is not null){e.Handled=true;_=OutputAsync("Pin");return;}
        if(!current.Properties.IsLeftButtonPressed)return;
        if(surface is not null && surface.Ink.Tool!="" && area is {} selected && selected.Contains((int)last.X,(int)last.Y))return;
        start=last;beforeMove=area??default;moving=area is {} box && box.Contains((int)last.X,(int)last.Y);if(!moving && area is not null)ClearArea();dragging=true;root.CapturePointer(e.Pointer);e.Handled=true;
    }
    private bool IsOnToolbar(DependencyObject? node)
    {while(node is not null){if(node==toolbar)return true;node=VisualTreeHelper.GetParent(node);}return false;}
    private async Task<bool> MarkElementAsync(Point point)
    {
        if(!detectElements || area is not {} selected || surface is null || !selected.Contains((int)point.X,(int)point.Y))return false;
        var ink=surface.Ink;
        if(ink.Tool is not ("Rectangle" or "Ellipse") && !(ink.Tool is "Marker" or "Mosaic" or "Blur" && ink.BrushMode=="Rectangle"))return false;
        var window=candidates.FirstOrDefault(w=>w.Bounds.Contains((int)point.X+desktop.X,(int)point.Y+desktop.Y));if(window is null)return false;
        var element=await detector.AtAsync(window,(int)point.X+desktop.X,(int)point.Y+desktop.Y,desktop);
        if(completed || element is not {} box || surface is null || area!=selected)return false;
        var region=(box with {X=box.X-desktop.X,Y=box.Y-desktop.Y}).Intersect(selected);
        if(region.IsEmpty)return false;
        surface.Document.Add(ink.Mark([new(region.X-selected.X,region.Y-selected.Y),new(region.Right-selected.X,region.Bottom-selected.Y)]));return true;
    }
    private async void Moved(object sender,PointerRoutedEventArgs e)
    {
        if(completed || exporting || IsOnToolbar(e.OriginalSource as DependencyObject))return;
        last=Pixel(e.GetCurrentPoint(root).Position);
        if(!eyedropper)
        {
            if(dragging)
            {
                int dx=(int)(last.X-start.X),dy=(int)(last.Y-start.Y);
                if(moving || spaceDown && !beforeMove.IsEmpty)SetArea(CaptureGeometry.Move(beforeMove,dx,dy,frame.Bounds));
                else if(Math.Abs(dx)+Math.Abs(dy)>3)SetArea(PixelRect.Between((int)start.X,(int)start.Y,(int)last.X,(int)last.Y));
            }
            else if(area is null)
            {
                var window=candidates.FirstOrDefault(w=>w.Bounds.Contains((int)last.X+desktop.X,(int)last.Y+desktop.Y));
                PixelRect? candidate=window?.Bounds;long request=++hoverVersion;
                if(detectElements && window is not null)
                {var element=await detector.AtAsync(window,(int)last.X+desktop.X,(int)last.Y+desktop.Y,desktop);if(completed || request!=hoverVersion || dragging || area is not null)return;if(element is not null)candidate=element;}
                SetHoverArea(candidate);
            }
        }
        UpdateInfo(last);
    }
    private async void Released(object sender,PointerRoutedEventArgs e)
    {
        if(!dragging)return;dragging=false;bool wasMoving=moving;moving=false;last=Pixel(e.GetCurrentPoint(root).Position);root.ReleasePointerCaptures();
        if(eyedropper){CompletePicker(pickerAction);e.Handled=true;return;}
        if(!wasMoving && Math.Abs(start.X-last.X)+Math.Abs(start.Y-last.Y)<=4)
        {
            PixelRect? selected=selection.Tag as PixelRect?;
            selected??=candidates.FirstOrDefault(w=>w.Bounds.Contains((int)last.X+desktop.X,(int)last.Y+desktop.Y))?.Bounds;
            if(selected is {} window)SetArea(window with {X=window.X-desktop.X,Y=window.Y-desktop.Y},false);
        }
        DrawSelection();if(ocrMode && area is not null)await OutputAsync("OCR");e.Handled=true;
    }
    private void ClearArea()
    {
        area=hoverArea=null;selection.Tag=null;++hoverVersion;if(surface is not null)adorners.Children.Remove(surface);if(toolbar is not null)adorners.Children.Remove(toolbar);surface=null;toolbar=null;anchors.Children.Clear();DrawSelection();UpdateInfo(last);
    }
    private void SetHoverArea(PixelRect? screenArea)
    {
        if(area is not null)return;
        hoverArea=screenArea is {} box?(box with {X=box.X-desktop.X,Y=box.Y-desktop.Y}).Intersect(frame.Bounds):null;
        selection.Tag=screenArea;DrawSelection();
    }
    private void DrawSelection()
    {
        double width=root.ActualWidth,height=root.ActualHeight;
        if((area??hoverArea) is not {} a || a.IsEmpty){selectionHalo.Visibility=selection.Visibility=Visibility.Collapsed;SetBox(shade[0],0,0,width,height);foreach(var s in shade.Skip(1))SetBox(s,0,0,0,0);return;}
        double x=a.X/ScaleX,y=a.Y/ScaleY,w=a.Width/ScaleX,h=a.Height/ScaleY;
        selectionHalo.Visibility=selection.Visibility=Visibility.Visible;SetBox(selectionHalo,x,y,w,h);SetBox(selection,x,y,w,h);
        SetBox(shade[0],0,0,width,y);SetBox(shade[1],0,y+h,width,height-y-h);SetBox(shade[2],0,y,x,h);SetBox(shade[3],x+w,y,width-x-w,h);
        if(area is null){anchors.Children.Clear();UpdateInfo(last);return;}
        if(surface is not null){Canvas.SetLeft(surface,x);Canvas.SetTop(surface,y);surface.RenderTransform=new ScaleTransform {ScaleX=1/ScaleX,ScaleY=1/ScaleY};}
        if(toolbar is not null)
        {
            toolbar.Visibility=toolbarShown?Visibility.Visible:Visibility.Collapsed;
            var point=new PointInt32(windowBounds.X+(int)(a.X*windowBounds.Width/(double)frame.Width),windowBounds.Y+(int)(a.Bottom*windowBounds.Height/(double)frame.Height));
            var monitor=DisplayArea.GetFromPoint(point,DisplayAreaFallback.Nearest).OuterBounds;
            double left=(monitor.X-windowBounds.X)*root.ActualWidth/Math.Max(1,windowBounds.Width),right=(monitor.X+monitor.Width-windowBounds.X)*root.ActualWidth/Math.Max(1,windowBounds.Width);
            double top=(monitor.Y-windowBounds.Y)*root.ActualHeight/Math.Max(1,windowBounds.Height),bottom=(monitor.Y+monitor.Height-windowBounds.Y)*root.ActualHeight/Math.Max(1,windowBounds.Height);
            toolbar.Width=Math.Max(240,Math.Min(AnnotationToolbar.PreferredWidth,Math.Min(width-16,right-left-16)));toolbar.Measure(new Size(toolbar.Width,double.PositiveInfinity));
            double tx=Math.Clamp(x,Math.Max(8,left+8),Math.Max(8,right-toolbar.Width-8)),ty=y+h+10;
            if(ty+toolbar.DesiredSize.Height>bottom-8)ty=y-toolbar.DesiredSize.Height-10;
            ty=Math.Clamp(ty,Math.Max(8,top+8),Math.Max(8,bottom-toolbar.DesiredSize.Height-8));
            Canvas.SetLeft(toolbar,tx);Canvas.SetTop(toolbar,ty);
        }
        if(!handleDrag)
        {
            anchors.Children.Clear();
            if(surface is not null)foreach(var (anchor,xx,yy) in new[]{("NW",x,y),("N",x+w/2,y),("NE",x+w,y),("W",x,y+h/2),("E",x+w,y+h/2),("SW",x,y+h),("S",x+w/2,y+h),("SE",x+w,y+h)})
            {
                var thumb=new ResizeThumb(anchor) {Width=12,Height=12,Background=ToolUi.Brush("#58CBE3"),RequestedTheme=ElementTheme.Dark,
                    HandleTemplate=(ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='#132631' BorderBrush='#132631' BorderThickness='1' CornerRadius='7'><Border Background='{TemplateBinding Background}' BorderBrush='White' BorderThickness='1.5' CornerRadius='6'/></Border></ControlTemplate>")};Canvas.SetLeft(thumb,xx-6);Canvas.SetTop(thumb,yy-6);anchors.Children.Add(thumb);
                PixelRect original=default;double dx=0,dy=0;
                thumb.DragStarted+=(_,_)=>{handleDrag=true;original=area??default;dx=dy=0;};
                thumb.DragDelta+=(_,e)=>{dx+=e.HorizontalChange*ScaleX;dy+=e.VerticalChange*ScaleY;SetArea(CaptureGeometry.Resize(original,anchor,(int)dx,(int)dy,frame.Bounds));};
                thumb.DragCompleted+=(_,_)=>{handleDrag=false;DrawSelection();};
            }
        }
        UpdateInfo(last);
    }
    private void UpdateInfo(Point p)
    {
        if(eyedropper){UpdatePickerInfo(p);return;}
        bool magnify=eyedropper || area is null || dragging || handleDrag || AnnotationSurface.Down(VirtualKey.Menu);
        zoomPanel.Visibility=magnify?Visibility.Visible:Visibility.Collapsed;
        int px=(int)p.X,py=(int)p.Y;
        if(magnify)
        {
            zoom.Source=frame.Magnify(px,py,144,16).Bitmap();
        }
        var color=frame.ColorAt(px,py);string format=(colorFormat%3) switch {1=>color.Rgb,2=>color.Hsl,_=>color.Hex};
        details.FontSize=magnify?12:13;details.FontWeight=magnify?Microsoft.UI.Text.FontWeights.Normal:Microsoft.UI.Text.FontWeights.SemiBold;
        info.Padding=magnify?new Thickness(10):new Thickness(8,4,8,4);info.CornerRadius=new(magnify?8:5);
        details.Text=(area??hoverArea) is {} box?$"{box.Width} × {box.Height} px":"";
        if(magnify)details.Text+=(details.Text.Length>0?"\n":"")+$"{format}\n{px+desktop.X}, {py+desktop.Y}\n"+L.T(area is null?"Drag to select · Tab: window / element · Alt: magnifier":"Enter: copy · Ctrl+T: pin · Alt+C: color · Esc: cancel");
        info.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));double xPos=p.X/ScaleX+22,yPos=p.Y/ScaleY+22;
        if(area is not null && !magnify){xPos=area.Value.X/ScaleX;yPos=area.Value.Y/ScaleY-info.DesiredSize.Height-10;}
        if(xPos+info.DesiredSize.Width>root.ActualWidth-8)xPos=Math.Max(8,p.X/ScaleX-info.DesiredSize.Width-20);
        if(yPos+info.DesiredSize.Height>root.ActualHeight-8)yPos=Math.Max(8,p.Y/ScaleY-info.DesiredSize.Height-20);
        Canvas.SetLeft(info,Math.Max(8,xPos));Canvas.SetTop(info,Math.Max(8,yPos));
    }
    private async void KeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(exporting || completed)return;
        bool ctrl=AnnotationSurface.Down(VirtualKey.Control),shift=AnnotationSurface.Down(VirtualKey.Shift);
        if(FocusManager.GetFocusedElement(root.XamlRoot) is TextBox)return;
        try
        {
            if(eyedropper && (e.Key==VirtualKey.Enter || ctrl && e.Key==VirtualKey.C)){CompletePicker(ctrl?"Copy":colorActionOverride??tools.Settings.ColorLeftAction);e.Handled=true;return;}
            if(e.Key==VirtualKey.Escape){if(surface?.FinishCurrent()!=true)Finish(null);e.Handled=true;}
            else if(ctrl && e.Key==VirtualKey.C){await OutputAsync(ocrMode?"OCR":"Copy");e.Handled=true;}
            else if(ctrl && e.Key==VirtualKey.S){await OutputAsync(shift?"QuickSave":"Save");e.Handled=true;}
            else if(ctrl && e.Key==VirtualKey.T){await OutputAsync("Pin");e.Handled=true;}
            else if(ctrl && e.Key==VirtualKey.P){await OutputAsync("Print");e.Handled=true;}
            else if(ctrl && e.Key==VirtualKey.Z){if(shift)surface?.Clear();else surface?.Undo();e.Handled=true;}
            else if(ctrl && e.Key==VirtualKey.Y){surface?.Redo();e.Handled=true;}
            else if(e.Key==VirtualKey.Enter){await OutputAsync(ocrMode?"OCR":"Copy");e.Handled=true;}
            else if(e.Key==VirtualKey.Space){if(!spaceDown){spaceDown=true;if(dragging && area is {} box){beforeMove=box;start=last;}else {toolbarShown=!toolbarShown;DrawSelection();}}e.Handled=true;}
            else if(e.Key==VirtualKey.Tab){if(surface?.Ink.Tool is "Line" or "Arrow")toolbar?.SelectTool(surface.Ink.Tool=="Line"?"Arrow":"Line");else detectElements=!detectElements;e.Handled=true;}
            else if(e.Key==VirtualKey.Menu)UpdateInfo(last);
            else if(e.Key==VirtualKey.Shift){colorFormat++;UpdateInfo(last);}
            else if(e.Key==VirtualKey.C && (eyedropper || area is null || AnnotationSurface.Down(VirtualKey.Menu)))
            {var c=frame.ColorAt((int)last.X,(int)last.Y);await ToolUi.CopyTextAsync(eyedropper?PickerText(c):(colorFormat%3) switch{1=>c.Rgb,2=>c.Hsl,_=>c.Hex});tools.Remember(c);e.Handled=true;}
            else if(ctrl && e.Key==VirtualKey.A)
            {
                var monitor=DisplayArea.GetFromPoint(new PointInt32(windowBounds.X+(int)(last.X*windowBounds.Width/frame.Width),windowBounds.Y+(int)(last.Y*windowBounds.Height/frame.Height)),DisplayAreaFallback.Nearest).OuterBounds;
                var screen=new PixelRect(monitor.X-windowBounds.X,monitor.Y-windowBounds.Y,monitor.Width,monitor.Height).Intersect(frame.Bounds);
                SetArea(area==screen?frame.Bounds:screen,false);e.Handled=true;
            }
            else if(e.Key==VirtualKey.R && tools.Sessions.LastArea is {} previous){SetArea((previous with{X=previous.X-desktop.X,Y=previous.Y-desktop.Y}).Intersect(frame.Bounds),false);e.Handled=true;}
            else if(e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down && area is {} current)
            {
                int dx=e.Key==VirtualKey.Left?-1:e.Key==VirtualKey.Right?1:0,dy=e.Key==VirtualKey.Up?-1:e.Key==VirtualKey.Down?1:0;
                if(ctrl || shift){string edge=dx<0?"W":dx>0?"E":dy<0?"N":"S";int direction=shift?-1:1;SetArea(CaptureGeometry.Resize(current,edge,dx*direction,dy*direction,frame.Bounds));}
                else SetArea(CaptureGeometry.Move(current,dx,dy,frame.Bounds));e.Handled=true;
            }
            else if(e.Key is VirtualKey.W or VirtualKey.A or VirtualKey.S or VirtualKey.D && !ctrl)
            {DesktopCapture.GetCursorPos(out var cursor);DesktopCapture.SetCursorPos(cursor.X+(e.Key==VirtualKey.A?-1:e.Key==VirtualKey.D?1:0),cursor.Y+(e.Key==VirtualKey.W?-1:e.Key==VirtualKey.S?1:0));e.Handled=true;}
            else if((int)e.Key is 188 or 190){await ReplayAsync((int)e.Key==188?1:-1);e.Handled=true;}
            else if((int)e.Key==192 || (int)e.Key==49 && shift && (surface is null || surface.Ink.Tool=="")){cursorIncluded=!cursorIncluded;background.Source=VisibleFrame.Bitmap();if(area is {} box)SetArea(box);e.Handled=true;}
            else if((int)e.Key is 49 or 50 or 219 or 221 && surface is not null && surface.Ink.Tool!=""){toolbar?.ChangeWidth((int)e.Key is 49 or 219?-1:1);e.Handled=true;}
        }
        catch(Exception ex){if(!completed)await ToolUi.ErrorAsync(this,ex);}
    }
    private async Task ReplayAsync(int direction)
    {
        if(tools.Sessions.History.Count==0)return;int index=Math.Clamp(historyIndex+direction,0,tools.Sessions.History.Count-1);var record=tools.Sessions.History[index];
        var content=await ImageContent.FromFileAsync(tools.Sessions.CapturePath(record.Id));frame=content.Frame;desktop=record.Desktop;cursorFrame=null;cursorIncluded=false;candidates=[];historyIndex=index;
        background.Source=frame.Bitmap();SetArea(record.Area,false,record.Marks.Select(m=>m.ToMark()).ToArray());
    }
    private async Task OutputAsync(string action)
    {
        if(completed || exporting)return;if(action=="Close"){Finish(null);return;}if(action=="Clear"){surface?.Clear();return;}
        if(area is not {} box || surface is null)return;exporting=true;
        try
        {
            PixelFrame output=surface.Snapshot();bool success=true;
            if(action=="Copy")await output.CopyAsync();
            else if(action=="Save")
            {Native.ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(this),0);success=await tools.SaveAsImageAsync(output,this) is not null;}
            else if(action=="QuickSave")await tools.SaveImageAsync(output,this);
            else if(action=="Print")
            {Native.ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(this),0);success=ImagePrinter.Print(output,this);if(!completed){Native.ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(this),5);Activate();}return;}
            if(!success){Native.ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(this),5);Activate();return;}
            await tools.CompletedCaptureAsync(VisibleFrame,desktop,box,surface.Document.Marks,output,action);
            Finish(new(box,Image:output,Action:action,Source:surface.Document.Source,Marks:surface.Document.Marks.ToArray(),Desktop:desktop));
        }
        finally{exporting=false;}
    }
    private static void SetBox(FrameworkElement box,double x,double y,double width,double height){Canvas.SetLeft(box,x);Canvas.SetTop(box,y);box.Width=Math.Max(0,width);box.Height=Math.Max(0,height);}
    private void Finish(CaptureSelection? result){if(completed)return;completed=true;completion.TrySetResult(result);Close();}
}

internal static class DesktopWindowState
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern nint GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindowThreadProcessId")]internal static extern uint Process(nint window,out uint process);
    public static nint Foreground=>GetForegroundWindow();
}
