using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal static class TranslationLanguages
{
    private static readonly (string Code,string Label)[] choices=[("Auto","Automatic language"),("en","English"),("zh","Simplified Chinese"),("ja","Japanese"),("ko","Korean"),("fr","French"),("de","German"),("es","Spanish"),("ru","Russian")];
    internal static ComboBox Create(string value,bool source,double width=180)
    {
        var box=new ComboBox{IsEditable=true,MinWidth=width,ItemsSource=Options(source).Select(item=>L.T(item.Label)).ToArray()};
        Set(box,value,source);bool loaded=false;
        box.Loaded+=(_,_)=>{if(!loaded){loaded=true;Set(box,value,source);}};return box;
    }
    private static (string Code,string Label)[] Options(bool source)=>source?choices.Take(3).ToArray():[("Auto","Automatic (Chinese ↔ English)"),..choices.Skip(1)];
    internal static void Set(ComboBox box,string value,bool source)
    {
        var options=Options(source);int index=Array.FindIndex(options,item=>item.Code.Equals(value,StringComparison.OrdinalIgnoreCase));
        box.SelectedIndex=index;box.Text=index>=0?L.T(options[index].Label):value;
    }
    internal static string Read(ComboBox box,bool source)
        =>ReadText(box.Text,source);
    internal static string ReadText(string value,bool source)
    {
        string text=value.Trim();var match=Options(source).Concat(choices).FirstOrDefault(item=>text.Equals(item.Code,StringComparison.OrdinalIgnoreCase) || text.Equals(item.Label,StringComparison.OrdinalIgnoreCase) || text==L.T(item.Label));
        return match.Code??TranslationProtocol.NormalizeLanguage(text,source);
    }
    internal static void CommitChanges(ComboBox box,bool source,Action<string> changed)
    {
        box.SelectionChanged+=(_,_)=>{if(box.SelectedIndex>=0)changed(Options(source)[box.SelectedIndex].Code);};
        box.LostFocus+=(_,_)=>changed(Read(box,source));
        box.TextSubmitted+=(_,e)=>{string code=ReadText(e.Text,source);changed(code);Set(box,code,source);e.Handled=true;};
    }
}
