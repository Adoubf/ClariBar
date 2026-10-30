using System.Runtime.InteropServices;
using SkiaSharp;
using Windows.Foundation;

namespace ClariBar.App;

internal sealed class AnnotationDocument(PixelFrame source)
{
    public PixelFrame Source { get; set; } = source;
    public List<CaptureMark> Marks { get; } = [];
    private readonly Stack<CaptureMark> undone = [];
    public bool CanUndo => Marks.Count > 0;
    public bool CanRedo => undone.Count > 0;
    public event Action? Changed;
    public void Add(CaptureMark mark) { Marks.Add(mark); undone.Clear(); Changed?.Invoke(); }
    public void Undo() { if (Marks.Count == 0) return; undone.Push(Marks[^1]); Marks.RemoveAt(Marks.Count - 1); Changed?.Invoke(); }
    public void Redo() { if (undone.TryPop(out var mark)) { Marks.Add(mark); Changed?.Invoke(); } }
    public void Clear() { Marks.Clear(); undone.Clear(); Changed?.Invoke(); }
    public void ReplaceLast(CaptureMark mark) { if (Marks.Count == 0) return; Marks[^1] = mark; Changed?.Invoke(); }
    public PixelFrame Render(CaptureMark? preview = null) => Source.Render(preview is null ? Marks : Marks.Append(preview));
}

internal static class AnnotationRenderer
{
    internal static SKBitmap Bitmap(PixelFrame frame)
    {
        var bitmap = new SKBitmap(new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        Marshal.Copy(frame.Pixels, 0, bitmap.GetPixels(), frame.Pixels.Length); return bitmap;
    }
    internal static PixelFrame Frame(SKBitmap bitmap)
    {
        byte[] pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        if (bitmap.RowBytes == bitmap.Width * 4) Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        else for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(bitmap.GetPixels() + y * bitmap.RowBytes, pixels, y * bitmap.Width * 4, bitmap.Width * 4);
        return new(bitmap.Width, bitmap.Height, pixels);
    }
    public static PixelFrame Render(PixelFrame source, IEnumerable<CaptureMark> marks)
    {
        var items=marks as IReadOnlyCollection<CaptureMark>??marks.ToArray();
        if(items.Count==0)return source;
        using var original = Bitmap(source);
        using var result = original.Copy();
        using var canvas = new SKCanvas(result);
        foreach (var mark in items)
        {
            if (mark.Points.Length < 2) continue;
            var a = mark.Points[0]; var b = mark.Points[^1];
            if(mark.Tool is "Arrow" or "Line" && Math.Abs(b.X-a.X)+Math.Abs(b.Y-a.Y)<.5)continue;
            var rect = new SKRect((float)Math.Min(a.X,b.X),(float)Math.Min(a.Y,b.Y),(float)Math.Max(a.X,b.X),(float)Math.Max(a.Y,b.Y));
            var ink = SKColor.Parse(mark.Color).WithAlpha((byte)Math.Clamp(mark.Tool=="Marker" && mark.Alpha==255?100:mark.Alpha,0,255));
            using var paint = new SKPaint { Color=ink, IsAntialias=true, StrokeWidth=mark.Width, StrokeCap=SKStrokeCap.Round, StrokeJoin=SKStrokeJoin.Round, Style=SKPaintStyle.Stroke };
            using var path = new SKPath(); path.MoveTo((float)a.X,(float)a.Y);
            foreach (var p in mark.Points.Skip(1)) path.LineTo((float)p.X,(float)p.Y);
            if (mark.Tool is "Mosaic" or "Blur")
            {
                using var combined = result.Copy();
                canvas.Save();
                if (mark.Fill=="Brush" || mark.Points.Length>2)
                { using var mask = new SKPath(); using var maskPaint=new SKPaint{Style=SKPaintStyle.Stroke,StrokeWidth=Math.Max(8,mark.Width),StrokeCap=SKStrokeCap.Round,StrokeJoin=SKStrokeJoin.Round};maskPaint.GetFillPath(path,mask);canvas.ClipPath(mask); }
                else canvas.ClipRect(rect);
                if(mark.Tool=="Blur")
                {
                    using var blur = SKImageFilter.CreateBlur(Math.Max(2,mark.Width),Math.Max(2,mark.Width));
                    using var blurPaint = new SKPaint { ImageFilter=blur, BlendMode=SKBlendMode.Src }; canvas.DrawBitmap(combined,0,0,blurPaint);
                }
                else
                {
                    byte[] data=Frame(combined).Pixels; int block=Math.Max(8,mark.Width*4);
                    int left=Math.Max(0,(int)mark.Points.Min(p=>p.X)-block),top=Math.Max(0,(int)mark.Points.Min(p=>p.Y)-block);
                    int right=Math.Min(source.Width,(int)mark.Points.Max(p=>p.X)+block),bottom=Math.Min(source.Height,(int)mark.Points.Max(p=>p.Y)+block);
                    for(int y=top;y<bottom;y+=block)for(int x=left;x<right;x+=block)
                    {
                        long red=0,green=0,blue=0,alpha=0;int count=0;
                        for(int yy=y;yy<Math.Min(y+block,bottom);yy++)for(int xx=x;xx<Math.Min(x+block,right);xx++)
                        {int i=(yy*source.Width+xx)*4;blue+=data[i];green+=data[i+1];red+=data[i+2];alpha+=data[i+3];count++;}
                        // Unpremultiply before creating an SKColor; SKCanvas performs premultiplication.
                        long denominator=Math.Max(1,alpha);
                        using var tile=new SKPaint {Color=new SKColor((byte)(red*255/denominator),(byte)(green*255/denominator),(byte)(blue*255/denominator),(byte)(alpha/count)),BlendMode=SKBlendMode.Src};
                        canvas.DrawRect(x,y,Math.Min(block,right-x),Math.Min(block,bottom-y),tile);
                    }
                }
                canvas.Restore(); continue;
            }
            if(mark.Tool=="Eraser")
            {
                canvas.Save();paint.StrokeWidth=Math.Max(8,mark.Width);
                if(mark.Fill=="Rectangle")canvas.ClipRect(rect);
                else {using var mask=new SKPath();paint.GetFillPath(path,mask);canvas.ClipPath(mask);}
                using(var restore=new SKPaint {BlendMode=SKBlendMode.Src})canvas.DrawBitmap(original,0,0,restore);
                canvas.Restore();
                continue;
            }
            if(mark.Tool is "Rectangle" or "Ellipse")
            {
                if(mark.Fill!="Outline") {paint.Style=SKPaintStyle.Fill;if(mark.Fill=="Bordered")paint.Color=ink.WithAlpha((byte)Math.Min(90,(int)ink.Alpha));if(mark.Tool=="Ellipse")canvas.DrawOval(rect,paint);else canvas.DrawRect(rect,paint);}
                if(mark.Fill!="Filled") {paint.Style=SKPaintStyle.Stroke;paint.Color=ink;if(mark.Tool=="Ellipse")canvas.DrawOval(rect,paint);else canvas.DrawRect(rect,paint);}
            }
            else if(mark.Tool=="Text")
            {
                using var face=SKTypeface.FromFamilyName(mark.FontFamily,new SKFontStyle(mark.Bold?SKFontStyleWeight.Bold:SKFontStyleWeight.Normal,SKFontStyleWidth.Normal,mark.Italic?SKFontStyleSlant.Italic:SKFontStyleSlant.Upright));
                using var font=new SKFont(face,mark.FontSize) {Edging=SKFontEdging.Antialias,Subpixel=true};
                paint.Style=SKPaintStyle.Fill;canvas.Save();canvas.Translate((float)a.X,(float)a.Y);canvas.RotateDegrees(mark.Rotation);
                float lineY=-font.Metrics.Ascent;
                foreach(string line in mark.Text.Replace("\r","").Split('\n')) {canvas.DrawText(line,0,lineY,SKTextAlign.Left,font,paint);lineY+=font.Spacing;}
                canvas.Restore();
            }
            else
            {
                if(mark.Tool=="Marker" && mark.Fill=="Rectangle"){paint.Style=SKPaintStyle.Fill;canvas.DrawRect(rect,paint);continue;}
                canvas.DrawPath(path,paint);
                if(mark.Tool=="Arrow")
                {
                    double angle=Math.Atan2(b.Y-a.Y,b.X-a.X),length=Math.Max(12,mark.Width*4);
                    foreach(double offset in new[]{-.5,.5})canvas.DrawLine((float)b.X,(float)b.Y,(float)(b.X-length*Math.Cos(angle+offset)),(float)(b.Y-length*Math.Sin(angle+offset)),paint);
                }
            }
        }
        return Frame(result);
    }
    public static PixelFrame Transform(PixelFrame frame,int quarterTurns,bool flipX=false,bool flipY=false)
    {
        int turns=((quarterTurns%4)+4)%4,width=turns%2==0?frame.Width:frame.Height,height=turns%2==0?frame.Height:frame.Width;
        byte[] result=new byte[checked(width*height*4)];
        for(int y=0;y<frame.Height;y++)for(int x=0;x<frame.Width;x++)
        {
            var (xx,yy)=turns switch {1=>(frame.Height-1-y,x),2=>(frame.Width-1-x,frame.Height-1-y),3=>(y,frame.Width-1-x),_=>(x,y)};
            if(flipX)xx=width-1-xx;if(flipY)yy=height-1-yy;
            System.Buffer.BlockCopy(frame.Pixels,(y*frame.Width+x)*4,result,(yy*width+xx)*4,4);
        }
        return new(width,height,result);
    }
}
