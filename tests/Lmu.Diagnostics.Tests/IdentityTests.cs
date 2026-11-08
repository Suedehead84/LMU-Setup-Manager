using System.Net;
using System.Text.Json.Nodes;
using Lmu.Diagnostics;

internal static class IdentityTests
{
    public static async Task Run(Action<bool,string> check)
    {
        string Fixture(string name)=>File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures",name));
        var vehiclesJson=Fixture("vehicles.synthetic.json");
        var tracksJson=Fixture("tracks.synthetic.json");
        var summaryJson=Fixture("summary.synthetic.json");
        var vehicles=JsonNode.Parse(vehiclesJson)!.AsArray().OfType<JsonObject>().ToArray();
        var tracks=JsonNode.Parse(tracksJson)!.AsArray().OfType<JsonObject>().ToArray();
        string Str(JsonObject row,string key)=>row[key]!.GetValue<string>();
        string Model(JsonObject row)=>Str(row,"fullPathTree").Split(',').Last().Trim();
        string Stem(JsonObject row)=>Str(row,"vehFile").Replace('\\','/').Split('/').Last()[..^4];
        var bmw=vehicles.First(v=>Str(v,"desc")=="Synthetic Team One A");
        var otherTeam=vehicles.First(v=>Str(v,"vehFile").Contains("SYNTHETIC_ASTER_GT3") && Str(v,"team")!=Str(bmw,"team"));
        var otherSkin=vehicles.First(v=>Str(v,"desc")=="Synthetic Team One B");
        var mclaren=vehicles.First(v=>Str(v,"vehFile").Contains("SYNTHETIC_BOREAL_A"));
        var monza=tracks.Single(t=>Str(t,"sceneDesc")=="SYNTHETIC_ALPHA_GP");
        var grande=tracks.Single(t=>Str(t,"sceneDesc")=="SYNTHETIC_ALPHA_ALT");
        var silverstone=tracks.Single(t=>Str(t,"sceneDesc")=="SYNTHETIC_BETA_SHORT");
        LiveIdentityContext Live(JsonObject v,JsonObject t)=>new(Model(v),Stem(v),Str(t,"properTrackName"),"process-1","10","True","42","0",100);
        string Summary(JsonObject v,JsonObject t)=>new JsonObject {
            ["car"]=new JsonObject { ["id"]=Str(v,"id"),["vehFile"]=Str(v,"vehFile"),["fullPathTree"]=Str(v,"fullPathTree") },
            ["track"]=new JsonObject {["id"]=Str(t,"id"),["sceneDesc"]=Str(t,"sceneDesc")},
            ["currentTrackFolder"]="UNUSED"
        }.ToJsonString();
        SessionIdentity Resolve(JsonObject v,JsonObject t)=>IdentityResolver.Resolve(Live(v,t),Summary(v,t),vehiclesJson,tracksJson);
        var initial=IdentityResolver.Resolve(Live(bmw,monza),summaryJson,vehiclesJson,tracksJson);
        check(initial.SetupKey==new SetupIdentityKey("SYNTHETIC_ASTER_GT3","SYNTHETIC_ALPHA","SYNTHETIC_ALPHA_GP"),"synthetic BMW/Monza summary binds technical component and scene");
                var liveCurvaVehicle=vehicles.Single(v=>Str(v,"id")=="SYNTHETIC_ASTER_03");
        var actualCurva=IdentityResolver.Resolve(Live(liveCurvaVehicle,grande),Fixture("summary-alt.synthetic.json"),vehiclesJson,tracksJson);
        check(actualCurva.SetupKey==new SetupIdentityKey("SYNTHETIC_ASTER_GT3","SYNTHETIC_ALPHA","SYNTHETIC_ALPHA_ALT"),"synthetic garage selection: other BMW livery on Monza Curva Grande");
        check(Resolve(otherTeam,monza).SetupKey==initial.SetupKey,"different synthetic teams yield same model key");
        check(Resolve(otherSkin,monza).SetupKey==initial.SetupKey,"different synthetic liveries yield same model key");
        check(Resolve(mclaren,monza).VehicleModelId!=initial.VehicleModelId && Resolve(mclaren,monza).SetupKey is not null,"different synthetic models remain separate");
        check(Resolve(bmw,grande).TrackId==initial.TrackId && Resolve(bmw,grande).TrackLayoutId!=initial.TrackLayoutId,"synthetic circuit layouts share track but not layout key");
        check(Resolve(bmw,silverstone).TrackId!=initial.TrackId,"different circuits remain separate");
        foreach(var package in new[]{"SYNTHETIC_CIRRUS_A","SYNTHETIC_CIRRUS_B","SYNTHETIC_BOREAL_B"})
        {
            var v=vehicles.First(v=>Str(v,"vehFile").Contains("/"+package+"/") || Str(v,"vehFile").Contains("\\"+package+"\\"));
            check(Resolve(v,monza).VehicleModelId==package,"same display model can retain technical generation/package "+package);
        }
        foreach(var ferrari in vehicles.Where(v=>Str(v,"vehFile").Contains("SYNTHETIC_DAWN")).GroupBy(Model).Select(g=>g.First()))
            check(Resolve(ferrari,monza).SetupKey is null,"same Dawn package/sig cannot silently merge variant "+Model(ferrari));
        check(IdentityResolver.Resolve(Live(bmw,monza),Summary(otherSkin,monza),vehiclesJson,tracksJson).SetupKey==initial.SetupKey,"UI car selection does not replace the native player binding");
        check(IdentityResolver.Resolve(Live(bmw,grande),summaryJson,vehiclesJson,tracksJson).TrackLayoutId=="SYNTHETIC_ALPHA_ALT","UI track selection cannot override native active layout");
        check(IdentityResolver.Resolve(Live(bmw,monza) with {ModelName="Other Model"},summaryJson,vehiclesJson,tracksJson).SetupKey is null,"conflicting native model rejected");
        foreach(var bad in new[]{"null","{}","[]","invalid"})
            check(IdentityResolver.Resolve(Live(bmw,monza),bad,vehiclesJson,tracksJson).SetupKey==initial.SetupKey,"missing/malformed UI summary cannot revoke active identity: "+bad);
        check(IdentityResolver.Resolve(Live(bmw,monza),summaryJson,"[]",tracksJson).SetupKey is null,"empty vehicle catalog rejected");
        check(IdentityResolver.Resolve(Live(bmw,monza),summaryJson,vehiclesJson,"[]").SetupKey is null,"empty track catalog rejected");
        var conflictingVehicles=JsonNode.Parse(vehiclesJson)!.AsArray();
        conflictingVehicles.Add(bmw.DeepClone());
        check(IdentityResolver.Resolve(Live(bmw,monza),summaryJson,conflictingVehicles.ToJsonString(),tracksJson).SetupKey is null,"duplicate vehicle filename binding rejected");
        var conflictingTracks=JsonNode.Parse(tracksJson)!.AsArray();
        var duplicate=(JsonObject)monza.DeepClone();duplicate["id"]="SYNTHETIC-other-id";duplicate["sceneDesc"]="SYNTHETIC-other-scene";
        conflictingTracks.Add(duplicate);
        check(IdentityResolver.Resolve(Live(bmw,monza),summaryJson,vehiclesJson,conflictingTracks.ToJsonString()).SetupKey is null,"same native track label cannot bind multiple technical scenes");
                var spa=tracks.First(t=>Str(t,"sceneDesc")=="SYNTHETIC_GAMMA_A");
        check(Resolve(bmw,spa).SetupKey is null,"synthetic Gamma WEC/ELMS ambiguity remains unknown");
        var renamedSummary=JsonNode.Parse(summaryJson)!.AsObject();
        renamedSummary["currentTrackFolder"]="different alias";
        renamedSummary["car"]!["id"]="different-entry-id";
        renamedSummary["car"]!["name"]="SYNTHETIC arbitrary team #999";
        check(IdentityResolver.Resolve(Live(bmw,monza),renamedSummary.ToJsonString(),vehiclesJson,tracksJson).SetupKey==initial.SetupKey,"team labels, car.id and folder alias are not library keys");

        var at=DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        Sample SampleFor(LiveIdentityContext ctx,DateTimeOffset now)
        {
            var f=Decoder.Empty();
            foreach(var (key,value) in new (string,object)[]{
                ("RawVehicleModel",ctx.ModelName),("PlayerVehicleFile",ctx.VehicleFile),("RawTrackName",ctx.TrackName),
                ("SessionCode",ctx.Session),("FixedSetup",ctx.Fixed),("PlayerSlotId",ctx.PlayerSlot),
                ("SessionStart",ctx.Start),("SessionElapsed",ctx.Elapsed),("TelemetryElapsed",ctx.Elapsed)})
                f[key]=new(value,"SYNTHETIC transition fixture");
            return new(now,"LIVE",f);
        }
        RestCapture[] Captures(DateTimeOffset start,string summary)=>new[]{
            new RestCapture(start.AddMilliseconds(100),"http://127.0.0.1:6397/rest/garage/summary",200,summary,null,start),
            new RestCapture(start.AddMilliseconds(100),"http://127.0.0.1:6397/rest/sessions/getAllVehicles",200,vehiclesJson,null,start),
            new RestCapture(start.AddMilliseconds(100),"http://127.0.0.1:6397/rest/sessions/getTracksAll",200,tracksJson,null,start)
        };
        var coordinator=new IdentityCoordinator();
        var current=Live(bmw,monza);
        var now=at;
        void Native()=>coordinator.UpdateLive(SampleFor(current,now),current.ProcessInstance);
        void Advance(double seconds)
        {
            int ticks=(int)Math.Round(seconds*10);
            for(int i=0;i<ticks;i++)
            {
                now=now.AddMilliseconds(100);current=current with {Elapsed=current.Elapsed+.1};
                Native();coordinator.Current(now);
            }
        }
        void Poll(string summary,Func<RestCapture[],RestCapture[]>? mutate=null)
        {
            Native();var ticket=coordinator.BeginRequest(now);
            var batch=Captures(now,summary);if(mutate is not null)batch=mutate(batch);
            Advance(.2);coordinator.Complete(ticket,batch,now);
        }
        void Confirm(string summary){Poll(summary);Advance(3);Poll(summary);}
        Native();
        var firstTicket=coordinator.BeginRequest(now);var firstBatch=Captures(now,summaryJson);
        Advance(.2);coordinator.Complete(firstTicket,firstBatch,now);
        check(coordinator.Current(now).SetupKey is null,"one content-catalog batch is insufficient after transition");
        coordinator.Complete(firstTicket,firstBatch,now);
        check(coordinator.Current(now).SetupKey is null,"same catalog batch cannot confirm itself twice");
        Advance(3);Poll(summaryJson);
        check(coordinator.Current(now).SetupKey==initial.SetupKey,"two fresh catalogs and current native binding confirm identity");
        foreach(var ui in new[]{Summary(otherSkin,monza),Summary(mclaren,grande),"{}","invalid"})
        {
            Advance(3);Poll(ui);
            check(coordinator.Current(now).SetupKey==initial.SetupKey,"unrelated or malformed garage UI does not revoke established player identity");
        }
        Advance(3);
        Poll(summaryJson,rows=>rows.Select(r=>r.Url.EndsWith("/summary")?r with {Status=503,Body=null,Error="SYNTHETIC UI failure"}:r).ToArray());
        check(coordinator.Current(now).SetupKey==initial.SetupKey,"UI transport failure is not a player identity failure");
        Advance(3);
        Poll(summaryJson,rows=>rows.Select(r=>r.Url.EndsWith("getAllVehicles")?r with {Status=null,Body=null,Error="SYNTHETIC catalog timeout"}:r).ToArray());
        check(coordinator.Current(now).SetupKey==initial.SetupKey && coordinator.CatalogRefreshWarning is not null,
            "temporary catalog refresh failure retains proved mapping only with current unchanged native context");
        Advance(61);
        check(coordinator.Current(now).SetupKey is null,"old content metadata expires after 60 s despite live native data");
        Poll(summaryJson);
        check(coordinator.Current(now).SetupKey is null,"expired metadata requires two new confirmations");
        Advance(3);Poll(summaryJson);
        check(coordinator.Current(now).SetupKey==initial.SetupKey,"fresh metadata restores the same active binding");
        Advance(.1);var inFlight=coordinator.BeginRequest(now);var oldBatch=Captures(now,summaryJson);
        current=Live(mclaren,grande) with {Elapsed=current.Elapsed};
        Native();Advance(.2);coordinator.Complete(inFlight,oldBatch,now);
        check(coordinator.Current(now).SetupKey is null,"in-flight catalogs from old native context are discarded");
        Confirm(Summary(mclaren,grande));
        check(coordinator.Current(now).SetupKey==Resolve(mclaren,grande).SetupKey,"real native vehicle/layout change resolves a new identity");
        foreach(var change in new Func<LiveIdentityContext,LiveIdentityContext>[]{
            c=>c with {Fixed=c.Fixed=="True"?"False":"True"},c=>c with {Elapsed=0},
            c=>c with {ProcessInstance=c.ProcessInstance+"-new"},
            c=>c with {VehicleFile=Stem(bmw),ModelName=Model(bmw)},
            c=>c with {TrackName=Str(silverstone,"properTrackName")}})
        {
            current=change(current);Native();
            check(coordinator.Current(now).SetupKey is null,"native fixed/session/process/vehicle/layout change invalidates old mapping immediately");
            Confirm("{}");
        }
        coordinator.UpdateLive(new(now,"LIVE",Decoder.Empty(),"disconnected"),"");
        check(coordinator.Current(now).SetupKey is null,"lost native source invalidates identity immediately");
        Native();Confirm("{}");
        check(coordinator.Current(now.AddSeconds(2.1)).SetupKey is null,"no new native observations cannot retain identity");
        Native();Confirm("{}");
        var unchanged=current;now=now.AddSeconds(3);current=unchanged with {Elapsed=unchanged.Elapsed+3};Native();
        check(coordinator.Current(now).SetupKey is null,"native observation gap resets mapping even if fingerprint later returns unchanged");
        Confirm("{}");
        Advance(3);
        Poll("{}",rows=>rows.Select(r=>r.Url.EndsWith("getAllVehicles")?r with {Body="[]"}:r).ToArray());
        check(coordinator.Current(now).SetupKey is null,"successful contradictory catalog invalidates; no blind old-mapping fallback");
        Confirm("{}");
        Advance(.1);var lateTicket=coordinator.BeginRequest(now);var lateBatch=Captures(now,"{}");Advance(6);
        coordinator.Complete(lateTicket,lateBatch,now);
        check(coordinator.CatalogRefreshWarning is not null,"delayed response cannot refresh metadata validity");
        var invalidClock=SampleFor(current,now);
        invalidClock.Fields["TelemetryElapsed"]=new(null,"SYNTHETIC invalid physics clock");
        coordinator.UpdateLive(invalidClock,current.ProcessInstance);
        check(coordinator.Current(now).SetupKey is null,"invalid provided physics clock cannot fall back to advancing scoring clock");
        Native();Confirm("{}");
        for(int i=0;i<22;i++){now=now.AddMilliseconds(100);Native();coordinator.Current(now);}
        check(coordinator.Current(now).SetupKey is null,"repeated frozen native frames cannot keep a proved identity alive");
        Advance(.1);Confirm("{}");
        var markerSample=SampleFor(current,now);
        markerSample.Fields["LapNumber"]=new(4,"SYNTHETIC");
        markerSample.Fields["LapStartET"]=new(90.0,"SYNTHETIC");
        coordinator.UpdateLive(markerSample,current.ProcessInstance);
        markerSample.Fields["LapNumber"]=new(0,"SYNTHETIC");
        markerSample.Fields["LapStartET"]=new(-1.0,"SYNTHETIC");
        coordinator.UpdateLive(markerSample,current.ProcessInstance);
        check(coordinator.Current(now).SetupKey is null,"native lap-marker reset clears identity even without physics clock rollback");
        var activeSample=SampleFor(current,now);
        var uiCapture=new RestCapture(now,"http://127.0.0.1:6397/rest/garage/summary",200,Summary(mclaren,monza),null,now);
        var selection=GarageUiContext.Read(uiCapture,activeSample);
        check(!selection.AuthoritativeForActivePlayer && selection.MatchesNativePlayerFile==false,
            "garage diagnostic explicitly separates nonmatching UI context from active player");
        var handler=new FixtureHandler(path=>path switch {
            "/rest/garage/summary"=>summaryJson,"/rest/sessions/getAllVehicles"=>vehiclesJson,
            "/rest/sessions/getTracksAll"=>tracksJson,_=>"{}" });
        using(var probe=new RestProbe("http://127.0.0.1:6397",handler))
        {
            var captures=await probe.Poll(CancellationToken.None);
            check(handler.Methods.All(m=>m=="GET") && handler.Methods.Count==6,"all diagnostic REST requests remain GET-only");
            check(captures.All(c=>c.Status==200 && c.RequestedAt<=c.ObservedAt),"HTTP adapter captures request/response timestamps");
        }
        using(var probe=new RestProbe("http://127.0.0.1:6397",new FailingHandler()))
        {
            var captures=await probe.Poll(CancellationToken.None);
            check(captures.All(c=>c.Status is null && c.Error is not null),"unreachable REST caught by actual HTTP adapter");
        }
        Console.WriteLine("Identity fixtures, transitions and HTTP faults are entirely SYNTHETIC.");
    }
    private sealed class FixtureHandler(Func<string,string> body):HttpMessageHandler
    {
        public List<string> Methods {get;}=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Methods.Add(request.Method.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content=new StringContent(body(request.RequestUri!.AbsolutePath)) });
        }
    }
    private sealed class FailingHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            =>Task.FromException<HttpResponseMessage>(new HttpRequestException("SYNTHETIC unreachable"));
    }
}




