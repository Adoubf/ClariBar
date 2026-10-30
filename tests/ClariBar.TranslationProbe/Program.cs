using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ClariBar.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

if(args.Length<3)throw new ArgumentException("Usage: TranslationProbe <explicit-settings-directory> <selected-image.png> <report.json>");
var settings=new SettingsStore(args[0]).Load();string key=Credential.Read(Path.Combine(args[0],"ocr-credential.bin"));
if(args.FirstOrDefault(value=>value.StartsWith("--ocr=")) is {} languageOption)settings.OcrLanguage=languageOption[6..];
var file=await StorageFile.GetFileFromPathAsync(Path.GetFullPath(args[1]));using var stream=await file.OpenReadAsync();
var decoder=await BitmapDecoder.CreateAsync(stream);double scale=Math.Min(1,OcrEngine.MaxImageDimension/(double)Math.Max(decoder.PixelWidth,decoder.PixelHeight));
if(scale==1 && Math.Max(decoder.PixelWidth,decoder.PixelHeight)<1400)scale=Math.Min(2,OcrEngine.MaxImageDimension/(double)Math.Max(decoder.PixelWidth,decoder.PixelHeight));
using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,new BitmapTransform{ScaledWidth=(uint)Math.Max(1,Math.Round(decoder.PixelWidth*scale)),ScaledHeight=(uint)Math.Max(1,Math.Round(decoder.PixelHeight*scale)),InterpolationMode=BitmapInterpolationMode.Cubic},ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);
var engine=settings.OcrLanguage!="Auto"?OcrEngine.TryCreateFromLanguage(new Language(settings.OcrLanguage)):OcrEngine.TryCreateFromUserProfileLanguages();
engine??=OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages.First());
var recognized=await engine.RecognizeAsync(bitmap);
var positioned=recognized.Lines.Where(line=>line.Words.Count>0 && !string.IsNullOrWhiteSpace(line.Text)).Select(line=>
{
    double x=line.Words.Min(word=>word.BoundingRect.X)/scale,y=line.Words.Min(word=>word.BoundingRect.Y)/scale;
    double right=line.Words.Max(word=>word.BoundingRect.Right)/scale,bottom=line.Words.Max(word=>word.BoundingRect.Bottom)/scale;
    return new TranslationLine(line.Text,new((int)x,(int)y,(int)Math.Ceiling(right-x),(int)Math.Ceiling(bottom-y))){TextHeight=(int)Math.Ceiling(bottom-y)};
}).ToArray();
if(args.Contains("--ocr-only")){Console.WriteLine(JsonSerializer.Serialize(new{language=engine.RecognizerLanguage.LanguageTag,lines=positioned.Select(line=>line.Source)},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));return;}
if(args.Contains("--plain"))
{
    var paragraphs=TranslationProtocol.Paragraphs(positioned);var results=new List<object>();var watchAll=Stopwatch.StartNew();
    using var http=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(settings.AiOcrTimeoutSeconds)};
    foreach(var paragraph in paragraphs)
    {
        var spec=TranslationProtocol.BuildAiRequest(settings,[paragraph.Source],settings.TranslationSourceLanguage,settings.TranslationTargetLanguage,plainText:true);
        using var req=new HttpRequestMessage(HttpMethod.Post,spec.Target.Endpoint){Content=new StringContent(spec.Payload,Encoding.UTF8,"application/json")};req.Headers.Authorization=new("Bearer",key);
        using var res=await http.SendAsync(req);string singleBody=await res.Content.ReadAsStringAsync();if(key.Length>0)singleBody=singleBody.Replace(key,"[redacted]",StringComparison.Ordinal);res.EnsureSuccessStatusCode();
        string singleText=TranslationProtocol.ReadSingleTranslation(AiOcrProtocol.ExtractText(singleBody,spec.Target.ApiType));
        results.Add(new{source=paragraph.Source,bounds=paragraph.Bounds,target=singleText,response=JsonDocument.Parse(singleBody).RootElement.Clone()});
    }
    await File.WriteAllTextAsync(args[2],JsonSerializer.Serialize(new{model=settings.AiTranslationModel,ocrLanguage=engine.RecognizerLanguage.LanguageTag,visualLines=positioned.Length,paragraphs=paragraphs.Length,elapsedSeconds=watchAll.Elapsed.TotalSeconds,results},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
    Console.WriteLine(JsonSerializer.Serialize(new{model=settings.AiTranslationModel,visualLines=positioned.Length,paragraphs=paragraphs.Length,results},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));return;
}
string[] lines=positioned.Select(line=>line.Source).ToArray();
var request=TranslationProtocol.BuildAiRequest(settings,lines,settings.TranslationSourceLanguage,settings.TranslationTargetLanguage);
using var client=new HttpClient(new HttpClientHandler{UseProxy=!AiOcrProtocol.IsPrivateHost(request.Target.Endpoint),AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(settings.AiOcrTimeoutSeconds)};
using var message=new HttpRequestMessage(HttpMethod.Post,request.Target.Endpoint){Content=new StringContent(request.Payload,Encoding.UTF8,"application/json")};
if(request.Target.ApiType==AiOcrProtocol.Anthropic){message.Headers.Add("anthropic-version","2023-06-01");if(key.Length>0)message.Headers.Add("x-api-key",key);}
else if(request.Target.ApiType==AiOcrProtocol.Gemini){if(key.Length>0)message.Headers.Add("x-goog-api-key",key);}
else if(key.Length>0)message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
var watch=Stopwatch.StartNew();using var response=await client.SendAsync(message);string body=await response.Content.ReadAsStringAsync();if(key.Length>0)body=body.Replace(key,"[redacted]",StringComparison.Ordinal);response.EnsureSuccessStatusCode();
string text=AiOcrProtocol.ExtractText(body,request.Target.ApiType);string[]? translated=null;string? error=null;
try{translated=TranslationProtocol.ReadAiTranslations(text,lines.Length);}catch(IOException ex){error=ex.Message;}
var report=new{model=settings.AiTranslationModel,source=Path.GetFileName(args[1]),ocrLanguage=engine.RecognizerLanguage.LanguageTag,inputLines=lines,inputCount=lines.Length,payload=JsonDocument.Parse(request.Payload).RootElement.Clone(),response=JsonDocument.Parse(body).RootElement.Clone(),text,translated,error,elapsedSeconds=watch.Elapsed.TotalSeconds};
await File.WriteAllTextAsync(args[2],JsonSerializer.Serialize(report,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{report.model,report.ocrLanguage,report.inputCount,text,translated,error,report.elapsedSeconds},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));

static class Credential
{
    [StructLayout(LayoutKind.Sequential)]private struct Blob{public int Size;public nint Data;}
    [DllImport("crypt32.dll",SetLastError=true)]private static extern bool CryptUnprotectData(ref Blob input,nint description,nint entropy,nint reserved,nint prompt,uint flags,out Blob output);
    [DllImport("kernel32.dll")]private static extern nint LocalFree(nint memory);
    internal static string Read(string path)
    {
        if(!File.Exists(path))return "";byte[] encrypted=File.ReadAllBytes(path);var input=new Blob{Size=encrypted.Length,Data=Marshal.AllocHGlobal(encrypted.Length)};Blob output=default;
        try{Marshal.Copy(encrypted,0,input.Data,encrypted.Length);if(!CryptUnprotectData(ref input,0,0,0,0,1,out output))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());byte[] plain=new byte[output.Size];Marshal.Copy(output.Data,plain,0,plain.Length);try{return Encoding.UTF8.GetString(plain);}finally{Array.Clear(plain);}}
        finally{if(output.Data!=0){for(int i=0;i<output.Size;i++)Marshal.WriteByte(output.Data,i,0);LocalFree(output.Data);}Marshal.FreeHGlobal(input.Data);}
    }
}
