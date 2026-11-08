using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace Lmu.Localization;

public sealed record LanguageInfo(string Code,string NativeName,string Name,string Direction,bool Complete,int Translated,int Total);
public sealed record LanguageDocument(LanguageInfo Info,IReadOnlyDictionary<string,string> Messages);

/// <summary>One validated JSON catalog shared by the browser, tray and API system messages.</summary>
public sealed class LanguageCatalog
{
    private static readonly Regex CodePattern=new("^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$",RegexOptions.CultureInvariant);
    private static readonly Regex KeyPattern=new("^[a-z][A-Za-z0-9]*(?:\\.[A-Za-z0-9_]+)+$",RegexOptions.CultureInvariant);
    private static readonly Regex TokenPattern=new("\\{([A-Za-z][A-Za-z0-9_]*)\\}",RegexOptions.CultureInvariant);
    private static readonly Regex HtmlPattern=new("<[/!]?[A-Za-z]|javascript\\s*:|[\\u0000-\\u0008\\u000B\\u000C\\u000E-\\u001F\\uFFFD]|[\\uD800-\\uDBFF](?![\\uDC00-\\uDFFF])|(?<![\\uD800-\\uDBFF])[\\uDC00-\\uDFFF]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private readonly Dictionary<string,LanguageDocument> languages=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,string> canonical;
    private readonly Dictionary<string,string> exact=new(StringComparer.Ordinal);
    private readonly List<(string Key,string Prefix,Regex Pattern)> patterns=[];
    public string[] RejectedFiles{get;private set;}=[];
    public HashSet<string> SecurityKeys{get;}

    public LanguageCatalog(string? directory=null)
    {
        var assembly=typeof(LanguageCatalog).Assembly;
        LanguageDocument Embedded(string code){using var input=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith("."+code+".json",StringComparison.Ordinal)))!;using var reader=new StreamReader(input);return Parse(reader.ReadToEnd());}
        var english=Embedded("en");var german=Embedded("de");
        using(var policy=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith(".locale-policy.json",StringComparison.Ordinal)))!)SecurityKeys=JsonSerializer.Deserialize<HashSet<string>>(policy)??throw new InvalidDataException("Missing locale security policy.");
        Validate(english,english.Messages,true);Validate(german,english.Messages,true);
        languages["en"]=english;languages["de"]=german;
        using(var wire=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith(".CanonicalMessages.json",StringComparison.Ordinal)))!)canonical=JsonSerializer.Deserialize<Dictionary<string,string>>(wire)??throw new InvalidDataException("Missing canonical message contracts.");
        var rejected=new List<string>();
        directory??=Path.Combine(AppContext.BaseDirectory,"wwwroot","locales");
        if(Directory.Exists(directory))foreach(var file in Directory.EnumerateFiles(directory,"*.json").Order(StringComparer.Ordinal))
        {
            try{var doc=Parse(File.ReadAllText(file,new UTF8Encoding(false,true)));if(!string.Equals(Path.GetFileNameWithoutExtension(file),doc.Info.Code,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Locale filename and code differ.");Validate(doc,english.Messages,doc.Info.Code is "de" or "en");languages[doc.Info.Code]=doc;}
            catch(Exception e)when(e is JsonException or InvalidDataException or IOException or ArgumentException or KeyNotFoundException or InvalidOperationException){rejected.Add(Path.GetFileName(file));}
        }
        RejectedFiles=rejected.ToArray();
        foreach(var pair in canonical)
        {
            if(pair.Key.StartsWith("web.",StringComparison.Ordinal)||pair.Key.StartsWith("settings.",StringComparison.Ordinal)||pair.Key.StartsWith("invitation.",StringComparison.Ordinal)||pair.Key.StartsWith("shell.",StringComparison.Ordinal)||pair.Key.StartsWith("language.",StringComparison.Ordinal))continue;
            if(!TokenPattern.IsMatch(pair.Value)){exact.TryAdd(pair.Value,pair.Key);continue;}
            var escaped=Regex.Escape(pair.Value);foreach(var token in Placeholders(pair.Value).Distinct())escaped=escaped.Replace(Regex.Escape("{"+token+"}"),"(?<"+token+">.*?)",StringComparison.Ordinal);
            patterns.Add((pair.Key,pair.Value.Split('{')[0],new Regex("\\A"+escaped+"\\z",RegexOptions.Singleline|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50))));
        }
        patterns.Sort((a,b)=>b.Prefix.Length.CompareTo(a.Prefix.Length));
    }

    public static LanguageDocument Parse(string json)
    {
        using var input=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=8});
        static void Unique(JsonElement element){if(element.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Expected an object.");var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var p in element.EnumerateObject())if(!seen.Add(p.Name))throw new InvalidDataException("Duplicate locale property.");}
        var root=input.RootElement;Unique(root);if(root.EnumerateObject().Any(p=>p.Name is not ("meta" or "messages")))throw new InvalidDataException("Unknown locale section.");
        var meta=root.GetProperty("meta");Unique(meta);var messages=root.GetProperty("messages");Unique(messages);
        string Get(string name)=>meta.GetProperty(name).GetString()??throw new InvalidDataException("Missing locale metadata.");
        if(meta.EnumerateObject().Any(p=>p.Name is not ("code" or "nativeName" or "name" or "direction" or "status")))throw new InvalidDataException("Unknown locale metadata.");
        var code=Get("code");var direction=Get("direction");var status=Get("status");
        if(!CodePattern.IsMatch(code)||direction is not ("ltr" or "rtl")||status is not ("complete" or "partial"))throw new InvalidDataException("Invalid locale metadata.");
        _=CultureInfo.GetCultureInfo(code);
        var native=Get("nativeName");var name=Get("name");if(new[]{native,name}.Any(s=>string.IsNullOrWhiteSpace(s)||s.Length>80||HtmlPattern.IsMatch(s)))throw new InvalidDataException("Invalid language name.");
        var map=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var p in messages.EnumerateObject())
        {
            var text=p.Value.ValueKind==JsonValueKind.String?p.Value.GetString():null;
            if(!KeyPattern.IsMatch(p.Name)||string.IsNullOrWhiteSpace(text)||HtmlPattern.IsMatch(text))throw new InvalidDataException("Invalid locale message.");
            var remaining=TokenPattern.Replace(text,"");if(remaining.Contains('{')||remaining.Contains('}'))throw new InvalidDataException("Invalid placeholder syntax.");map.Add(p.Name,text);
            if(p.Name=="common.restorePhrase"&&text!=(code=="de"?"BIBLIOTHEK ERSETZEN":"REPLACE LIBRARY"))throw new InvalidDataException("Do not translate the restore confirmation token.");
        }
        if(map.Count==0)throw new InvalidDataException("Empty language document.");
        return new(new(code,native,name,direction,status=="complete",map.Count,map.Count),map);
    }

    public static string[] Placeholders(string text)=>TokenPattern.Matches(text).Select(m=>m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ToArray();
    public static void Validate(LanguageDocument document,IReadOnlyDictionary<string,string> baseline,bool required=false)
    {
        if(document.Messages.Keys.Any(k=>!baseline.ContainsKey(k)))throw new InvalidDataException("Unknown translation key.");
        if((required||document.Info.Code is "de" or "en")&&document.Messages.Count!=baseline.Count)throw new InvalidDataException("Incomplete required language.");
        foreach(var p in document.Messages)if(!Placeholders(p.Value).SequenceEqual(Placeholders(baseline[p.Key])))throw new InvalidDataException("Translation placeholders differ.");
    }
    public LanguageInfo[] Languages=>languages.Values.Select(l=>l.Info with{Complete=l.Info.Complete&&l.Messages.Count==languages["en"].Messages.Count,Total=languages["en"].Messages.Count}).OrderBy(l=>l.Code,StringComparer.Ordinal).ToArray();
    public LanguageDocument Document(string code)=>languages.TryGetValue(code,out var doc)?doc:throw new KeyNotFoundException("Unknown language.");
    public string Resolve(string? preference){if(preference is not null){if(languages.ContainsKey(preference))return languages[preference].Info.Code;var basic=preference.Split('-')[0];if(languages.ContainsKey(basic))return languages[basic].Info.Code;}return "en";}
    public string Format(string key,string? language,IReadOnlyDictionary<string,object?>? values=null)
    {
        var code=Resolve(language);var doc=languages[code];var map=(!doc.Info.Complete||doc.Messages.Count!=languages["en"].Messages.Count)&&SecurityKeys.Contains(key)?languages["en"].Messages:doc.Messages;
        var template=map.GetValueOrDefault(key)??languages["en"].Messages.GetValueOrDefault(key)??throw new KeyNotFoundException("Unknown translation key: "+key);
        var culture=CultureInfo.GetCultureInfo(code);
        return TokenPattern.Replace(template,m=>values is not null&&values.TryGetValue(m.Groups[1].Value,out var value)?value is IFormattable f?f.ToString(null,culture):value?.ToString()??"":throw new ArgumentException("Missing translation argument: "+m.Groups[1].Value));
    }
    // Canonical wire messages keep existing diagnostic/IPC contracts stable. Only system-message
    // properties are localized at API serialization; names, notes and IDs are never inspected.
    public string Canonical(string key,params object?[] values)=>TokenPattern.Replace(canonical[key],m=>Convert.ToString(values[int.Parse(m.Groups[1].Value.AsSpan(5),CultureInfo.InvariantCulture)],CultureInfo.InvariantCulture)??"");
    public string Localize(string? text,string? language)
    {
        if(text is null)return "";if(exact.TryGetValue(text,out var key))return Format(key,language);
        if(text.Length>16000)return text;
        foreach(var p in patterns){if(!text.StartsWith(p.Prefix,StringComparison.Ordinal))continue;var match=p.Pattern.Match(text);if(!match.Success)continue;var args=Placeholders(canonical[p.Key]).ToDictionary(k=>k,k=>(object?)match.Groups[k].Value);
            // These two templates receive a developer-defined field label, never the
            // user's value. Other arguments stay opaque, including names, paths and IDs.
            if(p.Key is "library.textWith1CharactersRequired" or "library.negativeLevelOrVerifiedVehicleLimitExceeded")args["value0"]=Localize((string?)args["value0"],language);
            return Format(p.Key,language,args);}
        return text;
    }
}

public static class UiText
{
    public static LanguageCatalog Catalog{get;}=new();
    private static readonly AsyncLocal<string?> requested=new();
    public static string RequestLanguage=>requested.Value??"canonical";
    public static string SystemLanguage=>Catalog.Resolve(CultureInfo.InstalledUICulture.Name);
    public static IDisposable UseRequestLanguage(string? code){var old=requested.Value;requested.Value=string.IsNullOrWhiteSpace(code)?"canonical":Catalog.Resolve(code);return new Scope(()=>requested.Value=old);}
    public static string C(string key,params object?[] args)=>Catalog.Canonical(key,args);
    public static string T(string key,string? language=null,params object?[] args)=>Catalog.Format(key,language??SystemLanguage,args.Select((v,i)=>(v,i)).ToDictionary(p=>"value"+p.i,p=>p.v));
    public static string Localize(string? message,string? language=null)=>language=="canonical"?message??"":Catalog.Localize(message,language??SystemLanguage);
    private sealed class Scope(Action done):IDisposable{public void Dispose()=>done();}
}
