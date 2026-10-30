using ClariBar.Core;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed class PinModel
{
    public ImageContent Content {get;private set;}
    public AnnotationDocument Document {get;}
    public PinRecord Record {get;}
    public PinnedImageWindow Window {get;set;}=null!;
    public bool SourceDirty {get;set;}=true;
    public PinModel(ImageContent content,PinRecord record)
    {Content=content;Record=record;Document=new(content.Frame);Document.Marks.AddRange(record.Marks.Select(m=>m.ToMark()));}
    public void Replace(ImageContent content){Content=content;Document.Source=content.Frame;Record.Kind=content.Kind;Record.Turns=0;Record.FlipX=Record.FlipY=false;SourceDirty=true;Document.Clear();Window.ContentChanged();}
    public PixelFrame Render()=>AnnotationRenderer.Transform(Document.Render(),Record.Turns,Record.FlipX,Record.FlipY);
}

internal sealed class PinManager
{
    private readonly DesktopToolsController tools;
    private readonly ToolSessionStore store;
    private readonly List<PinModel> pins=[];
    private readonly LinkedList<PinModel> closed=[];
    private readonly DispatcherTimer persistTimer=new(){Interval=TimeSpan.FromMilliseconds(600)};
    private readonly SemaphoreSlim saving=new(1,1);
    private uint lastClipboardSequence;
    private uint filePasteSequence;
    private bool stopping,restoring;
    public string ActiveGroup {get;private set;}="Default";
    public List<string> Groups {get;}=["Default"];
    public bool Hidden {get;private set;}
    public IReadOnlyList<PinModel> Images=>pins;
    public event Action? Changed;
    public PinManager(DesktopToolsController tools,ToolSessionStore store)
    {this.tools=tools;this.store=store;persistTimer.Tick+=async(_,_)=>{persistTimer.Stop();try{await PersistAsync();}catch(Exception ex){await tools.ShowErrorAsync(ex);}};}
    public PinModel Adopt(ImageContent content,PinRecord? record=null)
    {
        if(pins.Count>=100)throw new IOException(L.T("Close or destroy some images before opening more."));
        record??=new PinRecord {Group=ActiveGroup,Opacity=tools.Settings.PinDefaultOpacity};record.Kind=content.Kind;
        var pin=new PinModel(content,record);pins.Add(pin);pin.Document.Changed+=()=>Dirty();return pin;
    }
    public PinModel Create(ImageContent content,CaptureMark[]? marks=null,PixelRect? position=null,PinRecord? record=null)
    {
        var pin=Adopt(content,record);if(marks is not null)pin.Document.Marks.AddRange(marks);
        if(position is {} origin && tools.Settings.PinUseCapturePosition){pin.Record.X=origin.X;pin.Record.Y=origin.Y;pin.Record.HasPosition=true;pin.Record.Zoom=origin.Width/(double)Math.Max(1,content.Frame.Width);}
        var window=new PinnedImageWindow(tools,pin);pin.Window=window;tools.Track(window);
        if(record is null){pin.Record.State="Visible";Hidden=false;window.Activate();}
        ApplyVisibility();Dirty();return pin;
    }
    public async Task PasteAsync()
    {
        uint sequence=DesktopCapture.GetClipboardSequenceNumber();
        if(sequence==lastClipboardSequence && closed.Last is {} item)
        {closed.RemoveLast();item.Value.Record.State="Visible";item.Value.Record.Group=ActiveGroup;Hidden=false;item.Value.Window.Activate();ApplyVisibility();Dirty();return;}
        var content=await ImageContent.ClipboardAsync(tools.Settings,filePasteSequence==sequence);
        if(content is null)
        {
            if(closed.Last is {} previous){closed.RemoveLast();previous.Value.Record.State="Visible";previous.Value.Record.Group=ActiveGroup;Hidden=false;previous.Value.Window.Activate();ApplyVisibility();Dirty();return;}
            throw new IOException(L.T("The clipboard has no image, text, color or file to pin."));
        }
        if(content.SourcePath.Length>0)filePasteSequence=sequence;else if(sequence!=filePasteSequence)filePasteSequence=0;
        lastClipboardSequence=sequence;Create(content);
    }
    public async Task ReplaceAsync(PinModel pin)
    {var content=await ImageContent.ClipboardAsync(tools.Settings);if(content is null)throw new IOException(L.T("The clipboard has no image, text, color or file to pin."));pin.Replace(content);Dirty();}
    public void Close(PinModel pin)
    {
        if(stopping){pin.Window.Destroy();return;}
        if(pin.Record.State=="Closed")return;pin.Record.State="Closed";closed.AddLast(pin);pin.Window.SetVisible(false);
        while(closed.Count>tools.Settings.PinRecoverableCount){var oldest=closed.First!.Value;closed.RemoveFirst();Destroy(oldest);}
        Dirty();
    }
    public void Destroy(PinModel pin)
    {closed.Remove(pin);pins.Remove(pin);pin.Record.State="Destroyed";pin.Window.Destroy();Dirty();}
    public bool ShouldShow(PinModel pin)=>pin.Record.State=="Visible" && pin.Record.Group==ActiveGroup && !Hidden;
    public void Show(PinModel pin)
    {closed.Remove(pin);pin.Record.State="Visible";SwitchGroup(pin.Record.Group);pin.Window.Activate();ApplyVisibility();Dirty();}
    public void ToggleAll(){Hidden=!Hidden;ApplyVisibility();Dirty();}
    public void SwitchGroup(string group)
    {if(!Groups.Contains(group))return;ActiveGroup=group;Hidden=false;ApplyVisibility();Dirty();}
    public bool AddGroup(string name)
    {name=name.Trim();if(name.Length is 0 or >60 || name.Any(char.IsControl) || Groups.Contains(name))return false;Groups.Add(name);SwitchGroup(name);return true;}
    public bool RenameGroup(string old,string name)
    {
        name=name.Trim();if(old=="Default" || !Groups.Contains(old) || name.Length is 0 or >60 || name.Any(char.IsControl) || Groups.Contains(name))return false;
        Groups[Groups.IndexOf(old)]=name;foreach(var p in pins)if(p.Record.Group==old)p.Record.Group=name;if(ActiveGroup==old)ActiveGroup=name;Dirty();return true;
    }
    public void CloseGroup(string group)
    {if(group=="Default")return;foreach(var p in pins.Where(p=>p.Record.Group==group).ToArray())Destroy(p);Groups.Remove(group);if(ActiveGroup==group)ActiveGroup="Default";ApplyVisibility();Dirty();}
    public void MoveToGroup(PinModel pin,string group)
    {if(!Groups.Contains(group))return;pin.Record.Group=group;ApplyVisibility();Dirty();}
    private void ApplyVisibility()
    {foreach(var pin in pins)if(pin.Window is not null)pin.Window.SetVisible(ShouldShow(pin));}
    public PinModel? UnderCursor()
    {
        DesktopCapture.GetCursorPos(out var cursor);var map=pins.Where(p=>p.Window is not null && p.Record.State=="Visible" && DesktopCapture.IsWindowVisible(p.Window.Handle)).ToDictionary(p=>p.Window.Handle);
        PinModel? found=null;DesktopCapture.EnumWindows((window,_)=>{if(map.TryGetValue(window,out var pin) && DesktopCapture.GetWindowRect(window,out var rect) && rect.Pixel.Contains(cursor.X,cursor.Y)){found=pin;return false;}return true;},0);return found;
    }
    public void ToggleClickThrough()
    {if(UnderCursor() is {} pin)pin.Window.SetClickThrough(!pin.Record.ClickThrough);else foreach(var item in pins)if(item.Record.ClickThrough)item.Window.SetClickThrough(false);Dirty();}
    public PointInt32 Snap(PinModel moving,PointInt32 point,int width,int height)
    {
        var bounds=DesktopCapture.DesktopBounds;var others=DesktopCapture.Windows(bounds,moving.Window.Handle).Select(w=>w.Bounds);
        int x=point.X,y=point.Y,bestX=13,bestY=13;
        foreach(var box in others)
        {
            foreach(int edge in new[]{box.X,box.Right,box.X-width,box.Right-width})if(Math.Abs(point.X-edge)<bestX){bestX=Math.Abs(point.X-edge);x=edge;}
            foreach(int edge in new[]{box.Y,box.Bottom,box.Y-height,box.Bottom-height})if(Math.Abs(point.Y-edge)<bestY){bestY=Math.Abs(point.Y-edge);y=edge;}
        }
        return new(x,y);
    }
    public void Dirty(){if(stopping || restoring)return;persistTimer.Stop();persistTimer.Start();Changed?.Invoke();}
    public async Task PersistAsync()
    {
        if(restoring)return;await saving.WaitAsync();
        try
        {
            var records=new List<PinRecord>();
            foreach(var pin in pins.ToArray())
            {
                if(pin.Window is null || pin.Record.State is "Closed" or "Destroyed")continue;
                pin.Window.UpdateRecord();var content=pin.Content;
                var record=pin.Record with {PlainText=content.PlainText,Html=content.Html,SourcePath=content.SourcePath,Marks=pin.Document.Marks.Select(StoredMark.From).ToArray()};
                if(pin.SourceDirty || !File.Exists(store.PinPath(record.Id)))await File.WriteAllBytesAsync(store.PinPath(record.Id),await content.Frame.PngAsync());
                if(content.AnimationBytes is {} animation && (pin.SourceDirty || !File.Exists(store.PinPath(record.Id,"gif"))))await File.WriteAllBytesAsync(store.PinPath(record.Id,"gif"),animation);
                else if(content.AnimationBytes is null)ToolSessionStore.TryDelete(store.PinPath(record.Id,"gif"));
                if(ReferenceEquals(pin.Content,content))pin.SourceDirty=false;records.Add(record);
            }
            store.SavePins(new(){ActiveGroup=ActiveGroup,Groups=Groups.ToArray(),Hidden=Hidden,Pins=records.ToArray()});
        }
        finally{saving.Release();}
    }
    public async Task RestoreAsync()
    {
        if(pins.Count>0)return;restoring=true;
        try
        {
            var session=store.LoadPins();Groups.Clear();Groups.AddRange(session.Groups.Where(x=>x.Length is >0 and <=60 && !x.Any(char.IsControl)).Distinct().Take(100));if(!Groups.Contains("Default"))Groups.Insert(0,"Default");
            ActiveGroup=Groups.Contains(session.ActiveGroup)?session.ActiveGroup:"Default";Hidden=session.Hidden;
            foreach(var record in session.Pins.Take(100))
            {
                if(!ToolSessionStore.ValidId(record.Id) || !File.Exists(store.PinPath(record.Id)))continue;
                try
                {
                    var content=await ImageContent.FromFileAsync(store.PinPath(record.Id));content.SourcePath=record.SourcePath;content.Kind=record.Kind;content.PlainText=record.PlainText;content.Html=record.Html;
                    if(File.Exists(store.PinPath(record.Id,"gif")))content.AnimationBytes=await File.ReadAllBytesAsync(store.PinPath(record.Id,"gif"));
                    if(!Groups.Contains(record.Group))record.Group="Default";record.State="Visible";record.Zoom=double.IsFinite(record.Zoom)?Math.Clamp(record.Zoom,.01,16):1;record.Opacity=Math.Clamp(record.Opacity,10,100);
                    var model=Create(content,record:record);model.SourceDirty=false;
                }
                catch(Exception ex)when(ex is IOException or ArgumentException or System.Runtime.InteropServices.COMException){}
            }
            ApplyVisibility();
        }
        finally{restoring=false;Changed?.Invoke();}
    }
    public void Shutdown(){stopping=true;persistTimer.Stop();foreach(var pin in pins.ToArray())pin.Window?.Destroy();pins.Clear();closed.Clear();}
}
