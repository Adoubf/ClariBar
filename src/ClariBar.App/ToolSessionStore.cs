using System.Text.Json;
using ClariBar.Core;
using Windows.Foundation;

namespace ClariBar.App;

internal sealed class StoredMark
{
    public string Tool {get;set;}="Pen";
    public double[] X {get;set;}=[];
    public double[] Y {get;set;}=[];
    public string Color {get;set;}="#FF5555";
    public int Width {get;set;}=3;
    public string Text {get;set;}="";
    public int Alpha {get;set;}=255;
    public string Fill {get;set;}="Outline";
    public string FontFamily {get;set;}="Microsoft YaHei UI";
    public float FontSize {get;set;}=22;
    public float Rotation {get;set;}
    public bool Bold {get;set;}
    public bool Italic {get;set;}
    public static StoredMark From(CaptureMark mark)=>new(){Tool=mark.Tool,X=mark.Points.Select(p=>p.X).ToArray(),Y=mark.Points.Select(p=>p.Y).ToArray(),Color=mark.Color,Width=mark.Width,Text=mark.Text,Alpha=mark.Alpha,Fill=mark.Fill,FontFamily=mark.FontFamily,FontSize=mark.FontSize,Rotation=mark.Rotation,Bold=mark.Bold,Italic=mark.Italic};
    public CaptureMark ToMark()=>new(Tool,X.Zip(Y,(x,y)=>new Point(x,y)).ToArray(),AppSettings.ValidColor(Color)?Color:"#FF5555",Math.Clamp(Width,1,200),Text){Alpha=Math.Clamp(Alpha,0,255),Fill=Fill,FontFamily=FontFamily,FontSize=Math.Clamp(FontSize,6,400),Rotation=Rotation,Bold=Bold,Italic=Italic};
}

internal sealed class CaptureRecord
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public DateTimeOffset CapturedAt {get;set;}=DateTimeOffset.Now;
    public PixelRect Desktop {get;set;}
    public PixelRect Area {get;set;}
    public StoredMark[] Marks {get;set;}=[];
}

internal sealed record PinRecord
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Group {get;set;}="Default";
    public string State {get;set;}="Visible";
    public int X {get;set;}
    public int Y {get;set;}
    public bool HasPosition {get;set;}
    public double Zoom {get;set;}=1;
    public int Opacity {get;set;}=100;
    public int Turns {get;set;}
    public bool FlipX {get;set;}
    public bool FlipY {get;set;}
    public bool ClickThrough {get;set;}
    public bool Thumbnail {get;set;}
    public bool Playing {get;set;}=true;
    public int AnimationFrame {get;set;}
    public double AnimationSpeed {get;set;}=1;
    public string Kind {get;set;}="Image";
    public string PlainText {get;set;}="";
    public string Html {get;set;}="";
    public string SourcePath {get;set;}="";
    public StoredMark[] Marks {get;set;}=[];
}
internal sealed class PinSession
{
    public string ActiveGroup {get;set;}="Default";
    public string[] Groups {get;set;}=["Default"];
    public bool Hidden {get;set;}
    public PinRecord[] Pins {get;set;}=[];
}

internal sealed class ToolSessionStore(string root)
{
    public string Root {get;}=root;
    public List<CaptureRecord> History {get;}=[];
    public PixelRect? LastArea {get;private set;}
    private static readonly JsonSerializerOptions Json=new(Wire.Json){WriteIndented=true};
    private string DirectoryFor(string child){string path=Path.Combine(Root,child);Directory.CreateDirectory(path);return path;}
    public static bool ValidId(string id)=>id.Length==32 && id.All(Uri.IsHexDigit);
    public string CapturePath(string id)=>Path.Combine(DirectoryFor("history"),(ValidId(id)?id:throw new IOException("Invalid capture ID"))+".png");
    public string PinPath(string id,string extension="png")=>Path.Combine(DirectoryFor("pins"),(ValidId(id)?id:throw new IOException("Invalid pin ID"))+"."+(extension=="gif"?"gif":"png"));
    private void Write<T>(string name,T value)
    {
        Directory.CreateDirectory(Root);string path=Path.Combine(Root,name),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(value,Json));File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public void LoadHistory()
    {
        try{var path=Path.Combine(Root,"capture-history.json");if(File.Exists(path))History.AddRange((JsonSerializer.Deserialize<CaptureRecord[]>(File.ReadAllText(path),Wire.Json)??[]).Where(x=>ValidId(x.Id) && File.Exists(CapturePath(x.Id)) && !x.Area.IsEmpty).Take(100));}
        catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){History.Clear();}
        try{var path=Path.Combine(Root,"last-area.json");if(File.Exists(path))LastArea=JsonSerializer.Deserialize<PixelRect>(File.ReadAllText(path),Wire.Json);}catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){}
    }
    public async Task RecordAsync(PixelFrame desktop,PixelRect desktopBounds,PixelRect area,IEnumerable<CaptureMark> marks,int limit)
    {
        LastArea=new PixelRect(area.X+desktopBounds.X,area.Y+desktopBounds.Y,area.Width,area.Height);Write("last-area.json",LastArea);
        if(limit<=0)return;
        var record=new CaptureRecord {Desktop=desktopBounds,Area=area,Marks=marks.Select(StoredMark.From).ToArray()};
        await File.WriteAllBytesAsync(CapturePath(record.Id),await desktop.PngAsync());History.Insert(0,record);
        TrimHistory(limit);Write("capture-history.json",History);
    }
    public void TrimHistory(int limit)
    {
        while(History.Count>Math.Clamp(limit,0,100)){var item=History[^1];History.RemoveAt(History.Count-1);TryDelete(CapturePath(item.Id));}
        Write("capture-history.json",History);
    }
    public void ClearHistory(){foreach(var item in History)TryDelete(CapturePath(item.Id));History.Clear();Write("capture-history.json",History);}
    public PinSession LoadPins()
    {
        try{string path=Path.Combine(Root,"pin-session.json");return File.Exists(path)?JsonSerializer.Deserialize<PinSession>(File.ReadAllText(path),Wire.Json)??new():new();}
        catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){return new();}
    }
    public void SavePins(PinSession session)
    {
        Write("pin-session.json",session);var keep=session.Pins.Select(p=>p.Id).Where(ValidId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var file in Directory.EnumerateFiles(DirectoryFor("pins")))if(ValidId(Path.GetFileNameWithoutExtension(file)) && !keep.Contains(Path.GetFileNameWithoutExtension(file)))TryDelete(file);
    }
    internal static void TryDelete(string file){try{if(File.Exists(file))File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}}
}
