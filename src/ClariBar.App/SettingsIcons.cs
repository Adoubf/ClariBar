using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ClariBar.App;

// One semantic icon source is shared by settings cards, shortcuts and expanders.
internal static class SettingsIcons
{
    private const string TextScan="M0,0 H6 V2 H2 V6 H0Z M14,0 H20 V6 H18 V2 H14Z M0,14 H2 V18 H6 V20 H0Z M18,14 H20 V20 H14 V18 H18Z M5,4 H15 V6 H11 V16 H9 V6 H5Z";
    private const string AiDocument="M2,1 H11 V3 H4 V18 H17 V11 H19 V20 H2Z M6,7 H10 V9 H6Z M6,11 H13 V13 H6Z M6,15 H13 V17 H6Z M16,0 L17.5,3.5 L21,5 L17.5,6.5 L16,10 L14.5,6.5 L11,5 L14.5,3.5Z";
    private const string Width="M0,4 H2 V16 H0Z M18,4 H20 V16 H18Z M3,10 L7,6 V9 H13 V6 L17,10 L13,14 V11 H7 V14Z";
    private const string Ruler="F0 M0,5 H20 V15 H0Z M2,7 V13 H18 V7Z M4,7 H5 V10 H4Z M8,7 H9 V9 H8Z M12,7 H13 V10 H12Z M16,7 H17 V9 H16Z";
    private const string Opacity="F0 M10,0 A10,10 0 1 1 9.99,0Z M10,2 A8,8 0 1 0 10.01,2Z M10,2 V18 A8,8 0 0 0 10,2Z";
    private const string ImageGroup="F0 M0,0 H15 V15 H0Z M2,2 V13 H13 V2Z M5,17 H17 V5 H20 V20 H5Z M3,11 L6,7 L8,9 L10,6 L12,11Z";
    private const string Clock="F0 M10,0 A10,10 0 1 1 9.99,0Z M10,2 A8,8 0 1 0 10.01,2Z M9,4 H11 V9 H16 V11 H9Z";
    private const string Chip="F0 M4,4 H16 V16 H4Z M6,6 V14 H14 V6Z M0,6 H4 V8 H0Z M0,12 H4 V14 H0Z M16,6 H20 V8 H16Z M16,12 H20 V14 H16Z M6,0 H8 V4 H6Z M12,0 H14 V4 H12Z M6,16 H8 V20 H6Z M12,16 H14 V20 H12Z";
    private const string Fan="M10,8 A2,2 0 1 1 9.99,8Z M9,7 C4,6 3,2 7,1 C11,0 14,1 12,7Z M13,9 C16,5 20,6 20,10 C20,14 17,16 13,12Z M9,13 C10,18 6,20 3,18 C0,15 2,12 7,11Z";
    private static readonly Dictionary<string,string> Glyphs=new()
    {
        ["Screenshot"]="\uE722",["Capture preferences"]="\uE722",["Screen color picker"]="\uEF3C",["Color editor"]="\uE790",
        ["Screenshot translation"]="\uE8F2",["Translation engine"]="\uE774",["Translation display"]="\uE7F4",["Source language"]="\uE8C1",["Target language"]="\uE8C8",["Optional offline models"]="\uE896",
        ["Pin clipboard image"]="\uE718",["Pinned image preferences"]="\uE718",["Show / hide all"]="\uE890",["Mouse click-through"]="\uE962",["Click-through"]="\uE962",
        ["Delayed capture"]="\uE917",["Screenshot folder"]="\uE8B7",["Saved images"]="\uE8B9",["Local OCR language"]="\uE774",
        ["Snipaste shortcuts"]="\uE765",["Enable global shortcuts"]="\uE765",["Monitor window"]="\uE7F4",["Show a floating monitor"]="\uE7F4",
        ["Choose monitoring metrics"]="\uE9D9",["Text size"]="\uE8E9",["Monitor font"]="\uE8D2",["Bold text"]="\uE8DD",
        ["Sampling interval"]="\uE917",["Custom metric names"]="\uE8AC",["Diagnostic records"]="\uE9D9",["Hardware readings"]="\uE950"
    };
    private static readonly Dictionary<string,string> Paths=new()
    {
        ["Extract text"]=TextScan,["Default recognition method"]=TextScan,["AI OCR (optional)"]=AiDocument,["AI configuration"]=AiDocument,
        ["Pinned images & groups"]=ImageGroup,["Background opacity"]=Opacity,["Floating monitor opacity"]=Opacity,
        ["Automatic content width"]=Width,["Manual width"]=Ruler,["Delayed capture"]=Clock,["Sampling interval"]=Clock,["Time"]=Clock,
        ["Hardware readings"]=Chip,["Fan channels"]=Fan
    };
    private static string? Match(Dictionary<string,string> items,string label)
    {foreach(var pair in items)if(pair.Key==label || Localizer.T(pair.Key)==label)return pair.Value;return null;}
    internal static IconElement Create(string label,string fallback="\uE713")
    {
        IconElement icon;
        if(Match(Paths,label) is {} path)icon=(PathIcon)XamlReader.Load("<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='"+path+"'/>");
        else icon=new FontIcon {Glyph=Match(Glyphs,label)??fallback,FontSize=20};
        icon.Width=24;icon.Height=20;icon.VerticalAlignment=VerticalAlignment.Center;icon.IsHitTestVisible=false;icon.Tag=label;
        return icon;
    }
    internal static FrameworkElement Header(string title,string fallback="\uE713")
    {
        var header=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12,VerticalAlignment=VerticalAlignment.Center};
        header.Children.Add(Create(title,fallback));header.Children.Add(ToolUi.Label(title,14));return header;
    }
    internal static Expander Section(string title,UIElement content)
    {
        var expander=new Expander {Header=Header(title),Content=content,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(expander,title);return expander;
    }
}
