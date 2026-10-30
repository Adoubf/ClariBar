using ClariBar.Core;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;
using L = ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private readonly DesktopToolsController desktopTools;
    private readonly List<string> toolHotkeyErrors = [];
    private InfoBar? toolsShortcutNotice;
    private ComboBox? defaultOcrEngine;
    private Task desktopRestore=Task.CompletedTask;
    internal nint Handle => hwnd;
    internal string ToolDataDirectory => store.DirectoryPath;
    internal void HideForTool() { Native.ClearCaptionHover(hwnd); Native.ShowWindow(hwnd, 0); }
    internal void RestoreFromTool() => ShowSettings();
    internal Task ToolErrorAsync(string message) => MessageAsync(L.T("Desktop tools"), message);
    internal async Task ExecuteDesktopCommandAsync(int action)
    {
        await desktopRestore;if(quitting)return;
        switch(action)
        {
            case 10:await desktopTools.BeginAsync();break;
            case 11:await desktopTools.ActivateColorAsync();break;
            case 12:await desktopTools.PinClipboardAsync();break;
            case 13:await desktopTools.BeginAsync(ocr:true);break;
            case 14:desktopTools.Pins.ToggleAll();break;
            case 15:desktopTools.Pins.ToggleClickThrough();break;
            case 16:await desktopTools.BeginAsync(mode:"Delayed");break;
            case 17:await desktopTools.BeginAsync(mode:"Screen");break;
            case 18:await desktopTools.BeginAsync(mode:"FullScreen");break;
            case 19:await desktopTools.BeginAsync(mode:"Last");break;
            case 20:await desktopTools.BeginAsync(mode:"Whiteboard");break;
            case 21:await desktopTools.OpenPinsAsync();break;
            case 22:await desktopTools.BeginTranslationAsync();break;
        }
    }
    private void NavigateTo(string target) => Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == target);
    private void ConfigureToolHotkeys()
    {
        toolHotkeyErrors.Clear();
        if (!nativeReady || smokeTest) return;
        var errors=DesktopShortcuts.Register(settings,Native.CB_SetToolHotkey);
        toolHotkeyErrors.AddRange(errors.Select(error=>error.Message));
        bool f1InUse=settings.ToolHotkeysEnabled && DesktopShortcuts.Bindings(settings).Any(binding=>ToolHotkey.TryParse(binding.Shortcut,out var key) && key==new ToolHotkey(0,112));
        var helpShortcut=Root.KeyboardAccelerators.FirstOrDefault(key=>key.Key==VirtualKey.F1);
        if(helpShortcut is not null)helpShortcut.IsEnabled=!f1InUse;
        if (toolsShortcutNotice is not null) { toolsShortcutNotice.Message = string.Join("\n", toolHotkeyErrors); toolsShortcutNotice.IsOpen = toolHotkeyErrors.Count > 0; }
        if(Environment.GetCommandLineArgs().Contains("--diagnostics"))
            File.WriteAllText(Path.Combine(AppPaths.Logs,"claribar-shortcuts-state.json"),JsonSerializer.Serialize(new{capturedAt=DateTimeOffset.Now,enabled=settings.ToolHotkeysEnabled,bindings=DesktopShortcuts.Bindings(settings).Select(binding=>new{action=binding.Action,shortcut=binding.Shortcut}),errors},new JsonSerializerOptions(Wire.Json){WriteIndented=true}));
    }
    private void BuildDesktopTools()
    {
        Notice(L.T("Capture, pick and pin"), L.T("Select a window automatically or drag any region. Captures stay on this computer. Use the tray menu or global shortcuts while settings are hidden."));
        Row(L.T("Screenshot"), L.T("Adjust and annotate directly in the selected region, then copy, save, pin or extract text."), CaptureModeButton(), "\uE722");
        Row(L.T("Screen color picker"), L.T("Floating color preview, wheel magnifier, HEX / RGB / HSL / HSV and editable color history."), Button(L.T("Pick from screen"), () => _ = desktopTools.BeginAsync(color: true)), "\uEF3C");
        Row(L.T("Color editor"), L.T("Edit and copy colors, or reuse a recent color. The same editor is available for taskbar and monitoring colors."), Button(L.T("Open editor"), () => desktopTools.OpenColor()), "\uE790");
        Row(L.T("Pin clipboard image"), L.T("Keep an image on top. Drag to move, wheel to zoom, Ctrl+wheel to adjust opacity, and right-click for actions."), Button(L.T("Pin image"), () => _ = desktopTools.PinClipboardAsync()), "\uE718");
        Row(L.T("Extract text"), L.T("Use your default recognition method for a selected region, screenshot or pinned image."), Button("OCR", () => _ = desktopTools.BeginAsync(ocr: true)), "\uE8D2");
        Row(L.T("Screenshot translation"),L.T("Select a region and translate it into a bilingual window or an overlay. The default shortcut is Ctrl + Alt + A."),Button(L.T("Translate a region"),()=>_=desktopTools.BeginTranslationAsync()),"\uE8F2");
        Row(L.T("Pinned images & groups"),L.T("Restore closed images, switch groups, open image files and manage visibility."),Button(L.T("Manage images"),()=>_=desktopTools.OpenPinsAsync()),"\uE8B7");
        BuildCapturePreferences();
        Heading(L.T("Global shortcuts"));
        Row(L.T("Snipaste shortcuts"),L.T("F1: screenshot · F3: paste · Shift+F3: show or hide all. Close Snipaste or disable its global hotkeys before using the same shortcuts in ClariBar."),
            Button(L.T("Use Snipaste shortcuts"),()=>{DesktopShortcuts.ApplySnipaste(settings);Save();ConfigureToolHotkeys();RenderPage();}),"\uE765");
        Row(L.T("Enable global shortcuts"), L.T("Shortcuts work while ClariBar is in the system tray. Change a shortcut if another application already uses it."), Toggle(settings.ToolHotkeysEnabled, value => { settings.ToolHotkeysEnabled = value; Save(); ConfigureToolHotkeys(); RenderPage(); }), "\uE765");
        void HotkeyRow(string name, string value, Action<string> changed)
        {
            var editor = new TextBox { Text = value, Width = 170, MaxLength = 40, PlaceholderText = L.T("Not assigned") };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor, L.T(name) + " " + L.T("Shortcut"));
            void Commit()
            {
                if (!string.IsNullOrWhiteSpace(editor.Text) && !ToolHotkey.TryParse(editor.Text, out _)) { editor.Text = value; return; }
                value = editor.Text.Trim(); changed(value); Save(); ConfigureToolHotkeys();
            }
            editor.LostFocus += (_, _) => Commit();
            editor.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { Commit(); PageScroll.Focus(FocusState.Programmatic); e.Handled = true; } };
            Row(L.T(name), L.T("Use a function key or Win, Ctrl, Alt or Shift with a letter. Leave blank to disable this shortcut. Press Enter to apply."), editor);
        }
        HotkeyRow("Screenshot", settings.CaptureHotkey, value => settings.CaptureHotkey = value);
        HotkeyRow("Screen color picker", settings.ColorHotkey, value => settings.ColorHotkey = value);
        HotkeyRow("Pin clipboard image", settings.PinHotkey, value => settings.PinHotkey = value);
        HotkeyRow("Extract text", settings.OcrHotkey, value => settings.OcrHotkey = value);
        HotkeyRow("Screenshot translation",settings.TranslationHotkey,value=>settings.TranslationHotkey=value);
        HotkeyRow("Show / hide all",settings.HidePinsHotkey,value=>settings.HidePinsHotkey=value);
        HotkeyRow("Mouse click-through",settings.ClickThroughHotkey,value=>settings.ClickThroughHotkey=value);
        HotkeyRow("Delayed capture",settings.DelayedCaptureHotkey,value=>settings.DelayedCaptureHotkey=value);
        toolsShortcutNotice = Notice(L.T("Shortcut unavailable"), string.Join("\n", toolHotkeyErrors), InfoBarSeverity.Warning);
        toolsShortcutNotice.ActionButton=Button(L.T("Retry shortcuts"),ConfigureToolHotkeys);
        toolsShortcutNotice.IsOpen = toolHotkeyErrors.Count > 0;
        Heading(L.T("Screenshots & OCR"));
        Row(L.T("Screenshot folder"), desktopTools.ScreenshotDirectory, Button(L.T("Choose folder"), () => _ = ChooseScreenshotFolderAsync()), "\uE8B7");
        Row(L.T("Saved images"), L.T("Images and OCR text use the selected folder. Completed captures are saved automatically only when Auto Save is enabled."), Button(L.T("Open folder"), () => OpenFolder(desktopTools.ScreenshotDirectory)), "\uE8B7");
        defaultOcrEngine=new ComboBox {ItemsSource=new[]{L.T("Local recognition (Windows OCR)"),L.T("AI text recognition")},SelectedIndex=settings.OcrDefaultEngine=="Ai"?1:0,MinWidth=190};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(defaultOcrEngine,L.T("Default recognition method"));
        defaultOcrEngine.SelectionChanged+=(_,_)=>{if(defaultOcrEngine.SelectedIndex<0)return;settings.OcrDefaultEngine=defaultOcrEngine.SelectedIndex==1?"Ai":"Local";Save();};
        Row(L.T("Default recognition method"),L.T("New recognition windows use this method. AI recognition sends the selected image to the model service configured below."),defaultOcrEngine);
        string[] languages;
        try { languages = ["Auto", .. OcrService.Languages]; } catch { languages = ["Auto"]; }
        var language = new ComboBox { ItemsSource = languages.Select(x => x == "Auto" ? L.T("Automatic language") : x).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(languages, settings.OcrLanguage)), MinWidth = 170 };
        language.SelectionChanged += (_, _) => { settings.OcrLanguage = languages[Math.Max(0, language.SelectedIndex)]; Save(); };
        Row(L.T("Local OCR language"), languages.Length > 1 ? L.T("Installed Windows OCR languages. Automatic follows the system language when available.") : L.T("No local OCR language is installed. Add an OCR language in Windows Settings."), language, "\uE774");
        BuildTranslationSettings();
        BuildAiOcrSettings();
    }
    private async Task ChooseScreenshotFolderAsync()
    {
        try
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            if (await picker.PickSingleFolderAsync() is { } folder) { settings.ScreenshotDirectory = folder.Path; Save(); if (page == "tools") RenderPage(); }
        }
        catch (Exception ex) { await ToolErrorAsync(ex.Message); }
    }
    private void BuildAiOcrSettings()=>PageContent.Children.Add(new AiOcrSettingsPanel(settings,Save));
}
