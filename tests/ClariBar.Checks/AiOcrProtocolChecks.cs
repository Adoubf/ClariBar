using System.Net;
using System.Text.Json;
using ClariBar.Core;

internal static class AiOcrProtocolChecks
{
    internal static void Run(Action<bool,string> require)
    {
        require(new AppSettings().OcrDefaultEngine=="Local","Existing installations retain local OCR as the initial default");
        var preference=new AppSettings{OcrDefaultEngine="AI"};preference.Normalize();
        require(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(preference,Wire.Json),Wire.Json)!.OcrDefaultEngine=="Ai","The AI recognition default is normalized and survives settings round trips");
        preference.OcrDefaultEngine="unknown";preference.Normalize();require(preference.OcrDefaultEngine=="Local","An invalid OCR default falls back to local recognition");
        foreach(string endpoint in new[]{"http://192.168.1.20:8000","http://10.1.2.3/v1","http://172.16.3.2:3000/v1/chat/completions","https://ocr.internal/v1/responses","http://[fd00::1234]:8000/v1","http://ocr-server:11434/v1"})
            require(AiOcrProtocol.ValidateUrl(endpoint).Scheme is "http" or "https","HTTP/HTTPS LAN URL is accepted: "+endpoint);
        foreach(string invalid in new[]{"file:///test","ftp://example.com/a","https://user:password@example.com/v1","https://example.com/v1#fragment","not a url"})
        {bool failed=false;try{AiOcrProtocol.ValidateUrl(invalid);}catch(InvalidOperationException){failed=true;}require(failed,"Invalid API URL rejected: "+invalid);}
        foreach(var (type,path) in new[]{(AiOcrProtocol.Chat,"/v1/chat/completions"),(AiOcrProtocol.Responses,"/v1/responses"),(AiOcrProtocol.Anthropic,"/v1/messages"),(AiOcrProtocol.Gemini,"/v1beta/models/test-model:generateContent")})
            require(AiOcrProtocol.Resolve(new("http://10.1.2.3:8000","test-model",type)).Endpoint.AbsolutePath==path,"Base URL completes the correct suffix for "+type);
        foreach(var (url,type,path) in new[]{
            ("http://host/v1",AiOcrProtocol.Chat,"/v1/chat/completions"),
            ("http://host/proxy/v1/",AiOcrProtocol.Responses,"/proxy/v1/responses"),
            ("http://host/v1beta",AiOcrProtocol.Auto,"/v1beta/models/test-model:generateContent"),
            ("http://host/v1/messages",AiOcrProtocol.Auto,"/v1/messages"),
            ("http://host/proxy/v1/chat/completions",AiOcrProtocol.Responses,"/proxy/v1/responses"),
            ("https://host/custom/ocr?api-version=2026",AiOcrProtocol.Chat,"/custom/ocr")})
            require(AiOcrProtocol.Resolve(new(url,"test-model",type)).Endpoint.AbsolutePath==path,"API suffix is normalized without duplicate versions: "+url);
        var query=AiOcrProtocol.Resolve(new("https://host/v1?api-version=2026","test-model",AiOcrProtocol.Responses));
        require(query.Endpoint.Query=="?api-version=2026","Reverse-proxy query parameters survive suffix completion");
        var gemini=AiOcrProtocol.Resolve(new("https://host/v1beta/models/old-model:generateContent","models/new-model",AiOcrProtocol.Auto));
        require(gemini.ApiType==AiOcrProtocol.Gemini && gemini.Endpoint.AbsolutePath=="/v1beta/models/new-model:generateContent","Gemini substitutes the selected model into a complete endpoint");
        foreach(string ip in new[]{"127.0.0.1","10.0.0.1","172.16.0.1","172.31.255.254","192.168.10.2","169.254.1.2","::1","fd00::1","fe80::1","::ffff:192.168.1.2"})
            require(AiOcrProtocol.IsPrivateAddress(IPAddress.Parse(ip)),"LAN transport bypasses the proxy for "+ip);
        foreach(string ip in new[]{"8.8.8.8","172.15.1.1","172.32.1.1","2001:4860:4860::8888"})
            require(!AiOcrProtocol.IsPrivateAddress(IPAddress.Parse(ip)),"Public address is not classified as a private network: "+ip);
        require(AiOcrProtocol.Prompt(new("http://host","PaddlePaddle/PaddleOCR-VL"))=="OCR:" && AiOcrProtocol.Prompt(new("http://host","deepseek-ai/DeepSeek-OCR-2"))=="Free OCR.","Specialized OCR models use their documented task prompts");
        require(AiOcrProtocol.ModelType(new("http://host","Qwen/Qwen2.5-VL"))=="Vl" && AiOcrProtocol.ModelType(new("http://host","custom-image-model"))=="Multimodal","VL and general multimodal models accept arbitrary deployment IDs");
        require(AiOcrProtocol.Prompt(new("http://host","PaddleOCR-VL",Prompt:"Recognize this table."))=="Recognize this table.","A custom OCR prompt overrides model presets");
        byte[] png=[137,80,78,71,13,10,26,10];
        foreach(string api in AiOcrProtocol.ApiTypes.Skip(1))
        {
            var request=AiOcrProtocol.BuildRequest(new("http://host","test-model",api),png);using var body=JsonDocument.Parse(request.Payload);
            require(request.Target.ApiType==api && request.Payload.Contains(Convert.ToBase64String(png)),"Selected PNG bytes are embedded in the request for "+api);
            require(api==AiOcrProtocol.Gemini?body.RootElement.TryGetProperty("contents",out _):body.RootElement.GetProperty("model").GetString()=="test-model","The selected model is represented in the correct protocol field: "+api);
        }
        foreach(var (api,json) in new[]{
            (AiOcrProtocol.Chat,"{\"choices\":[{\"message\":{\"content\":\"Sample OCR\"}}]}"),
            (AiOcrProtocol.Chat,"{\"choices\":[{\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Sample OCR\"}]}}]}"),
            (AiOcrProtocol.Responses,"{\"output\":[{\"type\":\"reasoning\",\"content\":[{\"text\":\"private reasoning\"}]},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"Sample OCR\"}]}]}"),
            (AiOcrProtocol.Anthropic,"{\"content\":[{\"type\":\"thinking\",\"text\":\"private reasoning\"},{\"type\":\"text\",\"text\":\"Sample OCR\"}]}"),
            (AiOcrProtocol.Gemini,"{\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"private reasoning\"},{\"text\":\"Sample OCR\"}]}}]}")})
            require(AiOcrProtocol.ExtractText(json,api)=="Sample OCR","Only recognition output is extracted for "+api);
        bool emptyFailed=false;try{AiOcrProtocol.ExtractText("{}",AiOcrProtocol.Chat);}catch(IOException){emptyFailed=true;}require(emptyFailed,"An empty or mismatched response cannot be reported as a successful OCR result");
        var legacy=JsonSerializer.Deserialize<AppSettings>("{\"aiOcrEndpoint\":\"https://host/custom/ocr\",\"aiOcrModel\":\"existing-model\",\"monitorColor\":\"#123456\"}",Wire.Json)!;legacy.Normalize();
        require(legacy.AiOcrApiType=="Auto" && legacy.AiOcrModel=="existing-model" && legacy.MonitorColor=="#123456" && AiOcrProtocol.Resolve(AiOcrOptions.From(legacy)).Endpoint.AbsolutePath=="/custom/ocr","Existing AI endpoints, model IDs and appearance settings remain compatible");
        legacy.AiOcrApiType="anthropic";legacy.AiOcrModelType="ocr";legacy.AiOcrPrompt="OCR custom prompt";legacy.AiOcrTimeoutSeconds=300;legacy.Normalize();
        var roundTrip=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(legacy,Wire.Json),Wire.Json)!;
        require(roundTrip.AiOcrApiType==AiOcrProtocol.Anthropic && roundTrip.AiOcrModelType=="Ocr" && roundTrip.AiOcrPrompt=="OCR custom prompt" && roundTrip.AiOcrTimeoutSeconds==300,"Endpoint format, model family, prompt and timeout survive settings round trips");
    }
}
