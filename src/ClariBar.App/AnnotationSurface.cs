using ClariBar.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SkiaSharp;
using Windows.Foundation;
using Windows.System;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed partial class AnnotationSurface:Canvas
{
    public AnnotationDocument Document {get;}
    public AnnotationStyle Ink {get;}=new();
    public Action? ToolChanged {get;set;}
    public event Action? Changed;
    public event Action? InkChanged;
    private readonly Image image=new(){Stretch=Stretch.Fill};
    private readonly Canvas preview=new(){IsHitTestVisible=false};
    private readonly Canvas handles=new();
    private readonly List<Point> stroke=[];
    private TextBox? textEditor;
    private Point textOrigin,start;
    private bool drawing,transforming,polyline,handleGesture;
    private int active=-1,straightStart=-1;
    private CaptureMark? beforeTransform;
    private string transformAnchor="";
    public bool IsEditing => textEditor is not null || drawing || polyline || active>=0;
    public AnnotationSurface(AnnotationDocument document)
    {
        Document=document;Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Children.Add(image);Children.Add(preview);Children.Add(handles);
        Document.Changed+=()=>{if(active>=Document.Marks.Count)active=-1;Refresh();Changed?.Invoke();};
        PointerPressed+=Start;PointerMoved+=Move;PointerReleased+=End;
        PointerCaptureLost+=(_,_)=>{if(drawing || transforming)CancelInput();};
        PointerWheelChanged+=(_,e)=>
        {
            if(Ink.Tool=="")return;
            int delta=e.GetCurrentPoint(this).Properties.MouseWheelDelta;
            if(Ink.Tool=="Text")Ink.FontSize=Math.Clamp(Ink.FontSize+(delta>0?1:-1),6,300);
            else Ink.Width=Math.Clamp(Ink.Width+(delta>0?1:-1),1,100);
            RestyleActive();e.Handled=true;
        };
        DoubleTapped+=(_,e)=>{if(polyline){FinishPolyline();e.Handled=true;}};
        Refresh();
    }
    public static bool Down(VirtualKey key)=>InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    private Point Position(PointerRoutedEventArgs e)
    {var p=e.GetCurrentPoint(this).Position;return new(Math.Clamp(p.X,0,Math.Max(0,Width-1)),Math.Clamp(p.Y,0,Math.Max(0,Height-1)));}
    public void ReplaceSource(PixelFrame frame){FinishText();Document.Source=frame;Refresh();}
    public PixelFrame Snapshot(){FinishText();FinishPolyline();return Document.Render();}
    public void Refresh()
    {
        Width=image.Width=preview.Width=handles.Width=Document.Source.Width;
        Height=image.Height=preview.Height=handles.Height=Document.Source.Height;
        Clip=new RectangleGeometry {Rect=new Rect(0,0,Width,Height)};
        rasterPreviewActive=false;image.Source=Document.Render().Bitmap();if(!handleGesture)ShowHandles();
    }
    public void Undo(){FinishText();FinishPolyline();EndEditing();Document.Undo();}
    public void Redo(){FinishText();FinishPolyline();Document.Redo();int last=Document.Marks.Count-1;if(last>=0){Ink.Apply(Document.Marks[last]);active=Transformable(Document.Marks[last])?last:-1;InkChanged?.Invoke();ToolChanged?.Invoke();}ShowHandles();}
    public void Clear(){FinishText();EndEditing();Document.Clear();}
    public void EndEditing(){FinishText();FinishPolyline();active=-1;handles.Children.Clear();}
    public bool FinishCurrent()
    {
        if(textEditor is not null){FinishText();active=-1;ShowHandles();return true;}
        if(polyline){FinishPolyline();active=-1;ShowHandles();return true;}
        if(active>=0){active=-1;ShowHandles();return true;}return false;
    }
    private void Start(object sender,PointerRoutedEventArgs e)
    {
        var current=e.GetCurrentPoint(this);
        if(current.Properties.IsRightButtonPressed){if(FinishCurrent())e.Handled=true;return;}
        if(!current.Properties.IsLeftButtonPressed || e.OriginalSource is TextBox || Within(e.OriginalSource as DependencyObject,textEditor) || Within(e.OriginalSource as DependencyObject,handles))return;
        if(BeginInput(Position(e))){if(drawing || transforming)CapturePointer(e.Pointer);e.Handled=true;}
    }
    private static bool Within(DependencyObject? child,DependencyObject? parent)
    {if(parent is null)return false;while(child is not null){if(child==parent)return true;child=VisualTreeHelper.GetParent(child);}return false;}
    private void Move(object sender,PointerRoutedEventArgs e)
    {
        if(drawing && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed){CancelInput();return;}
        if(UpdateInput(Position(e),Down(VirtualKey.Shift)))e.Handled=true;
    }
    private void End(object sender,PointerRoutedEventArgs e)
    {
        if(!drawing && !transforming)return;
        // Commit before releasing capture; PointerCaptureLost must only cancel unfinished gestures.
        CompleteInput(Position(e),Down(VirtualKey.Shift));ReleasePointerCaptures();e.Handled=true;
    }
    private void FinishPolyline()
    {if(!polyline)return;polyline=false;drawing=false;if(stroke.Count>=2){Document.Add(Ink.Mark(stroke.ToArray()));active=Document.Marks.Count-1;}stroke.Clear();preview.Children.Clear();ShowHandles();}
    private void DrawPreview(Point[] points)
    {
        preview.Children.Clear();if(points.Length<2)return;var a=points[0];var b=points[^1];
        if(Ink.Tool is "Mosaic" or "Blur" or "Eraser")
        {
            long now=Environment.TickCount64;if(now-lastRasterPreview<24)return;lastRasterPreview=now;
            image.Source=Document.Render(Ink.Mark(points)).Bitmap();rasterPreviewActive=true;return;
        }
        var ink=ToolUi.Brush(Ink.Color);ink.Opacity=(Ink.Tool=="Marker"?Ink.MarkerAlpha:Ink.Alpha)/255d;
        if(Ink.Tool is "Rectangle" or "Ellipse" || Ink.Tool=="Marker" && Ink.BrushMode=="Rectangle")
        {
            Shape shape=Ink.Tool=="Ellipse"?new Ellipse():new Rectangle();shape.Stroke=ink;shape.StrokeThickness=Ink.Width;
            shape.Width=Math.Abs(b.X-a.X);shape.Height=Math.Abs(b.Y-a.Y);Canvas.SetLeft(shape,Math.Min(a.X,b.X));Canvas.SetTop(shape,Math.Min(a.Y,b.Y));
            if(Ink.Fill!="Outline" || Ink.Tool=="Marker")shape.Fill=ink;preview.Children.Add(shape);
        }
        else
        {
            void Line(IEnumerable<Point> vertices)
            {var line=new Polyline{Stroke=ink,StrokeThickness=Ink.Width,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};foreach(var point in vertices)line.Points.Add(point);preview.Children.Add(line);}
            Line(points);
            if(Ink.Tool=="Arrow" && Distance(a,b)>=DragThreshold)
            {
                double angle=Math.Atan2(b.Y-a.Y,b.X-a.X),length=Math.Max(12,Ink.Width*4);
                Line([new(b.X-length*Math.Cos(angle-.5),b.Y-length*Math.Sin(angle-.5)),b,new(b.X-length*Math.Cos(angle+.5),b.Y-length*Math.Sin(angle+.5))]);
            }
        }
    }
    private void BeginText(Point point)
    {
        textOrigin=point;var editor=new TextBox {AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Width=Math.Min(340,Math.Max(70,Width-point.X)),MinHeight=40,FontFamily=new FontFamily(Ink.FontFamily),FontSize=Ink.FontSize,Foreground=ToolUi.Brush(Ink.Color),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(225,30,33,38)),FontWeight=Ink.Bold?Microsoft.UI.Text.FontWeights.Bold:Microsoft.UI.Text.FontWeights.Normal,FontStyle=Ink.Italic?Windows.UI.Text.FontStyle.Italic:Windows.UI.Text.FontStyle.Normal,PlaceholderText=L.T("Enter annotation text"),RequestedTheme=ElementTheme.Dark};
        textEditor=editor;Canvas.SetLeft(editor,point.X);Canvas.SetTop(editor,point.Y);Children.Add(editor);
        editor.KeyDown+=(_,e)=>{if(e.Key==VirtualKey.Escape || e.Key==VirtualKey.Enter && Down(VirtualKey.Control)){FinishText();e.Handled=true;}};
        editor.LostFocus+=(_,_)=>{if(textEditor==editor)FinishText();};editor.Focus(FocusState.Programmatic);
    }
    public void FinishText()
    {
        if(textEditor is not {} editor)return;textEditor=null;string text=editor.Text;Children.Remove(editor);
        if(!string.IsNullOrWhiteSpace(text)){Document.Add(Ink.Mark([textOrigin,new(textOrigin.X+Math.Max(1,editor.ActualWidth),textOrigin.Y+Math.Max(1,editor.ActualHeight))],text) with {Tool="Text"});active=Document.Marks.Count-1;ShowHandles();}
    }
    public void RestyleActive()
    {
        if(textEditor is {} text){text.FontFamily=new FontFamily(Ink.FontFamily);text.FontSize=Ink.FontSize;text.FontWeight=Ink.Bold?Microsoft.UI.Text.FontWeights.Bold:Microsoft.UI.Text.FontWeights.Normal;text.FontStyle=Ink.Italic?Windows.UI.Text.FontStyle.Italic:Windows.UI.Text.FontStyle.Normal;text.Foreground=ToolUi.Brush(Ink.Color);}
        if(active>=0 && active<Document.Marks.Count)
        {var mark=Document.Marks[active];Document.ReplaceLast(Ink.Mark(mark.Points,mark.Text) with {Tool=mark.Tool});}
        InkChanged?.Invoke();
    }
    internal static Rect Bounds(CaptureMark mark)
    {
        if(mark.Tool=="Text")
        {
            using var face=SKTypeface.FromFamilyName(mark.FontFamily);using var font=new SKFont(face,mark.FontSize);
            var lines=mark.Text.Replace("\r","").Split('\n');float width=lines.Length==0?20:lines.Max(s=>font.MeasureText(s));
            return new(mark.Points[0].X,mark.Points[0].Y,Math.Max(20,width+6),Math.Max(20,lines.Length*font.Spacing));
        }
        double left=mark.Points.Min(p=>p.X),top=mark.Points.Min(p=>p.Y),right=mark.Points.Max(p=>p.X),bottom=mark.Points.Max(p=>p.Y);
        return new(left,top,Math.Max(2,right-left),Math.Max(2,bottom-top));
    }
    private void ShowHandles()
    {
        handles.Children.Clear();if(active<0 || active>=Document.Marks.Count || textEditor is not null || !Transformable(Document.Marks[active]))return;
        var mark=Document.Marks[active];var box=Bounds(mark);
        var border=new Rectangle {Width=box.Width,Height=box.Height,Stroke=ToolUi.Brush("#5BE3ED"),StrokeThickness=1,IsHitTestVisible=false};Canvas.SetLeft(border,box.X);Canvas.SetTop(border,box.Y);handles.Children.Add(border);
        foreach(var (anchor,x,y) in new[]{("NW",box.X,box.Y),("NE",box.Right,box.Y),("SW",box.X,box.Bottom),("SE",box.Right,box.Bottom)})
        {
            var thumb=new ResizeThumb(anchor) {Width=10,Height=10,Background=ToolUi.Brush("#5BE3ED"),RequestedTheme=ElementTheme.Dark};Canvas.SetLeft(thumb,x-5);Canvas.SetTop(thumb,y-5);handles.Children.Add(thumb);
            thumb.DragStarted+=(_,_)=>handleGesture=true;thumb.DragCompleted+=(_,_)=>{handleGesture=false;ShowHandles();};
            thumb.DragDelta+=(_,e)=>
            {
                if(active<0 || active>=Document.Marks.Count)return;var original=Document.Marks[active];var bounds=Bounds(original);
                double dx=anchor.Contains('W')?-e.HorizontalChange:e.HorizontalChange,dy=anchor.Contains('N')?-e.VerticalChange:e.VerticalChange;
                double sx=Math.Max(.1,(bounds.Width+dx)/bounds.Width),sy=Math.Max(.1,(bounds.Height+dy)/bounds.Height);
                if(original.Tool=="Text"){Ink.FontSize=Math.Clamp(original.FontSize*(float)((sx+sy)/2),6,300);if(Down(VirtualKey.Shift))Ink.Rotation=0;Document.ReplaceLast(original with {FontSize=Ink.FontSize,Rotation=Ink.Rotation});}
                else {double ox=anchor.Contains('W')?bounds.Right:bounds.X,oy=anchor.Contains('N')?bounds.Bottom:bounds.Y;Document.ReplaceLast(original with {Points=original.Points.Select(p=>new Point(ox+(p.X-ox)*sx,oy+(p.Y-oy)*sy)).ToArray()});}
            };
        }
        if(mark.Tool=="Text")
        {
            var rotate=new Thumb {Width=14,Height=14,Background=ToolUi.Brush("#FFCD70"),RequestedTheme=ElementTheme.Dark};Canvas.SetLeft(rotate,box.X+box.Width/2-7);Canvas.SetTop(rotate,Math.Max(0,box.Y-22));handles.Children.Add(rotate);
            rotate.DragStarted+=(_,_)=>handleGesture=true;rotate.DragCompleted+=(_,_)=>{handleGesture=false;ShowHandles();};
            rotate.DragDelta+=(_,e)=>{if(active<0)return;var original=Document.Marks[active];float angle=Down(VirtualKey.Shift)?0:original.Rotation+(float)e.HorizontalChange;Ink.Rotation=angle;Document.ReplaceLast(original with {Rotation=angle});};
        }
    }
    public async Task ShowErrorAsync(Exception error)
    {if(XamlRoot is null)return;var dialog=new ContentDialog {Title=L.T("Screenshot"),Content=error.Message,CloseButtonText=L.T("Close"),XamlRoot=XamlRoot};try{await dialog.ShowAsync();}catch(InvalidOperationException){}}
}
