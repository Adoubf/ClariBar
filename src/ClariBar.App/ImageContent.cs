using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using SkiaSharp;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed class ImageContent
{
    public PixelFrame Frame {get;set;}
    public string Kind {get;set;}="Image";
    public string PlainText {get;set;}="";
    public string Html {get;set;}="";
    public string SourcePath {get;set;}="";
    public byte[]? AnimationBytes {get;set;}
    public ImageContent(PixelFrame frame)=>Frame=frame;
    public static async Task<ImageContent> FromFileAsync(string path)
    {
        if(new FileInfo(path).Length>128L*1024*1024)throw new IOException(L.T("The image is too large."));
        byte[] bytes=await File.ReadAllBytesAsync(path);
        using var codec=SKCodec.Create(new SKMemoryStream(bytes));
        if(codec is not null)
        {
            if((long)codec.Info.Width*codec.Info.Height>64_000_000)throw new IOException(L.T("The image is too large."));
            using var bitmap=new SKBitmap(new SKImageInfo(codec.Info.Width,codec.Info.Height,SKColorType.Bgra8888,SKAlphaType.Premul));
            var result=codec.GetPixels(bitmap.Info,bitmap.GetPixels(),new SKCodecOptions(0));
            if(result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))throw new IOException(L.T("Could not decode this image."));
            var decoded=AnnotationRenderer.Frame(bitmap);
            decoded=codec.EncodedOrigin switch
            {SKEncodedOrigin.TopRight=>AnnotationRenderer.Transform(decoded,0,true),SKEncodedOrigin.BottomRight=>AnnotationRenderer.Transform(decoded,2),SKEncodedOrigin.BottomLeft=>AnnotationRenderer.Transform(decoded,0,false,true),SKEncodedOrigin.LeftTop=>AnnotationRenderer.Transform(decoded,1,true),SKEncodedOrigin.RightTop=>AnnotationRenderer.Transform(decoded,1),SKEncodedOrigin.RightBottom=>AnnotationRenderer.Transform(decoded,1,false,true),SKEncodedOrigin.LeftBottom=>AnnotationRenderer.Transform(decoded,3),_=>decoded};
            return new(decoded){SourcePath=path,AnimationBytes=codec.FrameCount>1?bytes:null};
        }
        if(Path.GetExtension(path).Equals(".tga",StringComparison.OrdinalIgnoreCase))return new(DecodeTga(bytes)){SourcePath=path};
        var file=await StorageFile.GetFileFromPathAsync(path);using var stream=await file.OpenReadAsync();
        var decoder=await BitmapDecoder.CreateAsync(stream);
        if((long)decoder.PixelWidth*decoder.PixelHeight>64_000_000)throw new IOException(L.T("The image is too large."));
        var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,new BitmapTransform(),ExifOrientationMode.RespectExifOrientation,ColorManagementMode.ColorManageToSRgb);
        return new(new((int)decoder.OrientedPixelWidth,(int)decoder.OrientedPixelHeight,data.DetachPixelData())){SourcePath=path};
    }
    private static PixelFrame DecodeTga(byte[] bytes)
    {
        if(bytes.Length<18 || bytes[1]!=0 || bytes[2] is not (2 or 10))throw new IOException(L.T("Unsupported TGA image."));
        int width=BitConverter.ToUInt16(bytes,12),height=BitConverter.ToUInt16(bytes,14),depth=bytes[16],offset=18+bytes[0];
        if(width<1 || height<1 || (long)width*height>64_000_000 || depth is not (24 or 32))throw new IOException(L.T("Unsupported TGA image."));
        byte[] pixels=new byte[checked(width*height*4)];int count=width*height,index=0;
        void Pixel(int at)
        {
            if(offset+depth/8>bytes.Length)throw new IOException(L.T("Incomplete image data."));
            int x=at%width,y=at/width;if((bytes[17]&0x20)==0)y=height-1-y;if((bytes[17]&0x10)!=0)x=width-1-x;
            int i=(y*width+x)*4;byte a=depth==32?bytes[offset+3]:(byte)255;
            pixels[i]=(byte)(bytes[offset]*a/255);pixels[i+1]=(byte)(bytes[offset+1]*a/255);pixels[i+2]=(byte)(bytes[offset+2]*a/255);pixels[i+3]=a;offset+=depth/8;
        }
        while(index<count)
        {
            if(bytes[2]==2){Pixel(index++);continue;}
            if(offset>=bytes.Length)throw new IOException(L.T("Incomplete image data."));int header=bytes[offset++],run=(header&127)+1;
            if(index+run>count)throw new IOException(L.T("Incomplete image data."));
            if((header&128)==0)for(int n=0;n<run;n++)Pixel(index++);
            else{int saved=offset;for(int n=0;n<run;n++){offset=saved;Pixel(index++);}}
        }
        return new(width,height,pixels);
    }
    public static ImageContent Text(string text,string? html=null)
    {
        if(text.Length>100_000)text=text[..100_000];
        bool color=TryColor(text.Trim(),out var swatch);
        using var face=SKTypeface.FromFamilyName("Microsoft YaHei UI");using var font=new SKFont(face,20);using var paint=new SKPaint {IsAntialias=true,Color=SKColors.Black};
        var lines=new List<string>();int maxWidth=820;
        foreach(string line in text.Replace("\r","").Split('\n'))
        {
            string current="";foreach(var rune in line.EnumerateRunes()){string candidate=current+rune.ToString();if(font.MeasureText(candidate)>maxWidth && current.Length>0){lines.Add(current);current=rune.ToString();}else current=candidate;}lines.Add(current);
            if(lines.Count>1500)break;
        }
        int width=color?280:Math.Clamp((int)Math.Ceiling(lines.Select(x=>font.MeasureText(x)).DefaultIfEmpty(30).Max())+40,100,860);
        int height=color?170:Math.Clamp((int)(lines.Count*font.Spacing)+36,60,32000);
        using var bitmap=new SKBitmap(new SKImageInfo(width,height,SKColorType.Bgra8888,SKAlphaType.Premul));using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.White);
        if(color)
        {
            using var fill=new SKPaint{Color=new SKColor(swatch.R,swatch.G,swatch.B)};canvas.DrawRect(0,0,width,110,fill);canvas.DrawText(swatch.Hex,18,145,SKTextAlign.Left,font,paint);
        }
        else if(!string.IsNullOrWhiteSpace(html))HtmlText(canvas,html,font,paint,width-40,height-30);
        else {float y=18-font.Metrics.Ascent;foreach(string line in lines){canvas.DrawText(line,20,y,SKTextAlign.Left,font,paint);y+=font.Spacing;if(y>height+font.Spacing)break;}}
        return new(AnnotationRenderer.Frame(bitmap)){Kind=color?"Color":html is null?"Text":"Html",PlainText=text,Html=html??""};
    }
    private static void HtmlText(SKCanvas canvas,string html,SKFont defaultFont,SKPaint paint,int maxWidth,int maxHeight)
    {
        html=Regex.Replace(html,@"<(script|style)\b[^>]*>[\s\S]*?</\1>","",RegexOptions.IgnoreCase);
        var stack=new Stack<(bool Bold,bool Italic,SKColor Color,float Size)>();var state=(Bold:false,Italic:false,Color:SKColors.Black,Size:20f);
        float x=20,y=18-defaultFont.Metrics.Ascent;
        foreach(string token in Regex.Split(html,@"(<[^>]+>)"))
        {
            if(token.StartsWith('<'))
            {
                string name=Regex.Match(token,@"^</?\s*([\w]+)").Groups[1].Value.ToLowerInvariant();bool end=token.StartsWith("</");
                if(name is "br" or "p" or "div" or "li" or "tr" or "h1" or "h2" or "h3"){if(x>20 || name=="br"){x=20;y+=defaultFont.Spacing*1.1f;}}
                if(end){if(name is "b" or "strong" or "i" or "em" or "span" or "font" or "h1" or "h2" or "h3"){if(stack.TryPop(out var previous))state=previous;}continue;}
                if(name is "b" or "strong" or "i" or "em" or "span" or "font" or "h1" or "h2" or "h3")
                {
                    stack.Push(state);if(name is "b" or "strong" or "h1" or "h2" or "h3")state.Bold=true;if(name is "i" or "em")state.Italic=true;
                    if(name is "h1" or "h2" or "h3")state.Size=28;
                    var color=Regex.Match(token,@"(?:color\s*[:=]\s*['"" ]*)(#[\da-f]{6})",RegexOptions.IgnoreCase);if(color.Success && SKColor.TryParse(color.Groups[1].Value,out var c))state.Color=c;
                }
                continue;
            }
            using var face=SKTypeface.FromFamilyName("Microsoft YaHei UI",new SKFontStyle(state.Bold?700:400,5,state.Italic?SKFontStyleSlant.Italic:SKFontStyleSlant.Upright));using var font=new SKFont(face,state.Size);paint.Color=state.Color;
            foreach(var rune in WebUtility.HtmlDecode(token).EnumerateRunes())
            {
                string s=rune.ToString();if(s=="\r" || s=="\n")s=" ";float w=font.MeasureText(s);if(x+w>20+maxWidth){x=20;y+=Math.Max(font.Spacing,defaultFont.Spacing);}if(y>maxHeight)return;
                canvas.DrawText(s,x,y,SKTextAlign.Left,font,paint);x+=w;
            }
        }
    }
    private static string PlainHtml(string html)
    {
        string safe=Regex.Replace(html,@"<(script|style)\b[^>]*>[\s\S]*?</\1>","",RegexOptions.IgnoreCase);
        safe=Regex.Replace(safe,@"</?(?:p|div|li|tr|h[1-6])\b[^>]*>|<br\s*/?>","\n",RegexOptions.IgnoreCase);
        safe=Regex.Replace(safe,@"<!--[^>]*-->|<[^>]+>","");
        return WebUtility.HtmlDecode(safe).Trim();
    }
    private static bool TryColor(string text,out ClariBar.Core.ScreenColor color)
    {
        if(ClariBar.Core.ScreenColor.TryParse(text,out color))return true;
        var match=Regex.Match(text,@"^(?:rgb\s*\()?\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)\s*\)?$",RegexOptions.IgnoreCase);
        if(!match.Success)return false;
        var values=Enumerable.Range(1,3).Select(i=>double.TryParse(match.Groups[i].Value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double x)?x:-1).ToArray();
        if(values.Any(x=>x<0 || x>255))return false;if(values.All(x=>x<=1) && text.Contains('.'))values=values.Select(x=>x*255).ToArray();
        color=new((byte)Math.Round(values[0]),(byte)Math.Round(values[1]),(byte)Math.Round(values[2]));return true;
    }
    public static async Task<ImageContent?> ClipboardAsync(ClariBar.Core.AppSettings settings,bool fileAsText=false,DataPackageView? supplied=null)
    {
        var data=supplied??await ClipboardTransfer.ReadAsync();
        if(data.Contains(StandardDataFormats.StorageItems))
        {
            var items=await data.GetStorageItemsAsync();if(items.Count>0)
            {
                if(!fileAsText && items[0] is StorageFile file)
                {try{return await FromFileAsync(file.Path);}catch(Exception ex)when(ex is IOException or ArgumentException or COMException){if(!settings.PinFilePathsAsText)throw;}}
                if(settings.PinFilePathsAsText || fileAsText)return Text(string.Join(Environment.NewLine,items.Select(x=>x.Path)));
            }
        }
        if(supplied is null || data.Contains("PNG") || data.Contains(StandardDataFormats.Bitmap))
        {if(await PixelFrame.FromClipboardAsync(supplied is null?null:data) is {} image)return new(image);}
        if(!settings.PinIgnoreHtml && data.Contains(StandardDataFormats.Html))
        {
            string html=HtmlFormatHelper.GetStaticFragment(await data.GetHtmlFormatAsync());return Text(PlainHtml(html),html);
        }
        if(data.Contains(StandardDataFormats.Text))return Text(await data.GetTextAsync());
        return null;
    }
}

internal sealed class ImageAnimation:IDisposable
{
    private readonly SKMemoryStream stream;
    private readonly SKCodec codec;
    public int Count=>Math.Max(1,codec.FrameCount);
    public int Duration(int frame)=>codec.FrameInfo.Length>frame?Math.Max(20,codec.FrameInfo[frame].Duration):100;
    public ImageAnimation(byte[] bytes){stream=new(bytes);codec=SKCodec.Create(stream)??throw new IOException(L.T("Could not decode this image."));}
    public PixelFrame Frame(int frame)
    {using var bitmap=new SKBitmap(new SKImageInfo(codec.Info.Width,codec.Info.Height,SKColorType.Bgra8888,SKAlphaType.Premul));var result=codec.GetPixels(bitmap.Info,bitmap.GetPixels(),new SKCodecOptions(Math.Clamp(frame,0,Count-1)));if(result is not(SKCodecResult.Success or SKCodecResult.IncompleteInput))throw new IOException(L.T("Could not decode this image."));return AnnotationRenderer.Frame(bitmap);}
    public void Dispose(){codec.Dispose();stream.Dispose();}
}
