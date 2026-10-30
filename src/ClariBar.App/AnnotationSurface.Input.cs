using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace ClariBar.App;

internal sealed partial class AnnotationSurface
{
    private bool rasterPreviewActive;
    private long lastRasterPreview=-1000;
    private double DragThreshold=>3/Math.Max(.01,Math.Sqrt(RenderTransform is MatrixTransform matrix?Math.Abs(matrix.Matrix.M11*matrix.Matrix.M22-matrix.Matrix.M12*matrix.Matrix.M21):RenderTransform is ScaleTransform scale?Math.Abs(scale.ScaleX*scale.ScaleY):1));
    private static double Distance(Point a,Point b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
    private static bool Transformable(CaptureMark mark)=>mark.Tool=="Text" || mark.Tool is "Rectangle" or "Ellipse" or "Line" or "Arrow" or "Polyline" && mark.Points.Length>1 && mark.Points.Skip(1).Any(point=>Distance(mark.Points[0],point)>.5);
    private bool Brush=>Ink.Tool=="Pen" || Ink.Tool is "Marker" or "Mosaic" or "Blur" or "Eraser" && Ink.BrushMode=="Brush";
    private Point Constrain(Point p,bool shift)
    {
        if(!shift || stroke.Count==0)return p;
        var origin=straightStart>=0?stroke[straightStart]:stroke[0];
        var q=ClariBar.Core.CaptureGeometry.Constrain(origin.X,origin.Y,p.X,p.Y,
            Ink.Tool is "Rectangle" or "Ellipse" || Ink.BrushMode=="Rectangle" && Ink.Tool is "Marker" or "Mosaic" or "Blur" or "Eraser",
            Ink.Tool is "Line" or "Arrow" || straightStart>=0);
        return new(q.X,q.Y);
    }
    internal bool BeginInput(Point p)
    {
        if(Ink.Tool=="")return false;
        FinishText();start=p;
        if(!Brush && active>=0 && Transformable(Document.Marks[active]) && Bounds(Document.Marks[active]).Contains(start))
        {beforeTransform=Document.Marks[active];transforming=true;transformAnchor="Move";return true;}
        active=-1;ShowHandles();
        if(Ink.Tool=="Text"){BeginText(p);return true;}
        if(Ink.Tool=="Polyline")
        {if(!polyline){stroke.Clear();stroke.Add(p);polyline=true;}else stroke.Add(p);drawing=true;return true;}
        stroke.Clear();straightStart=-1;lastRasterPreview=-1000;stroke.Add(p);if(!Brush)stroke.Add(p);drawing=true;return true;
    }
    internal bool UpdateInput(Point p,bool shift=false)
    {
        if(transforming && beforeTransform is {} original)
        {
            if(transformAnchor=="Move")Document.ReplaceLast(original with{Points=original.Points.Select(q=>new Point(q.X+p.X-start.X,q.Y+p.Y-start.Y)).ToArray()});return true;
        }
        if((!drawing && !polyline) || stroke.Count==0)return false;
        if(polyline){DrawPreview(stroke.Append(p).ToArray());return true;}
        if(Brush)
        {
            if(shift)
            {
                if(straightStart<0)straightStart=stroke.Count-1;
                if(stroke.Count>straightStart+1)stroke.RemoveRange(straightStart+1,stroke.Count-straightStart-1);
                p=Constrain(p,true);
            }
            else straightStart=-1;
            if(stroke.Count==1 || Distance(stroke[^1],p)>.25)stroke.Add(p);
        }
        else stroke[^1]=Constrain(p,shift);
        DrawPreview(stroke.ToArray());return true;
    }
    internal void CompleteInput(Point p,bool shift=false)
    {
        if(transforming){transforming=false;beforeTransform=null;return;}
        if(!drawing)return;
        if(polyline)
        {drawing=false;if(stroke.Count==1 && Distance(start,p)>=DragThreshold){stroke.Add(p);FinishPolyline();}return;}
        UpdateInput(p,shift);drawing=false;
        bool valid=Brush || stroke.Any(point=>Distance(stroke[0],point)>=DragThreshold);
        if(valid)
        {
            if(stroke.Count==1)stroke.Add(stroke[0]);var mark=Ink.Mark(stroke.ToArray());
            Document.Add(mark);active=Transformable(mark)?Document.Marks.Count-1:-1;ShowHandles();
        }
        ClearStrokePreview();stroke.Clear();straightStart=-1;
    }
    internal void CancelInput()
    {drawing=transforming=polyline=false;beforeTransform=null;stroke.Clear();straightStart=-1;ClearStrokePreview();}
    private void ClearStrokePreview()
    {preview.Children.Clear();if(rasterPreviewActive){rasterPreviewActive=false;image.Source=Document.Render().Bitmap();}}
}
