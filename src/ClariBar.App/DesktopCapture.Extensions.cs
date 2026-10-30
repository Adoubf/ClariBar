using System.Runtime.InteropServices;
using ClariBar.Core;

namespace ClariBar.App;

internal static partial class DesktopCapture
{
    [StructLayout(LayoutKind.Sequential)]private struct CursorInfo {public int Size,Flags;public nint Cursor;public Point Position;}
    [StructLayout(LayoutKind.Sequential)]private struct IconInfo {public int Icon;public uint XHotspot,YHotspot;public nint Mask,Color;}
    [DllImport("user32.dll")]private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")]private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")]private static extern bool GetIconInfo(nint icon,out IconInfo info);
    [DllImport("user32.dll")]private static extern bool DrawIconEx(nint dc,int x,int y,nint icon,int width,int height,uint step,nint brush,uint flags);
    [DllImport("user32.dll")]internal static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")]internal static extern bool MessageBeep(uint type);
    internal sealed class CapturedCursor:IDisposable
    {
        public nint Handle {get;private set;}
        public int X {get;}
        public int Y {get;}
        public CapturedCursor()
        {
            var cursor=new CursorInfo {Size=Marshal.SizeOf<CursorInfo>()};
            if(!GetCursorInfo(ref cursor) || (cursor.Flags&1)==0)return;
            Handle=CopyIcon(cursor.Cursor);X=cursor.Position.X;Y=cursor.Position.Y;
        }
        public void Dispose(){if(Handle!=0){DestroyIcon(Handle);Handle=0;}}
    }
    public static PixelFrame WithCursor(PixelFrame frame,PixelRect desktop,CapturedCursor cursor)
    {
        if(cursor.Handle==0)return frame;
        using var surface=new Surface(frame.Width,frame.Height);Marshal.Copy(frame.Pixels,0,surface.Bits,frame.Pixels.Length);
        if(GetIconInfo(cursor.Handle,out var info))
        {
            DrawIconEx(surface.Dc,cursor.X-desktop.X-(int)info.XHotspot,cursor.Y-desktop.Y-(int)info.YHotspot,cursor.Handle,0,0,0,0,3);
            if(info.Mask!=0)DeleteObject(info.Mask);if(info.Color!=0)DeleteObject(info.Color);
        }
        byte[] pixels=new byte[frame.Pixels.Length];Marshal.Copy(surface.Bits,pixels,0,pixels.Length);for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
        return new(frame.Width,frame.Height,pixels);
    }
    public static List<PixelRect> ChildRegions(nint parent,PixelRect desktop)
    {
        var regions=new List<PixelRect>();
        EnumChildWindows(parent,(child,_)=>
        {if(regions.Count>=1000)return false;if(IsWindowVisible(child) && GetWindowRect(child,out var rect)){var box=rect.Pixel.Intersect(desktop);if(box.Width>2 && box.Height>2)regions.Add(box);}return true;},0);
        return regions;
    }
}

internal sealed class ElementDetector
{
    [ComImport,Guid("618736E0-3C3D-11CF-810C-00AA00389B71"),InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IAccessible
    {
        [DispId(-5000)]object accParent {[return:MarshalAs(UnmanagedType.IDispatch)]get;}
        [DispId(-5015)]void accLocation(out int left,out int top,out int width,out int height,[In,MarshalAs(UnmanagedType.Struct)]object child);
        [DispId(-5017)][return:MarshalAs(UnmanagedType.Struct)]object accHitTest(int x,int y);
    }
    [DllImport("oleacc.dll")]private static extern int AccessibleObjectFromWindow(nint window,uint objectId,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IAccessible accessible);
    [DllImport("ClariBar.Native.dll",CallingConvention=CallingConvention.StdCall)]private static extern int CB_ElementRegions(nint window,[Out]DesktopCapture.Rect[] rectangles,int capacity);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<nint,PixelRect[]> elementRegions=new();
    private Task<PixelRect?>? pending;
    private readonly Dictionary<nint,List<PixelRect>> children=[];
    private static PixelRect? Hit(nint window,int x,int y)
    {
        try
        {
            var iid=typeof(IAccessible).GUID;if(AccessibleObjectFromWindow(window,unchecked((uint)-4),ref iid,out var root)!=0)return null;
            IAccessible current=root;object child=0;
            for(int depth=0;depth<12;depth++)
            {
                object hit=current.accHitTest(x,y);
                if(hit is IAccessible next){if(ReferenceEquals(next,current))break;current=next;child=0;}
                else {child=hit is int id?id:0;break;}
            }
            current.accLocation(out int left,out int top,out int width,out int height,child);
            return width>2 && height>2?new(left,top,width,height):null;
        }
        catch(Exception ex)when(ex is COMException or InvalidCastException or NotSupportedException or ArgumentException){return null;}
    }
    public async Task<PixelRect?> AtAsync(DetectedWindow window,int x,int y,PixelRect desktop)
    {
        if(!children.TryGetValue(window.Handle,out var boxes)){boxes=DesktopCapture.ChildRegions(window.Handle,desktop);children[window.Handle]=boxes;}
        PixelRect? fallback=boxes.Where(r=>r.Contains(x,y)).OrderBy(r=>(long)r.Width*r.Height).Cast<PixelRect?>().FirstOrDefault();
        // A hung accessibility provider is never allowed to create an unbounded queue of threads.
        if(pending is {IsCompleted:false})return fallback;
        pending=Task.Run(()=>
        {
            if(!elementRegions.TryGetValue(window.Handle,out var cached))
            {
                try{var boxes=new DesktopCapture.Rect[4096];int count=CB_ElementRegions(window.Handle,boxes,boxes.Length);cached=boxes.Take(Math.Clamp(count,0,boxes.Length)).Select(r=>r.Pixel).ToArray();}
                catch(Exception ex)when(ex is EntryPointNotFoundException or DllNotFoundException){cached=[];}
                elementRegions[window.Handle]=cached;
            }
            PixelRect? hit=cached.Where(r=>r.Contains(x,y)).OrderBy(r=>(long)r.Width*r.Height).Select(r=>(PixelRect?)r).FirstOrDefault();
            return hit??Hit(window.Handle,x,y);
        });
        if(await Task.WhenAny(pending,Task.Delay(140))==pending && await pending is {} result)
        {result=result.Intersect(window.Bounds).Intersect(desktop);if(result.Contains(x,y) && !result.IsEmpty && (fallback is null || (long)result.Width*result.Height<(long)fallback.Value.Width*fallback.Value.Height))return result;}
        return fallback;
    }
}
