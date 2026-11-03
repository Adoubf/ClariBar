using ClariBar.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using L=ClariBar.Core.Localizer;
using System.Runtime.InteropServices;

namespace ClariBar.App;

internal sealed class CopyTranslationWindow:Window
{
    private readonly DesktopToolsController tools;
    private readonly Func<string,CancellationToken,Task<string>>? translator;
    private readonly System.Net.Http.HttpMessageHandler? translationHandler;
    private readonly Border surface=new(){CornerRadius=new(12),BorderThickness=new(1),Padding=new(16,12,12,12)};
    private readonly TextBlock source=new(){TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=2,FontSize=14,LineHeight=21,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold};
    private readonly TextBlock result=new(){TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,FontSize=14,LineHeight=23};
    private readonly TextBlock engine=ToolUi.Label("",11),direction=ToolUi.Label("",11);
    private readonly TextBlock failure=ToolUi.Label("",12);
    private readonly ProgressRing progress=new(){Width=14,Height=14,IsActive=true};
    private readonly ScrollViewer scroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new(0,0,5,0)};
    private readonly DispatcherTimer dismiss=new(){Interval=TimeSpan.FromMilliseconds(50)};
    private readonly CopyPopupDismissal movement=new();
    private readonly Button copy,retry;
    private CancellationTokenSource? pending;
    private string text="";
    private int cursorX,cursorY,generation;
    private bool closed,placing,mouseDown,watching;
    private readonly nint windowHandle;
    private readonly DispatcherTimer streamTimer=new(){Interval=TimeSpan.FromMilliseconds(60)};
    private string? pendingText;
    internal int StreamingEvents {get;private set;}
    internal int ProgressiveRenders {get;private set;}
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    private static bool Down(VirtualKey key)=>(GetAsyncKeyState((int)key)&0x8000)!=0;
    internal string SourceText=>text;
    internal string TranslatedText=>copy.IsEnabled?result.Text:"";
    internal string DisplayedText=>result.Text;
    internal bool IsRunning=>progress.IsActive;
    internal Task TranslationTask {get;private set;}=Task.CompletedTask;
    internal CopyTranslationWindow(DesktopToolsController tools,Func<string,CancellationToken,Task<string>>? translator=null,System.Net.Http.HttpMessageHandler? translationHandler=null)
    {
        this.tools=tools;this.translator=translator;
        this.translationHandler=translationHandler;streamTimer.Tick+=OnStreamTick;
        var layout=new StackPanel{Spacing=10};
        var header=new Grid{ColumnSpacing=8};header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        header.Children.Add(source);var close=IconButton("\uE8BB","Close",Close);Grid.SetColumn(close,1);header.Children.Add(close);layout.Children.Add(header);
        layout.Children.Add(new Border{Height=1,Background=ToolUi.Brush("#DDE2E7"),Opacity=.55});
        var metadata=new Grid{ColumnSpacing=8};metadata.ColumnDefinitions.Add(new(){Width=GridLength.Auto});metadata.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});metadata.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var label=ToolUi.Label(L.T("Translation"),12);label.Opacity=.65;metadata.Children.Add(label);Grid.SetColumn(engine,1);engine.Opacity=.6;engine.HorizontalAlignment=HorizontalAlignment.Right;metadata.Children.Add(engine);Grid.SetColumn(progress,2);metadata.Children.Add(progress);layout.Children.Add(metadata);
        scroll.Content=result;layout.Children.Add(scroll);
        failure.Visibility=Visibility.Collapsed;failure.Opacity=.8;failure.MaxLines=3;failure.TextTrimming=TextTrimming.CharacterEllipsis;layout.Children.Add(failure);
        var footer=new Grid{ColumnSpacing=6};footer.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        direction.Opacity=.65;direction.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(direction);
        copy=IconButton("\uE8C8","Copy translation",()=>_ = CopyAsync());copy.IsEnabled=false;Grid.SetColumn(copy,1);footer.Children.Add(copy);
        retry=IconButton("\uE72C","Translate again",()=>_ = RetryAsync());Grid.SetColumn(retry,2);footer.Children.Add(retry);layout.Children.Add(footer);
        surface.Child=layout;Content=surface;ToolUi.Configure(this,tools,"Translate on copy",344,220,true);ToolWindowGeometry.Borderless(this);SystemBackdrop=new TransparentToolBackdrop();
        AppWindow.IsShownInSwitchers=false;
        windowHandle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        surface.ActualThemeChanged+=OnThemeChanged;surface.Loaded+=OnLoaded;ApplyTheme();
        surface.KeyDown+=OnKeyDown;dismiss.Tick+=OnDismissTick;Closed+=OnClosed;
    }
    private void ApplyTheme()
    {if(closed)return;bool dark=surface.ActualTheme==ElementTheme.Dark;surface.Background=ToolUi.Brush(dark?"#232A31":"#FFFFFF");surface.BorderBrush=ToolUi.Brush(dark?"#46515D":"#D7DFE5");result.Foreground=ToolUi.Brush(dark?"#EEF3F7":"#152E42");}
    private void OnThemeChanged(FrameworkElement sender,object args)=>ApplyTheme();
    private void OnLoaded(object sender,RoutedEventArgs args){surface.Loaded-=OnLoaded;ApplyTheme();Place();}
    private void OnKeyDown(object sender,Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {if(args.Key==VirtualKey.Escape){Close();args.Handled=true;}}
    private void OnStreamTick(object? sender,object args)=>FlushProgress();
    private void OnDismissTick(object? sender,object args)
    {
        if(closed)return;
        bool down=Down(VirtualKey.LeftButton)||Down(VirtualKey.RightButton);
        if(Native.CB_PollCopyPopup(windowHandle)!=0 || Down(VirtualKey.Escape)){Close();return;}
        if(DesktopCapture.GetCursorPos(out var point))
        {
            if(!watching && down && !mouseDown && !ToolWindowGeometry.ClientBounds(this).Contains(point.X,point.Y)){Close();return;}
            ObservePointer(point.X,point.Y,down,Environment.TickCount64);
        }
        mouseDown=down;
    }
    private void OnClosed(object sender,WindowEventArgs args)
    {
        closed=true;generation++;pending?.Cancel();dismiss.Stop();streamTimer.Stop();pendingText=null;
        Native.CB_UnwatchCopyPopup(windowHandle);
        // WinRT timers and event sources can hold their delegates after Stop.
        // Explicitly detach callbacks so closed cards can be collected.
        dismiss.Tick-=OnDismissTick;streamTimer.Tick-=OnStreamTick;surface.Loaded-=OnLoaded;
        surface.ActualThemeChanged-=OnThemeChanged;surface.KeyDown-=OnKeyDown;Closed-=OnClosed;
    }
    private static Button IconButton(string glyph,string label,Action action)
    {
        var button=ToolUi.Button(label,action,label);button.Content=new FontIcon{Glyph=glyph,FontSize=14};button.Padding=new(7);button.MinWidth=28;button.Height=30;button.CornerRadius=new(6);button.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);button.BorderThickness=new(0);
        return button;
    }
    internal Task ShowTextAsync(string value,int x,int y)
    {
        if(closed)return Task.CompletedTask;
        text=value;source.Text=value;ToolTipService.SetToolTip(source,value.Length>500?value[..500]+"…":value);cursorX=x;cursorY=y;
        AppWindow.Move(new(x,y));Place();AppWindow.Show(false);movement.Reset();watching=Native.CB_WatchCopyPopup(windowHandle)==0;
        mouseDown=Down(VirtualKey.LeftButton)||Down(VirtualKey.RightButton);dismiss.Start();
        return RetryAsync();
    }
    private void ObservePointer(int x,int y,bool dragging,long milliseconds)
    {
        if(!closed && movement.Observe(ToolWindowGeometry.ClientBounds(this),cursorX,cursorY,x,y,DesktopWindowDpi.For(windowHandle)/96d,dragging,milliseconds))Close();
    }
    internal Task RetryAsync(){TranslationTask=RunAsync();return TranslationTask;}
    private async Task RunAsync()
    {
        if(closed || text.Length==0)return;
        pending?.Cancel();using var request=new CancellationTokenSource();pending=request;int current=++generation;
        streamTimer.Stop();pendingText=null;StreamingEvents=ProgressiveRenders=0;failure.Visibility=Visibility.Collapsed;failure.Text="";scroll.ChangeView(null,0,null);
        string input=text;result.Text=L.T("Translating…");progress.IsActive=true;progress.Visibility=Visibility.Visible;copy.IsEnabled=false;retry.IsEnabled=false;
        engine.Text=L.T(tools.Settings.TranslationEngine switch{"Ai"=>"AI translation","Local"=>"Offline Chinese ↔ English",_=>"Free online (MyMemory)"});
        string target=TranslationProtocol.ResolveTargetLanguage(tools.Settings.TranslationTargetLanguage,input);
        string sourceLanguage=tools.Settings.TranslationSourceLanguage=="Auto"?TranslationProtocol.DetectLanguage(input):tools.Settings.TranslationSourceLanguage;
        direction.Text=L.T(TranslationProtocol.LanguageName(sourceLanguage))+" → "+L.T(TranslationProtocol.LanguageName(target));Place();
        try
        {
            if(input.Length>CopyTranslation.MaximumCharacters)throw new InvalidOperationException(L.T("The copied text is too long. Copy up to 20,000 characters."));
            string output;
            if(translator is not null)output=await translator(input,request.Token);
            else
            {
                var paragraphs=TranslationProtocol.TextParagraphs(input);var pieces=new string[paragraphs.Length];var synchronization=new object();var queue=DispatcherQueue;
                var translated=await TranslationService.TranslateLinesAsync(paragraphs,tools.Settings.TranslationSourceLanguage,target,tools.Settings.TranslationEngine,tools.Settings,request.Token,handler:translationHandler,
                    updated:update=>
                    {
                        lock(synchronization)
                        {
                            pieces[update.Index]=update.Text;string snapshot=string.Join(Environment.NewLine+Environment.NewLine,pieces.Select(part=>part??""));
                            queue.TryEnqueue(()=>{if(closed || current!=generation || request.IsCancellationRequested || !progress.IsActive)return;if(!update.Complete)StreamingEvents++;pendingText=snapshot;if(!streamTimer.IsEnabled)streamTimer.Start();});
                        }
                    });
                output=string.Join(Environment.NewLine+Environment.NewLine,translated);
            }
            if(closed || current!=generation || request.IsCancellationRequested)return;
            if(string.IsNullOrWhiteSpace(output))throw new IOException(L.T("The translation service returned no text."));
            streamTimer.Stop();pendingText=null;
            result.Text=output;copy.IsEnabled=true;
        }
        catch(OperationCanceledException)when(request.IsCancellationRequested){}
        catch(Exception ex)
        {
            if(!closed && current==generation)
            {
                FlushProgress();
                if(copy.IsEnabled){failure.Text=L.T("Translation interrupted. Received text is kept.")+"\n"+ex.Message;ToolTipService.SetToolTip(failure,failure.Text);failure.Visibility=Visibility.Visible;}
                else result.Text=ex.Message;
            }
        }
        finally
        {
            if(!closed && current==generation){streamTimer.Stop();pendingText=null;progress.IsActive=false;progress.Visibility=Visibility.Collapsed;retry.IsEnabled=true;Place();}
            if(ReferenceEquals(pending,request))pending=null;
        }
    }
    private void FlushProgress()
    {
        streamTimer.Stop();if(closed || pendingText is null)return;
        result.Text=pendingText;pendingText=null;copy.IsEnabled=!string.IsNullOrWhiteSpace(result.Text);ProgressiveRenders++;Place();
    }
    internal async Task CopyAsync()
    {
        if(closed || !copy.IsEnabled)return;
        try{await ToolUi.CopyTextAsync(result.Text);if(!closed)direction.Text=L.T("Copied to clipboard");}
        catch(Exception ex){if(!closed)direction.Text=ex.Message;}
    }
    private void Place()
    {
        if(closed || placing)return;placing=true;
        try
        {
            var area=DisplayArea.GetFromPoint(new(cursorX,cursorY),DisplayAreaFallback.Nearest).WorkArea;
            double dpi=DesktopWindowDpi.For(WinRT.Interop.WindowNative.GetWindowHandle(this))/96d;
            double width=Math.Min(344,Math.Max(1,area.Width/dpi-16));surface.Width=width;
            scroll.MaxHeight=Math.Max(32,Math.Min(340,area.Height/dpi-(failure.Visibility==Visibility.Visible?230:160)));
            surface.Measure(new Size(width,double.PositiveInfinity));
            var placed=CopyTranslation.Place(new(area.X,area.Y,area.Width,area.Height),cursorX,cursorY,(int)Math.Ceiling(width*dpi),(int)Math.Ceiling(surface.DesiredSize.Height*dpi),(int)Math.Ceiling(12*dpi));
            if(AppWindow.Position.X!=placed.X || AppWindow.Position.Y!=placed.Y || AppWindow.Size.Width!=placed.Width || AppWindow.Size.Height!=placed.Height)
                AppWindow.MoveAndResize(new(placed.X,placed.Y,placed.Width,placed.Height));
        }
        finally{placing=false;}
    }
}
