using System.Text;
using ClariBar.Core;

internal static class TranslationStreamChecks
{
    internal static async Task RunAsync(Action<bool,string> require)
    {
        string[] bodies=[
            ": keepalive\r\ndata: {\"choices\":[{\"delta\":{\"reasoning_content\":\"private reasoning\"}}]}\r\n\r\ndata: {\"choices\":[{\"delta\":{\"content\":\"你好\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"，世界🌏\"}}]}\n\ndata: {\"choices\":[],\"usage\":{}}\n\ndata: [DONE]\n\n",
            "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"你好\"}\n\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"，世界🌏\"}\n\ndata: {\"type\":\"response.output_text.done\",\"text\":\"你好，世界🌏\"}\n\ndata: {\"type\":\"response.completed\"}\n\n",
            "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"thinking_delta\",\"thinking\":\"private reasoning\"}}\n\ndata: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"你好\"}}\n\ndata: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"，世界🌏\"}}\n\ndata: {\"type\":\"message_stop\"}\n\n",
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"private reasoning\"},{\"text\":\"你好\"}]}}]}\n\ndata: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"，世界🌏\"}]},\"finishReason\":\"STOP\"}]}\n\n"
        ];
        var types=AiOcrProtocol.ApiTypes.Skip(1).ToArray();
        for(int index=0;index<types.Length;index++)
        {
            var updates=new List<string>();using var stream=new FragmentedStream(Encoding.UTF8.GetBytes(bodies[index]));
            string text=await AiTextStream.ReadAsync(stream,types[index],updates.Add);
            require(text=="你好，世界🌏" && updates.SequenceEqual(new[]{"你好","你好，世界🌏"}),types[index]+": fragmented UTF-8 SSE displays cumulative text and ignores reasoning, metadata and duplicate final text");
        }
        using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("data: {\"choices\": [\n"+"data: {\"delta\":{\"content\":\"Hello\"}}]}\n\ndata: [DONE]")))
            require(await AiTextStream.ReadAsync(stream,AiOcrProtocol.Chat,_=>{})=="Hello","SSE joins multiline data and accepts a final event without a trailing blank line");
        var prefix=Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"First\"}}]}\n\n");
        var suffix=Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\" result\"},\"finish_reason\":\"stop\"}]}\n\n");
        using(var stream=new PausedStream(prefix,suffix))
        {
            var first=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var response=AiTextStream.ReadAsync(stream,AiOcrProtocol.Chat,text=>first.TrySetResult(text));
            require(await first.Task.WaitAsync(TimeSpan.FromSeconds(3))=="First" && !response.IsCompleted,"The first text is observable while the HTTP body is still unfinished");
            stream.Release.TrySetResult();require(await response=="First result","A resumed stream completes with exactly the full generated text");
        }
        using(var stream=new PausedStream(prefix,suffix))
        using(var cancellation=new CancellationTokenSource())
        {
            var response=AiTextStream.ReadAsync(stream,AiOcrProtocol.Chat,_=>{},cancellation.Token);cancellation.Cancel();bool cancelled=false;
            try{await response;}catch(OperationCanceledException){cancelled=true;}require(cancelled,"Cancellation stops waiting on a slow stream");
        }
        foreach(string ending in new[]{"", "data: {broken}\n\n", "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"length\"}]}\n\n", "data: {\"error\":{\"message\":\"private server detail\"}}\n\n"})
        {
            using var stream=new MemoryStream(prefix.Concat(Encoding.UTF8.GetBytes(ending)).ToArray());var updates=new List<string>();bool failed=false;
            try{await AiTextStream.ReadAsync(stream,AiOcrProtocol.Chat,updates.Add);}catch(IOException){failed=true;}
            require(failed && updates.SequenceEqual(new[]{"First"}),"Malformed, truncated, limited or failed streams preserve received text and do not report success");
        }
        foreach(string type in types)
        {
            var settings=new AppSettings{AiOcrEndpoint="https://example.test/v1?region=test",AiOcrApiType=type,AiTranslationModel="hy-mt2-7b"};
            var original=TranslationProtocol.BuildAiRequest(settings,["Hello"],"en","zh",plainText:true);
            var streaming=AiTextStream.Configure(original,true);var restored=AiTextStream.Configure(streaming,false);
            require(type==AiOcrProtocol.Gemini?streaming.Target.Endpoint.AbsolutePath.EndsWith(":streamGenerateContent") && streaming.Target.Endpoint.Query.Contains("alt=sse") && !restored.Target.Endpoint.Query.Contains("alt="):
                streaming.Payload.Contains("\"stream\":true") && restored.Payload.Contains("\"stream\":false"),type+": streaming and normal requests retain the selected endpoint and model");
        }
        require(TranslationProtocol.TextParagraphs("First\r\n\r\nSecond\n \nThird").SequenceEqual(new[]{"First","Second","Third"}),"Clipboard paragraph boundaries keep their reading order across Windows and Unix newlines");
    }
    private sealed class FragmentedStream(byte[] data):MemoryStream(data)
    {public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellation=default)=>base.ReadAsync(buffer[..Math.Min(1,buffer.Length)],cancellation);}
    private sealed class PausedStream(byte[] prefix,byte[] suffix):Stream
    {
        private int position;internal TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellation=default)
        {
            if(position>=prefix.Length)await Release.Task.WaitAsync(cancellation);
            byte[] part=position<prefix.Length?prefix:suffix;int offset=position<prefix.Length?position:position-prefix.Length;
            int count=Math.Min(buffer.Length,part.Length-offset);if(count==0)return 0;part.AsMemory(offset,count).CopyTo(buffer);position+=count;return count;
        }
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>position;set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override void Flush(){}
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long length)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
