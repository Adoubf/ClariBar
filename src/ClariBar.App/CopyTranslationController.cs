using System.Runtime.InteropServices;
using ClariBar.Core;

namespace ClariBar.App;

internal sealed class CopyTranslationController(DesktopToolsController tools,nint owner):IDisposable
{
    private uint serial;
    private bool disposed;
    private CancellationTokenSource? pending;
    internal CopyTranslationWindow? Window {get;private set;}
    internal Func<string,CancellationToken,Task<string>>? Translator {get;set;}
    [DllImport("user32.dll")]private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")]private static extern bool CloseClipboard();
    [DllImport("user32.dll")]private static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll")]private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")]private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")]private static extern nuint GlobalSize(nint memory);
    internal async Task ReceiveAsync()
    {
        if(disposed || !tools.Settings.CopyTranslationEnabled || Native.CB_GetCopyTranslationTrigger(out var trigger)==0 || trigger.Serial==serial)return;
        serial=trigger.Serial;pending?.Cancel();using var request=new CancellationTokenSource();pending=request;
        try
        {
            // A clipboard owner may render text on demand, or stop responding.
            // Never perform that cross-process wait on the XAML thread.
            string? text=await Task.Run(()=>ReadAsync(trigger,request.Token),request.Token);
            if(request.IsCancellationRequested || disposed || !tools.Settings.CopyTranslationEnabled || !CopyTranslation.HasText(text))return;
            if(Window is null)
            {
                var created=new CopyTranslationWindow(tools,Translator);Window=created;tools.Track(created);
                void Closed(object sender,Microsoft.UI.Xaml.WindowEventArgs args){created.Closed-=Closed;if(ReferenceEquals(Window,created))Window=null;}
                created.Closed+=Closed;
            }
            await Window.ShowTextAsync(text!,trigger.X,trigger.Y);
        }
        catch(OperationCanceledException){}
        catch(Exception ex){AppDiagnostics.Error("copy-translation",ex);}
        finally{if(ReferenceEquals(pending,request))pending=null;}
    }
    private async Task<string?> ReadAsync(Native.CopyTrigger trigger,CancellationToken cancellation)
    {
        for(int attempt=0;attempt<12;attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            if(DesktopCapture.GetClipboardSequenceNumber()!=trigger.Sequence)return null;
            if(!OpenClipboard(owner)){await Task.Delay(25*(attempt+1),cancellation);continue;}
            try
            {
                if(DesktopCapture.GetClipboardSequenceNumber()!=trigger.Sequence || Native.CB_BeginCopyTranslationRead(trigger.Serial)==0)return null;
                try
                {
                    nint memory=GetClipboardData(13);if(memory==0)return null;
                    nuint bytes=GlobalSize(memory);if(bytes<2)return null;
                    nint address=GlobalLock(memory);if(address==0)return null;
                    try
                    {
                        int length=(int)Math.Min(bytes/2,(nuint)(CopyTranslation.MaximumCharacters+2));
                        string text=Marshal.PtrToStringUni(address,length)??"";int end=text.IndexOf('\0');if(end>=0)text=text[..end];
                        return text.Trim();
                    }
                    finally{GlobalUnlock(memory);}
                }
                finally{Native.CB_EndCopyTranslationRead(trigger.Serial,DesktopCapture.GetClipboardSequenceNumber());}
            }
            finally{CloseClipboard();}
        }
        return null;
    }
    internal void Cancel(){serial=0;pending?.Cancel();Window?.Close();Window=null;}
    public void Dispose(){disposed=true;Cancel();}
}
