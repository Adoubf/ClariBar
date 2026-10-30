using System.Runtime.InteropServices;
using ClariBar.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed class PinnedImageWindow:Window
{
    private readonly DesktopToolsController tools;
    internal PinModel Model {get;}
    private readonly Grid root=new();
    private readonly ContentControl focus=new(){IsTabStop=true,UseSystemFocusVisuals=false,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    private readonly Image image=new(){Stretch=Stretch.Fill};
    private readonly TextBlock badge=ToolUi.Label("");
    private readonly Border badgeHost=new(){HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Background=ToolUi.Brush("#20252D"),CornerRadius=new(5),Padding=new(5),Margin=new(6),IsHitTestVisible=false,Visibility=Visibility.Collapsed};
    private readonly Border magnifierHost=new(){HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Background=ToolUi.Brush("#20252D"),CornerRadius=new(7),Padding=new(7),Margin=new(5),Visibility=Visibility.Collapsed,IsHitTestVisible=false};
    private readonly Image magnifier=new(){Width=108,Height=108,Stretch=Stretch.Fill};
    private readonly TextBlock colorLabel=ToolUi.Label("",12);
    private readonly DispatcherTimer animationTimer=new();
    private readonly DispatcherTimer lensTimer=new(){Interval=TimeSpan.FromMilliseconds(80)};
    private ImageAnimation? animation;
    private PixelFrame current=null!;
    private Point lastPoint;
    private bool moving,destroying,dead,shiftWasDown,initializing=true;
    private DesktopCapture.Point dragStart;
    private PointInt32 originalPosition;
    private SizeInt32 originalSize;
    private string resizing="";
    private int colorFormat;
    private PinEditWindow? editor;
    public nint Handle {get;}
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint GetWindowLong(nint window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern nint SetWindowLong(nint window,int index,nint value);
    [DllImport("user32.dll")]private static extern bool SetLayeredWindowAttributes(nint window,uint key,byte alpha,uint flags);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    public PinnedImageWindow(DesktopToolsController tools,PixelFrame frame):this(tools,tools.Pins.Adopt(new(frame))){}
    internal PinnedImageWindow(DesktopToolsController tools,PinModel model)
    {
        this.tools=tools;Model=model;model.Window=this;
        root.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);root.Children.Add(image);
        badge.Foreground=ToolUi.Brush("#FFFFFF");badge.FontSize=11;badgeHost.Child=badge;root.Children.Add(badgeHost);
        var lens=new StackPanel {Spacing=5};lens.Children.Add(magnifier);colorLabel.Foreground=ToolUi.Brush("#FFFFFF");lens.Children.Add(colorLabel);magnifierHost.Child=lens;root.Children.Add(magnifierHost);
        root.PointerEntered+=(_,_)=>{badgeHost.Visibility=Visibility.Visible;UpdateBadge();};
        root.PointerExited+=(_,_)=>{if(!moving)badgeHost.Visibility=Visibility.Collapsed;};
        root.PointerPressed+=Pressed;root.PointerMoved+=Moved;root.PointerReleased+=(_,_)=>{moving=false;resizing="";root.ReleasePointerCaptures();UpdateRecord();tools.Pins.Dirty();};
        root.PointerCaptureLost+=(_,_)=>{moving=false;resizing="";};
        root.PointerWheelChanged+=(_,e)=>
        {
            int direction=e.GetCurrentPoint(root).Properties.MouseWheelDelta>0?1:-1;
            if(AnnotationSurface.Down(VirtualKey.Control))SetOpacity(Model.Record.Opacity+direction*5);
            else if(animation is not null && AnnotationSurface.Down(VirtualKey.Shift)){Model.Record.AnimationSpeed=Math.Clamp(Model.Record.AnimationSpeed*(direction>0?1.2:1/1.2),.1,10);RunAnimation();}
            else SetZoom(Model.Record.Zoom*(direction>0?1.1:1/1.1));e.Handled=true;
        };
        root.DoubleTapped+=(_,e)=>
        {
            if(AnnotationSurface.Down(VirtualKey.Shift)){Model.Record.Thumbnail=!Model.Record.Thumbnail;ResizeImage();tools.Pins.Dirty();}
            else tools.Pins.Close(Model);e.Handled=true;
        };
        root.AllowDrop=true;root.DragOver+=(_,e)=>{e.AcceptedOperation=DataPackageOperation.Copy;};
        root.Drop+=async(_,e)=>{try{var content=await ImageContent.ClipboardAsync(tools.Settings,supplied:e.DataView);if(content is not null)Model.Replace(content);}catch(Exception ex){await ToolUi.ErrorAsync(this,ex);}};
        focus.Content=root;focus.KeyDown+=KeyDown;Content=focus;
        ToolUi.Configure(this,tools,"Pinned image",400,300,true);
        Handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        ToolWindowGeometry.Borderless(this);
        if(AppWindow.Presenter is OverlappedPresenter presenter){presenter.PreferredMinimumWidth=16;presenter.PreferredMinimumHeight=16;}
        SystemBackdrop=new TransparentToolBackdrop();
        AppWindow.Closing+=(_,e)=>{if(destroying)return;e.Cancel=true;tools.Pins.Close(Model);};
        Closed+=(_,_)=>{dead=true;animationTimer.Stop();lensTimer.Stop();animation?.Dispose();editor?.Close();};
        AppWindow.Changed+=(_,e)=>{if(!initializing && !dead && !destroying && (e.DidPositionChange || e.DidSizeChange)){UpdateRecord();tools.Pins.Dirty();}};
        Model.Document.Changed+=()=>{if(!dead)Refresh();};
        animationTimer.Tick+=(_,_)=>AdvanceFrame(1,false);
        lensTimer.Tick+=(_,_)=>UpdateLens();
        root.Loaded+=(_,_)=>{focus.Focus(FocusState.Programmatic);ApplyOpacity();lensTimer.Start();};
        if(!Model.Record.HasPosition)
        {var work=DisplayArea.Primary.WorkArea;Model.Record.Zoom=Math.Min(1,Math.Min(work.Width*.8/model.Content.Frame.Width,work.Height*.8/model.Content.Frame.Height));}
        ContentChanged();
        if(!Model.Record.HasPosition)
        {
            var work=DisplayArea.Primary.WorkArea;AppWindow.Move(new(work.X+(work.Width-AppWindow.Size.Width)/2,work.Y+(work.Height-AppWindow.Size.Height)/2));
        }
        else AppWindow.Move(new(Model.Record.X,Model.Record.Y));
        RecoverOnScreen();initializing=false;UpdateRecord();ApplyOpacity();SetClickThrough(Model.Record.ClickThrough,false);BuildMenu();
    }
    internal void ContentChanged()
    {
        animationTimer.Stop();animation?.Dispose();animation=null;
        if(Model.Content.AnimationBytes is {} bytes){animation=new(bytes);Model.Record.AnimationFrame=Math.Clamp(Model.Record.AnimationFrame,0,animation.Count-1);Model.Document.Source=animation.Frame(Model.Record.AnimationFrame);}
        Refresh();RunAnimation();BuildMenu();tools.Pins.Dirty();
    }
    internal void Refresh(){if(dead)return;current=Model.Render();image.Source=current.Bitmap();ResizeImage();UpdateBadge();}
    private void ResizeImage()
    {
        if(dead || current is null)return;
        double z=Model.Record.Thumbnail?tools.Settings.PinThumbnailSize/(double)Math.Max(current.Width,current.Height):Model.Record.Zoom;
        z=Math.Clamp(z,.005,16);
        int width=Math.Clamp((int)Math.Round(current.Width*z),16,16000),height=Math.Clamp((int)Math.Round(current.Height*z),16,16000);
        if(AppWindow.Size.Width!=width || AppWindow.Size.Height!=height)AppWindow.Resize(new(width,height));
    }
    private void SetZoom(double value)
    {
        if(current is null)return;
        double min=Math.Min(16,Math.Max(16d/current.Width,16d/current.Height)),max=Math.Max(min,Math.Min(16,16000d/Math.Max(current.Width,current.Height)));
        var before=AppWindow.Size;var position=AppWindow.Position;double fx=root.ActualWidth>0?lastPoint.X/root.ActualWidth:.5,fy=root.ActualHeight>0?lastPoint.Y/root.ActualHeight:.5;
        Model.Record.Zoom=Math.Clamp(value,min,max);Model.Record.Thumbnail=false;ResizeImage();
        if(!moving)AppWindow.Move(new(position.X+(int)((before.Width-AppWindow.Size.Width)*Math.Clamp(fx,0,1)),position.Y+(int)((before.Height-AppWindow.Size.Height)*Math.Clamp(fy,0,1))));
        UpdateRecord();UpdateBadge();tools.Pins.Dirty();
    }
    private void SetOpacity(int value){Model.Record.Opacity=Math.Clamp(value,10,100);ApplyOpacity();UpdateBadge();tools.Pins.Dirty();}
    private void ApplyOpacity(){SetWindowLong(Handle,-20,GetWindowLong(Handle,-20)|0x80000);SetLayeredWindowAttributes(Handle,0,(byte)(Model.Record.Opacity*255/100),2);}
    internal void SetClickThrough(bool value,bool save=true)
    {Model.Record.ClickThrough=value;var style=GetWindowLong(Handle,-20);SetWindowLong(Handle,-20,value?style|0x80000|0x20:style&~0x20);ApplyOpacity();UpdateBadge();if(save)tools.Pins.Dirty();}
    internal void SetVisible(bool visible)
    {if(dead)return;if(editor is not null && visible)return;Native.ShowWindow(Handle,visible?4:0);if(visible)RunAnimation();else {animationTimer.Stop();editor?.Close();}}
    internal void Destroy(){if(dead)return;destroying=true;animationTimer.Stop();lensTimer.Stop();editor?.Close();Close();}
    internal void UpdateRecord(){if(dead)return;Model.Record.X=AppWindow.Position.X;Model.Record.Y=AppWindow.Position.Y;Model.Record.HasPosition=true;}
    private void RecoverOnScreen()
    {
        var bounds=DesktopCapture.DesktopBounds;var point=AppWindow.Position;var size=AppWindow.Size;
        if(new PixelRect(point.X,point.Y,size.Width,size.Height).Intersect(bounds).IsEmpty){var work=DisplayArea.Primary.WorkArea;AppWindow.Move(new(work.X+30,work.Y+30));}
    }
    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        var point=e.GetCurrentPoint(root);lastPoint=point.Position;
        if(point.Properties.IsRightButtonPressed){BuildMenu();return;}
        if(point.Properties.IsMiddleButtonPressed){Model.Record.Thumbnail=false;SetZoom(1);SetOpacity(100);e.Handled=true;return;}
        if(!point.Properties.IsLeftButtonPressed)return;
        DesktopCapture.GetCursorPos(out dragStart);originalPosition=AppWindow.Position;originalSize=AppWindow.Size;moving=true;resizing="";
        if(lastPoint.X<5)resizing+="W";else if(lastPoint.X>root.ActualWidth-5)resizing+="E";
        if(lastPoint.Y<5)resizing+="N";else if(lastPoint.Y>root.ActualHeight-5)resizing+="S";
        root.CapturePointer(e.Pointer);e.Handled=true;
    }
    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        lastPoint=e.GetCurrentPoint(root).Position;if(!moving)return;DesktopCapture.GetCursorPos(out var p);int dx=p.X-dragStart.X,dy=p.Y-dragStart.Y;
        if(resizing.Length>0)
        {
            double width=originalSize.Width+(resizing.Contains('W')?-dx:dx),height=originalSize.Height+(resizing.Contains('N')?-dy:dy);
            double scale=resizing is "N" or "S"?height/current.Height:width/current.Width;Model.Record.Zoom=Math.Clamp(scale,.01,16);Model.Record.Thumbnail=false;ResizeImage();
            AppWindow.Move(new(originalPosition.X+(resizing.Contains('W')?originalSize.Width-AppWindow.Size.Width:0),originalPosition.Y+(resizing.Contains('N')?originalSize.Height-AppWindow.Size.Height:0)));
        }
        else
        {var point=new PointInt32(originalPosition.X+dx,originalPosition.Y+dy);if(AnnotationSurface.Down(VirtualKey.Shift))point=tools.Pins.Snap(Model,point,AppWindow.Size.Width,AppWindow.Size.Height);AppWindow.Move(point);}
    }
    private void Rotate(int turns){Model.Record.Turns=((Model.Record.Turns+turns)%4+4)%4;Refresh();tools.Pins.Dirty();}
    private void RunAnimation()
    {animationTimer.Stop();if(animation is not null && Model.Record.Playing && Model.Record.State=="Visible" && DesktopCapture.IsWindowVisible(Handle)){animationTimer.Interval=TimeSpan.FromMilliseconds(Math.Clamp(animation.Duration(Model.Record.AnimationFrame)/Math.Max(.1,Model.Record.AnimationSpeed),10,60000));animationTimer.Start();}}
    private void AdvanceFrame(int direction,bool pause=true)
    {if(animation is null)return;if(pause)Model.Record.Playing=false;Model.Record.AnimationFrame=(Model.Record.AnimationFrame+direction+animation.Count)%animation.Count;Model.Document.Source=animation.Frame(Model.Record.AnimationFrame);Refresh();RunAnimation();}
    private void UpdateBadge()=>badge.Text=$"{Model.Record.Zoom:P0} · {Model.Record.Opacity}%"+(animation is not null?$" · {Model.Record.AnimationFrame+1}/{animation.Count} · ×{Model.Record.AnimationSpeed:0.#}":"");
    private ScreenColor PixelUnderCursor()
    {int x=(int)(lastPoint.X/Math.Max(1,root.ActualWidth)*current.Width),y=(int)(lastPoint.Y/Math.Max(1,root.ActualHeight)*current.Height);return current.ColorAt(x,y);}
    private void UpdateLens()
    {
        if(dead || !DesktopCapture.IsWindowVisible(Handle))return;
        bool alt=(GetAsyncKeyState((int)VirtualKey.Menu)&0x8000)!=0;DesktopCapture.GetCursorPos(out var cursor);DesktopCapture.GetWindowRect(Handle,out var box);
        bool show=alt && box.Pixel.Contains(cursor.X,cursor.Y);magnifierHost.Visibility=show?Visibility.Visible:Visibility.Collapsed;if(!show){shiftWasDown=false;return;}
        lastPoint=new((cursor.X-box.Left)*root.ActualWidth/Math.Max(1,box.Right-box.Left),(cursor.Y-box.Top)*root.ActualHeight/Math.Max(1,box.Bottom-box.Top));
        bool shift=(GetAsyncKeyState((int)VirtualKey.Shift)&0x8000)!=0;if(shift && !shiftWasDown)colorFormat++;shiftWasDown=shift;
        int x=(int)(lastPoint.X/Math.Max(1,root.ActualWidth)*current.Width),y=(int)(lastPoint.Y/Math.Max(1,root.ActualHeight)*current.Height);byte[] pixels=new byte[108*108*4];
        for(int yy=0;yy<108;yy++)for(int xx=0;xx<108;xx++){int sx=Math.Clamp(x+xx/12-4,0,current.Width-1),sy=Math.Clamp(y+yy/12-4,0,current.Height-1);System.Buffer.BlockCopy(current.Pixels,(sy*current.Width+sx)*4,pixels,(yy*108+xx)*4,4);}magnifier.Source=new PixelFrame(108,108,pixels).Bitmap();var color=current.ColorAt(x,y);colorLabel.Text=(colorFormat%3) switch{1=>color.Rgb,2=>color.Hsl,_=>color.Hex};
    }
    private async void KeyDown(object sender,KeyRoutedEventArgs e)
    {
        bool ctrl=AnnotationSurface.Down(VirtualKey.Control),shift=AnnotationSurface.Down(VirtualKey.Shift);
        try
        {
            if(e.Key==VirtualKey.Escape){if(shift)tools.Pins.Destroy(Model);else tools.Pins.Close(Model);}
            else if(ctrl && e.Key==VirtualKey.W)tools.Pins.Close(Model);
            else if(ctrl && e.Key==VirtualKey.C){if(shift && Model.Content.PlainText.Length>0)await ToolUi.CopyTextAsync(Model.Content.PlainText);else await current.CopyAsync();}
            else if(ctrl && e.Key==VirtualKey.V)await tools.Pins.ReplaceAsync(Model);
            else if(ctrl && e.Key==VirtualKey.S){if(shift)await tools.SaveImageAsync(current,this);else await tools.SaveAsImageAsync(current,this);}
            else if(ctrl && shift && e.Key==VirtualKey.P)tools.ShowSettings();
            else if(ctrl && e.Key==VirtualKey.P)ImagePrinter.Print(current,this);
            else if(ctrl && e.Key==VirtualKey.Z){if(shift)Model.Document.Clear();else Model.Document.Undo();}
            else if(ctrl && e.Key==VirtualKey.Y)Model.Document.Redo();
            else if(e.Key==VirtualKey.Space)OpenEditor();
            else if(e.Key==VirtualKey.C && magnifierHost.Visibility==Visibility.Visible){await ToolUi.CopyTextAsync(colorLabel.Text);tools.Remember(PixelUnderCursor());}
            else if((int)e.Key==49){if(animation is null)Rotate(1);else AdvanceFrame(-1);}
            else if((int)e.Key==50){if(animation is null)Rotate(-1);else AdvanceFrame(1);}
            else if((int)e.Key==51){Model.Record.FlipX=!Model.Record.FlipX;Refresh();}
            else if((int)e.Key==52){Model.Record.FlipY=!Model.Record.FlipY;Refresh();}
            else if((int)e.Key is 187 or 107){if(ctrl)SetOpacity(Model.Record.Opacity+5);else SetZoom(Model.Record.Zoom*1.1);}
            else if((int)e.Key is 189 or 109){if(ctrl)SetOpacity(Model.Record.Opacity-5);else SetZoom(Model.Record.Zoom/1.1);}
            else if(e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down){var p=AppWindow.Position;AppWindow.Move(new(p.X+(e.Key==VirtualKey.Left?-1:e.Key==VirtualKey.Right?1:0),p.Y+(e.Key==VirtualKey.Up?-1:e.Key==VirtualKey.Down?1:0)));}
            else if(e.Key is VirtualKey.W or VirtualKey.A or VirtualKey.S or VirtualKey.D){DesktopCapture.GetCursorPos(out var p);DesktopCapture.SetCursorPos(p.X+(e.Key==VirtualKey.A?-1:e.Key==VirtualKey.D?1:0),p.Y+(e.Key==VirtualKey.W?-1:e.Key==VirtualKey.S?1:0));}
            else return;
            e.Handled=true;tools.Pins.Dirty();
        }
        catch(Exception ex){await ToolUi.ErrorAsync(this,ex);}
    }
    internal void OpenEditor()
    {
        if(editor is not null){editor.Activate();return;}
        PinEditWindow? next=null;
        try
        {
            // Construct the replacement before hiding the pin so initialization errors leave it usable.
            next=new PinEditWindow(tools,Model);tools.Track(next);animationTimer.Stop();SetVisible(false);editor=next;
            next.Closed+=(_,_)=>{if(!ReferenceEquals(editor,next))return;editor=null;RestoreAfterEditing();};
            next.Activate();
        }
        catch
        {
            editor=null;next?.Close();RestoreAfterEditing();throw;
        }
    }
    private void RestoreAfterEditing()
    {if(!dead && !destroying && tools.Pins.ShouldShow(Model)){Refresh();SetVisible(true);Activate();tools.Pins.Dirty();}}
    private void BuildMenu()
    {
        var menu=new MenuFlyout();
        void Add(string text,Func<Task> action)
        {var item=new MenuFlyoutItem{Text=L.T(text)};item.Click+=async(_,_)=>{try{await action();tools.Pins.Dirty();}catch(Exception ex){await ToolUi.ErrorAsync(this,ex);}};menu.Items.Add(item);}
        Add("Copy",()=>current.CopyAsync());
        if(Model.Content.PlainText.Length>0)Add("Copy text",()=>ToolUi.CopyTextAsync(Model.Content.PlainText));
        Add("Save",async()=>{await tools.SaveAsImageAsync(current,this);});Add("Quick save",async()=>{await tools.SaveImageAsync(current,this);});
        Add("Edit annotations",()=>{OpenEditor();return Task.CompletedTask;});Add("OCR",()=>{tools.OpenOcr(current);return Task.CompletedTask;});Add("Print",()=>{ImagePrinter.Print(current,this);return Task.CompletedTask;});
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Rotate right",()=>{Rotate(1);return Task.CompletedTask;});Add("Rotate left",()=>{Rotate(-1);return Task.CompletedTask;});
        Add("Horizontal flip",()=>{Model.Record.FlipX=!Model.Record.FlipX;Refresh();return Task.CompletedTask;});Add("Vertical flip",()=>{Model.Record.FlipY=!Model.Record.FlipY;Refresh();return Task.CompletedTask;});
        Add("Actual size",()=>{SetZoom(1);SetOpacity(100);return Task.CompletedTask;});
        Add("Thumbnail",()=>{Model.Record.Thumbnail=!Model.Record.Thumbnail;ResizeImage();return Task.CompletedTask;});
        Add("Mouse click-through",()=>{SetClickThrough(!Model.Record.ClickThrough);return Task.CompletedTask;});
        Add("More transparent",()=>{SetOpacity(Model.Record.Opacity-10);return Task.CompletedTask;});Add("More opaque",()=>{SetOpacity(Model.Record.Opacity+10);return Task.CompletedTask;});
        if(animation is not null){Add("Play / pause",()=>{Model.Record.Playing=!Model.Record.Playing;RunAnimation();return Task.CompletedTask;});Add("Previous frame",()=>{AdvanceFrame(-1);return Task.CompletedTask;});Add("Next frame",()=>{AdvanceFrame(1);return Task.CompletedTask;});}
        var groups=new MenuFlyoutSubItem {Text=L.T("Move to group")};foreach(string name in tools.Pins.Groups){var entry=new MenuFlyoutItem {Text=name=="Default"?L.T("Default group"):name};entry.Click+=(_,_)=>tools.Pins.MoveToGroup(Model,name);groups.Items.Add(entry);}menu.Items.Add(groups);
        Add("Manage images",()=>tools.OpenPinsAsync());menu.Items.Add(new MenuFlyoutSeparator());
        Add("Close",()=>{tools.Pins.Close(Model);return Task.CompletedTask;});Add("Destroy",()=>{tools.Pins.Destroy(Model);return Task.CompletedTask;});root.ContextFlyout=menu;
    }
}

internal sealed class TransparentToolBackdrop:SystemBackdrop
{
    private Windows.UI.Composition.Compositor? compositor;
    private nint window;
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]private static extern int CB_EnableWindowAlpha(nint window);
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]private static extern void CB_DisableWindowAlpha(nint window);
    protected override void OnTargetConnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop target,XamlRoot xamlRoot)
    {
        window=(nint)xamlRoot.ContentIslandEnvironment.AppWindowId.Value;CB_EnableWindowAlpha(window);
        compositor=new Windows.UI.Composition.Compositor();target.SystemBackdrop=compositor.CreateColorBrush(Microsoft.UI.Colors.Transparent);
        base.OnTargetConnected(target,xamlRoot);
    }
    protected override void OnTargetDisconnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop target)
    {CB_DisableWindowAlpha(window);var brush=target.SystemBackdrop;target.SystemBackdrop=null;brush?.Dispose();compositor?.Dispose();compositor=null;base.OnTargetDisconnected(target);}
}
