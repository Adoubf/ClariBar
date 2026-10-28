using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClariBar.Core;

public static class Localizer
{
    private static readonly Dictionary<string,string> English=Read("en-US");
    private static readonly Dictionary<string,string> Chinese=Read("zh-CN");
    private static readonly Dictionary<string,string> ChineseKeys=Chinese.GroupBy(pair=>pair.Value,StringComparer.Ordinal)
        .ToDictionary(group=>group.Key,group=>group.First().Key,StringComparer.Ordinal);
    private static readonly Regex Placeholder=new(@"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})",RegexOptions.CultureInvariant);
    private static readonly (string Key,Regex English,Regex Chinese)[] Formats=English.Keys.Where(key=>Placeholder.IsMatch(key))
        .OrderByDescending(key=>key.Length).Select(key=>(key,Pattern(key),Pattern(Chinese.GetValueOrDefault(key,key)))).ToArray();
    private static string languageCode=ResolveLanguage(Environment.GetEnvironmentVariable("CLARIBAR_LANGUAGE"),CultureInfo.CurrentUICulture.Name);

    public static string LanguageCode=>languageCode;
    public static bool IsChinese=>languageCode=="zh-CN";
    public static IReadOnlyCollection<string> MessageKeys=>English.Keys;
    public static string NormalizePreference(string? preference)=>preference?.Trim() switch
    {
        "en" or "en-US"=>"en-US",
        "zh" or "zh-CN" or "zh-Hans"=>"zh-CN",
        _=>"System"
    };
    public static string ResolveLanguage(string? preference,string? systemLanguage)
    {
        string normalized=NormalizePreference(preference);
        if(normalized!="System")return normalized;
        return systemLanguage?.StartsWith("zh",StringComparison.OrdinalIgnoreCase)==true?"zh-CN":"en-US";
    }
    public static void Configure(string? preference,string? systemLanguage=null)
        =>languageCode=ResolveLanguage(preference,systemLanguage??CultureInfo.CurrentUICulture.Name);

    public static string T(string? value)
    {
        if(string.IsNullOrEmpty(value))return value??"";
        if(English.ContainsKey(value))return IsChinese?Chinese.GetValueOrDefault(value,value):value;
        if(ChineseKeys.TryGetValue(value,out string? key))return IsChinese?Chinese[key]:key;
        foreach(var item in Formats)
        {
            var match=item.Chinese.Match(value);
            if(match.Success)return RenderCaptured(IsChinese?Chinese[item.Key]:item.Key,match);
            if(IsChinese && (match=item.English.Match(value)).Success)return RenderCaptured(Chinese[item.Key],match);
        }
        return value;
    }
    public static string F(string format,params object?[] arguments)
        =>string.Format(CultureInfo.CurrentCulture,T(format),arguments);
    public static bool HasTranslation(string key)=>English.ContainsKey(key)&&Chinese.ContainsKey(key);

    private static Dictionary<string,string> Read(string culture)
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ClariBar.Core.Localization."+culture+".json")
            ??throw new InvalidOperationException("Language resources are missing: "+culture);
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??throw new InvalidDataException("Empty language resources.");
    }
    private static Regex Pattern(string format)
    {
        var pattern=new StringBuilder(@"\A");int offset=0;
        foreach(Match match in Placeholder.Matches(format))
        {
            pattern.Append(Regex.Escape(format[offset..match.Index])).Append("(?<p").Append(match.Groups[1].Value).Append(">.*?)");
            offset=match.Index+match.Length;
        }
        pattern.Append(Regex.Escape(format[offset..])).Append(@"\z");
        return new Regex(pattern.ToString(),RegexOptions.Singleline|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    }
    private static string RenderCaptured(string format,Match values)=>Placeholder.Replace(format,match=>values.Groups["p"+match.Groups[1].Value].Value);
}
