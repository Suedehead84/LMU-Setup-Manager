using Lmu.Localization;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Lmu.Diagnostics;
namespace Lmu.LiveCapture;

public sealed record SessionEvidence(bool Confirmed,string? ContextId,string Source,string Message,string? InstanceId=null,bool Ended=false);
public sealed record NavigationTicket(long Epoch,string? Context,DateTimeOffset RequestedAt);
// Scoring publication freshness is independent of vehicle physics; no last-known-player fallback.
public sealed class SessionIdentityState
{
    private Sample? current;private string? context;private string process="";private long epoch,request,lastRequest;
    private SessionIdentity result=SessionIdentity.Unknown(UiText.C("live.noNativeSessionIdentity"));private SessionIdentity? candidate;private int confirmations;private DateTimeOffset validated;
    private string? instanceId;private bool ended;private double? endedClock;private DateTimeOffset navigationAt,lastNavigationRequest;private string? navigationContext;
    public void Observe(Sample? s,string processInstance)
    {
        var next=Context(s,processInstance);
        bool boundary=ValueOrNull(s,"SessionSourceStatus") is "Ended" or "Loading"||process.Length>0&&process!=processInstance;
        if(boundary){instanceId=null;ended=true;endedClock=NumberOrNull(s,"SessionElapsed");navigationAt=default;navigationContext=null;}
        if(next is not null&&current is not null&&Number(s!,"SessionElapsed")<Number(current,"SessionElapsed")){instanceId=null;navigationAt=default;navigationContext=null;}
        bool reset=next is null||next!=context||current is not null&&s is not null&&((s.ObservedAt-current.ObservedAt).TotalSeconds is <0 or >2||Number(s,"SessionElapsed")<Number(current,"SessionElapsed"));
        if(reset){epoch++;result=SessionIdentity.Unknown(UiText.C("live.sessionChangedOrNativeIdentityMissingTwoCatalog"));candidate=null;confirmations=0;validated=default;}
        context=next;current=s;process=processInstance;
        if(next is not null&&instanceId is null&&s!.Error is null&&(!ended||Number(s,"SessionElapsed")!=endedClock)){instanceId=Guid.NewGuid().ToString("N");ended=false;}
    }
    public NavigationTicket BeginNavigation(DateTimeOffset now)=>new(epoch,context,now);
    public void CompleteNavigation(NavigationTicket ticket,RestCapture? row,DateTimeOffset now)
    {
        if(ticket.Epoch!=epoch||ticket.Context!=context||row is null||row.Status!=200||row.Body is null||!Uri.TryCreate(row.Url,UriKind.Absolute,out var uri)||uri.AbsolutePath!="/navigation/state"||row.RequestedAt<ticket.RequestedAt||row.ObservedAt<row.RequestedAt||row.ObservedAt>now||now-row.RequestedAt>TimeSpan.FromSeconds(2))return;
        if(row.RequestedAt<lastNavigationRequest)return;lastNavigationRequest=row.RequestedAt;navigationAt=default;navigationContext=null;
        try {
            using var doc=JsonDocument.Parse(row.Body);var root=doc.RootElement;var state=root.GetProperty("state");
            if(root.GetProperty("loadingStatus").GetProperty("loading").ValueKind!=JsonValueKind.False||state.GetProperty("appBuild").GetInt32()!=14200)return;
            string? Text(string k)=>state.GetProperty(k).GetString();
            if(Text("navigationState")=="NAV_MAIN_MENU"&&Text("gameState") is "GSTATE_POST" or "GSTATE_SETUP"){
                instanceId=null;ended=true;endedClock=NumberOrNull(current,"SessionElapsed");result=SessionIdentity.Unknown(UiText.C("live.restConfirmsTheRaceWeekendWasLeft"));candidate=null;confirmations=0;validated=default;return;
            }
            // Exactly the combination passively observed during the real paused ESC test.
            // This proves ongoing navigation, never fresh scoring, electronics or a new identity.
            if(context is not null&&current is not null&&Text("navigationState")=="NAV_REALTIME"&&Text("gameState")=="GSTATE_DYN"&&Text("internalStateCode")=="OP_RAN_REALTIME_PASS"&&Text("gamePhase")=="GREEN"&&Int(current,"GamePhase")==5&&SessionCode(Text("gameSession"))==Int(current,"SessionCode")){
                navigationAt=row.RequestedAt;navigationContext=context;
            }
        }catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){/* Unknown states cannot extend proof. */}
    }
    private static int? SessionCode(string? name)=>name switch{"TESTDAY"=>0,"PRACTICE1"=>1,"PRACTICE2"=>2,"PRACTICE3"=>3,"PRACTICE4"=>4,"QUALIFY1"=>5,"QUALIFY2"=>6,"QUALIFY3"=>7,"QUALIFY4"=>8,"WARMUP"=>9,"RACE1"=>10,"RACE2"=>11,"RACE3"=>12,"RACE4"=>13,_=>null};
    private bool PublicationCurrent(DateTimeOffset now)=>!ended&&(current is {Error:null}||current is not null&&Value(current,"SessionSourceStatus") as string=="Frozen"&&result.SetupKey is not null&&navigationContext==context&&now>=navigationAt&&now-navigationAt<=TimeSpan.FromSeconds(2));
    public IdentityTicket Begin(DateTimeOffset now)=>new(epoch,now,context is not null&&PublicationCurrent(now),++request);
    public void Complete(IdentityTicket ticket,RestCapture[] rows,DateTimeOffset now)
    {
        if(!ticket.Active||ticket.Epoch!=epoch||context is null||current is null||!PublicationCurrent(now)||ticket.RequestId<=lastRequest)return;lastRequest=ticket.RequestId;
        var paths=new[]{"/rest/sessions/getAllVehicles","/rest/sessions/getTracksAll"};var captures=paths.Select(p=>rows.Where(r=>Uri.TryCreate(r.Url,UriKind.Absolute,out var uri)&&uri.AbsolutePath==p).ToArray()).ToArray();
        if(captures.Any(a=>a.Length!=1||a[0].Status!=200||a[0].Body is null||a[0].RequestedAt<ticket.RequestedAt||a[0].ObservedAt<a[0].RequestedAt||a[0].ObservedAt>now||now-a[0].RequestedAt>TimeSpan.FromSeconds(5))){candidate=null;confirmations=0;return;}
        SessionIdentity next;
        try {
            var entries=IdentityResolver.ReadVehicleCatalog(captures[0][0].Body!);var file=Value(current,"PlayerVehicleFile") as string;
            var matches=entries.Where(v=>v.Stem.Equals(file,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length!=1)next=SessionIdentity.Unknown(UiText.C("live.nativeScoringPlayerFileIsNotUniqueIn"));
            else {
                var selected=matches[0];
                next=IdentityResolver.ResolveActive(new(selected.Model,selected.Stem,(string)Value(current,"RawTrackName")!,process,"scoring","known","local","native",0),captures[0][0].Body!,captures[1][0].Body!);
                if(next.SetupKey is not null)next=next with{IdentitySource="Native scoring player file + exact content catalogs; no garage selection; no physics requirement"};
            }
        }catch(Exception e)when(e is FormatException or JsonException){next=SessionIdentity.Unknown(UiText.C("live.unknownCatalogStructure"));}
        if(next.SetupKey is null){result=next;candidate=null;confirmations=0;validated=default;return;}
        if(result.SetupKey is not null&&result==next){validated=now;return;}
        // Same bounded completion-to-next-request interval as telemetry identity;
        // each response still has its separate five-second freshness check above.
        if(ticket.RequestedAt<validated||ticket.RequestedAt-validated>TimeSpan.FromSeconds(5))confirmations=0;
        confirmations=candidate==next?confirmations+1:1;candidate=next;validated=now;
        result=confirmations>=2?next:SessionIdentity.Unknown(UiText.C("live.firstScoringCheck"));
    }
    public SessionIdentity Current(DateTimeOffset now)
    {
        if(current is null||context is null||!PublicationCurrent(now)||now<current.ObservedAt||now-current.ObservedAt>TimeSpan.FromSeconds(2)||result.SetupKey is not null&&now-validated>TimeSpan.FromSeconds(60)){
            result=SessionIdentity.Unknown(current?.Error??UiText.C("live.sessionPublicationOrCatalogEvidenceIsNotCurrent"));candidate=null;confirmations=0;
        }
        return result;
    }
    public SessionEvidence Evidence(DateTimeOffset now){var i=Current(now);return new(i.SetupKey is not null,i.SetupKey is null?null:Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context!))),i.IdentitySource,i.Reason??UiText.C("live.sessionIdentityConfirmedIndependentlyOfLiveElectronics"),instanceId,ended);}
    private static string? Context(Sample? s,string p) {
        if(s is null||s.Mode!="LIVE"||s.Error is not null&&Value(s,"SessionSourceStatus") as string!="Frozen"||string.IsNullOrWhiteSpace(p)||Value(s,"GameRunning") is not true||Int(s,"GameVersion")!=14200||Int(s,"OptionsLocation") is null or 1||Int(s,"PlayerControl") is not (0 or 1)||Int(s,"GamePhase") is null or 7 or 8||Int(s,"SessionCode") is not (>=0 and <=13)||Value(s,"FixedSetup") is not bool||Number(s,"SessionElapsed") is null||Number(s,"SessionStart") is null||Int(s,"PlayerSlotId") is null||Value(s,"PlayerVehicleFile") is not string f||string.IsNullOrWhiteSpace(f)||Value(s,"RawTrackName") is not string t||string.IsNullOrWhiteSpace(t))return null;
        return JsonSerializer.Serialize(new object?[]{p,f,t,Value(s,"SessionCode"),Value(s,"FixedSetup"),Value(s,"SessionStart"),Value(s,"PlayerSlotId")});
    }
    private static object? Value(Sample s,string k)=>s.Fields.GetValueOrDefault(k)?.Value;
    private static object? ValueOrNull(Sample? s,string k)=>s is null?null:Value(s,k);
    private static double? NumberOrNull(Sample? s,string k)=>s is null?null:Number(s,k);
    private static int? Int(Sample s,string k)=>Value(s,k) switch{int i=>i,byte b=>b,_=>null};
    private static double? Number(Sample s,string k)=>Value(s,k) switch{double d when double.IsFinite(d)=>d,float f when float.IsFinite(f)=>f,_=>null};
}
