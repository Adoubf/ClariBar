using System.Runtime.InteropServices;

namespace ClariBar.App;

// Materialize clipboard handles before a desktop check changes the clipboard.
// Reinstalling an OleGetClipboard proxy can recurse into the new clipboard owner.
internal sealed class ClipboardSnapshot
{
    private sealed record Entry(uint Format,nint Handle,bool Bitmap=false,bool Enhanced=false,nint Metafile=0,bool Palette=false);
    private readonly List<Entry> entries=[];
    [StructLayout(LayoutKind.Sequential)]private struct MetafilePicture {public int Mode,X,Y;public nint Metafile;}
    [DllImport("user32.dll")]private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")]private static extern bool CloseClipboard();
    [DllImport("user32.dll")]private static extern bool EmptyClipboard();
    [DllImport("user32.dll")]private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")]private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterClipboardFormat(string format);
    [DllImport("user32.dll")]private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")]private static extern nint SetClipboardData(uint format,nint data);
    [DllImport("kernel32.dll")]private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll")]private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")]private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")]private static extern nint GlobalAlloc(uint flags,nuint bytes);
    [DllImport("kernel32.dll")]private static extern nint GlobalFree(nint memory);
    [DllImport("user32.dll")]private static extern nint CopyImage(nint image,uint type,int width,int height,uint flags);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(nint image);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)]private static extern nint CopyEnhMetaFile(nint image,string? path);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)]private static extern nint CopyMetaFile(nint image,string? path);
    [DllImport("gdi32.dll")]private static extern bool DeleteEnhMetaFile(nint image);
    [DllImport("gdi32.dll")]private static extern bool DeleteMetaFile(nint image);
    [DllImport("gdi32.dll")]private static extern uint GetPaletteEntries(nint palette,uint start,uint count,byte[]? entries);
    [DllImport("gdi32.dll")]private static extern nint CreatePalette(byte[] entries);
    private static void Open(nint window)
    {for(int attempt=0;!OpenClipboard(window);attempt++){if(attempt>=20)throw new IOException("The clipboard is busy.");Thread.Sleep(25);}}
    public static ClipboardSnapshot Capture(nint window)
    {
        var snapshot=new ClipboardSnapshot();Open(window);
        try
        {
            bool hasSourceImage=IsClipboardFormatAvailable(8) || IsClipboardFormatAvailable(17) || IsClipboardFormatAvailable(RegisterClipboardFormat("PNG"));
            for(uint format=EnumClipboardFormats(0);format!=0;format=EnumClipboardFormats(format))
            {
                if(format==0x80)continue;
                // CF_BITMAP can be a temporary, synthesized GDI handle. Preserve the
                // lossless PNG/DIB source and let Windows regenerate this derived format.
                if(format==2 && hasSourceImage)continue;
                nint source=GetClipboardData(format);if(source==0)continue;
                if(format is 2 or 0x82){nint copy=CopyImage(source,0,0,0,0x2000);if(copy==0)throw new IOException("Cannot preserve the clipboard bitmap.");snapshot.entries.Add(new(format,copy,Bitmap:true));continue;}
                if(format is 14 or 0x8E){nint copy=CopyEnhMetaFile(source,null);if(copy==0)throw new IOException("Cannot preserve the clipboard metafile.");snapshot.entries.Add(new(format,copy,Enhanced:true));continue;}
                if(format==9)
                {
                    uint count=GetPaletteEntries(source,0,0,null);byte[] colors=new byte[count*4];GetPaletteEntries(source,0,count,colors);
                    byte[] palette=new byte[4+colors.Length];BitConverter.GetBytes((ushort)0x300).CopyTo(palette,0);BitConverter.GetBytes((ushort)count).CopyTo(palette,2);colors.CopyTo(palette,4);
                    nint copy=CreatePalette(palette);if(copy==0)throw new IOException("Cannot preserve the clipboard palette.");snapshot.entries.Add(new(format,copy,Palette:true));continue;
                }
                nuint size=GlobalSize(source);if(size==0 || size>512*1024*1024)throw new IOException("Cannot safely preserve this clipboard format: "+format);
                nint from=GlobalLock(source),copyMemory=GlobalAlloc(2,size),to=GlobalLock(copyMemory);
                if(from==0 || to==0){if(from!=0)GlobalUnlock(source);if(copyMemory!=0)GlobalFree(copyMemory);throw new IOException("Cannot preserve clipboard memory.");}
                nint metafile=0;
                try
                {
                    byte[] bytes=new byte[checked((int)size)];Marshal.Copy(from,bytes,0,bytes.Length);Marshal.Copy(bytes,0,to,bytes.Length);
                    if(format is 3 or 0x83){var picture=Marshal.PtrToStructure<MetafilePicture>(from);metafile=CopyMetaFile(picture.Metafile,null);picture.Metafile=metafile;Marshal.StructureToPtr(picture,to,false);}
                }
                finally{GlobalUnlock(source);GlobalUnlock(copyMemory);}
                snapshot.entries.Add(new(format,copyMemory,Metafile:metafile));
            }
            return snapshot;
        }
        catch{snapshot.Release();throw;}
        finally{CloseClipboard();}
    }
    public void Restore(nint window)
    {
        Open(window);
        try
        {
            if(!EmptyClipboard())throw new IOException("Cannot restore the clipboard.");
            for(int i=0;i<entries.Count;i++)if(SetClipboardData(entries[i].Format,entries[i].Handle)!=0)entries[i]=entries[i] with {Handle=0,Metafile=0};
        }
        finally{CloseClipboard();Release();}
    }
    private void Release()
    {
        foreach(var entry in entries)
        {
            if(entry.Handle==0)continue;
            if(entry.Bitmap || entry.Palette)DeleteObject(entry.Handle);else if(entry.Enhanced)DeleteEnhMetaFile(entry.Handle);else GlobalFree(entry.Handle);
            if(entry.Metafile!=0)DeleteMetaFile(entry.Metafile);
        }
        entries.Clear();
    }
}
