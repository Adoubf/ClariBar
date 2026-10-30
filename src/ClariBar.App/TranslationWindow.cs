using ClariBar.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed class TranslationWindow:Window
{
    private readonly DesktopToolsController tools;
    private readonly PixelFrame frame;
    private readonly PixelRect position;
    private readonly bool overlay;
    private readonly CancellationTokenSource cancellation=new();
    private readonly Grid root=new(){RowSpacing=12};
    private readonly TextBlock status=ToolUi.Label("");
    private readonly TextBox? original,translated;
    private readonly Canvas masks=new();
    private readonly Grid comparison=new(){ColumnSpacing=16,RowSpacing=12};
    private readonly ComboBox engines=new(){MinWidth=230,Header=L.T("Translation engine")};
    private readonly ComboBox? source,target;
    private readonly Button translateButton;
    private readonly Button switchButton;
    private TranslationToolbarWindow? toolbar;
    private TranslationDocument? document;
    private string sourceText="";
    private bool closed,running,started,showOriginal;
    internal Task InitialTranslation {get;private set;}=Task.CompletedTask;
    internal TranslationDocument? Result=>document;
    internal bool IsOverlay=>overlay;
    internal Canvas TranslationMasks=>masks;
    internal TranslationWindow(DesktopToolsController tools,PixelFrame frame,PixelRect position,bool overlay,TranslationDocument? document=null,bool startAutomatically=true,string? initialStatus=null,string? initialSource=null)
    {
        this.tools=tools;this.frame=frame;this.position=position;this.overlay=overlay;this.document=document;
        // Keep the overlay's text in the document. Unattached TextBoxes can keep
        // native text services alive past their XAML window during shutdown.
        if(!overlay)
        {
            original=new(){AcceptsReturn=true,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Header=L.T("Original text"),HorizontalAlignment=HorizontalAlignment.Stretch};
            translated=new(){AcceptsReturn=true,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Header=L.T("Translation"),HorizontalAlignment=HorizontalAlignment.Stretch};
            source=TranslationLanguages.Create(tools.Settings.TranslationSourceLanguage,true,230);source.Header=L.T("Source language");
            target=TranslationLanguages.Create(tools.Settings.TranslationTargetLanguage,false,230);target.Header=L.T("Target language");
        }
        translateButton=ToolUi.Button("Translate",()=>{});translateButton.Click+=async(_,_)=>await RunAsync();
        switchButton=ToolUi.Button(overlay?"Bilingual comparison":"Show over captured region",SwitchMode);switchButton.IsEnabled=document is not null || !startAutomatically;
        root.KeyDown+=(_,e)=>{if(e.Key==VirtualKey.Escape){Close();e.Handled=true;}};
        if(overlay)BuildOverlay();else BuildComparison();
        Content=root;ToolUi.Configure(this,tools,"Screenshot translation",overlay?Math.Max(1,position.Width):960,overlay?Math.Max(1,position.Height):700,overlay);
        if(overlay)
        {
            ToolWindowGeometry.Borderless(this);AppWindow.MoveAndResize(new(position.X,position.Y,position.Width,position.Height));
            toolbar=new(this,tools,BuildOverlayActions());
            tools.Track(toolbar);
            AppWindow.Changed+=(_,_)=>toolbar?.Reposition();
        }
        root.Loaded+=async(_,_)=>
        {
            if(started)return;started=true;
            if(overlay){AppWindow.MoveAndResize(new(position.X,position.Y,position.Width,position.Height));toolbar?.Activate();toolbar?.Reposition();}
            if(document is not null){ShowDocument();return;}
            if(!startAutomatically){status.Text=initialStatus??"";SetSource(initialSource??"");return;}
            InitialTranslation=RunAsync(saveOptions:false);await InitialTranslation;
        };
        Closed+=(_,_)=>{closed=true;cancellation.Cancel();toolbar?.CloseWithOwner();cancellation.Dispose();};
    }
    private void BuildComparison()
    {
        root.Padding=new(20);foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})root.RowDefinitions.Add(new(){Height=height});
        root.Children.Add(ToolUi.Label(L.T("Screenshot translation"),24));
        engines.ItemsSource=new[]{L.T("Free online (MyMemory)"),L.T("Offline Chinese ↔ English"),L.T("AI translation")};engines.SelectedIndex=tools.Settings.TranslationEngine switch{"Local"=>1,"Ai"=>2,_=>0};
        var options=new VariableSizedWrapGrid{Orientation=Orientation.Horizontal,MaximumRowsOrColumns=3,ItemWidth=260,ItemHeight=72};options.Children.Add(engines);options.Children.Add(source!);options.Children.Add(target!);Grid.SetRow(options,1);root.Children.Add(options);
        var disclosure=ToolUi.Label(L.T("Chinese ↔ English is automatic by default. Free online and AI send only recognized text to their service. Offline translation stays on this computer."),12);Grid.SetRow(disclosure,2);root.Children.Add(disclosure);
        comparison.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});comparison.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        comparison.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});comparison.RowDefinitions.Add(new(){Height=new(0)});
        comparison.Children.Add(original!);Grid.SetColumn(translated!,1);comparison.Children.Add(translated!);Grid.SetRow(comparison,3);root.Children.Add(comparison);
        comparison.SizeChanged+=(_,_)=>
        {
            bool narrow=comparison.ActualWidth<660;comparison.ColumnDefinitions[1].Width=narrow?new(0):new(1,GridUnitType.Star);comparison.RowDefinitions[1].Height=narrow?new(1,GridUnitType.Star):new(0);
            Grid.SetColumn(translated!,narrow?0:1);Grid.SetRow(translated!,narrow?1:0);
        };
        var bottom=new StackPanel{Spacing=8};var actions=new VariableSizedWrapGrid{Orientation=Orientation.Horizontal,MaximumRowsOrColumns=4,ItemWidth=190,ItemHeight=44};
        actions.Children.Add(translateButton);actions.Children.Add(switchButton);actions.Children.Add(ToolUi.AsyncButton("Copy translation",()=>document is null?Task.CompletedTask:ToolUi.CopyTextAsync(document.TargetText)));
        actions.Children.Add(ToolUi.AsyncButton("Copy bilingual text",()=>document is null?Task.CompletedTask:ToolUi.CopyTextAsync(string.Join("\n\n",document.Lines.Select(line=>line.Source+"\n"+line.Target)))));
        bottom.Children.Add(actions);bottom.Children.Add(status);Grid.SetRow(bottom,4);root.Children.Add(bottom);
    }
    private void BuildOverlay()
    {
        root.Children.Add(new Image{Source=frame.Bitmap(),Stretch=Stretch.Fill});masks.IsHitTestVisible=false;root.Children.Add(masks);
        var message=new Border{Background=ToolUi.Brush("#20252D"),CornerRadius=new(8),Padding=new(12),Margin=new(8),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left};status.Foreground=ToolUi.Brush("#FFFFFF");message.Child=status;root.Children.Add(message);
        root.SizeChanged+=(_,_)=>DrawMasks();
        var menu=new MenuFlyout();var close=new MenuFlyoutItem{Text=L.T("Close")};close.Click+=(_,_)=>Close();menu.Items.Add(close);root.ContextFlyout=menu;
    }
    private FrameworkElement BuildOverlayActions()
    {
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};actions.Children.Add(switchButton);
        var originalButton=ToolUi.Button("Show original",()=>{});originalButton.Click+=(_,_)=>{showOriginal=!showOriginal;originalButton.Content=L.T(showOriginal?"Show translation":"Show original");masks.Visibility=showOriginal?Visibility.Collapsed:Visibility.Visible;};actions.Children.Add(originalButton);
        actions.Children.Add(ToolUi.AsyncButton("Copy translation",()=>document is null?Task.CompletedTask:ToolUi.CopyTextAsync(document.TargetText)));
        actions.Children.Add(ToolUi.Button("Close",Close));return actions;
    }
    internal async Task RunAsync(bool saveOptions=true)
    {
        if(running || closed)return;
        if(saveOptions && !overlay)
        {
            tools.Settings.TranslationEngine=engines.SelectedIndex switch{1=>"Local",2=>"Ai",_=>"Free"};
            tools.Settings.TranslationSourceLanguage=TranslationLanguages.Read(source!,true);tools.Settings.TranslationTargetLanguage=TranslationLanguages.Read(target!,false);tools.Save();
        }
        running=true;translateButton.IsEnabled=switchButton.IsEnabled=false;SetOptionsEnabled(false);
        document=null;if(translated is not null)translated.Text="";masks.Children.Clear();if(overlay && status.Parent is Border badge)badge.Visibility=Visibility.Visible;
        try
        {
            var queue=DispatcherQueue;
            document=await TranslationService.TranslateAsync(frame,tools.Settings,text=>queue.TryEnqueue(()=>{if(!closed && running)status.Text=text;}),cancellation.Token,
                lines=>{if(!closed)SetSource(string.Join(Environment.NewLine,lines.Select(line=>line.Source)));});
            if(!closed)ShowDocument();
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(!closed)status.Text=ex.Message;}
        finally{running=false;if(!closed){translateButton.IsEnabled=switchButton.IsEnabled=true;SetOptionsEnabled(true);}}
    }
    private void SetSource(string text){sourceText=text;if(original is not null)original.Text=text;}
    private void SetOptionsEnabled(bool value){engines.IsEnabled=value;if(source is not null)source.IsEnabled=value;if(target is not null)target.IsEnabled=value;}
    private void ShowDocument()
    {
        if(document is null)return;SetSource(document.SourceText);if(translated is not null)translated.Text=document.TargetText;
        status.Text=L.F("{0} → {1} · {2}",document.SourceLanguage,document.TargetLanguage,L.T(document.Engine switch{"Ai"=>"AI translation","Local"=>"Offline Chinese ↔ English",_=>"Free online (MyMemory)"}));
        if(overlay){DrawMasks();if(status.Parent is Border badge)badge.Visibility=Visibility.Collapsed;}switchButton.IsEnabled=true;
    }
    private void DrawMasks()
    {
        if(!overlay || document is null || root.ActualWidth<=0 || root.ActualHeight<=0)return;masks.Children.Clear();
        double sx=root.ActualWidth/frame.Width,sy=root.ActualHeight/frame.Height;
        for(int index=0;index<document.Lines.Length;index++)
        {
            var line=document.Lines[index];var a=line.Bounds;
            double left=Math.Max(0,(a.X-3)*sx),top=Math.Max(0,(a.Y-2)*sy);
            double width=Math.Min(root.ActualWidth-left,Math.Max(a.Width*sx+8,Math.Min(root.ActualWidth-left,180*sx)));
            // Extend only into empty space before the next OCR line. The original
            // screenshot remains visible around translated text blocks.
            double next=document.Lines.Skip(index+1).Where(other=>other.Bounds.Y>=a.Bottom-2 && other.Bounds.X<a.Right+20 && other.Bounds.Right>a.X).Select(other=>(other.Bounds.Y-2)*sy).DefaultIfEmpty(Math.Min(root.ActualHeight,(a.Bottom+a.Height)*sy)).Min();
            double height=Math.Max(6,Math.Min(root.ActualHeight-top,Math.Max(a.Height*sy+4,next-top)));
            var color=frame.ColorAt(Math.Clamp(a.X-2,0,frame.Width-1),Math.Clamp(a.Y-2,0,frame.Height-1));
            var text=new TextBlock{Text=line.Target,TextWrapping=TextWrapping.Wrap,FontFamily=new("Microsoft YaHei UI"),FontSize=Math.Clamp((line.TextHeight>0?line.TextHeight:a.Height)*sy*.82,8,32),Foreground=ToolUi.Brush((color.R*.299+color.G*.587+color.B*.114)>145?"#151515":"#FFFFFF")};
            text.Measure(new(Math.Max(1,width-4),double.PositiveInfinity));
            while(text.DesiredSize.Height>height-2 && text.FontSize>7){text.FontSize-=.5;text.Measure(new(Math.Max(1,width-4),double.PositiveInfinity));}
            double coverHeight=Math.Min(height,Math.Max(a.Height*sy+4,text.DesiredSize.Height+2));
            var cover=new Border{Width=Math.Max(1,width),Height=Math.Max(1,coverHeight),Background=ToolUi.Brush(color.Hex),Padding=new(2,0,2,0),Child=text};
            ToolTipService.SetToolTip(cover,line.Source+"\n"+line.Target);Canvas.SetLeft(cover,left);Canvas.SetTop(cover,top);masks.Children.Add(cover);
        }
    }
    private void SwitchMode()
    {
        if(running || closed)return;tools.Settings.TranslationDisplay=overlay?"Bilingual":"Overlay";tools.Save();
        var replacement=new TranslationWindow(tools,frame,position,!overlay,document,startAutomatically:false,initialStatus:status.Text,initialSource:sourceText);tools.Track(replacement);replacement.Activate();Close();
    }
}

internal sealed class TranslationToolbarWindow:Window
{
    private readonly Window owner;
    private readonly Border surface;
    private bool closed,disposing;
    internal TranslationToolbarWindow(Window owner,DesktopToolsController tools,FrameworkElement actions)
    {
        this.owner=owner;surface=new(){Padding=new(8),CornerRadius=new(10),Child=actions};Content=surface;
        surface.KeyDown+=(_,e)=>{if(e.Key==VirtualKey.Escape){owner.Close();e.Handled=true;}};
        void UpdateSurface()=>surface.Background=ToolUi.Brush(surface.ActualTheme==ElementTheme.Dark?"#20252D":"#F4F4F4");
        surface.ActualThemeChanged+=(_,_)=>UpdateSurface();surface.Loaded+=(_,_)=>UpdateSurface();UpdateSurface();
        ToolUi.Configure(this,tools,"Screenshot translation",620,52,true);ToolWindowGeometry.Borderless(this,owner);SystemBackdrop=new TransparentToolBackdrop();
        surface.Loaded+=(_,_)=>Reposition();Closed+=(_,_)=>{closed=true;if(!disposing)owner.Close();};
    }
    internal void Reposition()
    {
        if(closed)return;var image=ToolWindowGeometry.ClientBounds(owner);var monitor=DisplayArea.GetFromPoint(new(image.X+image.Width/2,image.Y+image.Height/2),DisplayAreaFallback.Nearest).WorkArea;
        double dpi=DesktopWindowDpi.For(WinRT.Interop.WindowNative.GetWindowHandle(this))/96d;
        surface.Measure(new(double.PositiveInfinity,double.PositiveInfinity));ToolWindowGeometry.ResizeClient(this,Math.Min(monitor.Width,(int)Math.Ceiling(surface.DesiredSize.Width*dpi)),(int)Math.Ceiling(surface.DesiredSize.Height*dpi));
        int x=Math.Clamp(image.X,monitor.X,Math.Max(monitor.X,monitor.X+monitor.Width-AppWindow.Size.Width));int y=image.Bottom+4;
        if(y+AppWindow.Size.Height>monitor.Y+monitor.Height)y=Math.Max(monitor.Y,image.Y-AppWindow.Size.Height-4);AppWindow.Move(new(x,y));
    }
    internal void CloseWithOwner(){if(closed)return;disposing=true;Close();}
}
