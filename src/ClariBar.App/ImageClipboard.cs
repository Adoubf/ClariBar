using System.Runtime.InteropServices;

namespace ClariBar.App;

internal static class ImageClipboard
{
    private static nint owner;
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint CreateWindowEx(uint style,string className,string title,uint windowStyle,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterClipboardFormat(string format);
    [DllImport("user32.dll")]private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")]private static extern bool CloseClipboard();
    [DllImport("user32.dll")]private static extern bool EmptyClipboard();
    [DllImport("user32.dll")]private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")]private static extern nint SetClipboardData(uint format,nint data);
    [DllImport("kernel32.dll")]private static extern nint GlobalAlloc(uint flags,nuint size);
    [DllImport("kernel32.dll")]private static extern nint GlobalFree(nint memory);
    [DllImport("kernel32.dll")]private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")]private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")]private static extern nuint GlobalSize(nint memory);
    private static async Task OpenAsync()
    {
        if(owner==0)owner=CreateWindowEx(0,"STATIC","ClariBar.Clipboard",0,0,0,0,0,-3,0,0,0);
        for(int attempt=0;!OpenClipboard(owner);attempt++){if(attempt>=8)throw new IOException(ClariBar.Core.Localizer.T("The clipboard is busy."));await Task.Delay(30*(attempt+1));}
    }
    private static void Put(uint format,byte[] bytes)
    {
        nint memory=GlobalAlloc(2,(nuint)bytes.Length);if(memory==0)throw new OutOfMemoryException();
        try
        {
            nint target=GlobalLock(memory);if(target==0)throw new IOException(ClariBar.Core.Localizer.T("Cannot access clipboard memory."));
            try{Marshal.Copy(bytes,0,target,bytes.Length);}finally{GlobalUnlock(memory);}
            if(SetClipboardData(format,memory)==0)throw new IOException(ClariBar.Core.Localizer.T("Cannot write the clipboard image."));memory=0;
        }
        finally{if(memory!=0)GlobalFree(memory);}
    }
    public static async Task WriteAsync(PixelFrame image,byte[] png)
    {
        byte[] dib=new byte[checked(124+image.Pixels.Length)];
        void Int(int offset,int value)=>BitConverter.GetBytes(value).CopyTo(dib,offset);
        Int(0,124);Int(4,image.Width);Int(8,-image.Height);dib[12]=1;dib[14]=32;Int(16,3);Int(20,image.Pixels.Length);
        Int(40,0x00ff0000);Int(44,0x0000ff00);Int(48,0x000000ff);Int(52,unchecked((int)0xff000000));Int(56,0x73524742);Int(108,4);
        image.Pixels.CopyTo(dib,124);
        await OpenAsync();
        try
        {
            if(!EmptyClipboard())throw new IOException(ClariBar.Core.Localizer.T("Cannot open the clipboard for writing."));
            Put(RegisterClipboardFormat("PNG"),png);Put(17,dib);
        }
        finally{CloseClipboard();}
    }
    public static async Task<byte[]?> ReadPngAsync()
    {
        await OpenAsync();
        try
        {
            nint memory=GetClipboardData(RegisterClipboardFormat("PNG"));if(memory==0)return null;
            nuint size=GlobalSize(memory);if(size is <8 or >256*1024*1024)return null;
            nint source=GlobalLock(memory);if(source==0)return null;
            try
            {byte[] bytes=new byte[(int)size];Marshal.Copy(source,bytes,0,bytes.Length);return bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})?bytes:null;}
            finally{GlobalUnlock(memory);}
        }
        finally{CloseClipboard();}
    }
}
