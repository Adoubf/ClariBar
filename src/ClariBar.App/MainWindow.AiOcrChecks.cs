using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private static string AiTestResponse(string api,string text="CLARIBAR OCR 2026")=>api switch
    {
        AiOcrProtocol.Responses=>JsonSerializer.Serialize(new{output=new[]{new{type="message",content=new[]{new{type="output_text",text}}}}}),
        AiOcrProtocol.Anthropic=>JsonSerializer.Serialize(new{content=new[]{new{type="text",text}}}),
        AiOcrProtocol.Gemini=>JsonSerializer.Serialize(new{candidates=new[]{new{content=new{parts=new[]{new{text}}}}}}),
        _=>JsonSerializer.Serialize(new{choices=new[]{new{message=new{content=text}}}})
    };
    private async Task CheckAiOcrProtocolsAsync(Action<bool,string> require)
    {
        byte[] pixels=new byte[64*48*4];Array.Fill(pixels,(byte)255);var frame=new PixelFrame(64,48,pixels);byte[] selected=await frame.PngAsync();
        foreach(bool tls in new[]{false,true})foreach(string api in AiOcrProtocol.ApiTypes.Skip(1))
        {
            await using var server=new AiOcrCheckServer(AiTestResponse(api),tls:tls);
            using var handler=tls?server.TrustedTestHandler():null;
            var options=new AiOcrOptions(server.Address,"test-model",api);
            string text=await OcrService.SendAiAsync(frame,options,"synthetic-ocr-key",512,handler:handler);
            var request=await server.Request.WaitAsync(TimeSpan.FromSeconds(5));using var json=JsonDocument.Parse(request.Body);var root=json.RootElement;
            string base64=api switch
            {
                AiOcrProtocol.Responses=>root.GetProperty("input")[0].GetProperty("content")[1].GetProperty("image_url").GetString()!.Split(',')[1],
                AiOcrProtocol.Anthropic=>root.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("source").GetProperty("data").GetString()!,
                AiOcrProtocol.Gemini=>root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("inlineData").GetProperty("data").GetString()!,
                _=>root.GetProperty("messages")[0].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString()!.Split(',')[1]
            };
            string name=(tls?"HTTPS":"HTTP")+" / "+api;
            require(text=="CLARIBAR OCR 2026" && Convert.FromBase64String(base64).SequenceEqual(selected),name+": the real transport sends only the selected PNG and extracts recognition text");
            string authHeader=api==AiOcrProtocol.Anthropic?"x-api-key":api==AiOcrProtocol.Gemini?"x-goog-api-key":"Authorization";
            require(request.Headers.TryGetValue(authHeader,out string? auth) && auth==(api is AiOcrProtocol.Anthropic or AiOcrProtocol.Gemini?"synthetic-ocr-key":"Bearer synthetic-ocr-key"),name+": the request uses the provider's authentication header");
            require(request.Path==AiOcrProtocol.Resolve(options).Endpoint.PathAndQuery,name+": the server receives the resolved API suffix");
        }
        await using(var server=new AiOcrCheckServer(AiTestResponse(AiOcrProtocol.Chat),tls:true))
        {
            bool failed=false;try{await OcrService.AiAsync(frame,new AiOcrOptions(server.Address,"test-model"),"");}catch(IOException ex){failed=ex.Message==L.T("HTTPS certificate validation failed. Configure a trusted certificate for the local service or use its HTTP address.");}
            require(failed,"An untrusted HTTPS server is rejected with a readable certificate error; production TLS validation stays enabled");
        }
        await using(var server=new AiOcrCheckServer(AiTestResponse(AiOcrProtocol.Chat)))
        {
            string text=await OcrService.AiAsync(frame,new AiOcrOptions(server.Address,"PaddlePaddle/PaddleOCR-VL"),"");var request=await server.Request;
            using var json=JsonDocument.Parse(request.Body);var content=json.RootElement.GetProperty("messages")[0].GetProperty("content");
            require(text.Length>0 && !request.Headers.ContainsKey("Authorization") && content[0].GetProperty("type").GetString()=="image_url" && content[1].GetProperty("text").GetString()=="OCR:","A local OCR model accepts no key and receives its specialized image-first OCR prompt");
        }
        await using(var server=new AiOcrCheckServer("{\"choices\":[]}"))
        {
            bool failed=false;try{await OcrService.AiAsync(frame,new AiOcrOptions(server.Address,"test-model"),"");}catch(IOException){failed=true;}
            require(failed,"A successful HTTP response without recognition text is reported as a failed test");
        }
    }
    private async Task CheckAiOcrSettingsAsync(Action<bool,string> require)
    {
        NavigateTo("tools");await Task.Delay(120);var panel=PageContent.Children.OfType<AiOcrSettingsPanel>().Single();panel.Expander.IsExpanded=true;await Task.Delay(120);
        require(panel.ApiType.Items.Count==5 && panel.ModelType.Items.Count==4,"AI OCR settings expose automatic detection, four endpoint formats and OCR/VL/multimodal model types");
        static void Invoke(Button button)=>((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
        panel.Model.Text="test-model";panel.ApiType.SelectedIndex=Array.IndexOf(AiOcrProtocol.ApiTypes,AiOcrProtocol.Responses);panel.ModelType.SelectedIndex=2;
        string original=settings.AiOcrEndpoint;
        await using(var server=new AiOcrCheckServer(AiTestResponse(AiOcrProtocol.Responses)))
        {
            panel.Endpoint.Text=server.Address;panel.Key.Password="synthetic-ocr-key";Invoke(panel.TestButton);await panel.LastTest;
            require(panel.Status.Severity==InfoBarSeverity.Success && panel.TestOutput.Text.Contains("2026"),"The Test connection button completes a real image request and displays success, timing and recognized text");
            require(settings.AiOcrEndpoint==original && !OcrCredential.Exists,"Testing unsaved fields does not save settings or an API key");
            require(panel.ResolvedAddress.Text.Contains("/v1/responses"),"The settings page previews the exact API suffix that will be requested");
            Invoke(panel.SaveButton);
            require(settings.AiOcrApiType==AiOcrProtocol.Responses && settings.AiOcrModelType=="Multimodal" && OcrCredential.Read()=="synthetic-ocr-key" && panel.Key.Password.Length==0,"Saving preserves the selected API/model types and encrypts the key separately");
        }
        await using(var server=new AiOcrCheckServer("{\"error\":{\"message\":\"Invalid synthetic-ocr-key\"}}",status:401))
        {
            panel.Endpoint.Text=server.Address;Invoke(panel.TestButton);await panel.LastTest;
            require(panel.Status.Severity==InfoBarSeverity.Error && panel.Status.Message.Contains("401") && !panel.Status.Message.Contains("synthetic-ocr-key"),"Authentication failures show the HTTP status and redact any echoed key");
        }
        await using(var server=new AiOcrCheckServer(AiTestResponse(AiOcrProtocol.Responses,"Hello world")))
        {
            panel.Endpoint.Text=server.Address;Invoke(panel.TestButton);await panel.LastTest;
            require(panel.Status.Severity==InfoBarSeverity.Warning && panel.TestOutput.Text=="Hello world","A connected service with an unrecognized sample is distinguished from verified image recognition");
            panel.Model.Text="changed-model";require(!panel.Status.IsOpen,"Changing model settings clears stale connection-test status");
        }
        await using(var server=new AiOcrCheckServer(AiTestResponse(AiOcrProtocol.Responses),delay:TimeSpan.FromSeconds(10)))
        {
            panel.Endpoint.Text=server.Address;Invoke(panel.TestButton);await server.Request.WaitAsync(TimeSpan.FromSeconds(5));
            require(!panel.TestButton.IsEnabled && panel.CancelButton.Visibility==Visibility.Visible,"A running connection test exposes Cancel and prevents duplicate submissions");
            Invoke(panel.CancelButton);await panel.LastTest;
            require(panel.TestButton.IsEnabled && panel.Status.Title==L.T("Connection test cancelled"),"Cancelling the request restores controls and reports a cancelled state");
        }
        Invoke(panel.RemoveKeyButton);require(!OcrCredential.Exists,"Removing the saved API key updates encrypted credential storage");
        await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,L.LanguageCode+"-ai-ocr-settings.png"),await (await RenderOwnElementAsync(panel)).PngAsync());
    }
}

internal sealed record AiOcrCheckRequest(string Path,Dictionary<string,string> Headers,string Body);
internal sealed class AiOcrCheckServer:IAsyncDisposable
{
    private readonly TcpListener listener=new(IPAddress.Loopback,0);
    private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(20));
    private readonly X509Certificate2? certificate;
    private readonly Task serve;
    private readonly TaskCompletionSource<AiOcrCheckRequest> received=new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal string Address {get;}
    internal Task<AiOcrCheckRequest> Request=>received.Task;
    internal AiOcrCheckServer(string response,int status=200,bool tls=false,TimeSpan delay=default)
    {
        if(tls)
        {
            using var rsa=RSA.Create(2048);var request=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            var names=new SubjectAlternativeNameBuilder();names.AddDnsName("localhost");names.AddIpAddress(IPAddress.Loopback);request.CertificateExtensions.Add(names.Build());
            using var generated=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
            // Schannel needs a normal key handle for server credentials. Import the
            // test key without EphemeralKeySet; disposal deletes its temporary key.
            // No certificate is added to a Windows trust store.
            byte[] encoded=generated.Export(X509ContentType.Pkcs12);
            try{certificate=X509CertificateLoader.LoadPkcs12(encoded,null,X509KeyStorageFlags.Exportable);}
            finally{CryptographicOperations.ZeroMemory(encoded);}
        }
        listener.Start();Address=$"{(tls?"https":"http")}://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        serve=ServeAsync(response,status,delay);
    }
    internal HttpClientHandler TrustedTestHandler()=>new(){UseProxy=false,AllowAutoRedirect=false,
        ServerCertificateCustomValidationCallback=(_,presented,_,_)=>presented?.Thumbprint==certificate!.Thumbprint};
    private async Task ServeAsync(string response,int status,TimeSpan delay)
    {
        try{await ServeCoreAsync(response,status,delay);}
        catch(Exception ex)
        {
            if(!stop.IsCancellationRequested)File.AppendAllText(Path.Combine(AppPaths.Logs,"ai-ocr-test-server.log"),ex+Environment.NewLine);
            throw;
        }
    }
    private async Task ServeCoreAsync(string response,int status,TimeSpan delay)
    {
        using var client=await listener.AcceptTcpClientAsync(stop.Token);await using var network=client.GetStream();
        using var secure=certificate is null?null:new SslStream(network,leaveInnerStreamOpen:true);
        if(secure is not null)await secure.AuthenticateAsServerAsync(new SslServerAuthenticationOptions{ServerCertificate=certificate,ClientCertificateRequired=false},stop.Token);
        Stream stream=secure is null?network:secure;
        var header=new List<byte>();byte[] next=new byte[1];
        while(true)
        {
            await stream.ReadExactlyAsync(next,stop.Token);header.Add(next[0]);
            int count=header.Count;if(count>65536)throw new IOException("Test request header too large");
            if(count>=4 && header[count-4]==13 && header[count-3]==10 && header[count-2]==13 && header[count-1]==10)break;
        }
        var lines=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n",StringSplitOptions.RemoveEmptyEntries);
        var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var line in lines.Skip(1)){int colon=line.IndexOf(':');headers[line[..colon]]=line[(colon+1)..].Trim();}
        byte[] body=new byte[int.Parse(headers["Content-Length"])];await stream.ReadExactlyAsync(body,stop.Token);
        received.TrySetResult(new(lines[0].Split(' ')[1],headers,Encoding.UTF8.GetString(body)));
        if(delay>TimeSpan.Zero)await Task.Delay(delay,stop.Token);
        byte[] payload=Encoding.UTF8.GetBytes(response);byte[] reply=Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(reply,stop.Token);await stream.WriteAsync(payload,stop.Token);await stream.FlushAsync(stop.Token);
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();listener.Stop();
        try{await serve;}catch(Exception ex)when(ex is OperationCanceledException or IOException or SocketException or AuthenticationException){}
        certificate?.Dispose();stop.Dispose();
    }
}
