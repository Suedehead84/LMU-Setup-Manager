using Lmu.Localization;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lmu.Diagnostics;

public sealed record SetupIdentityKey(string VehicleModelId, string TrackId, string TrackLayoutId);
public sealed record SessionIdentity(
    string? VehicleModelId, string? VehicleModelName,
    string? TrackId, string? TrackName, string? TrackLayoutId, string? TrackLayoutName,
    string IdentitySource, string IdentityConfidence, string? Reason)
{
    public SetupIdentityKey? SetupKey => IdentityConfidence == "Corroborated" &&
        VehicleModelId is not null && TrackId is not null && TrackLayoutId is not null
        ? new(VehicleModelId, TrackId, TrackLayoutId) : null;
    public static SessionIdentity Unknown(string reason) => new(null,null,null,null,null,null,"none","Unknown",reason);
    public void AddTo(Sample sample)
    {
        foreach(var (name,value) in new (string,object?)[] {
            ("VehicleModelId",VehicleModelId),("VehicleModelName",VehicleModelName),
            ("TrackId",TrackId),("TrackName",TrackName),("TrackLayoutId",TrackLayoutId),
            ("TrackLayoutName",TrackLayoutName),("IdentitySource",IdentitySource),
            ("IdentityConfidence",IdentityConfidence),("IdentityReason",Reason)})
            sample.Fields[name]=new(value,IdentitySource,Note:Reason);
    }
}
public sealed record LiveIdentityContext(string ModelName,string VehicleFile,string TrackName,
    string ProcessInstance,string Session,string Fixed,string PlayerSlot,string Start,double Elapsed,int? LapNumber=null,double? LapStartET=null)
{
    public string Fingerprint => JsonSerializer.Serialize(new[] {
        ModelName,VehicleFile,TrackName,ProcessInstance,Session,Fixed,PlayerSlot,Start });
    public static LiveIdentityContext? From(Sample sample,string processInstance)
    {
        string? Get(string key) => sample.Fields.GetValueOrDefault(key)?.Value is { } value
            ? Convert.ToString(value,CultureInfo.InvariantCulture) : null;
        var clock=sample.Fields.ContainsKey("TelemetryElapsed") ? Get("TelemetryElapsed"):Get("SessionElapsed");
        var model=Get("RawVehicleModel");var file=Get("PlayerVehicleFile");var track=Get("RawTrackName");
        if(sample.Mode!="LIVE" || sample.Error is not null || string.IsNullOrEmpty(processInstance) ||
            string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(track) ||
            Get("SessionCode") is not { } session || Get("FixedSetup") is not { } fixedSetup ||
            Get("PlayerSlotId") is not { } slot || Get("SessionStart") is not { } start ||
            !double.TryParse(clock,CultureInfo.InvariantCulture,out var elapsed) || !double.IsFinite(elapsed))
            return null;
        return new(model.Trim(),file.Trim(),track.Trim(),processInstance,session,fixedSetup,slot,start,elapsed,
            int.TryParse(Get("LapNumber"),CultureInfo.InvariantCulture,out var lap) ? lap:null,
            double.TryParse(Get("LapStartET"),CultureInfo.InvariantCulture,out var lapStart) && double.IsFinite(lapStart) ? lapStart:null);
    }
}
public static class IdentityResolver
{
    public sealed record CatalogVehicle(string Path,string Stem,string Package,string Model,string Class);
    public sealed record CatalogTrack(string Id,string Package,string Scene,string Name,string LayoutName,string? EventName);
    private sealed record Track(string Id,string Package,string Scene,string Name,string LayoutName);
    private static string? Text(JsonElement e,string key) => e.ValueKind==JsonValueKind.Object &&
        e.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : null;
    private static string CanonicalPath(string path) => path.Replace('/','\\').ToUpperInvariant();
    // Exact observed Installed/Vehicles/<component>/<version>/<entry>.VEH structure.
    // No substring matching of teams, numbers, models, years or class names.
    private static CatalogVehicle? ParseVehicle(JsonElement e)
    {
        var path=Text(e,"vehFile");var tree=Text(e,"fullPathTree");
        if(path is null || tree is null) return null;
        var parts=path.Replace('/','\\').Split('\\',StringSplitOptions.RemoveEmptyEntries);
        var anchors=Enumerable.Range(0,Math.Max(0,parts.Length-1))
            .Where(i=>parts[i].Equals("Installed",StringComparison.OrdinalIgnoreCase) &&
                parts[i+1].Equals("Vehicles",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(anchors.Length!=1 || parts.Length!=anchors[0]+5) return null;
        int p=anchors[0];
        if(!Regex.IsMatch(parts[p+2],@"\A[A-Za-z0-9_.-]+\z") ||
            !Regex.IsMatch(parts[p+3],@"\A[0-9]+(?:\.[0-9]+)*\z") ||
            !parts[p+4].EndsWith(".VEH",StringComparison.OrdinalIgnoreCase) || parts[p+4].Length<=4) return null;
        var labels=tree.Split(',',StringSplitOptions.TrimEntries);
        if(labels.Length!=3 || labels.Any(string.IsNullOrWhiteSpace)) return null;
        return new(CanonicalPath(path),parts[p+4][..^4],parts[p+2],labels[2],labels[1]);
    }
    public static IReadOnlyList<CatalogVehicle> ReadVehicleCatalog(string json)
    {
        using var doc=JsonDocument.Parse(json);
        if(doc.RootElement.ValueKind!=JsonValueKind.Array)throw new FormatException(UiText.C("diagnostics.unknownVehicleCatalogSchema"));
        var rows=doc.RootElement.EnumerateArray().Select(ParseVehicle).ToArray();
        if(rows.Length==0||rows.Any(v=>v is null))throw new FormatException(UiText.C("diagnostics.incompleteVehicleCatalogOrUnknownModelKeys"));
        return rows.OfType<CatalogVehicle>().ToArray();
    }
    public static IReadOnlyList<CatalogTrack> ReadTrackCatalog(string json)
    {
        using var doc=JsonDocument.Parse(json);
        if(doc.RootElement.ValueKind!=JsonValueKind.Array)throw new FormatException(UiText.C("diagnostics.unknownTrackCatalogSchema"));
        var rows=doc.RootElement.EnumerateArray().Select(t=>new CatalogTrack(Text(t,"id")??"",Text(t,"cmpName")??"",Text(t,"sceneDesc")??"",
            Text(t,"trackName")??"",Text(t,"properTrackName")??"",Text(t,"eventName"))).ToArray();
        if(rows.Length==0||rows.Any(t=>string.IsNullOrEmpty(t.Id)||string.IsNullOrEmpty(t.Package)||string.IsNullOrEmpty(t.Scene)||string.IsNullOrEmpty(t.Name)||string.IsNullOrEmpty(t.LayoutName)))
            throw new FormatException(UiText.C("diagnostics.incompleteTrackCatalog"));
        return rows;
    }
    // Kept as a source-compatible overload: garage selection is deliberately NOT a player identity input.
    public static SessionIdentity Resolve(LiveIdentityContext live,string summaryJson,string vehiclesJson,string tracksJson)
        => ResolveActive(live,vehiclesJson,tracksJson);
    public static SessionIdentity ResolveActive(LiveIdentityContext live,string vehiclesJson,string tracksJson)
    {
        try
        {
            using var vehiclesDoc=JsonDocument.Parse(vehiclesJson);
            using var tracksDoc=JsonDocument.Parse(tracksJson);
            if(vehiclesDoc.RootElement.ValueKind!=JsonValueKind.Array || tracksDoc.RootElement.ValueKind!=JsonValueKind.Array)
                return SessionIdentity.Unknown(UiText.C("diagnostics.missingOrUnknownContentCatalogSchema"));
            var vehicles=vehiclesDoc.RootElement.EnumerateArray().Select(ParseVehicle).ToArray();
            if(vehicles.Length==0 || vehicles.Any(v=>v is null))
                return SessionIdentity.Unknown(UiText.C("diagnostics.incompleteVehicleCatalogOrUnknownVehfileFullpathtreeSchema"));
            var entries=vehicles.OfType<CatalogVehicle>().ToArray();
            var byFile=entries.Where(v=>v.Stem.Equals(live.VehicleFile,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(byFile.Length!=1)
                return SessionIdentity.Unknown(UiText.C("diagnostics.activeShmVehicleFileIsNotUniqueIn"));
            var selected=byFile[0];
            var modelLabels=entries.Where(v=>v.Package.Equals(selected.Package,StringComparison.OrdinalIgnoreCase))
                .Select(v=>v.Model).Distinct(StringComparer.Ordinal).ToArray();
            if(modelLabels.Length!=1)
                return SessionIdentity.Unknown(UiText.C("diagnostics.vehiclePackageContainsMultipleModelVariantsNoUnique"));
            if(selected.Model!=live.ModelName)
                return SessionIdentity.Unknown(UiText.C("diagnostics.activeShmModelConflictsWithTheCatalogModel"));
            var tracks=tracksDoc.RootElement.EnumerateArray().Select(t=>new Track(
                Text(t,"id")??"",Text(t,"cmpName")??"",Text(t,"sceneDesc")??"",
                Text(t,"trackName")??"",Text(t,"properTrackName")??"")).ToArray();
            if(tracks.Length==0 || tracks.Any(t=>string.IsNullOrEmpty(t.Id) || string.IsNullOrEmpty(t.Package) ||
                string.IsNullOrEmpty(t.Scene) || string.IsNullOrEmpty(t.Name) || string.IsNullOrEmpty(t.LayoutName)))
                return SessionIdentity.Unknown(UiText.C("diagnostics.incompleteTrackCatalog"));
            // A unique exact native track-name -> component/scene binding; never use a UI-selected track.
            var matches=tracks.Where(t=>t.LayoutName==live.TrackName).ToArray();
            if(matches.Select(t=>(t.Package,t.Scene)).Distinct().Count()!=1)
                return SessionIdentity.Unknown(UiText.C("diagnostics.activeShmTrackNameMatchesNoTechnicalLayout"));
            var current=matches[0];
            if(matches.Select(t=>(t.Name,t.LayoutName)).Distinct().Count()!=1)
                return SessionIdentity.Unknown(UiText.C("diagnostics.conflictingMetadataForTheSameTechnicalLayout"));
            return new(selected.Package,live.ModelName,current.Package,current.Name,current.Scene,current.LayoutName,
                "LMU_Data.player(mVehicleModel,mVehFilename,mTrackName) + REST content catalogs(getAllVehicles,getTracksAll)",
                "Corroborated",null);
        }
        catch(JsonException) { return SessionIdentity.Unknown(UiText.C("diagnostics.contentCatalogIsNotValidJson")); }
    }
}
public sealed record IdentityTicket(long Epoch,DateTimeOffset RequestedAt,bool Active,long RequestId);
public sealed class IdentityCoordinator
{
    // A response batch must itself still be recent. Content metadata is not a live UI selection.
    public static readonly TimeSpan MaximumAge=TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumCatalogAge=TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaximumLiveAge=TimeSpan.FromSeconds(2);
    private LiveIdentityContext? live;
    private long epoch,nextRequestId,lastCompletedRequest;
    private SessionIdentity result=SessionIdentity.Unknown(UiText.C("diagnostics.waitingForTheCurrentSessionAndContentCatalogs"));
    private SessionIdentity? candidate;
    private DateTimeOffset catalogValidatedAt,candidateCompletedAt,lastNativeAt,lastPhysicsProgress;
    private int confirmations;
    public string? CatalogRefreshWarning {get;private set;}
    public void UpdateLive(Sample sample,string processInstance)
    {
        var next=LiveIdentityContext.From(sample,processInstance);
        bool changed=next is null || live is null || next.Fingerprint!=live.Fingerprint ||
            next.Elapsed<live.Elapsed ||
            (next.LapNumber is { } lap && live.LapNumber is { } oldLap && lap<oldLap) ||
            (next.LapStartET is { } start && live.LapStartET is { } oldStart && start<oldStart) ||
            sample.ObservedAt<lastNativeAt ||
            (live is not null && sample.ObservedAt-lastNativeAt>MaximumLiveAge);
        if(changed)
        {
            epoch++;Invalidate(next is null ? UiText.C("diagnostics.noFreshPlayerSessionIdentity") :
                UiText.C("diagnostics.nativeContextChanged"));
            catalogValidatedAt=default;CatalogRefreshWarning=null;
            lastPhysicsProgress=sample.ObservedAt;
        }
        else if(next!.Elapsed>live!.Elapsed) lastPhysicsProgress=sample.ObservedAt;
        live=next;
        lastNativeAt=sample.ObservedAt;
    }
    public IdentityTicket BeginRequest(DateTimeOffset now)=>new(epoch,now,live is not null,++nextRequestId);
    public void Complete(IdentityTicket ticket,IReadOnlyList<RestCapture> captures,DateTimeOffset now)
    {
        if(!ticket.Active || ticket.Epoch!=epoch || live is null || ticket.RequestId<=lastCompletedRequest) return;
        lastCompletedRequest=ticket.RequestId;
        string[] paths=["/rest/sessions/getAllVehicles","/rest/sessions/getTracksAll"];
        var required=paths.Select(path=>captures.Where(c=>Uri.TryCreate(c.Url,UriKind.Absolute,out var uri) &&
            uri.AbsolutePath==path).ToArray()).ToArray();
        if(required.Any(rows=>rows.Length!=1 || rows[0].Status!=200 || rows[0].Body is null ||
            rows[0].RequestedAt<ticket.RequestedAt || rows[0].ObservedAt<rows[0].RequestedAt ||
            rows[0].ObservedAt>now || now-rows[0].RequestedAt>MaximumAge))
        {
            CatalogRefreshWarning=UiText.C("diagnostics.contentCatalogRefreshMissingOrLateRetainThe");
            candidate=null;confirmations=0;
            // Transport failure is not evidence of a changed player. Bound retention, never infinite fallback.
            if(result.SetupKey is null) Invalidate(UiText.C("diagnostics.noConfirmedMappingAndNoCurrentContentCatalog"));
            return;
        }
        var next=IdentityResolver.ResolveActive(live,required[0][0].Body!,required[1][0].Body!);
        if(next.SetupKey is null)
        {
            // A successful contradictory/ambiguous catalog is positive evidence against the old mapping.
            CatalogRefreshWarning=null;Invalidate(next.Reason??UiText.C("diagnostics.activeIdentityIsAmbiguous"));catalogValidatedAt=default;return;
        }
        CatalogRefreshWarning=null;
        if(result.SetupKey is not null && result==next)
        {
            catalogValidatedAt=required.Select(rows=>rows[0].RequestedAt).Min();
            candidateCompletedAt=now;candidate=next;confirmations=2;return;
        }
        // Response freshness is checked above. Measure the independent follow-up request
        // from completion of the first proof, not from its request start: otherwise the
        // two HTTP latencies consume the confirmation interval despite continuous physics.
        if(candidate is null || ticket.RequestedAt<candidateCompletedAt || ticket.RequestedAt-candidateCompletedAt>MaximumAge)
            confirmations=0;
        confirmations=candidate==next ? confirmations+1:1;
        candidate=next;
        candidateCompletedAt=now;
        catalogValidatedAt=required.Select(rows=>rows[0].RequestedAt).Min();
        result=confirmations>=2 ? next : SessionIdentity.Unknown(UiText.C("diagnostics.firstActiveContentCatalogCheckMatchedWaitingFor"));
    }
    private void Invalidate(string reason)
    {candidate=null;confirmations=0;result=SessionIdentity.Unknown(reason);}
    public SessionIdentity Current(DateTimeOffset now)
    {
        if(live is null || now<lastNativeAt || now-lastNativeAt>MaximumLiveAge ||
            now-lastPhysicsProgress>MaximumLiveAge)
        {Invalidate(UiText.C("diagnostics.activeShmIdentityIsMissingFrozenOrOlder"));return result;}
        if(result.SetupKey is not null && (now<catalogValidatedAt || now-catalogValidatedAt>MaximumCatalogAge))
            Invalidate(UiText.C("diagnostics.confirmedContentCatalogMappingIsOlderThan60"));
        return result;
    }
}
public sealed record GarageUiContext(DateTimeOffset ObservedAt,int? HttpStatus,string? SelectedVehicleFile,
    string? SelectedModelLabel,string? SelectedTrackId,string? SelectedScene,bool? MatchesNativePlayerFile,
    bool AuthoritativeForActivePlayer,string Note)
{
    public static GarageUiContext Read(RestCapture capture,Sample sample)
    {
        string? file=null,model=null,trackId=null,scene=null;
        if(capture.Status==200 && capture.Body is not null)
        {
            try
            {
                using var doc=JsonDocument.Parse(capture.Body);
                var root=doc.RootElement;
                if(root.ValueKind==JsonValueKind.Object && root.TryGetProperty("car",out var car) && car.ValueKind==JsonValueKind.Object)
                {
                    if(car.TryGetProperty("vehFile",out var f) && f.ValueKind==JsonValueKind.String) file=f.GetString();
                    if(car.TryGetProperty("fullPathTree",out var m) && m.ValueKind==JsonValueKind.String) model=m.GetString();
                }
                if(root.ValueKind==JsonValueKind.Object && root.TryGetProperty("track",out var track) && track.ValueKind==JsonValueKind.Object)
                {
                    if(track.TryGetProperty("id",out var id) && id.ValueKind==JsonValueKind.String) trackId=id.GetString();
                    if(track.TryGetProperty("sceneDesc",out var s) && s.ValueKind==JsonValueKind.String) scene=s.GetString();
                }
            }catch(JsonException) { }
        }
        var native=sample.Error is null ? sample.Fields.GetValueOrDefault("PlayerVehicleFile")?.Value as string:null;
        var leaf=file?.Replace('\\','/').Split('/').LastOrDefault();
        var stem=leaf?.EndsWith(".VEH",StringComparison.OrdinalIgnoreCase)==true ? leaf[..^4]:null;
        bool? matches=native is not null && stem is not null ? stem.Equals(native,StringComparison.OrdinalIgnoreCase):null;
        return new(capture.ObservedAt,capture.Status,file,model,trackId,scene,matches,false,
            UiText.C("diagnostics.garageUiSelectionContextNotEvidenceOfThe"));
    }
}


