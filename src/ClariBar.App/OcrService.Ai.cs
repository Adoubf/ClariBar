using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClariBar.Core;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed record AiOcrConnectionTest(AiOcrTarget Target,string Model,long ElapsedMilliseconds,string Text,bool SampleRecognized);

internal static partial class OcrService
{
    internal static Uri ValidateEndpoint(string endpoint)=>AiOcrProtocol.ValidateUrl(endpoint);
    public static Task<string> AiAsync(PixelFrame frame,string endpoint,string model,string key,CancellationToken cancellation=default)=>AiAsync(frame,new AiOcrOptions(endpoint,model),key,cancellation);
    public static Task<string> AiAsync(PixelFrame frame,AiOcrOptions options,string key,CancellationToken cancellation=default)=>SendAiAsync(frame,options,key,4096,cancellation);
    internal static async Task<AiOcrConnectionTest> TestConnectionAsync(AiOcrOptions options,string key,CancellationToken cancellation=default,HttpMessageHandler? handler=null)
    {
        byte[] pixels=new byte[560*170*4];Array.Fill(pixels,(byte)255);
        var sample=new PixelFrame(560,170,pixels).Render([
            new("Text",[new(24,18),new(25,19)],"#101010",3,"CLARIBAR OCR 2026"){FontSize=32},
            new("Text",[new(24,70),new(25,71)],"#303030",3,"Connection test / 文字识别测试"){FontSize=22}
        ]);
        var watch=Stopwatch.StartNew();string text=await SendAiAsync(sample,options,key,2048,cancellation,handler);
        string normalized=Regex.Replace(text,"[^A-Za-z0-9]","").ToUpperInvariant();
        return new(AiOcrProtocol.Resolve(options),options.Model,watch.ElapsedMilliseconds,text,normalized.Contains("CLARIBAR") && normalized.Contains("2026"));
    }
    private static async Task<HttpClientHandler> HandlerAsync(Uri uri,CancellationToken cancellation)
    {
        bool direct=AiOcrProtocol.IsPrivateHost(uri);
        if(!direct && !IPAddress.TryParse(uri.DnsSafeHost,out _))
        {
            try{direct=(await Dns.GetHostAddressesAsync(uri.DnsSafeHost,cancellation)).Any(AiOcrProtocol.IsPrivateAddress);}
            catch(System.Net.Sockets.SocketException){/* The HTTP stack reports the final DNS/proxy error. */}
        }
        return new(){AllowAutoRedirect=false,UseProxy=!direct};
    }
    internal static async Task<string> SendAiAsync(PixelFrame frame,AiOcrOptions options,string key,int maxTokens,CancellationToken cancellation=default,HttpMessageHandler? handler=null)
    {
        var specification=AiOcrProtocol.BuildRequest(options,await frame.PngAsync(),maxTokens);
        return await SendAiRequestAsync(specification,options.TimeoutSeconds,key,cancellation,handler);
    }
    // OCR and text translation share endpoint resolution, authentication, private
    // network routing, TLS validation, cancellation and bounded response handling.
    internal static async Task<string> SendAiRequestAsync(AiOcrRequest specification,int timeoutSeconds,string key,CancellationToken cancellation=default,HttpMessageHandler? handler=null)
    {
        if(key.Contains('\r') || key.Contains('\n'))throw new InvalidOperationException(L.T("The API key contains an invalid line break."));
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds,5,1800)));
        try
        {
            var transport=handler??await HandlerAsync(specification.Target.Endpoint,timeout.Token);
            using var client=new HttpClient(transport,disposeHandler:handler is null){Timeout=Timeout.InfiniteTimeSpan};
            using var request=new HttpRequestMessage(HttpMethod.Post,specification.Target.Endpoint);
            request.Headers.Accept.Add(new("application/json"));
            if(specification.Target.ApiType==AiOcrProtocol.Anthropic)
            {
                request.Headers.Add("anthropic-version","2023-06-01");if(!string.IsNullOrWhiteSpace(key))request.Headers.Add("x-api-key",key.Trim());
            }
            else if(specification.Target.ApiType==AiOcrProtocol.Gemini){if(!string.IsNullOrWhiteSpace(key))request.Headers.Add("x-goog-api-key",key.Trim());}
            else if(!string.IsNullOrWhiteSpace(key))request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key.Trim());
            // Each caller supplies only the selected image or recognized text.
            request.Content=new StringContent(specification.Payload,Encoding.UTF8,"application/json");
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var buffer=new MemoryStream();byte[] block=new byte[8192];
            int count;while((count=await stream.ReadAsync(block,timeout.Token))>0)
            {if(buffer.Length+count>2*1024*1024)throw new IOException(L.T("The AI OCR response is too large."));buffer.Write(block,0,count);}
            string body=Encoding.UTF8.GetString(buffer.ToArray());
            if(!response.IsSuccessStatusCode)
            {
                string detail="";try{using var json=JsonDocument.Parse(body);detail=AiOcrProtocol.ErrorMessage(json.RootElement);}catch(JsonException){}
                if(key.Length>0)detail=detail.Replace(key,"[redacted]",StringComparison.Ordinal);
                if(detail.Length>500)detail=detail[..500]+"…";
                string reason=(int)response.StatusCode switch
                {
                    401 or 403=>L.T("Authentication failed. Check the API key and access permissions."),
                    404=>L.T("API or model not found. Check the resolved address, endpoint type and model ID."),
                    400 or 422=>L.T("The server rejected the request. Check the model's image support and endpoint type."),
                    429=>L.T("The service is busy or its quota is exhausted. Try again later."),
                    _=>L.T("Check that the model service is running and its API address is correct.")
                };
                throw new IOException(L.F("AI OCR failed (HTTP {0}). {1}",(int)response.StatusCode,reason)+(detail.Length>0?"\n"+detail:""));
            }
            try{return AiOcrProtocol.ExtractText(body,specification.Target.ApiType);}
            catch(JsonException ex){throw new IOException(L.T("The server returned a non-JSON response. Check the API suffix and any gateway login page."),ex);}
        }
        catch(OperationCanceledException ex)when(!cancellation.IsCancellationRequested)
        {throw new IOException(L.F("The AI request timed out after {0} seconds. A local model may need a longer startup timeout.",timeoutSeconds),ex);}
        catch(HttpRequestException ex)
        {
            bool certificate=false;for(Exception? cause=ex;cause is not null;cause=cause.InnerException)certificate|=cause is AuthenticationException;
            string message=certificate?L.T("HTTPS certificate validation failed. Configure a trusted certificate for the local service or use its HTTP address."):
                ex.HttpRequestError==HttpRequestError.SecureConnectionError?L.T("The HTTPS handshake failed. Check the service's HTTPS port, certificate and TLS configuration."):
                L.T("Cannot connect to the AI service. Check the server address, LAN access, port and whether the service is running.");
            throw new IOException(message,ex);
        }
    }
}
