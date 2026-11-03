using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private async Task CheckStreamingTranslationAsync(Action<bool,string> require)
    {
        var original=JsonSerializer.Serialize(settings,Wire.Json);TranslationWindow? window=null;CopyTranslationWindow? copy=null;
        async Task UntilAsync(Func<bool> predicate)
        {for(int attempt=0;attempt<250;attempt++){if(predicate())return;await Task.Delay(20);}throw new IOException("A progressive translation state was not visible before completion.");}
        try
        {
            settings.TranslationEngine="Ai";settings.AiOcrEndpoint="http://127.0.0.1:8000/v1";settings.AiTranslationModel="hy-mt2-7b";settings.TranslationTargetLanguage=settings.TranslationSourceLanguage="Auto";
            foreach(string api in AiOcrProtocol.ApiTypes.Skip(1))
            {
                settings.AiOcrApiType=api;using var handler=new StreamingTranslationHandler(api);var updates=new List<TranslationUpdate>();
                var pending=TranslationService.TranslateLinesAsync(["First: Hello"],"en","zh","Ai",settings,handler:handler,key:"",updated:updates.Add);
                await UntilAsync(()=>updates.Count>0);
                require(!pending.IsCompleted && updates[0] is {Text:"第一",Complete:false},api+": actual SSE text is published before the HTTP response finishes");
                handler.Release.TrySetResult();require((await pending).Single()=="第一段" && updates.Last() is {Complete:true} && handler.StreamingRequests==1,api+": streamed text finishes exactly once with its source block index");
            }
            settings.AiOcrApiType=AiOcrProtocol.Chat;
            foreach(bool rejected in new[]{false,true})
            {
                using var handler=new StreamingTranslationHandler(AiOcrProtocol.Chat,json:true,reject:rejected);var updates=new List<TranslationUpdate>();
                string[] output=await TranslationService.TranslateLinesAsync(["First: Hello"],"en","zh","Ai",settings,handler:handler,key:"",updated:updates.Add);
                require(output.Single()=="普通译文" && updates.Count==1 && updates[0].Complete && handler.Requests==(rejected?2:1),rejected?"A server explicitly rejecting streaming retries once as a normal request":"A server returning ordinary JSON publishes a completed paragraph without simulated typing");
            }
            byte[] pixels=new byte[720*180*4];Array.Fill(pixels,(byte)255);
            var frame=new PixelFrame(720,180,pixels).Render([
                new("Text",[new(20,20),new(21,21)],"#101010",4,"First: Hello streaming."){FontSize=26},
                new("Text",[new(20,95),new(21,96)],"#101010",4,"Second: World streaming."){FontSize=26}
            ]);
            HideForTool();
            foreach(bool overlay in new[]{false,true})
            {
                using var handler=new StreamingTranslationHandler(AiOcrProtocol.Chat,stagger:true);
                window=new(desktopTools,frame,new(120,120,720,180),overlay,translationHandler:handler);desktopTools.Track(window);window.Activate();
                await UntilAsync(()=>window.ProgressiveRenders>0 && window.Result is {Lines.Length:2} partial && partial.Lines[0].Target=="" && partial.Lines[1].Target=="第二段");
                require(!window.InitialTranslation.IsCompleted && (!overlay || window.TranslationMasks.Children.Count==1),"A pending source paragraph stays visible while an out-of-order translated paragraph is already shown");
                handler.Start.TrySetResult();
                await UntilAsync(()=>window.ProgressiveRenders>0 && window.Result is {Lines.Length:2} result && result.Lines[0].Target=="第一" && result.Lines[1].Target=="第二段");
                require(!window.InitialTranslation.IsCompleted && window.Result!.Lines[0].Target=="第一" && window.Result.Lines[1].Target=="第二段",overlay?"The screenshot overlay shows partial and out-of-order paragraphs at their original positions":"Bilingual screenshot translation becomes readable while another paragraph is still generating");
                if(overlay)require(window.TranslationMasks.Children.Count==2,"Progressive overlay masks only the areas whose translations have arrived");
                else require(InteractionField<TextBox>(window,"translated").Text.Contains("第一") && InteractionField<TextBox>(window,"translated").Text.IndexOf("第一",StringComparison.Ordinal)<InteractionField<TextBox>(window,"translated").Text.IndexOf("第二",StringComparison.Ordinal),"Bilingual text retains source order instead of arrival order");
                await RenderCheckImageAsync(L.LanguageCode+(overlay?"-translation-stream-overlay":"-translation-stream-bilingual"),(FrameworkElement)window.Content);
                handler.Release.TrySetResult();await window.InitialTranslation;
                require(window.Result!.Lines.Select(line=>line.Target).SequenceEqual(new[]{"第一段","第二段"}),"Completing the stream preserves every paragraph and the final result");window.Close();window=null;
            }
            using(var handler=new StreamingTranslationHandler(AiOcrProtocol.Chat,truncate:true))
            {
                window=new(desktopTools,frame,new(120,120,720,180),true,translationHandler:handler);desktopTools.Track(window);window.Activate();
                await UntilAsync(()=>window.ProgressiveRenders>0 && window.Result?.Lines[0].Target=="第一");handler.Release.TrySetResult();await window.InitialTranslation;
                require(window.Result!.Lines[0].Target=="第一" && InteractionField<TextBlock>(window,"status").Text.Contains(L.T("Translation interrupted. Received text is kept.")),"An interrupted screenshot stream retains its masks and displays the error");
                typeof(TranslationWindow).GetMethod("SwitchMode",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,null);await Task.Delay(150);
                window=InteractionField<HashSet<Window>>(desktopTools,"windows").OfType<TranslationWindow>().Single();
                require(!window.IsOverlay && window.Result!.Lines[0].Target=="第一" && InteractionField<TextBlock>(window,"status").Text.Contains(L.T("Translation interrupted. Received text is kept.")),"Switching a partial result to bilingual comparison preserves both text and error state");window.Close();window=null;
            }
            using(var handler=new StreamingTranslationHandler(AiOcrProtocol.Chat))
            {
                copy=new(desktopTools,translationHandler:handler);desktopTools.Track(copy);
                var pending=copy.ShowTextAsync("First: Hello\n\nSecond: World",120,120);InteractionField<DispatcherTimer>(copy,"dismiss").Stop();
                await UntilAsync(()=>copy.ProgressiveRenders>0 && copy.TranslatedText.StartsWith("第一",StringComparison.Ordinal) && copy.TranslatedText.Contains("第二段"));
                require(!pending.IsCompleted && copy.IsRunning && copy.TranslatedText.StartsWith("第一",StringComparison.Ordinal),"Copy translation streams ordered paragraphs into the same popup before completion");
                await RenderCheckImageAsync(L.LanguageCode+"-copy-translation-stream",(FrameworkElement)copy.Content);
                handler.Release.TrySetResult();await pending;
                require(copy.TranslatedText=="第一段"+Environment.NewLine+Environment.NewLine+"第二段" && !copy.IsRunning,"The completed copy popup preserves paragraph breaks and clears its busy state");copy.Close();copy=null;
            }
            using(var handler=new StreamingTranslationHandler(AiOcrProtocol.Chat,truncate:true))
            {
                copy=new(desktopTools,translationHandler:handler);desktopTools.Track(copy);
                var pending=copy.ShowTextAsync("First: Hello",120,120);InteractionField<DispatcherTimer>(copy,"dismiss").Stop();await UntilAsync(()=>copy.ProgressiveRenders>0);
                handler.Release.TrySetResult();await pending;
                require(copy.TranslatedText=="第一" && !copy.IsRunning && InteractionField<TextBlock>(copy,"failure").Visibility==Visibility.Visible,"A broken copy stream retains received text with a separate error and an available retry");copy.Close();copy=null;
            }
            using(var handler=new StreamingTranslationHandler(AiOcrProtocol.Chat))
            {
                copy=new(desktopTools,translationHandler:handler);desktopTools.Track(copy);var pending=copy.ShowTextAsync("First: Hello",120,120);InteractionField<DispatcherTimer>(copy,"dismiss").Stop();await UntilAsync(()=>copy.ProgressiveRenders>0);copy.Close();copy=null;
                await pending.WaitAsync(TimeSpan.FromSeconds(3));require(handler.Cancelled,"Closing a popup cancels a blocked HTTP stream immediately");
            }
        }
        finally
        {
            copy?.Close();window?.Close();var saved=JsonSerializer.Deserialize<AppSettings>(original,Wire.Json)!;
            foreach(var property in typeof(AppSettings).GetProperties().Where(property=>property.CanWrite))property.SetValue(settings,property.GetValue(saved));
        }
    }
}

internal sealed class StreamingTranslationHandler(string api,bool json=false,bool reject=false,bool truncate=false,bool stagger=false):HttpMessageHandler
{
    internal TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Start {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Requests,StreamingRequests;
    internal bool Cancelled;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
    {
        Interlocked.Increment(ref Requests);string payload=await request.Content!.ReadAsStringAsync(cancellation);using var body=JsonDocument.Parse(payload);
        bool streaming=api==AiOcrProtocol.Gemini?request.RequestUri!.AbsolutePath.EndsWith(":streamGenerateContent"):body.RootElement.GetProperty("stream").GetBoolean();
        if(streaming)Interlocked.Increment(ref StreamingRequests);
        if(json && !request.Headers.Accept.Any(value=>value.MediaType=="application/json"))return new(HttpStatusCode.NotAcceptable){Content=new StringContent("JSON response required")};
        if(reject && streaming)return new(HttpStatusCode.BadRequest){Content=new StringContent("{\"error\":{\"message\":\"stream is not supported\"}}",Encoding.UTF8,"application/json")};
        if(json || !streaming)return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{choices=new[]{new{message=new{content="普通译文"}}}}),Encoding.UTF8,"application/json")};
        bool second=payload.Contains("Second",StringComparison.OrdinalIgnoreCase);string start=second?"第二段":"第一",end=second?"":"段";
        string Event(string text)=>"data: "+(api switch
        {
            AiOcrProtocol.Chat=>JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=text}}}}),
            AiOcrProtocol.Responses=>JsonSerializer.Serialize(new{type="response.output_text.delta",delta=text}),
            AiOcrProtocol.Anthropic=>JsonSerializer.Serialize(new{type="content_block_delta",delta=new{type="text_delta",text}}),
            _=>JsonSerializer.Serialize(new{candidates=new[]{new{content=new{parts=new[]{new{text}}}}}})
        })+"\n\n";
        string terminal=api switch{AiOcrProtocol.Responses=>"data: {\"type\":\"response.completed\"}\n\n",AiOcrProtocol.Anthropic=>"data: {\"type\":\"message_stop\"}\n\n",_=>"data: [DONE]\n\n"};
        var stream=new StreamingCheckStream(Encoding.UTF8.GetBytes(Event(start)),Encoding.UTF8.GetBytes(truncate && !second?"":(end.Length>0?Event(end):"")+terminal),second?Task.CompletedTask:Release.Task,()=>Cancelled=true,stagger && !second?Start.Task:Task.CompletedTask);
        return new(HttpStatusCode.OK){Content=new StreamContent(stream){Headers={ContentType=new("text/event-stream")}}};
    }
}
internal sealed class StreamingCheckStream(byte[] prefix,byte[] suffix,Task release,Action cancelled,Task start):Stream
{
    private int position;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellation=default)
    {
        try{if(position==0)await start.WaitAsync(cancellation);if(position>=prefix.Length)await release.WaitAsync(cancellation);}catch(OperationCanceledException){cancelled();throw;}
        byte[] part=position<prefix.Length?prefix:suffix;int offset=position<prefix.Length?position:position-prefix.Length;int count=Math.Min(buffer.Length,part.Length-offset);
        if(count==0)return 0;part.AsMemory(offset,count).CopyTo(buffer);position+=count;return count;
    }
    public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
    public override long Length=>throw new NotSupportedException();public override long Position{get=>position;set=>throw new NotSupportedException();}
    public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override void Flush(){}
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}
