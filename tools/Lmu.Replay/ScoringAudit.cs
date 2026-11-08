using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
namespace Lmu.Replay;

// Read-only audit of every raw JSONL sample. Does not infer official validity from timing.
public static class ScoringAudit
{
    public sealed record Point(int Line,DateTimeOffset At,int? Session,int? Lap,double? Elapsed,double? Start,
        double? ScoringET,double? ScoringStart,int? Total,int? Flag,double? Last,double? RawLast,double? Best,
        bool? Invalidated,int? Steps,bool? InPits,double? CurrentSector1,double? CurrentSector2);
    public sealed record Transition(Point? Before,Point After);
    public sealed class LapSummary(Point first,int epoch)
    {
        public int NativeEpoch {get;}=epoch;
        public Point First {get;}=first;
        public Point Last {get;set;}=first;
        public int Samples {get;set;}
        public Dictionary<string,int> MatchingFlags {get;}=[];
        public bool InvalidatedSeen {get;set;}
        public int? MaxSteps {get;set;}
        public double MaxGapSeconds {get;set;}
        public Point? SequentialEnd {get;set;}
        public List<Point> NextScoringChanges {get;}=[];
    }
    public sealed record FileAudit(string Path,long Bytes,string Sha256,int Lines,int LiveSamples,int OtherSamples,
        Dictionary<string,int> Types,List<Transition> FlagChanges,List<Transition> TimingChanges,
        List<Transition> Boundaries,List<LapSummary> Laps,List<JsonElement> RecordedCompletedLaps,
        Dictionary<string,string[]> RestCandidateKeys);
    public static FileAudit Read(string path)
    {
        var types=new Dictionary<string,int>();var flags=new List<Transition>();var times=new List<Transition>();
        var boundaries=new List<Transition>();var laps=new Dictionary<string,LapSummary>();
        var completed=new List<JsonElement>();var restKeys=new Dictionary<string,HashSet<string>>();
        var bodies=new Dictionary<string,string>();Point? previous=null;string? previousScope=null;
        int lineNo=0,live=0,other=0,epoch=0;LapSummary? current=null;
        foreach(var line in File.ReadLines(path))
        {
            lineNo++;using var doc=JsonDocument.Parse(line);var root=doc.RootElement;
            var type=root.GetProperty("type").GetString()!;var d=root.GetProperty("data");
            types[type]=types.GetValueOrDefault(type)+1;
            if(type=="rest-body") {
                var body=d.GetProperty("Body").GetString()!;var id=d.GetProperty("BodyId").GetString()!;
                if(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body)))!=id)
                    throw new InvalidDataException($"Body hash mismatch: {path}:{lineNo}");
                bodies[id]=body;continue;
            }
            if(type=="rest") {
                string? body=d.TryGetProperty("Body",out var b)&&b.ValueKind==JsonValueKind.String?b.GetString():null;
                if(d.TryGetProperty("BodyId",out var id)&&id.ValueKind==JsonValueKind.String)body=bodies[id.GetString()!];
                var url=d.GetProperty("Url").GetString()!;string endpoint=new Uri(url).AbsolutePath;
                if(!restKeys.TryGetValue(endpoint,out var keys))restKeys[endpoint]=keys=[];
                if(body is not null)try{using var json=JsonDocument.Parse(body);CandidateKeys(json.RootElement,"",keys);}catch(JsonException){}
                continue;
            }
            if(type=="lap-complete"){completed.Add(d.GetProperty("Lap").Clone());continue;}
            if(type!="sample")continue;
            if(d.GetProperty("Mode").GetString()!="LIVE"){other++;continue;}live++;
            bool compact=d.TryGetProperty("Values",out var values);if(!compact)values=d.GetProperty("Fields");
            JsonElement V(string k) {if(!values.TryGetProperty(k,out var v))return default;return compact?v:v.GetProperty("Value");}
            double? N(string k)=>V(k) is var v && v.ValueKind==JsonValueKind.Number?v.GetDouble():null;
            int? I(string k)=>N(k) is {} n?(int)n:null;
            bool? B(string k)=>V(k).ValueKind switch {JsonValueKind.True=>true,JsonValueKind.False=>false,_=>null};
            string T(string k)=>V(k).ToString();
            var p=new Point(lineNo,d.GetProperty("ObservedAt").GetDateTimeOffset(),I("SessionCode"),I("LapNumber"),N("TelemetryElapsed"),N("LapStartET"),
                N("SessionElapsed"),N("ScoringLapStartET"),I("CompletedLaps"),I("CountLapFlag"),N("LastLapTime"),N("RawLastLapTime"),
                N("BestLap"),B("LapInvalidated"),I("TrackLimitsSteps"),B("InPits"),N("RawCurrentSector1"),N("RawCurrentSector2"));
            string scope=string.Join('|',new[]{T("SessionCode"),T("SessionStart"),T("PlayerSlotId"),T("PlayerVehicleFile"),T("RawTrackName"),
                d.TryGetProperty("ProcessInstance",out var process)?process.ToString():"unrecorded"});
            bool reset=scope!=previousScope || p.Elapsed<previous?.Elapsed || p.Lap<previous?.Lap || p.Start<previous?.Start;
            if(reset)epoch++;
            bool sameScope=!reset;
            if(previous is null || !sameScope || p.Flag!=previous.Flag)flags.Add(new(sameScope?previous:null,p));
            if(previous is null || !sameScope || p.Last!=previous.Last || p.RawLast!=previous.RawLast || p.Best!=previous.Best)
                times.Add(new(sameScope?previous:null,p));
            if(previous is null || !sameScope || p.Lap!=previous.Lap || p.Start!=previous.Start) {
                boundaries.Add(new(sameScope?previous:null,p));
                if(sameScope && current is not null && p.Lap==previous!.Lap+1 && p.Start>previous.Start)current.SequentialEnd=p;
            }
            string key=epoch.ToString(CultureInfo.InvariantCulture)+"|"+p.Lap+"|"+p.Start?.ToString("R",CultureInfo.InvariantCulture);
            if(!laps.TryGetValue(key,out current)){current=new(p,epoch);laps.Add(key,current);}
            if(current.Samples>0)current.MaxGapSeconds=Math.Max(current.MaxGapSeconds,(p.At-current.Last.At).TotalSeconds);
            current.Samples++;current.Last=p;current.InvalidatedSeen|=p.Invalidated==true;
            if(p.Steps is {} steps)current.MaxSteps=Math.Max(current.MaxSteps??steps,steps);
            if(p.Start is {} start && p.ScoringStart is {} scoreStart && Math.Abs(start-scoreStart)<.000001) {
                string flag=p.Flag?.ToString(CultureInfo.InvariantCulture)??"null";
                current.MatchingFlags[flag]=current.MatchingFlags.GetValueOrDefault(flag)+1;
            }
            foreach(var lap in laps.Values.Where(l=>l.NativeEpoch==epoch && l.SequentialEnd is {} end && p.ScoringStart==end.Start &&
                p.Total==l.Last.Total+1)) {
                var last=lap.NextScoringChanges.LastOrDefault();
                if(last is null || p.Last!=last.Last || p.RawLast!=last.RawLast || p.Best!=last.Best || p.Flag!=last.Flag)
                    lap.NextScoringChanges.Add(p);
            }
            previous=p;previousScope=scope;
        }
        using var file=File.OpenRead(path);string hash=Convert.ToHexString(SHA256.HashData(file));
        return new(path,new FileInfo(path).Length,hash,lineNo,live,other,types,flags,times,boundaries,laps.Values.ToList(),completed,
            restKeys.ToDictionary(p=>p.Key,p=>p.Value.Order().ToArray()));
    }
    private static void CandidateKeys(JsonElement element,string path,HashSet<string> keys)
    {
        if(element.ValueKind==JsonValueKind.Object)foreach(var p in element.EnumerateObject()) {
            var next=path+"/"+p.Name;
            if(new[]{"invalid","valid","lap","limit","penal","warn","flag"}.Any(s=>p.Name.Contains(s,StringComparison.OrdinalIgnoreCase)))keys.Add(next);
            CandidateKeys(p.Value,next,keys);
        }
        else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())CandidateKeys(item,path+"/*",keys);
    }
}
