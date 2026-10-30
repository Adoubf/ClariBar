namespace ClariBar.Core;

public sealed record ToolShortcutError(string Action,string Shortcut,int Error,string? DuplicateAction=null)
{
    public string Message=>DuplicateAction is not null
        ?Localizer.F("{0}: {1} is also assigned to {2}. Choose a different shortcut.",Localizer.T(Action),Shortcut,Localizer.T(DuplicateAction))
        :Error==1409
            ?Localizer.F("{0}: {1} is already in use. Close the app using it or choose another shortcut, then retry.",Localizer.T(Action),Shortcut)
            :Localizer.F("{0}: {1} is unavailable (Windows error {2}).",Localizer.T(Action),Shortcut,Error);
}

public static class DesktopShortcuts
{
    public const string Capture="F1",Pin="F3",HidePins="Shift+F3";
    public static (string Action,string Shortcut)[] Bindings(AppSettings settings)=>
    [
        ("Screenshot",settings.CaptureHotkey),("Screen color picker",settings.ColorHotkey),
        ("Pin clipboard image",settings.PinHotkey),("Extract text",settings.OcrHotkey),
        ("Show / hide all",settings.HidePinsHotkey),("Mouse click-through",settings.ClickThroughHotkey),
        ("Delayed capture",settings.DelayedCaptureHotkey),("Screenshot translation",settings.TranslationHotkey)
    ];
    public static bool Migrate(AppSettings settings)
    {
        if(settings.ToolShortcutVersion>=1)return false;
        if(settings.CaptureHotkey.Equals("Ctrl+Alt+A",StringComparison.OrdinalIgnoreCase))settings.CaptureHotkey=Capture;
        if(settings.PinHotkey.Equals("Ctrl+Alt+V",StringComparison.OrdinalIgnoreCase))settings.PinHotkey=Pin;
        if(settings.HidePinsHotkey.Equals("Ctrl+Alt+H",StringComparison.OrdinalIgnoreCase))settings.HidePinsHotkey=HidePins;
        if(settings.ClickThroughHotkey.Equals("Ctrl+Alt+X",StringComparison.OrdinalIgnoreCase))settings.ClickThroughHotkey="";
        if(settings.DelayedCaptureHotkey.Equals("Ctrl+Alt+D",StringComparison.OrdinalIgnoreCase))settings.DelayedCaptureHotkey="";
        settings.ToolShortcutVersion=1;return true;
    }
    public static void ApplySnipaste(AppSettings settings)
    {
        settings.CaptureHotkey=Capture;settings.PinHotkey=Pin;settings.HidePinsHotkey=HidePins;
        settings.ClickThroughHotkey=settings.DelayedCaptureHotkey="";settings.ToolShortcutVersion=1;
    }
    public static ToolShortcutError[] Register(AppSettings settings,Func<int,uint,uint,int> setHotkey)
    {
        var bindings=Bindings(settings);var errors=new List<ToolShortcutError>();
        // Release all slots first so exchanging two bindings cannot create a false conflict.
        for(int i=0;i<bindings.Length;i++)setHotkey(i,0,0);
        if(!settings.ToolHotkeysEnabled)return [];
        var assigned=new Dictionary<ToolHotkey,string>();
        for(int i=0;i<bindings.Length;i++)
        {
            var (action,text)=bindings[i];if(string.IsNullOrWhiteSpace(text))continue;
            if(!ToolHotkey.TryParse(text,out var key)){errors.Add(new(action,text,87));continue;}
            if(assigned.TryGetValue(key,out string? previous)){errors.Add(new(action,text,1409,previous));continue;}
            assigned.Add(key,action);int error=setHotkey(i,key.Modifiers,key.Key);
            if(error!=0)errors.Add(new(action,text,error));
        }
        return errors.ToArray();
    }
}
