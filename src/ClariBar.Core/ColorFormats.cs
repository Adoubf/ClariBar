using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClariBar.Core;

public sealed class ColorFormatDefinition
{
    public string Name {get;set;}="";
    public string Format {get;set;}="";
    public bool Enabled {get;set;}=true;
}

public static class ColorFormats
{
    private static readonly Regex Token=new(@"%(Re|Gr|Bl|Al)([bhHxXfF]?)|%(Hu|Sl|Ll|Sb|Va|Br|Wh|Bn|Cy|Ma|Ye|Bk|Dv|Na)",RegexOptions.CultureInvariant);
    public static List<ColorFormatDefinition> Defaults()=>
    [
        new(){Name="HEX",Format="#%ReX%GrX%BlX"},new(){Name="RGB",Format="rgb(%Re, %Gr, %Bl)"},
        new(){Name="HSL",Format="hsl(%Hu, %Sl%, %Ll%)"},
        new(){Name="HSV",Format="hsv(%Hu, %Sb%, %Va%)"},
        new(){Name="CMYK",Format="cmyk(%Cy%, %Ma%, %Ye%, %Bk%)",Enabled=false},
        new(){Name="Decimal",Format="%Dv",Enabled=false}
    ];
    public static bool ValidTemplate(string? template)=>template is {Length:>0 and <=200} &&
        !Regex.IsMatch(Token.Replace(template,""),@"%[A-Za-z]");
    public static void Normalize(AppSettings settings)
    {
        settings.ColorFormats=(settings.ColorFormats??Defaults()).Where(item=>item is not null && !string.IsNullOrWhiteSpace(item.Name) && item.Name.Length<=24 && ValidTemplate(item.Format))
            .DistinctBy(item=>item.Name,StringComparer.OrdinalIgnoreCase).Take(24).ToList();
        if(settings.ColorFormats.Count==0)settings.ColorFormats=Defaults();
        if(!settings.ColorFormats.Any(item=>item.Enabled))settings.ColorFormats[0].Enabled=true;
        if(!settings.ColorFormats.Any(item=>item.Name==settings.ColorCopyFormat))settings.ColorCopyFormat=settings.ColorFormats[0].Name;
        if(settings.ColorPickerActivation is not ("PickAndEdit" or "Editor" or "PickOnly"))settings.ColorPickerActivation="PickAndEdit";
        if(settings.ColorLeftAction is not ("Edit" or "Copy" or "Cancel"))settings.ColorLeftAction="Edit";
        if(settings.ColorMiddleAction is not ("Edit" or "Copy" or "Cancel"))settings.ColorMiddleAction="Copy";
        if(settings.ColorRightAction is not ("Edit" or "Copy" or "Cancel"))settings.ColorRightAction="Cancel";
    }
    public static (double H,double S,double V) Hsv(ScreenColor color)
    {
        var (h,_,_)=color.HslValues;double max=Math.Max(color.R,Math.Max(color.G,color.B)),min=Math.Min(color.R,Math.Min(color.G,color.B));
        return(h,max==0?0:(max-min)*100/max,max*100/255);
    }
    public static ScreenColor FromHsv(double h,double s,double v)
    {
        s=Math.Clamp(s,0,100)/100;v=Math.Clamp(v,0,100)/100;double l=v*(1-s/2);
        return ScreenColor.FromHsl(h,l is <=0 or >=1?0:(v-l)/Math.Min(l,1-l)*100,l*100);
    }
    public static string Format(ScreenColor color,string template)
    {
        var (h,sl,l)=color.HslValues;var (_,sb,v)=Hsv(color);double max=Math.Max(color.R,Math.Max(color.G,color.B))/255d;
        string Number(double n)=>n.ToString("0.#",CultureInfo.InvariantCulture);
        return Token.Replace(template,match=>
        {
            if(match.Groups[1].Success)
            {
                int channel=match.Groups[1].Value switch{"Re"=>color.R,"Gr"=>color.G,"Bl"=>color.B,_=>255};
                return match.Groups[2].Value switch
                {
                    "h"=>(channel/16).ToString("x",CultureInfo.InvariantCulture),"H"=>(channel/16).ToString("X",CultureInfo.InvariantCulture),
                    "x"=>channel.ToString("x2",CultureInfo.InvariantCulture),"X"=>channel.ToString("X2",CultureInfo.InvariantCulture),
                    "f"=>(channel/255d).ToString("0.###",CultureInfo.InvariantCulture),"F"=>channel==0?"0":(channel/255d).ToString(".###",CultureInfo.InvariantCulture),
                    _=>channel.ToString(CultureInfo.InvariantCulture)
                };
            }
            return match.Groups[3].Value switch
            {
                "Hu"=>Number(h),"Sl"=>Number(sl),"Ll"=>Number(l),"Sb"=>Number(sb),"Va" or "Br"=>Number(v),
                "Wh"=>Number(Math.Min(color.R,Math.Min(color.G,color.B))*100d/255),"Bn"=>Number(100-v),
                "Cy"=>Number(max==0?0:(max-color.R/255d)/max*100),"Ma"=>Number(max==0?0:(max-color.G/255d)/max*100),
                "Ye"=>Number(max==0?0:(max-color.B/255d)/max*100),"Bk"=>Number((1-max)*100),
                "Dv"=>((color.R<<16)|(color.G<<8)|color.B).ToString(CultureInfo.InvariantCulture),"Na"=>Name(color),_=>match.Value
            };
        });
    }
    public static string DefaultText(ScreenColor color,AppSettings settings)=>Format(color,settings.ColorFormats.FirstOrDefault(item=>item.Name==settings.ColorCopyFormat)?.Format??"#%ReX%GrX%BlX");
    public static string Name(ScreenColor color)
    {
        var (h,s,l)=color.HslValues;
        string name=l<7?"Black":l>96?"White":s<12?(l>72?"Light gray":l<30?"Dark gray":"Gray"):
            h<15?"Red":h<40?(l<45?"Brown":"Orange"):h<65?"Yellow":h<90?"Lime":h<160?"Green":h<185?"Teal":h<200?"Cyan":h<225?"Sky blue":h<260?"Blue":h<290?"Violet":h<325?"Purple":"Rose";
        return Localizer.T(name);
    }
    public static string Export(IEnumerable<string> colors,IEnumerable<ColorFormatDefinition> formats,bool byFormat,bool json=true)
    {
        var values=colors.Where(AppSettings.ValidColor).Distinct(StringComparer.OrdinalIgnoreCase).Select(hex=>{ScreenColor.TryParse(hex,out var value);return value;}).ToArray();
        var enabled=formats.Where(item=>item.Enabled).ToArray();
        if(!json)return byFormat
            ?string.Join("\n\n",enabled.Select(item=>item.Name+"\n"+string.Join("\n",values.Select(value=>Format(value,item.Format)))))
            :string.Join("\n\n",values.Select(value=>value.Hex+"\n"+string.Join("\n",enabled.Select(item=>item.Name+": "+Format(value,item.Format)))));
        object data=byFormat?enabled.ToDictionary(item=>item.Name,item=>values.Select(value=>Format(value,item.Format)).ToArray()):
            values.Select(value=>new{color=value.Hex,formats=enabled.ToDictionary(item=>item.Name,item=>Format(value,item.Format))}).ToArray();
        return JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true});
    }
}
