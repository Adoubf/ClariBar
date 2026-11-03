using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private static async Task WaitForToolStateAsync(Func<bool> condition)
    {for(int attempt=0;attempt<300;attempt++){if(condition())return;await Task.Delay(20);}throw new IOException("The expected desktop tool state did not appear.");}
    private async Task CheckOcrStreamingAsync(Action<bool,string> require)
    {
        string saved=JsonSerializer.Serialize(settings,Wire.Json);OcrResultWindow? window=null;
        byte[] pixels=new byte[560*170*4];Array.Fill(pixels,(byte)255);
        var frame=new PixelFrame(560,170,pixels).Render([new("Text",[new(24,18),new(25,19)],"#101010",3,"CLARIBAR OCR 2026"){FontSize=32}]);
        try
        {
            settings.OcrDefaultEngine="Ai";settings.AiOcrEndpoint="http://127.0.0.1:8000/v1";settings.AiOcrModel="test-vl";
            foreach(string api in AiOcrProtocol.ApiTypes.Skip(1))
            {
                settings.AiOcrApiType=api;using var handler=new StreamingTranslationHandler(api);
                window=new(desktopTools,frame,handler);desktopTools.Track(window);window.Activate();
                await WaitForToolStateAsync(()=>window.ProgressiveRenders>0);
                var output=InteractionField<TextBox>(window,"result");
                require(!window.InitialRecognition.IsCompleted && output.Text=="第一" && output.IsReadOnly,api+": AI OCR is readable before the server finishes and remains protected during generation");
                await RenderCheckImageAsync(L.LanguageCode+"-ocr-stream-"+api,(FrameworkElement)window.Content);
                handler.Release.TrySetResult();await window.InitialRecognition;
                require(output.Text=="第一段" && !output.IsReadOnly && handler.StreamingRequests==1,api+": OCR completes without missing or duplicated text and becomes editable");
                window.Close();window=null;
            }
            settings.AiOcrApiType=AiOcrProtocol.Chat;
            using(var handler=new StreamingTranslationHandler(AiOcrProtocol.Chat,truncate:true))
            {
                window=new(desktopTools,frame,handler);desktopTools.Track(window);window.Activate();await WaitForToolStateAsync(()=>window.ProgressiveRenders>0);
                handler.Release.TrySetResult();await window.InitialRecognition;
                require(InteractionField<TextBox>(window,"result").Text=="第一" && InteractionField<TextBlock>(window,"status").Text.Contains(L.T("Recognition interrupted. Received text is kept.")),"Interrupted AI OCR preserves received text with a localized failure status");
                window.Close();window=null;
            }
            using(var handler=new StreamingTranslationHandler(AiOcrProtocol.Chat,json:true,reject:true))
            {
                window=new(desktopTools,frame,handler);desktopTools.Track(window);window.Activate();await WaitForToolStateAsync(()=>InteractionField<TextBox>(window,"result").Text=="普通译文");await window.InitialRecognition;
                require(handler.Requests==2 && InteractionField<TextBox>(window,"result").Text=="普通译文","An OCR server rejecting streaming automatically returns its complete JSON result");window.Close();window=null;
            }
            using(var handler=new StreamingTranslationHandler(AiOcrProtocol.Chat))
            {
                window=new(desktopTools,frame,handler);desktopTools.Track(window);window.Activate();await WaitForToolStateAsync(()=>window.ProgressiveRenders>0);
                var pending=window.InitialRecognition;window.Close();window=null;await pending.WaitAsync(TimeSpan.FromSeconds(3));
                require(handler.Cancelled,"Closing AI OCR cancels a blocked response without waiting for the server");
            }
            settings.OcrDefaultEngine="Local";
            using(var handler=new StreamingTranslationHandler(AiOcrProtocol.Chat))
            {
                window=new(desktopTools,frame,handler);desktopTools.Track(window);window.Activate();
                await WaitForToolStateAsync(()=>InteractionField<TextBox>(window,"result").Text.Contains("2026"));await window.InitialRecognition;
                require(handler.Requests==0 && window.StreamingEvents==0 && window.ProgressiveRenders==0,"Local OCR recognizes the test image without streaming or an AI request");window.Close();window=null;
            }
        }
        finally
        {
            window?.Close();var original=JsonSerializer.Deserialize<AppSettings>(saved,Wire.Json)!;
            foreach(var property in typeof(AppSettings).GetProperties().Where(property=>property.CanWrite))property.SetValue(settings,property.GetValue(original));
        }
    }
    private async Task CheckCaptureResponsivenessAsync(Action<bool,string> require)
    {
        bool previous=settings.CaptureCancelOnDeactivate;settings.CaptureCancelOnDeactivate=false;CaptureOverlay? overlay=null;
        var desktop=DesktopCapture.DesktopBounds;byte[] pixels=new byte[checked(desktop.Width*desktop.Height*4)];Array.Fill(pixels,(byte)235);
        for(int index=3;index<pixels.Length;index+=4)pixels[index]=255;
        var source=new PixelFrame(desktop.Width,desktop.Height,pixels).Render([new("Rectangle",[new(330,250),new(400,300)],"#2D3C4A",4)]);var box=new PixelRect(300,220,640,360).Intersect(source.Bounds);
        var metrics=new List<object>();
        try
        {
            HideForTool();
            overlay=new(desktopTools,source,desktop,[],false,preset:box);desktopTools.Track(overlay);var baseline=overlay.SelectAsync();await overlay.FirstPresented.WaitAsync(TimeSpan.FromSeconds(4));await Task.Delay(300);
            var baselineScreen=DesktopCapture.Capture(desktop);var baselineXaml=await RenderOwnElementAsync((FrameworkElement)overlay.Content);
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,"capture-direct-screen.png"),await baselineScreen.PngAsync());
            await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,"capture-direct-xaml.png"),await baselineXaml.PngAsync());
            var root=InteractionField<Grid>(overlay,"root");
            var baselineState=new{native=ToolWindowGeometry.ClientBounds(overlay),root.ActualWidth,root.ActualHeight,xamlWidth=baselineXaml.Width,xamlHeight=baselineXaml.Height,physical=baselineScreen.ColorAt(box.X+box.Width/2,box.Y+box.Height/2),overlay.PresentationFallback};
            await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"capture-direct-state.json"),JsonSerializer.Serialize(baselineState,Wire.Json));
            require(baselineState.native==desktop,"The borderless capture client covers every physical desktop pixel without a system frame offset");
            overlay.Close();overlay=null;await baseline;
            using(var cover=new CaptureTransition(source,desktop,box))
            {
                DesktopCapture.DwmFlush();
                var initial=DesktopCapture.Capture(desktop);var light=initial.ColorAt(box.X+box.Width/2,box.Y+box.Height/2);var dark=initial.ColorAt(30,30);
                await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"capture-initial-cover.json"),JsonSerializer.Serialize(new{light,dark,desktop,box},Wire.Json));
                if(light.R<=230 || dark.R is <=60 or >=85)await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,"capture-initial-unexpected.png"),await initial.PngAsync());
                require(cover.Handle!=0 && light.R>230 && dark.R is >60 and <85,"The immediate frozen cover preserves the selected pixels and dims the exterior before XAML loads");
                require(!DesktopCapture.Windows(desktop,Handle,cover.Handle).Any(item=>item.Handle==cover.Handle),"Window auto-detection excludes the startup cover");
                var observed=new List<byte>();var trace=new List<object>();PixelFrame? unexpected=null;int phase=0;var probeClock=Stopwatch.StartNew();using var sampling=new CancellationTokenSource();
                var probe=Task.Run(async()=>
                {
                    while(!sampling.IsCancellationRequested)
                    {
                        var color=DesktopCapture.Capture(new(desktop.X+box.X+box.Width/2,desktop.Y+box.Y+box.Height/2,1,1)).ColorAt(0,0);observed.Add(color.R);trace.Add(new{atMs=probeClock.Elapsed.TotalMilliseconds,phase,color.R,color.G,color.B});
                        if(color.R is <232 or >238 && unexpected is null)unexpected=DesktopCapture.Capture(desktop);
                        await Task.Delay(5);
                    }
                });
                Task<CaptureSelection?> selection;bool presented=false;
                try
                {
                    overlay=new(desktopTools,source,desktop,[],false,preset:box);desktopTools.Track(overlay);selection=overlay.SelectAsync(cover);overlay.Reactivate();phase=1;
                    presented=await overlay.FirstPresented.WaitAsync(TimeSpan.FromSeconds(4));phase=2;await Task.Delay(120);
                }
                finally{sampling.Cancel();await probe;}
                await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"capture-handoff-samples.json"),JsonSerializer.Serialize(trace,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
                if(unexpected is not null)await File.WriteAllBytesAsync(Path.Combine(AppPaths.Logs,"capture-handoff-unexpected.png"),await unexpected.PngAsync());
                require(presented && !overlay.PresentationFallback && cover.Handle==0,"The rendered XAML frame replaces the cover without waiting for the watchdog");
                require(observed.Count>=2 && observed.All(value=>value is >=232 and <=238),"Screen samples preserve the original brightness across the native-to-XAML handoff with no blank white or black frame");
                metrics.Add(new{handoffSamples=observed.Count,minimum=observed.Min(),maximum=observed.Max()});
                var anchors=InteractionField<Canvas>(overlay,"anchors");var originalHandles=anchors.Children.ToArray();int commits=overlay.SourceCommits;
                var setArea=typeof(CaptureOverlay).GetMethod("SetArea",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var dragging=typeof(CaptureOverlay).GetField("handleDrag",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var mark=new CaptureMark("Arrow",[new(20,30),new(160,80)],"#FF5555",3);overlay.Document!.Add(mark);
                dragging.SetValue(overlay,true);var timer=Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();PixelRect final=box;
                for(int step=0;step<80;step++){final=box with{Width=box.Width+step,Height=box.Height+step/2};setArea.Invoke(overlay,[final,true,null]);}
                allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;double previewMs=timer.Elapsed.TotalMilliseconds;
                require(overlay.PreviewUpdates>=80 && overlay.SourceCommits==commits,"Repeated selection resizing previews geometry without cropping or uploading a new source image");
                require(anchors.Children.SequenceEqual(originalHandles),"All eight resize handles survive repeated geometry updates as the same controls");
                dragging.SetValue(overlay,false);setArea.Invoke(overlay,[final,true,null]);
                require(overlay.SourceCommits==commits+1 && overlay.Document!.Source.Pixels.SequenceEqual(source.Crop(final).Pixels) && overlay.Document.Marks.Single()==mark,"Releasing a resized selection commits exactly one accurate crop and preserves annotations");
                metrics.Add(new{previewUpdates=80,previewMs,allocatedBytes=allocated,commitsDuringDrag=0,commitsOnRelease=1});
                overlay.Close();overlay=null;require(await selection is null && Native.FindWindow("ClariBar.CaptureCover",null)==0,"Cancelling a selection cleans up the fullscreen transition window");
            }
            using(var cancelled=new CaptureTransition(source,desktop))
            {
                overlay=new(desktopTools,source,desktop,[],false);desktopTools.Track(overlay);var pending=overlay.SelectAsync(cancelled);overlay.Close();overlay=null;
                require(await pending is null && cancelled.Handle==0,"Cancellation before the first rendered frame also disposes the native cover");
            }
            foreach(var mode in new[]{"screenshot","ocr","translation"})
            {
                var pending=desktopTools.BeginAsync(ocr:mode!="screenshot",translate:mode=="translation",returnToSettings:false);
                await WaitForToolStateAsync(()=>InteractionField<CaptureOverlay?>(desktopTools,"overlay") is {} current && current.FirstPresented.IsCompleted);
                var current=InteractionField<CaptureOverlay>(desktopTools,"overlay");require(await current.FirstPresented && !current.PresentationFallback,"The real "+mode+" entry point presents a ready capture frame without fallback");
                await desktopTools.BeginAsync(returnToSettings:false);current.Close();await pending;
                using var startup=JsonDocument.Parse(JsonSerializer.Serialize(desktopTools.LastCaptureStartup,Wire.Json));
                require(startup.RootElement.GetProperty("presented").GetBoolean() && !startup.RootElement.GetProperty("fallback").GetBoolean(),"The "+mode+" startup diagnostics confirm a rendered handoff");
                metrics.Add(new{mode,startup=desktopTools.LastCaptureStartup});
            }
            await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"ocr-capture-responsiveness.json"),JsonSerializer.Serialize(metrics,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
        }
        finally{overlay?.Close();settings.CaptureCancelOnDeactivate=previous;}
    }
    private async Task CheckConfiguredOcrStreamingAsync(Action<bool,string> require)
    {
        require(settings.OcrDefaultEngine=="Ai" && !string.IsNullOrWhiteSpace(settings.AiOcrModel),"The live OCR check uses the existing configured AI engine and model");
        byte[] pixels=new byte[960*260*4];Array.Fill(pixels,(byte)255);
        var source=new PixelFrame(960,260,pixels).Render([
            new("Text",[new(24,16),new(25,17)],"#101010",3,"CLARIBAR OCR 2026"){FontSize=32},
            new("Text",[new(24,72),new(25,73)],"#101010",3,"Recognized text should appear while the model is still generating."){FontSize=24},
            new("Text",[new(24,120),new(25,121)],"#101010",3,"Keep the existing settings, keyboard shortcuts and clipboard contents."){FontSize=24},
            new("Text",[new(24,168),new(25,169)],"#101010",3,"Streaming OCR connection test. English and Chinese: 文字识别测试。"){FontSize=24}
        ]);
        HideForTool();var clock=Stopwatch.StartNew();var window=new OcrResultWindow(desktopTools,source);desktopTools.Track(window);window.Activate();
        try
        {
            await WaitForToolStateAsync(()=>InteractionField<bool>(window,"initialStarted"));double? firstTextMs=null;
            while(!window.InitialRecognition.IsCompleted)
            {if(firstTextMs is null && window.ProgressiveRenders>0){firstTextMs=clock.Elapsed.TotalMilliseconds;await RenderCheckImageAsync(L.LanguageCode+"-configured-ocr-progress",(FrameworkElement)window.Content);}await Task.Delay(20);}
            await window.InitialRecognition;string text=InteractionField<TextBox>(window,"result").Text;
            require(text.Contains("CLARIBAR",StringComparison.OrdinalIgnoreCase) && text.Contains("2026"),"The saved model recognizes a real controlled image through the existing encrypted credentials");
            require(firstTextMs is not null && window.StreamingEvents>1,"The actual configured AI OCR model shows partial recognized text before completion");
            var evidence=new{model=settings.AiOcrModel,api=AiOcrProtocol.Resolve(AiOcrOptions.From(settings)).ApiType,firstTextMs,completeMs=clock.Elapsed.TotalMilliseconds,window.StreamingEvents,window.ProgressiveRenders,text};
            await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"configured-ocr-streaming-evidence.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
            await RenderCheckImageAsync(L.LanguageCode+"-configured-ocr-complete",(FrameworkElement)window.Content);
        }
        finally{window.Close();}
    }
}
