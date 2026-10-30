using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ClariBar.Core;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal static class TranslationService
{
    internal static string OfflineDirectory=>Path.Combine(AppPaths.Data,"translation","offline");
    internal static bool OfflineReady=>File.Exists(Path.Combine(OfflineDirectory,"ready.json")) && File.Exists(Path.Combine(OfflineDirectory,"python","python.exe"))
        && new[]{"en_zh","zh_en"}.All(pair=>File.Exists(Path.Combine(OfflineDirectory,"models",pair,"model","model.bin")));
    internal static async Task<TranslationDocument> TranslateAsync(PixelFrame frame,AppSettings settings,Action<string>? progress=null,CancellationToken cancellation=default,Action<TranslationLine[]>? prepared=null)
    {
        progress?.Invoke(L.T("Recognizing text for translation…"));
        string source=settings.TranslationSourceLanguage,target=settings.TranslationTargetLanguage,engine=TranslationProtocol.NormalizeEngine(settings.TranslationEngine);
        string ocrLanguage=source=="Auto"?settings.OcrLanguage:OcrService.Languages.FirstOrDefault(code=>code.Equals(source,StringComparison.OrdinalIgnoreCase) || code.StartsWith(source+"-",StringComparison.OrdinalIgnoreCase))??settings.OcrLanguage;
        var recognized=await OcrService.LayoutAsync(frame,ocrLanguage,cancellation);
        if(recognized.Lines.Length==0)throw new InvalidOperationException(L.T("No text was found. Try a larger region or another language."));
        if(source=="Auto" && settings.OcrLanguage=="Auto" && !recognized.Language.StartsWith("en",StringComparison.OrdinalIgnoreCase))
        {
            string text=string.Join("\n",recognized.Lines.Select(line=>line.Source));
            string? english=OcrService.Languages.FirstOrDefault(language=>language.StartsWith("en",StringComparison.OrdinalIgnoreCase));
            if(english is not null && text.Count(char.IsAsciiLetter)>=30 && !text.Any(character=>char.IsLetter(character) && !char.IsAsciiLetter(character)))
            {
                var candidate=await OcrService.LayoutAsync(frame,english,cancellation);
                if(candidate.Lines.Sum(line=>line.Source.Count(char.IsAsciiLetter))>=text.Count(char.IsAsciiLetter)*.8)recognized=candidate;
            }
        }
        if(recognized.Lines.Sum(line=>line.Source.Length)>20000)throw new InvalidOperationException(L.T("The selected text is too long. Select a smaller region (up to 20,000 characters)."));
        var lines=engine=="Ai"?TranslationProtocol.Paragraphs(recognized.Lines):recognized.Lines;prepared?.Invoke(lines);
        target=TranslationProtocol.ResolveTargetLanguage(target,string.Join("\n",lines.Select(line=>line.Source)));
        progress?.Invoke(L.T("Translating…"));
        string[] translated=await TranslateLinesAsync(lines.Select(line=>line.Source).ToArray(),source,target,engine,settings,cancellation,
            progress:(done,total)=>progress?.Invoke(L.F("Translating {0} of {1}…",done,total)));
        return new(frame.Width,frame.Height,source=="Auto"?TranslationProtocol.DetectLanguage(string.Join("\n",recognized.Lines.Select(line=>line.Source))):source,target,engine,
            lines.Select((line,index)=>line with {Target=translated[index]}).ToArray());
    }
    internal static async Task<string[]> TranslateLinesAsync(string[] lines,string source,string target,string engine,AppSettings settings,CancellationToken cancellation=default,HttpMessageHandler? handler=null,string? key=null,Action<int,int>? progress=null)
    {
        if(lines.Length==0)return [];
        target=TranslationProtocol.ResolveTargetLanguage(target,string.Join("\n",lines));
        if(engine=="Ai")
        {
            key??=OcrCredential.Read();var result=new string[lines.Length];int completed=0;
            async Task SingleAsync(IEnumerable<int> indices)
            {
                await Parallel.ForEachAsync(indices,new ParallelOptions{MaxDegreeOfParallelism=2,CancellationToken=cancellation},async(index,token)=>
                {
                    var specification=TranslationProtocol.BuildAiRequest(settings,[lines[index]],source,target,plainText:true);
                    string response=await OcrService.SendAiRequestAsync(specification,settings.AiOcrTimeoutSeconds,key,token,handler);
                    string translated=TranslationProtocol.ReadSingleTranslation(response);
                    if(string.IsNullOrWhiteSpace(translated))throw new IOException(L.T("The translation service returned no text."));
                    result[index]=translated;int count=Interlocked.Increment(ref completed);progress?.Invoke(count,lines.Length);
                });
            }
            if(TranslationProtocol.IsHunyuan(settings.AiTranslationModel))
            {await SingleAsync(Enumerable.Range(0,lines.Length));return result;}
            // Keep outputs bounded and preserve each OCR block's position.
            for(int start=0;start<lines.Length;start+=20)
            {
                var batch=lines.Skip(start).Take(20).ToArray();
                var request=TranslationProtocol.BuildAiRequest(settings,batch,source,target);
                string response=await OcrService.SendAiRequestAsync(request,settings.AiOcrTimeoutSeconds,key,cancellation,handler);
                string[] translated;
                try{translated=TranslationProtocol.ReadAiTranslations(response,batch.Length);}
                catch(IOException){await SingleAsync(Enumerable.Range(start,batch.Length));continue;}
                Array.Copy(translated,0,result,start,translated.Length);completed+=translated.Length;progress?.Invoke(completed,lines.Length);
            }
            return result;
        }
        if(!TranslationProtocol.IsChineseEnglish(target) || source!="Auto" && !TranslationProtocol.IsChineseEnglish(source))
            throw new InvalidOperationException(L.T("Free and offline translation support Chinese and English here. Choose AI for other languages."));
        if(engine=="Local" && (!settings.OfflineTranslationEnabled || !OfflineReady))
            throw new InvalidOperationException(L.T("Offline models are not installed. Enable and download them in Desktop tools → Screenshot translation, or choose free online translation."));
        var output=new string[lines.Length];
        foreach(var group in lines.Select((text,index)=>(Text:text,Index:index,Language:source=="Auto"?TranslationProtocol.DetectLanguage(text):source.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"zh":"en" )).GroupBy(item=>item.Language))
        {
            string normalizedTarget=target.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"zh":"en";
            var items=group.ToArray();
            if(group.Key==normalizedTarget){foreach(var item in items)output[item.Index]=item.Text;continue;}
            if(engine=="Local")
            {
                string[] result=await LocalAsync(items.Select(item=>item.Text).ToArray(),group.Key,normalizedTarget,cancellation);
                for(int i=0;i<items.Length;i++)output[items[i].Index]=result[i];
            }
            else foreach(var item in items)output[item.Index]=await FreeAsync(item.Text,group.Key,normalizedTarget,cancellation,handler);
        }
        return output;
    }
    internal static async Task<string> FreeAsync(string text,string source,string target,CancellationToken cancellation=default,HttpMessageHandler? handler=null)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(35));
        using var client=handler is null?new HttpClient():new HttpClient(handler,false);client.Timeout=Timeout.InfiniteTimeSpan;
        var result=new StringBuilder();
        try
        {
            foreach(string part in TranslationProtocol.SplitUtf8(text))
            {
                using var response=await client.GetAsync(TranslationProtocol.FreeAddress(part,source,target),HttpCompletionOption.ResponseHeadersRead,timeout.Token);
                if((int)response.StatusCode==429)throw new IOException(L.T("The free translation quota has been reached. Try later or choose AI / offline translation."));
                if(!response.IsSuccessStatusCode)throw new IOException(L.F("Free translation failed (HTTP {0}). Try again or choose another engine.",(int)response.StatusCode));
                await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var buffer=new MemoryStream();byte[] block=new byte[8192];int read;
                while((read=await stream.ReadAsync(block,timeout.Token))>0){if(buffer.Length+read>1024*1024)throw new IOException(L.T("The translation response is too large."));buffer.Write(block,0,read);}
                if(result.Length>0 && !target.StartsWith("zh",StringComparison.OrdinalIgnoreCase))result.Append(' ');
                result.Append(TranslationProtocol.ReadFreeResponse(Encoding.UTF8.GetString(buffer.ToArray())));
            }
            return result.ToString();
        }
        catch(OperationCanceledException ex)when(!cancellation.IsCancellationRequested){throw new IOException(L.T("Free translation timed out. Check the network or choose another engine."),ex);}
        catch(HttpRequestException ex){throw new IOException(L.T("Cannot connect to the free translation service. Check the network or choose AI / offline translation."),ex);}
        catch(JsonException ex){throw new IOException(L.T("The free translation service returned an invalid response."),ex);}
    }
    internal static async Task<string[]> LocalAsync(string[] lines,string source,string target,CancellationToken cancellation)
    {
        var info=new ProcessStartInfo(Path.Combine(OfflineDirectory,"python","python.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false),WorkingDirectory=OfflineDirectory};
        info.ArgumentList.Add("-I");info.ArgumentList.Add("-B");info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"Translation","translator.py"));info.ArgumentList.Add(OfflineDirectory);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var process=Process.Start(info)??throw new IOException(L.T("The offline translator could not start."));
        using var registration=timeout.Token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){} });
        Task<string> output=process.StandardOutput.ReadToEndAsync(timeout.Token),error=process.StandardError.ReadToEndAsync(timeout.Token);
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new{source,target,texts=lines}).AsMemory(),timeout.Token);process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync(timeout.Token);string response=await output;await error;
            if(process.ExitCode!=0)throw new IOException(L.T("Offline translation failed. Reinstall the optional models in Desktop tools."));
            return TranslationProtocol.ReadAiTranslations(response,lines.Length);
        }
        catch(OperationCanceledException ex)when(!cancellation.IsCancellationRequested){throw new IOException(L.T("Offline translation timed out. Try a smaller selection."),ex);}
    }
}
