using System.Diagnostics;
using System.Runtime.InteropServices;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint FindWindowEx(nint parent,nint after,string type,string? title);
    [DllImport("user32.dll",EntryPoint="SendMessageW",CharSet=CharSet.Unicode)]private static extern nint CopyProbeText(nint window,uint message,nint parameter,string text);
    [DllImport("user32.dll")]private static extern void keybd_event(byte key,byte scan,uint flags,nuint extra);
    [DllImport("user32.dll")]private static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
    private async Task CheckCopyTranslationAsync(Action<bool,string> require)
    {
        string probePath=Environment.GetEnvironmentVariable("CLARIBAR_COPY_PROBE")??throw new IOException("Set CLARIBAR_COPY_PROBE to the native copy fixture.");
        var savedClipboard=ClipboardSnapshot.Capture(hwnd);var foreground=DesktopWindowState.Foreground;DesktopCapture.GetCursorPos(out var originalCursor);
        bool enabled=settings.CopyTranslationEnabled;string theme=settings.Theme;
        var controller=InteractionField<CopyTranslationController>(desktopTools,"copyTranslator");
        string title="ClariBar.CopyTest."+Guid.NewGuid().ToString("N");
        using var process=Process.Start(new ProcessStartInfo(probePath,title){UseShellExecute=false})!;
        CopyTranslationWindow? standalone=null;
        try
        {
            HideForTool();
            nint fixture=0;
            for(int i=0;i<100 && fixture==0;i++){await Task.Delay(30);fixture=Native.FindWindow("ClariBar.CopyProbe",title);}
            require(fixture!=0,"A separate process provides a real native text-copy source");
            nint edit=FindWindowEx(fixture,0,"EDIT",null);require(edit!=0,"The fixture exposes an actual Windows text control");
            DesktopCapture.GetWindowRect(fixture,out var fixtureBounds);SetCursorPos(fixtureBounds.Left+60,fixtureBounds.Top+80);
            mouse_event(2,0,0,0,0);await Task.Delay(80);mouse_event(4,0,0,0,0);
            Native.SetForegroundWindow(fixture);Native.SendMessage(fixture,0x8001,0,0);await Task.Delay(150);
            require(DesktopWindowState.Foreground==fixture,"Real input gives the external copy source keyboard focus before testing Ctrl+C");
            var work=Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;int x=work.X+100,y=work.Y+100;SetCursorPos(x,y);
            settings.CopyTranslationEnabled=true;controller.Translator=(text,_)=>Task.FromResult("Translated: "+text);
            require(Native.CB_SetCopyTranslation(1)==0,"The clipboard observer starts without reserving copy shortcuts");
            async Task WaitAsync(Func<bool> condition)
            {
                for(int i=0;i<250;i++){if(condition())return;await Task.Delay(20);}
                int observed=Native.CB_GetCopyTranslationTrigger(out var state);
                throw new IOException($"Timed out waiting for a copy translation UI state: native={observed}, serial={state.Serial}, sequence={state.Sequence}, clipboard={DesktopCapture.GetClipboardSequenceNumber()}, foregroundMatches={DesktopWindowState.Foreground==fixture}, popup={desktopTools.CopyTranslationPopup is not null}, sourceCharacters={desktopTools.CopyTranslationPopup?.SourceText.Length}, resultCharacters={desktopTools.CopyTranslationPopup?.TranslatedText.Length}.");
            }
            async Task CopyAsync(string text)
            {
                CopyProbeText(edit,0x000C,0,text);Native.SetForegroundWindow(fixture);Native.SendMessage(fixture,0x8001,0,0);
                // Give the source process time to consume each transition before
                // changing its selection or beginning the next copy gesture.
                keybd_event(0x11,0,0,0);await Task.Delay(35);keybd_event(0x43,0,0,0);await Task.Delay(35);
                keybd_event(0x43,0,2,0);await Task.Delay(35);keybd_event(0x11,0,2,0);await Task.Delay(60);
            }
            CopyProbeText(edit,0x000C,0,"Programmatic clipboard text");Native.SendMessage(edit,0x00B1,0,-1);Native.SendMessage(edit,0x0301,0,0);await Task.Delay(250);
            await WaitAsync(()=>desktopTools.CopyTranslationPopup?.TranslatedText=="Translated: Programmatic clipboard text");
            require(desktopTools.CopyTranslationPopup?.SourceText=="Programmatic clipboard text","Application copy commands translate text without a keyboard gesture");desktopTools.CancelCopyTranslation();
            await CopyAsync("Hello from another application.");await WaitAsync(()=>desktopTools.CopyTranslationPopup?.TranslatedText=="Translated: Hello from another application.");
            var popup=desktopTools.CopyTranslationPopup!;
            require(DesktopWindowState.Foreground==fixture,"The translation popup opens without activating or stealing focus from the source");
            require(await (await ClipboardTransfer.ReadAsync()).GetTextAsync()=="Hello from another application.","Ctrl+C keeps its normal clipboard contents while translating");
            require(popup.AppWindow.Position.X>x && popup.AppWindow.Position.Y>y,"The popup starts below and to the right of the copy pointer");
            require(popup.AppWindow.IsVisible && !popup.AppWindow.IsShownInSwitchers && !DesktopCapture.IsWindowVisible(hwnd),"Copy translation uses a compact tool window while settings stay hidden");
            await RenderCheckImageAsync(L.LanguageCode+"-copy-translation",(FrameworkElement)popup.Content);
            await CopyAsync("Hello from another application.");await Task.Delay(300);
            require(ReferenceEquals(popup,desktopTools.CopyTranslationPopup),"Repeating Ctrl+C reuses the popup instead of stacking windows");
            Native.SendMessage(fixture,0x8002,1,0);await CopyAsync("A delayed-rendering browser style copy.");await WaitAsync(()=>popup.TranslatedText=="Translated: A delayed-rendering browser style copy.");
            require(popup.SourceText=="A delayed-rendering browser style copy.","Delayed clipboard rendering is accepted even when materializing text advances the sequence");Native.SendMessage(fixture,0x8002,0,0);
            SetCursorPos(work.X+work.Width-4,work.Y+work.Height-4);await CopyAsync("中文会自动翻译成英文。");await WaitAsync(()=>popup.SourceText=="中文会自动翻译成英文。" && !popup.IsRunning);
            var bounds=ToolWindowGeometry.ClientBounds(popup);
            require(bounds.Intersect(new(work.X,work.Y,work.Width,work.Height))==bounds,"A popup at the bottom-right screen edge stays entirely inside the work area");
            await popup.CopyAsync();await Task.Delay(200);
            require(ReferenceEquals(popup,desktopTools.CopyTranslationPopup) && popup.SourceText=="中文会自动翻译成英文。","Copying the translation does not recursively translate its own output");
            settings.Theme="Dark";desktopTools.RefreshAppearance();await Task.Delay(150);
            require(((FrameworkElement)popup.Content).ActualTheme==ElementTheme.Dark,"The copy popup follows the selected application theme");
            await RenderCheckImageAsync(L.LanguageCode+"-copy-translation-dark",(FrameworkElement)popup.Content);
            SetCursorPos(fixtureBounds.Left+60,fixtureBounds.Top+80);mouse_event(2,0,0,0,0);
            try{await WaitAsync(()=>desktopTools.CopyTranslationPopup is null);}finally{mouse_event(4,0,0,0,0);}
            require(desktopTools.CopyTranslationPopup is null,"A real click outside closes a popup that never took keyboard focus");
            await CopyAsync("Reopen after dismissal");await WaitAsync(()=>desktopTools.CopyTranslationPopup?.TranslatedText=="Translated: Reopen after dismissal");
            keybd_event(0x1B,0,0,0);try{await WaitAsync(()=>desktopTools.CopyTranslationPopup is null);}finally{keybd_event(0x1B,0,2,0);}
            require(desktopTools.CopyTranslationPopup is null,"Escape dismisses the popup while the copy source still has keyboard focus");
            desktopTools.CancelCopyTranslation();require(desktopTools.CopyTranslationPopup is null,"Closing the copy popup clears its tracked window");
            require(Native.CB_SetCopyTranslation(0)==0,"Copy translation can stop and remove its clipboard observer");
            await CopyAsync("Disabled translation");await Task.Delay(300);require(desktopTools.CopyTranslationPopup is null,"Disabling copy translation leaves regular copying active without opening a popup");
            var first=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            standalone=new(desktopTools,(text,_)=>text=="First"?first.Task:Task.FromResult("Latest result"));desktopTools.Track(standalone);
            var stale=standalone.ShowTextAsync("First",x,y);await standalone.ShowTextAsync("Second",x,y);first.SetResult("Stale result");await stale;
            require(standalone.SourceText=="Second" && standalone.TranslatedText=="Latest result","A late response from an older copy cannot replace the most recent translation");standalone.Close();
            int requests=0;standalone=new(desktopTools,(_,_)=>++requests==1?Task.FromException<string>(new IOException("Test service unavailable")):Task.FromResult("Recovered translation"));desktopTools.Track(standalone);
            await standalone.ShowTextAsync("Source retained after an error",x,y);
            require(standalone.DisplayedText=="Test service unavailable" && standalone.SourceText=="Source retained after an error" && !standalone.IsRunning,"Service errors remain in the popup with the source text and stop the busy indicator");
            await standalone.RetryAsync();require(standalone.TranslatedText=="Recovered translation","Retry reuses the copied source and recovers without another Ctrl+C");
            await standalone.ShowTextAsync(new string('a',CopyTranslation.MaximumCharacters+1),x,y);
            require(requests==2 && standalone.TranslatedText=="" && !standalone.IsRunning,"Oversized copied text displays an error without being sent to a translation service");standalone.Close();standalone=null;
        }
        finally
        {
            standalone?.Close();Native.CB_SetCopyTranslation(0);desktopTools.CancelCopyTranslation();controller.Translator=null;settings.CopyTranslationEnabled=enabled;settings.Theme=theme;
            if(!process.HasExited){process.CloseMainWindow();if(!process.WaitForExit(3000))process.Kill();}
            savedClipboard.Restore(hwnd);SetCursorPos(originalCursor.X,originalCursor.Y);if(foreground!=0)Native.SetForegroundWindow(foreground);
        }
    }
}
