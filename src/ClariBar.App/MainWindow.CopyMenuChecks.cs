using System.Diagnostics;
using System.Text.Json;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private async Task CheckCopyMenuTranslationAsync(Action<bool,string> require)
    {
        string probe=Environment.GetEnvironmentVariable("CLARIBAR_COPY_PROBE")??throw new IOException("An external copy fixture is required.");
        var clipboard=ClipboardSnapshot.Capture(hwnd);bool enabled=settings.CopyTranslationEnabled;var controller=InteractionField<CopyTranslationController>(desktopTools,"copyTranslator");
        var requests=new List<string>();string title="ClariBar.CopyMenu."+Guid.NewGuid().ToString("N");
        using var process=Process.Start(new ProcessStartInfo(probe,title){UseShellExecute=false})!;
        try
        {
            HideForTool();nint fixture=0;await WaitForToolStateAsync(()=>{fixture=Native.FindWindow("ClariBar.CopyProbe",title);return fixture!=0;});
            nint edit=FindWindowEx(fixture,0,"EDIT",null),button=FindWindowEx(fixture,0,"BUTTON",null);
            require(edit!=0 && button!=0,"A separate application supplies a native text editor, context menu and Copy button");
            void Select(string text){CopyProbeText(edit,0x000C,0,text);Native.SendMessage(edit,0x00B1,0,-1);}
            controller.Translator=(text,_)=>
            {
                requests.Add(text);
                if(desktopTools.CopyTranslationPopup is {} popup)InteractionField<DispatcherTimer>(popup,"dismiss").Stop();
                return Task.FromResult("Translated: "+text);
            };
            settings.CopyTranslationEnabled=true;
            Select("Existing clipboard fixture");Native.SendMessage(edit,0x0301,0,0);
            require(Native.CB_SetCopyTranslation(1)==0,"Translate on copy starts with a clipboard listener");await Task.Delay(220);
            require(!requests.Contains("Existing clipboard fixture"),"Enabling the feature leaves existing clipboard text untranslated");
            async Task CopyAsync(string text,Action action)
            {
                int count=requests.Count(value=>value==text);Select(text);action();
                await WaitForToolStateAsync(()=>requests.Count(value=>value==text)==count+1 && desktopTools.CopyTranslationPopup?.TranslatedText=="Translated: "+text);
            }
            await CopyAsync("Context menu copy without Ctrl+C",()=>Native.SendMessage(fixture,0x8003,0,0));
            require(Native.SendMessage(fixture,0x8004,0,0).ToInt64()==1 && Native.SendMessage(fixture,0x8007,0,0).ToInt64()>=1,"Choosing Copy from a displayed native context menu translates without any keyboard gesture");
            require(await (await ClipboardTransfer.ReadAsync()).GetTextAsync()=="Context menu copy without Ctrl+C","Context-menu translation preserves the text available for pasting");
            var popup=desktopTools.CopyTranslationPopup!;
            require(popup.AppWindow.IsVisible && !popup.AppWindow.IsShownInSwitchers && !DesktopCapture.IsWindowVisible(hwnd),"Menu copying opens the compact translation card while settings remain hidden");
            await RenderCheckImageAsync(L.LanguageCode+"-copy-context-menu",(FrameworkElement)popup.Content);
            await CopyAsync("Copy button without Ctrl+C",()=>Native.SendMessage(button,0x00F5,0,0));
            require(ReferenceEquals(popup,desktopTools.CopyTranslationPopup),"An application's Copy button updates the same translation card");
            await CopyAsync("Standard edit Copy command",()=>Native.SendMessage(edit,0x0301,0,0));
            require(popup.SourceText=="Standard edit Copy command","The native edit Copy command used by Ctrl+C also triggers translation");
            await CopyAsync("Standard edit Copy command",()=>Native.SendMessage(edit,0x0301,0,0));
            require(requests.Count(text=>text=="Standard edit Copy command")==2,"Copying identical text again still performs the new copy operation");
            popup.Close();await CopyAsync("Standard edit Copy command",()=>Native.SendMessage(edit,0x0301,0,0));
            require(!ReferenceEquals(popup,desktopTools.CopyTranslationPopup),"Copying the same text reopens a card after it has been dismissed");
            foreach(int delay in new[]{0,180})
            {
                Native.SendMessage(fixture,0x8002,1,0);Native.SendMessage(fixture,0x8006,delay,0);string text="Delayed context menu copy "+delay;
                await CopyAsync(text,()=>Native.SendMessage(fixture,0x8003,0,0));await Task.Delay(400);
                require(requests.Count(value=>value==text)==1,"A delayed clipboard format produces exactly one translation even when rendering advances the clipboard sequence: "+delay);
            }
            Native.SendMessage(fixture,0x8002,0,0);Native.SendMessage(fixture,0x8006,0,0);
            int ticks=0;var heartbeat=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};heartbeat.Tick+=(_,_)=>ticks++;
            Native.SendMessage(fixture,0x8002,1,0);Native.SendMessage(fixture,0x8006,600,0);heartbeat.Start();
            try{await CopyAsync("Slow clipboard rendering keeps the UI responsive",()=>Native.SendMessage(fixture,0x8003,0,0));}
            finally{heartbeat.Stop();Native.SendMessage(fixture,0x8002,0,0);Native.SendMessage(fixture,0x8006,0,0);}
            require(ticks>=8,"A clipboard owner taking 600 ms to render text does not block the application dispatcher");
            foreach(var item in new[]{(Mode:1,Text:"Private clipboard fixture",Name:"Content excluded from clipboard monitoring"),(Mode:2,Text:"File copy fixture",Name:"File copies with a text representation"),(Mode:3,Text:"Image only fixture",Name:"Images without text")})
            {
                Select(item.Text);Native.SendMessage(fixture,0x8005,item.Mode,0);await Task.Delay(250);
                require(!requests.Contains(item.Text),item.Name+" do not start a text translation request");
            }
            Select(" 12345 \r\n");Native.SendMessage(edit,0x0301,0,0);await Task.Delay(250);
            require(!requests.Any(text=>text.Trim()=="12345"),"Empty or numeric clipboard data is ignored");
            await CopyAsync("Source before copying its result",()=>Native.SendMessage(edit,0x0301,0,0));
            await desktopTools.CopyTranslationPopup!.CopyAsync();await Task.Delay(250);
            require(!requests.Contains("Translated: Source before copying its result"),"Copying ClariBar's translation never translates that result recursively");
            await ToolUi.CopyTextAsync("ClariBar internal clipboard fixture");await Task.Delay(250);
            require(!requests.Contains("ClariBar internal clipboard fixture"),"Other copies made inside ClariBar are also ignored");
            Select("Superseded clipboard fixture");Native.SendMessage(edit,0x0301,0,0);
            await CopyAsync("Newest clipboard fixture",()=>Native.SendMessage(edit,0x0301,0,0));await Task.Delay(180);
            require(desktopTools.CopyTranslationPopup?.SourceText=="Newest clipboard fixture","Rapid successive copy commands retain the newest clipboard text");
            settings.CopyTranslationEnabled=false;Native.CB_SetCopyTranslation(0);desktopTools.CancelCopyTranslation();
            Select("Copied while disabled");Native.SendMessage(fixture,0x8003,0,0);await Task.Delay(250);
            require(!requests.Contains("Copied while disabled"),"Disabling translate on copy also disables context-menu translation");
            settings.CopyTranslationEnabled=true;Native.CB_SetCopyTranslation(1);await Task.Delay(180);
            require(!requests.Contains("Copied while disabled"),"Re-enabling the feature does not translate text copied while it was disabled");
            await CopyAsync("Fresh copy after re-enabling",()=>Native.SendMessage(button,0x00F5,0,0));
            require(requests.Contains("Fresh copy after re-enabling"),"A fresh copy after re-enabling starts translation normally");
        }
        finally
        {
            Native.CB_SetCopyTranslation(0);desktopTools.CancelCopyTranslation();controller.Translator=null;settings.CopyTranslationEnabled=enabled;
            if(!process.HasExited){process.CloseMainWindow();if(!process.WaitForExit(3000))process.Kill();}
            clipboard.Restore(hwnd);
        }
    }
    private async Task CheckConfiguredCopyMenuAsync(Action<bool,string> require)
    {
        string probe=Environment.GetEnvironmentVariable("CLARIBAR_COPY_PROBE")??throw new IOException("An external copy fixture is required.");
        var clipboard=ClipboardSnapshot.Capture(hwnd);bool enabled=settings.CopyTranslationEnabled;string engine=settings.TranslationEngine,source=settings.TranslationSourceLanguage,target=settings.TranslationTargetLanguage;
        string title="ClariBar.ConfiguredCopyMenu."+Guid.NewGuid().ToString("N");
        using var process=Process.Start(new ProcessStartInfo(probe,title){UseShellExecute=false})!;
        try
        {
            HideForTool();nint fixture=0;await WaitForToolStateAsync(()=>{fixture=Native.FindWindow("ClariBar.CopyProbe",title);return fixture!=0;});
            nint edit=FindWindowEx(fixture,0,"EDIT",null);string text="Copy this text using the context menu. Translate it beside the pointer as the response arrives. The original text must remain available for pasting, and the source application must keep its keyboard focus.";
            settings.CopyTranslationEnabled=true;settings.TranslationEngine="Ai";settings.TranslationSourceLanguage=settings.TranslationTargetLanguage="Auto";
            Native.CB_SetCopyTranslation(1);CopyProbeText(edit,0x000C,0,text);Native.SendMessage(edit,0x00B1,0,-1);Native.SendMessage(fixture,0x8003,0,0);
            await WaitForToolStateAsync(()=>desktopTools.CopyTranslationPopup?.SourceText==text);
            var popup=desktopTools.CopyTranslationPopup!;InteractionField<DispatcherTimer>(popup,"dismiss").Stop();Native.CB_SetCopyTranslation(0);
            bool partial=false;
            while(!popup.TranslationTask.IsCompleted)
            {if(popup.ProgressiveRenders>0 && popup.TranslatedText.Any(character=>character is >= '\u3400' and <= '\u9fff')){partial=true;break;}await Task.Delay(20);}
            if(partial)await RenderCheckImageAsync(L.LanguageCode+"-configured-copy-menu-progress",(FrameworkElement)popup.Content);
            await popup.TranslationTask.WaitAsync(TimeSpan.FromMinutes(3));
            require(Native.SendMessage(fixture,0x8004,0,0).ToInt64()==1 && Native.SendMessage(fixture,0x8007,0,0).ToInt64()>=1,"The actual configured model is reached through a native context-menu Copy command");
            require(popup.TranslatedText.Any(character=>character is >= '\u3400' and <= '\u9fff'),"The saved AI connection translates the copied English text into Chinese");
            require(partial && popup.StreamingEvents>0,"Context-menu translation displays actual streamed text before the request completes");
            require(await (await ClipboardTransfer.ReadAsync()).GetTextAsync()==text,"Real AI menu translation keeps the original clipboard text available for pasting");
            await File.WriteAllTextAsync(Path.Combine(AppPaths.Logs,"configured-copy-menu-evidence.json"),JsonSerializer.Serialize(new{model=settings.AiTranslationModel,contextMenus=Native.SendMessage(fixture,0x8007,0,0).ToInt64(),partial,popup.StreamingEvents,popup.ProgressiveRenders,source=text,translation=popup.TranslatedText},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
        }
        finally
        {
            Native.CB_SetCopyTranslation(0);desktopTools.CancelCopyTranslation();settings.CopyTranslationEnabled=enabled;settings.TranslationEngine=engine;settings.TranslationSourceLanguage=source;settings.TranslationTargetLanguage=target;
            if(!process.HasExited){process.CloseMainWindow();if(!process.WaitForExit(3000))process.Kill();}clipboard.Restore(hwnd);
        }
    }
}
