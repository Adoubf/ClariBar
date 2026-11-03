using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClariBar.Core;

public static class AiTextStream
{
    public static AiOcrRequest Configure(AiOcrRequest request,bool enabled)
    {
        var body=JsonNode.Parse(request.Payload)!.AsObject();var target=request.Target;
        if(target.ApiType==AiOcrProtocol.Gemini)
        {
            var address=new UriBuilder(target.Endpoint);
            if(enabled)address.Path=address.Path.Replace(":generateContent",":streamGenerateContent",StringComparison.OrdinalIgnoreCase);
            else address.Path=address.Path.Replace(":streamGenerateContent",":generateContent",StringComparison.OrdinalIgnoreCase);
            var query=address.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Where(part=>!part.Split('=',2)[0].Equals("alt",StringComparison.OrdinalIgnoreCase)).ToList();
            if(enabled)query.Add("alt=sse");address.Query=string.Join('&',query);target=target with{Endpoint=address.Uri};
        }
        else body["stream"]=enabled;
        return new(target,body.ToJsonString());
    }
    public static async Task<string> ReadAsync(Stream stream,string apiType,Action<string> received,CancellationToken cancellation=default)
    {
        using var limited=new LimitedStream(stream,8*1024*1024);
        using var reader=new StreamReader(limited,new UTF8Encoding(false,true),detectEncodingFromByteOrderMarks:true,bufferSize:4096,leaveOpen:true);
        var text=new StringBuilder();var data=new StringBuilder();bool complete=false;
        void Append(string value)
        {
            if(value.Length==0)return;
            if(text.Length+value.Length>1024*1024)throw Invalid();
            text.Append(value);received(text.ToString());
        }
        void Stop(string reason,params string[] accepted)
        {
            if(reason.Length==0)return;
            if(reason is "length" or "max_tokens" or "MAX_TOKENS")throw new IOException(Localizer.T("The AI output was cut off. Try a smaller selection."));
            if(!accepted.Contains(reason))throw new IOException(Localizer.T("The AI service interrupted its response. Try again."));
            complete=true;
        }
        void Event()
        {
            string payload=data.ToString();data.Clear();if(payload.Length==0)return;
            if(payload.Trim()=="[DONE]"){complete=true;return;}
            using var json=JsonDocument.Parse(payload);var root=json.RootElement;
            string type=Value(root,"type");
            if(type=="error" || root.TryGetProperty("error",out var error) && error.ValueKind is not (JsonValueKind.Null or JsonValueKind.False))
                throw new IOException(Localizer.T("The AI service interrupted its response. Try again."));
            if(apiType==AiOcrProtocol.Chat)
            {
                if(!root.TryGetProperty("choices",out var choices) || choices.ValueKind!=JsonValueKind.Array)return;
                foreach(var choice in choices.EnumerateArray())
                {
                    if(choice.TryGetProperty("index",out var index) && index.GetInt32()!=0)continue;
                    if(choice.TryGetProperty("delta",out var delta) && delta.TryGetProperty("content",out var content))Append(Content(content));
                    Stop(Value(choice,"finish_reason"),"stop","eos");
                }
            }
            else if(apiType==AiOcrProtocol.Responses)
            {
                if(type=="response.output_text.delta")Append(Value(root,"delta"));
                else if(type=="response.output_text.done" && text.Length==0)Append(Value(root,"text"));
                else if(type=="response.completed")complete=true;
                else if(type is "response.failed" or "response.incomplete")throw new IOException(Localizer.T("The AI service interrupted its response. Try again."));
            }
            else if(apiType==AiOcrProtocol.Anthropic)
            {
                if(type=="content_block_start" && root.TryGetProperty("content_block",out var block) && Value(block,"type")=="text")Append(Value(block,"text"));
                if(type=="content_block_delta" && root.TryGetProperty("delta",out var delta) && Value(delta,"type")=="text_delta")Append(Value(delta,"text"));
                if(type=="message_delta" && root.TryGetProperty("delta",out var message))
                {string reason=Value(message,"stop_reason");if(reason.Length>0 && reason is not ("end_turn" or "stop_sequence"))Stop(reason,"end_turn","stop_sequence");}
                if(type=="message_stop")complete=true;
            }
            else if(apiType==AiOcrProtocol.Gemini)
            {
                if(root.TryGetProperty("promptFeedback",out var feedback) && Value(feedback,"blockReason").Length>0)throw Invalid();
                if(!root.TryGetProperty("candidates",out var candidates) || candidates.ValueKind!=JsonValueKind.Array)return;
                foreach(var candidate in candidates.EnumerateArray().Take(1))
                {
                    if(candidate.TryGetProperty("content",out var content) && content.TryGetProperty("parts",out var parts))Append(Content(parts));
                    Stop(Value(candidate,"finishReason"),"STOP");
                }
            }
        }
        try
        {
            while(await reader.ReadLineAsync(cancellation) is {} line)
            {
                if(line.Length>256*1024)throw Invalid();
                if(line.Length==0){Event();if(complete)break;continue;}
                if(line.StartsWith("data:",StringComparison.Ordinal))
                {if(data.Length>0)data.Append('\n');data.Append(line.AsSpan(5).TrimStart(' '));if(data.Length>256*1024)throw Invalid();}
            }
            Event();
            if(!complete)throw new IOException(Localizer.T("The AI stream ended before completion. Try again."));
            if(string.IsNullOrWhiteSpace(text.ToString()))throw new IOException(Localizer.T("The AI service returned no text."));
            return text.ToString();
        }
        catch(Exception ex)when(ex is JsonException or DecoderFallbackException or InvalidOperationException or FormatException)
        {throw Invalid();}
    }
    private static IOException Invalid()=>new(Localizer.T("The AI service returned an invalid stream."));
    private static string Value(JsonElement root,string key)=>root.ValueKind==JsonValueKind.Object && root.TryGetProperty(key,out var value) && value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
    private static string Content(JsonElement content)
    {
        if(content.ValueKind==JsonValueKind.String)return content.GetString()??"";
        if(content.ValueKind!=JsonValueKind.Array)return "";
        return string.Concat(content.EnumerateArray().Where(part=>!(part.TryGetProperty("thought",out var thought) && thought.ValueKind==JsonValueKind.True))
            .Where(part=>Value(part,"type") is "" or "text" or "output_text").Select(part=>Value(part,"text")));
    }
    private sealed class LimitedStream(Stream inner,long maximum):Stream
    {
        private long bytes;
        private int Count(int count){bytes+=count;if(bytes>maximum)throw Invalid();return count;}
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>bytes;set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>Count(inner.Read(buffer,offset,count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellation=default)=>Count(await inner.ReadAsync(buffer,cancellation));
        public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken cancellation)=>Count(await inner.ReadAsync(buffer.AsMemory(offset,count),cancellation));
        public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
