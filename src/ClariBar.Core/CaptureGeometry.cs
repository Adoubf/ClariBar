namespace ClariBar.Core;

public static class CaptureGeometry
{
    public static PixelRect Move(PixelRect area,int dx,int dy,PixelRect limit) => new(
        Math.Clamp(area.X+dx,limit.X,Math.Max(limit.X,limit.Right-area.Width)),
        Math.Clamp(area.Y+dy,limit.Y,Math.Max(limit.Y,limit.Bottom-area.Height)),
        Math.Min(area.Width,limit.Width),Math.Min(area.Height,limit.Height));
    public static PixelRect Resize(PixelRect area,string anchor,int dx,int dy,PixelRect limit)
    {
        int left=area.X,top=area.Y,right=area.Right,bottom=area.Bottom;
        if(anchor.Contains('W'))left=Math.Clamp(left+dx,limit.X,right-1);
        if(anchor.Contains('E'))right=Math.Clamp(right+dx,left+1,limit.Right);
        if(anchor.Contains('N'))top=Math.Clamp(top+dy,limit.Y,bottom-1);
        if(anchor.Contains('S'))bottom=Math.Clamp(bottom+dy,top+1,limit.Bottom);
        return new(left,top,right-left,bottom-top);
    }
    public static (double X,double Y) Constrain(double ax,double ay,double bx,double by,bool square,bool angle)
    {
        double dx=bx-ax,dy=by-ay;
        if(square){double length=Math.Max(Math.Abs(dx),Math.Abs(dy));return(ax+Math.CopySign(length,dx),ay+Math.CopySign(length,dy));}
        if(angle){double radius=Math.Sqrt(dx*dx+dy*dy),theta=Math.Round(Math.Atan2(dy,dx)/(Math.PI/4))*(Math.PI/4);return(ax+radius*Math.Cos(theta),ay+radius*Math.Sin(theta));}
        return(bx,by);
    }
}
