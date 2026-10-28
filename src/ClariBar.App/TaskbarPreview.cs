using L = ClariBar.Core.Localizer;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace ClariBar.App;

// A live WinUI preview built from the same metric/configuration data as the
// Explorer adapter. System glyphs illustrate their configured foregrounds.
internal sealed class TaskbarPreview : Grid
{
    private readonly Grid metrics = new() { VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(4,0,6,0) };
    private readonly Grid bar = new() { Height=48, Padding=new Thickness(12,0,12,0), ColumnSpacing=12 };
    private readonly StackPanel tray = new() { Orientation=Orientation.Horizontal,Spacing=12,VerticalAlignment=VerticalAlignment.Center };
    private readonly TextBlock time = new() { FontSize=12,HorizontalAlignment=HorizontalAlignment.Center };
    private readonly TextBlock date = new() { FontSize=11,HorizontalAlignment=HorizontalAlignment.Center };
    private readonly TextBlock network = Glyph("\uE701"),volume=Glyph("\uE767"),input=new(){Text=L.T("EN"),FontSize=13,VerticalAlignment=VerticalAlignment.Center};
    private bool dark = true;
    private AppSettings? currentSettings;
    private TaskbarConfig currentConfig;
    public int MetricCount => metrics.Children.Count;
    public double TaskbarHeight=>bar.Height;
    public double UpShift {get;private set;}
    public IEnumerable<TextBlock> MetricTexts => metrics.Children.OfType<Canvas>().SelectMany(cell=>cell.Children.OfType<TextBlock>());

    public TaskbarPreview()
    {
        MinHeight=170;RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});RowDefinitions.Add(new(){Height=GridLength.Auto});
        var desktop = new StackPanel { Margin=new Thickness(22,20,22,20),Spacing=6,VerticalAlignment=VerticalAlignment.Center };
        desktop.Children.Add(new TextBlock {Text=L.T("Your desktop"),FontSize=17,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Foreground=Solid("#FFFFFF")});
        desktop.Children.Add(new TextBlock {Text=L.T("Live readings · Current layout and text style"),FontSize=12,Foreground=Solid("#DAE6EE")});Children.Add(desktop);
        bar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});bar.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});bar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var apps = new StackPanel {Orientation=Orientation.Horizontal,Spacing=16,VerticalAlignment=VerticalAlignment.Center};
        var start=new Grid {Width=18,Height=18,RowSpacing=2,ColumnSpacing=2};
        for(int i=0;i<2;i++){start.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});start.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});}
        for(int i=0;i<4;i++){var tile=new Border {Background=Solid("#49AAEE")};Grid.SetRow(tile,i/2);Grid.SetColumn(tile,i%2);start.Children.Add(tile);}apps.Children.Add(start);
        foreach(string glyph in new[]{"\uE721","\uE8B7"}){var icon=Glyph(glyph);icon.FontSize=17;apps.Children.Add(icon);}bar.Children.Add(apps);
        tray.Children.Add(metrics);tray.Children.Add(network);tray.Children.Add(volume);tray.Children.Add(input);
        var clock=new StackPanel {Spacing=1,VerticalAlignment=VerticalAlignment.Center};clock.Children.Add(time);clock.Children.Add(date);tray.Children.Add(clock);
        Grid.SetColumn(tray,2);bar.Children.Add(tray);Grid.SetRow(bar,1);Children.Add(bar);
        SizeChanged+=(_,_)=>{if(currentSettings is not null)Update(currentSettings,currentConfig);};
        SetDesktop(true);
    }
    private static TextBlock Glyph(string value)=>new(){Text=value,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=14,VerticalAlignment=VerticalAlignment.Center};
    private static SolidColorBrush Solid(string value,byte alpha=255)=>new(Color.FromArgb(alpha,Convert.ToByte(value.Substring(1,2),16),Convert.ToByte(value.Substring(3,2),16),Convert.ToByte(value.Substring(5,2),16)));
    private static SolidColorBrush Argb(uint value)=>new(Color.FromArgb((byte)(value>>24),(byte)(value>>16),(byte)(value>>8),(byte)value));
    public void SetDesktop(bool isDark)
    {
        dark=isDark;
        Background=new LinearGradientBrush {StartPoint=new Point(0,0),EndPoint=new Point(1,1),GradientStops=new GradientStopCollection
        {
            new(){Color=isDark?Color.FromArgb(255,18,45,67):Color.FromArgb(255,121,163,195),Offset=0},
            new(){Color=isDark?Color.FromArgb(255,27,96,108):Color.FromArgb(255,204,220,231),Offset=1}
        }};
        if(currentSettings is not null)Update(currentSettings,currentConfig);
    }
    public void Update(AppSettings settings,TaskbarConfig config)
    {
        currentSettings=settings;currentConfig=config;
        string system=dark?"#F5F5F5":"#1D2935";
        bar.Background=config.BackgroundMode==0?Solid(dark?"#20262D":"#EEF2F5",235):Argb(config.BackgroundArgb);
        foreach(var icon in ((StackPanel)bar.Children[0]).Children.OfType<TextBlock>())icon.Foreground=Solid(system);
        time.Text=DateTime.Now.ToString("HH:mm");date.Text=DateTime.Now.ToString("yyyy/MM/dd");
        time.Foreground=Solid(settings.TimeColorEnabled?settings.TimeColor:system);date.Foreground=Solid(settings.DateColorEnabled?settings.DateColor:system);
        network.Foreground=Solid(settings.NetworkColorEnabled?settings.NetworkColor:settings.TrayColorEnabled?settings.TrayColor:system);
        volume.Foreground=Solid(settings.VolumeColorEnabled?settings.VolumeColor:settings.TrayColorEnabled?settings.TrayColor:system);
        input.Foreground=Solid(settings.InputColorEnabled?settings.InputColor:settings.TrayColorEnabled?settings.TrayColor:system);
        metrics.Children.Clear();metrics.ColumnDefinitions.Clear();metrics.RowDefinitions.Clear();
        int count=(int)config.MetricCount;
        int columns=Math.Max(1,config.Layout==1?2:config.Layout==2?(count+1)/2:count),rows=Math.Max(1,(count+columns-1)/columns);
        double size=settings.MonitorFontSize,available=Native.TaskbarHeight();bar.Height=available;
        const double gap=4;
        var widths=Enumerable.Repeat(16d,columns).ToArray();
        var heights=new double[rows];var above=new double[rows];var below=new double[rows];
        var texts=new List<TextBlock>();
        for(int i=0;i<rows;i++)metrics.RowDefinitions.Add(new(){Height=GridLength.Auto});
        for(int i=0;i<Math.Max(count,1);i++)
        {
            var item=new TextBlock {Text=count==0?L.T("Waiting for monitoring data"):config.Metrics[i].Text,FontSize=size,FontFamily=new FontFamily(settings.MonitorFontFamily),
                FontWeight=settings.MonitorBold?Microsoft.UI.Text.FontWeights.Bold:Microsoft.UI.Text.FontWeights.Normal,
                Foreground=count==0?Solid(system):Argb(config.Metrics[i].Color),TextWrapping=TextWrapping.NoWrap,
                TextTrimming=TextTrimming.CharacterEllipsis,TextLineBounds=TextLineBounds.Full,MaxLines=1};
            var cell=new Canvas();cell.Children.Add(item);texts.Add(item);
            Grid.SetColumn(cell,i%columns);Grid.SetRow(cell,i/columns);metrics.Children.Add(cell);
        }
        double contentHeight;
        while(true)
        {
            Array.Fill(widths,16d);Array.Clear(above);Array.Fill(below,-1000d);
            for(int i=0;i<texts.Count;i++)
            {
                var item=texts[i];item.FontSize=size;item.Width=double.NaN;
                item.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                widths[i%columns]=Math.Max(widths[i%columns],Math.Ceiling((item.DesiredSize.Width+12)/4)*4);
                var ink=Native.MeasureInk(item);
                above[i/columns]=Math.Max(above[i/columns],ink.Above);below[i/columns]=Math.Max(below[i/columns],ink.Below);
            }
            for(int i=0;i<rows;i++)heights[i]=Math.Max(0,Math.Ceiling(above[i]+below[i]));
            contentHeight=heights.Sum()+gap*(rows-1);
            if(contentHeight<=available-4 || size<=7)break;
            size-=.5;
        }
        double padding=Math.Max(0,(available-contentHeight)/2);
        double up=rows>1?Math.Min(2,Math.Max(0,padding-2)):0;
        UpShift=up;
        metrics.Height=available;metrics.Padding=new Thickness(0,padding-up,0,padding+up);metrics.RowSpacing=gap;
        for(int i=0;i<rows;i++)metrics.RowDefinitions[i].Height=new GridLength(heights[i]);
        if(count==0)widths[0]=100;
        bool narrow=ActualWidth>0 && ActualWidth<580;
        bar.Children[0].Visibility=narrow?Visibility.Collapsed:Visibility.Visible;
        tray.Spacing=narrow?8:12;bar.ColumnSpacing=narrow?8:12;
        bar.Padding=narrow?new Thickness(8,0,8,0):new Thickness(12,0,12,0);
        double natural=widths.Sum(),limit=Math.Max(100,(ActualWidth>100?ActualWidth:850)-(narrow?180:300));
        double width=Math.Min(limit,settings.MonitorAutoWidth?natural:settings.MonitorWidth);
        foreach(double cell in widths)metrics.ColumnDefinitions.Add(new(){Width=new GridLength(settings.MonitorAutoWidth?cell*width/natural:width/columns)});
        for(int i=0;i<texts.Count;i++)
        {
            texts[i].Width=Math.Max(1,metrics.ColumnDefinitions[i%columns].Width.Value-8);
            Canvas.SetLeft(texts[i],4);Canvas.SetTop(texts[i],above[i/columns]-texts[i].BaselineOffset);
        }
        metrics.Width=width;
    }
}
