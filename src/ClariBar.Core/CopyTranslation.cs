namespace ClariBar.Core;

public static class CopyTranslation
{
    public const int MaximumCharacters=20000;
    public static bool HasText(string? text)=>!string.IsNullOrWhiteSpace(text) && text.Any(char.IsLetter);
    public static PixelRect Place(PixelRect work,int cursorX,int cursorY,int width,int height,int gap)
    {
        width=Math.Clamp(width,1,Math.Max(1,work.Width));height=Math.Clamp(height,1,Math.Max(1,work.Height));
        int x=cursorX+gap,y=cursorY+gap;
        if(x+width>work.Right)x=cursorX-gap-width;
        if(y+height>work.Bottom)y=cursorY-gap-height;
        return new(Math.Clamp(x,work.X,Math.Max(work.X,work.Right-width)),Math.Clamp(y,work.Y,Math.Max(work.Y,work.Bottom-height)),width,height);
    }
}

public sealed class CopyPopupDismissal
{
    // Allow reading nearby and travelling into a card that grows while streaming.
    // A brief excursion or a text-selection drag must not dismiss the card.
    private long? distantSince;
    public void Reset()=>distantSince=null;
    public bool Observe(PixelRect popup,int anchorX,int anchorY,int x,int y,double dpiScale,bool dragging,long milliseconds)
    {
        double margin=160*Math.Clamp(dpiScale,.5,8);
        double dx=Math.Max(Math.Max((double)popup.X-x,x-(double)popup.Right),0);
        double dy=Math.Max(Math.Max((double)popup.Y-y,y-(double)popup.Bottom),0);
        double ax=(double)x-anchorX,ay=(double)y-anchorY;
        if(dragging || dx*dx+dy*dy<=margin*margin || ax*ax+ay*ay<=margin*margin){Reset();return false;}
        distantSince??=milliseconds;
        return milliseconds-distantSince.Value>=250;
    }
}
