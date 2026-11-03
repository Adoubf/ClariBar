using System.Diagnostics;
using System.Reflection;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private async Task CheckCopyPopupDismissalAsync(Action<bool,string> require)
    {
        string probe=Environment.GetEnvironmentVariable("CLARIBAR_COPY_PROBE")??throw new IOException("An external copy fixture is required.");
        var clipboard=ClipboardSnapshot.Capture(hwnd);bool enabled=settings.CopyTranslationEnabled;
        var controller=InteractionField<CopyTranslationController>(desktopTools,"copyTranslator");
        string title="ClariBar.CopyDismiss."+Guid.NewGuid().ToString("N");
        using var process=Process.Start(new ProcessStartInfo(probe,title){UseShellExecute=false})!;
        nint fixture=0;
        try
        {
            HideForTool();await WaitForToolStateAsync(()=>{fixture=Native.FindWindow("ClariBar.CopyProbe",title);return fixture!=0;});
            nint edit=FindWindowEx(fixture,0,"EDIT",null);
            require(edit!=0 && Native.SendMessage(fixture,0x8008,1,0)==1,"The external fixture guards tagged test input before it can click a desktop application");
            settings.CopyTranslationEnabled=true;Native.CB_SetCopyTranslation(1);
            controller.Translator=(text,_)=>Task.FromResult("Translated: "+text);
            async Task<CopyTranslationWindow> CopyAsync(string text,bool wait=true)
            {
                CopyProbeText(edit,0x000C,0,text);Native.SendMessage(edit,0x00B1,0,-1);Native.SendMessage(edit,0x0301,0,0);
                await WaitForToolStateAsync(()=>desktopTools.CopyTranslationPopup is {} current && current.SourceText==text && (!wait || !current.IsRunning));
                var popup=desktopTools.CopyTranslationPopup!;await WaitForToolStateAsync(()=>((FrameworkElement)popup.Content).IsLoaded);
                InteractionField<DispatcherTimer>(popup,"dismiss").Stop();return popup;
            }
            async Task ClickAsync(int button)
            {
                long before=Native.SendMessage(fixture,0x800A,0,0).ToInt64();
                require(Native.SendMessage(fixture,0x8009,button,0)==2,"A complete "+(button==1?"left":"right")+" click is injected without waiting for a polling interval");
                await WaitForToolStateAsync(()=>Native.SendMessage(fixture,0x800A,0,0).ToInt64()>=before+2);
                require(Native.SendMessage(fixture,0x800A,0,0).ToInt64()==before+2,"The passive popup observer passes both mouse events along the hook chain");
            }
            foreach(int button in new[]{1,2})
            {
                var popup=await CopyAsync("Close on outside "+button);nint handle=WinRT.Interop.WindowNative.GetWindowHandle(popup);
                require(InteractionField<bool>(popup,"watching"),"The native mouse observer starts for a new copy popup");
                DesktopCapture.GetCursorPos(out var pointer);var area=Microsoft.UI.Windowing.DisplayArea.GetFromPoint(new(pointer.X,pointer.Y),Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
                int x=pointer.X<area.X+area.Width/2?area.X+area.Width-popup.AppWindow.Size.Width-8:area.X+8;
                int y=pointer.Y<area.Y+area.Height/2?area.Y+area.Height-popup.AppWindow.Size.Height-8:area.Y+8;
                popup.AppWindow.Move(new(x,y));
                require(!ToolWindowGeometry.ClientBounds(popup).Contains(pointer.X,pointer.Y),"The test click position is outside the translation card");
                await ClickAsync(button);
                require(Native.CB_PollCopyPopup(handle)==button,"A completed short click stays recorded even with the UI polling timer stopped");
                InteractionField<DispatcherTimer>(popup,"dismiss").Start();await WaitForToolStateAsync(()=>desktopTools.CopyTranslationPopup is null);
                require(Native.CB_PollCopyPopup(handle)==0,"Outside "+(button==1?"left":"right")+" click closes the popup and releases its mouse observer");
                require(!DesktopCapture.IsWindowVisible(hwnd),"Dismissing the translation card keeps the settings window hidden");
            }
            var inside=await CopyAsync("Keep internal actions available");nint insideHandle=WinRT.Interop.WindowNative.GetWindowHandle(inside);
            DesktopCapture.GetCursorPos(out var cursor);inside.AppWindow.Move(new(cursor.X-40,cursor.Y-40));
            await ClickAsync(1);await ClickAsync(2);
            require(Native.CB_PollCopyPopup(insideHandle)==0 && ReferenceEquals(inside,desktopTools.CopyTranslationPopup),"Left and right clicks inside the card do not dismiss it");
            await inside.CopyAsync();await Task.Delay(150);
            require(await (await ClipboardTransfer.ReadAsync()).GetTextAsync()=="Translated: Keep internal actions available" && ReferenceEquals(inside,desktopTools.CopyTranslationPopup),"Copying the result remains usable and never recursively starts another translation");
            await RenderCheckImageAsync(L.LanguageCode+"-copy-popup-dismiss",(FrameworkElement)inside.Content);
            inside.Close();

            bool canceled=false;
            controller.Translator=async (_,token)=>
            {
                using var registration=token.Register(()=>canceled=true);
                await Task.Delay(Timeout.Infinite,token);return "Late result";
            };
            var waiting=await CopyAsync("Dismiss an in-progress translation",false);
            var observe=typeof(CopyTranslationWindow).GetMethod("ObservePointer",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var bounds=ToolWindowGeometry.ClientBounds(waiting);int farX=bounds.Right+2000,farY=bounds.Bottom+2000;
            void Move(int x,int y,bool drag,long now)=>observe.Invoke(waiting,[x,y,drag,now]);
            Move(farX,farY,false,0);Move(farX,farY,false,249);
            require(ReferenceEquals(waiting,desktopTools.CopyTranslationPopup) && waiting.IsRunning,"A brief distant pointer excursion retains an in-progress translation");
            Move(bounds.X+20,bounds.Y+20,false,250);Move(farX,farY,false,300);Move(farX,farY,false,549);
            require(ReferenceEquals(waiting,desktopTools.CopyTranslationPopup),"Returning to the card resets its movement dismissal countdown");
            Move(farX,farY,true,550);Move(farX,farY,true,5000);
            require(ReferenceEquals(waiting,desktopTools.CopyTranslationPopup),"A selection drag can extend outside the card without closing it");
            Move(farX,farY,false,5001);Move(farX,farY,false,5250);
            require(ReferenceEquals(waiting,desktopTools.CopyTranslationPopup),"Releasing the drag starts a fresh 250 millisecond grace interval");
            Move(farX,farY,false,5251);await waiting.TranslationTask;
            require(canceled && desktopTools.CopyTranslationPopup is null,"Staying far away closes the card, cancels its active request and prevents a late popup");
            require(!DesktopCapture.IsWindowVisible(hwnd),"Automatic dismissal never restores the settings window");
            controller.Translator=(text,_)=>Task.FromResult("Translated: "+text);
            var reopened=await CopyAsync("A fresh copy after automatic dismissal");
            require(reopened.TranslatedText=="Translated: A fresh copy after automatic dismissal","The next copy opens a working new popup after automatic dismissal");
            nint reopenedHandle=WinRT.Interop.WindowNative.GetWindowHandle(reopened);settings.CopyTranslationEnabled=false;Native.CB_SetCopyTranslation(0);desktopTools.CancelCopyTranslation();
            require(desktopTools.CopyTranslationPopup is null && Native.CB_PollCopyPopup(reopenedHandle)==0,"Disabling the feature removes both the popup and its click observer");
        }
        finally
        {
            Native.CB_SetCopyTranslation(0);desktopTools.CancelCopyTranslation();controller.Translator=null;settings.CopyTranslationEnabled=enabled;
            if(!process.HasExited){if(fixture!=0)Native.SendMessage(fixture,0x8008,0,0);process.CloseMainWindow();if(!process.WaitForExit(3000))process.Kill();}
            clipboard.Restore(hwnd);
        }
    }
}
