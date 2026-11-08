using System.Text;
using Lmu.Diagnostics;
using Lmu.LiveCapture;
using Lmu.SetupLibrary;
using Lmu.Localization;
using Lmu.Web;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

static class Rc2CaptureTests
{
    public static async Task Passive()
    {
        var source=new NativeFrameSource();using var transport=new LmuCatalogTransport();
        var engine=new LiveStateEngine();var coordinator=new IdentityCoordinator();Sample? previous=null;
        var until=DateTimeOffset.UtcNow.AddSeconds(18);var next=DateTimeOffset.MinValue;string? last=null;
        Task<RestCapture[]>? pending=null;IdentityTicket? a=null,b=null;bool saved=false;
        while(DateTimeOffset.UtcNow<until){
            var frame=source.Read();engine.Observe(frame);coordinator.UpdateLive(frame.Sample,frame.ProcessInstance);
            if(pending?.IsCompleted==true){var rows=await pending;engine.Complete(a!,rows);coordinator.Complete(b!,rows,DateTimeOffset.UtcNow);Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Catalog=rows.Select(r=>new{r.Status,Milliseconds=(r.ObservedAt-r.RequestedAt).TotalMilliseconds}).ToArray()}));pending=null;next=DateTimeOffset.UtcNow.AddSeconds(3);}
            if(pending is null&&DateTimeOffset.UtcNow>=next){a=engine.BeginRequest();b=coordinator.BeginRequest(DateTimeOffset.UtcNow);pending=transport.Read(default);}
            var status=engine.Status();string? rejection=null;
            if(status.CanCapture&&!saved){var db=Path.GetFullPath(Path.Combine("artifacts","rc2-live-isolated",Guid.NewGuid()+".sqlite"));
                var library=new SetupLibraryService(new LibraryDatabase(db),engine.Reader(status.ContextToken!));
                var captured=await library.CaptureNew("RC2 isolated verification",SetupCategory.FixedLab);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{IsolatedCapture=true,captured.Current.Content.Values,HasEvidence=captured.Current.Capture is not null}));saved=true;}
            try{if(previous is not null){var snapshot=LiveSetupReader.FromSamples(previous,frame.Sample,coordinator.Current(DateTimeOffset.UtcNow),frame.ProcessInstance,DateTimeOffset.UtcNow);SetupLibraryService.ValidateObservation(snapshot,DateTimeOffset.UtcNow);}}
            catch(LibraryValidationException e){rejection=e.Message;}
            var output=System.Text.Json.JsonSerializer.Serialize(new{status.Fresh,status.CanCapture,Identity=status.Key is not null,TelemetryIdentity=coordinator.Current(DateTimeOffset.UtcNow).SetupKey is not null,status.Values,status.Message,Rejection=rejection,
                IdentityReason=coordinator.Current(DateTimeOffset.UtcNow).Reason,frame.Sample.Error,At=frame.Sample.ObservedAt,
                ContextChanges=previous is null?[]:new[]{"RawVehicleModel","RawTrackName","PlayerVehicleFile","PlayerSlotId","SessionCode","FixedSetup","SessionStart"}.Where(k=>!Equals(previous.Fields.GetValueOrDefault(k)?.Value,frame.Sample.Fields.GetValueOrDefault(k)?.Value)).ToArray(),
                State=new[]{"TelemetryElapsed","LapNumber","LapStartET","GameVersion","PlayerControl","InGarage","InRealtime","GamePhase"}.ToDictionary(k=>k,k=>frame.Sample.Fields.GetValueOrDefault(k)?.Value),
                Maxima=new[]{"TCMax","TCCutMax","TCSlipMax","ABSMax"}.ToDictionary(k=>k,k=>frame.Sample.Fields.GetValueOrDefault(k)?.Value)});
            if(output!=last){Console.WriteLine(output);last=output;}previous=frame.Sample;await Task.Delay(100);
        }
    }
    public static async Task Run(Action<bool,string> check,string root,string vehicles,string tracks)
    {
        var clock=new RcClock(DateTimeOffset.UtcNow);
        var entry=IdentityResolver.ReadVehicleCatalog(vehicles).First(v=>v.Model=="SYNTHETIC Aster GT3");
        var scoring=new byte[Layout.S_Size];var telemetry=new byte[Layout.T_Size];var player=new byte[Layout.V_Size];
        void Text(byte[] bytes,int offset,string value){Array.Clear(bytes,offset,bytes==scoring?64:bytes==telemetry?30:32);Encoding.UTF8.GetBytes(value).CopyTo(bytes,offset);}
        void D(byte[] bytes,int offset,double value)=>BitConverter.GetBytes(value).CopyTo(bytes,offset);
        void I(byte[] bytes,int offset,int value)=>BitConverter.GetBytes(value).CopyTo(bytes,offset);
        Text(scoring,Layout.S_mTrackName,"SYNTHETIC Circuit Alpha");I(scoring,Layout.S_mSession,5);
        scoring[Layout.S_mInRealtime]=1;scoring[Layout.S_mGamePhase]=5;scoring[Layout.S_mIsFixedSetup]=1;
        Text(telemetry,Layout.T_mVehicleModel,entry.Model);Text(player,Layout.V_mVehFilename,entry.Stem);
        player[Layout.V_mIsPlayer]=1;player[Layout.V_mControl]=0;
        telemetry[Layout.T_mTC]=7;telemetry[Layout.T_mTCMax]=10;
        telemetry[Layout.T_mTCCut]=7;telemetry[Layout.T_mTCCutMax]=10;
        telemetry[Layout.T_mTCSlip]=7;telemetry[Layout.T_mTCSlipMax]=10;
        telemetry[Layout.T_mABS]=6;telemetry[Layout.T_mABSMax]=12;D(telemetry,Layout.T_mRearBrakeBias,.4875);
        var engine=new LiveStateEngine(clock);double elapsed=10;string? missing=null;bool running=true;
        void Observe(){if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();clock.Now=clock.Now.AddMilliseconds(100);elapsed+=.1;D(telemetry,Layout.T_mElapsedTime,elapsed);D(scoring,Layout.S_mCurrentET,elapsed);
            var sample=new Sample(clock.Now,"LIVE",Lmu.Diagnostics.Decoder.Decode(scoring,telemetry,player));sample.Fields["GameRunning"]=new(true,"SYNTHETIC raw ABI bytes");
            if(missing is not null)sample.Fields[missing]=new(null,"SYNTHETIC unavailable");
            engine.Observe(new(running,"SYNTHETIC-RC2",sample,SharedMemorySource.DecodeSession(scoring,player,3,clock.Now)));}
        void Bind(){for(int i=0;i<2;i++){Observe();var ticket=engine.BeginRequest();engine.Complete(ticket,[new(clock.Now,"http://127.0.0.1:6397/rest/sessions/getAllVehicles",200,vehicles,null,clock.Now),new(clock.Now,"http://127.0.0.1:6397/rest/sessions/getTracksAll",200,tracks,null,clock.Now)]);}}
        for(int attempt=0;attempt<2;attempt++){
            Observe();var ticket=engine.BeginRequest();var requested=clock.Now;
            for(int i=0;i<17;i++)Observe(); // Measured live catalog latency ~1.5-1.8s; physics continues.
            engine.Complete(ticket,[new(clock.Now,"http://127.0.0.1:6397/rest/sessions/getAllVehicles",200,vehicles,null,requested),new(clock.Now,"http://127.0.0.1:6397/rest/sessions/getTracksAll",200,tracks,null,requested)]);
            if(attempt==0)for(int i=0;i<30;i++)Observe(); // Same three-second completion-based worker delay.
        }
        var status=engine.Status();
        check(status.Fresh&&status.Values.TC==7&&status.Values.BrakeBiasFrontPercent==51.25,"RC2 native decoder provides five fresh values");
        check(status.Key is not null,"RC2 slow successful catalogs confirm scoring identity independently");
        check(status.CanCapture,"RC2 actual native decoder types allow capture with scoring identity");
        Bind();
        var service=new SetupLibraryService(new LibraryDatabase(Path.Combine(root,"rc2.sqlite")),engine.Reader(status.ContextToken!),clock);
        var saved=await service.CaptureNew("RC2",SetupCategory.FixedLab);
        check(saved.Current.Content.Values.BrakeBiasFrontPercent==51.25,"RC2 native decoded values save without existing setup");
        service.Delete(saved.Id,saved.Current.Id);
        check(service.Search(new()).Count==0&&service.Get(saved.Id,true).DeletedAt is not null,"RC2 soft delete retains history and removes only active setup");
        var replacement=await service.CaptureNew("After deletion",SetupCategory.FixedLab);
        check(replacement.Id!=saved.Id&&replacement.Current.Capture is not null,"RC2 deleting last setup does not block new live capture");
        foreach(var (key,label) in new[]{("TC","TC"),("TCCut","TC Cut"),("TCSlip","TC Slip"),("ABS","ABS"),("BrakeBias","Brake Bias")}){
            missing=key;Observe();var incomplete=engine.Status();
            check(incomplete.Fresh&&!incomplete.CanCapture&&incomplete.ContextToken is null,"RC2 missing "+label+" blocks capture despite other current values");
            var de=UiText.Localize(incomplete.Message,"de");var en=UiText.Localize(incomplete.Message,"en");
            check(de.Contains("fehlt",StringComparison.OrdinalIgnoreCase)&&en.Contains("missing",StringComparison.OrdinalIgnoreCase),"RC2 missing "+label+" reason translated DE/EN");
            if(key!="BrakeBias")check(de.Contains(label)&&en.Contains(label),"RC2 missing field explicitly names "+label);
            missing=null;Observe();Observe();check(engine.Status().CanCapture,"RC2 complete pair recovers after missing "+label);
        }
        telemetry[Layout.T_mTC]=0;telemetry[Layout.T_mTCCut]=0;telemetry[Layout.T_mTCSlip]=0;telemetry[Layout.T_mABS]=0;
        D(telemetry,Layout.T_mRearBrakeBias,1);Observe();Observe();
        check(engine.Status().CanCapture&&engine.Status().Values==new LiveElectronics(0,0,0,0,0),"RC2 five legal zero values are available, never treated as missing");
        var zero=await service.CaptureNew("Zero",SetupCategory.FixedLab);
        check(zero.Current.Content.Values==new Electronics(0,0,0,0,0),"RC2 native zero values persist without defaults or scale changes");
        D(telemetry,Layout.T_mRearBrakeBias,.47800000000000004);Observe();Observe();
        check(engine.Status().Values.BrakeBiasFrontPercent==52.2&&engine.Status().CanCapture,"RC2 rear fraction converts once to rounded front percent");
        clock.Now=clock.Now.AddSeconds(3);
        check(!engine.Status().CanCapture&&engine.Status().Values==new LiveElectronics(null,null,null,null,null),"RC2 stale observation clears all values and blocks capture");
        running=false;Observe();check(!engine.Status().CanCapture&&engine.Status().Values.TC is null,"RC2 disconnect clears current values");
        running=true;Observe();Bind();check(engine.Status().CanCapture,"RC2 complete revalidated data after reconnect unlocks capture");
        var currentToken=engine.Status().ContextToken!;
        Text(scoring,Layout.S_mTrackName,"Unknown track");Observe();
        check(!engine.Status().CanCapture&&engine.Status().Key is null,"RC2 track switch cannot inherit capture identity");
        Text(scoring,Layout.S_mTrackName,"SYNTHETIC Circuit Alpha");Observe();Bind();
        Text(telemetry,Layout.T_mVehicleModel,"Unknown model");Observe();
        check(!engine.Status().CanCapture&&engine.Status().Key is null,"RC2 vehicle switch cannot inherit capture identity");
        Text(telemetry,Layout.T_mVehicleModel,entry.Model);Observe();Bind();
        bool oldRejected=false;try{engine.ReadForContext(currentToken);}catch(LibraryConflictException){oldRejected=true;}
        check(oldRejected,"RC2 context token from before identity switch remains rejected");

        // Fresh browser/session/API reads use the same isolated SQLite, not a cached library match.
        var options=new ServerOptions(Path.Combine(root,"rc2-http.sqlite"),0,WebRoot:Path.GetFullPath("src/Lmu.Web/wwwroot"));
        await using var app=WebHostFactory.Create(options,engine,startLiveWorker:false,timeProvider:clock);await app.StartAsync();
        try{
            var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            async Task<(HttpClient Client,string Csrf)> Client(){var c=new HttpClient(new HttpClientHandler()){BaseAddress=new(address)};var r=await c.PostAsJsonAsync("/api/session",new{key=options.AccessKey});r.EnsureSuccessStatusCode();var body=await r.Content.ReadFromJsonAsync<JsonElement>();var csrf=body.GetProperty("csrf").GetString()!;c.DefaultRequestHeaders.Add("X-LMU-CSRF",csrf);return(c,csrf);}
            using var first=(await Client()).Client;
            async Task<JsonElement> Live(HttpClient c)=>await c.GetFromJsonAsync<JsonElement>("/api/live");
            var live=await Live(first);check(live.GetProperty("matchingSetups").GetArrayLength()==0&&live.GetProperty("status").GetProperty("canCapture").GetBoolean(),"RC2 HTTP empty library still permits complete live capture");
            var token=live.GetProperty("status").GetProperty("contextToken").GetString()!;
            using var created=await first.PostAsJsonAsync("/api/live/setups",new{contextToken=token,name="HTTP RC2",category="FixedLab"});created.EnsureSuccessStatusCode();
            var data=await created.Content.ReadFromJsonAsync<JsonElement>();var id=data.GetProperty("id").GetGuid();var version=data.GetProperty("current").GetProperty("id").GetGuid();
            using var deleted=await first.PostAsJsonAsync($"/api/setups/{id}/delete",new{expectedVersion=version});deleted.EnsureSuccessStatusCode();
            using var refreshed=(await Client()).Client;live=await Live(refreshed);
            check(live.GetProperty("matchingSetups").GetArrayLength()==0&&live.GetProperty("status").GetProperty("canCapture").GetBoolean(),"RC2 fresh browser session after deletion sees no match but can capture");
            token=live.GetProperty("status").GetProperty("contextToken").GetString()!;
            using var recreated=await refreshed.PostAsJsonAsync("/api/live/setups",new{contextToken=token,name="HTTP after refresh",category="FixedLab"});
            check(recreated.IsSuccessStatusCode,"RC2 HTTP capture works after deleting setup and refreshing browser session");
            missing="TCCut";Observe();
            foreach(var language in new[]{"de","en"}){refreshed.DefaultRequestHeaders.Remove("X-LMU-Language");refreshed.DefaultRequestHeaders.Add("X-LMU-Language",language);var body=await Live(refreshed);var message=body.GetProperty("status").GetProperty("message").GetString()!;
                check(!body.GetProperty("status").GetProperty("canCapture").GetBoolean()&&message.Contains("TC Cut")&&message.Contains(language=="de"?"fehlt":"missing"),"RC2 HTTP missing-value message names TC Cut in "+language);}
            using var blocked=await refreshed.PostAsJsonAsync("/api/live/setups",new{contextToken=token,name="Rejected",category="FixedLab"});
            check(blocked.StatusCode==System.Net.HttpStatusCode.Conflict,"RC2 HTTP rechecks actual missing data and rejects former valid token");missing=null;Observe();Observe();
        }finally{await app.StopAsync();}
        RestCapture[] Rows(DateTimeOffset requested)=>[new(clock.Now,"http://127.0.0.1:6397/rest/sessions/getAllVehicles",200,vehicles,null,requested),new(clock.Now,"http://127.0.0.1:6397/rest/sessions/getTracksAll",200,tracks,null,requested)];
        engine=new LiveStateEngine(clock);Observe();var firstProof=engine.BeginRequest();engine.Complete(firstProof,Rows(clock.Now));
        for(int i=0;i<51;i++)Observe();var tooLate=engine.BeginRequest();engine.Complete(tooLate,Rows(clock.Now));
        check(!engine.Status().CanCapture,"RC2 more than five seconds before follow-up request still requires two new confirmations");
        engine.Complete(tooLate,Rows(clock.Now));check(!engine.Status().CanCapture,"RC2 same response cannot count twice");
        Observe();var timely=engine.BeginRequest();engine.Complete(timely,Rows(clock.Now));
        check(engine.Status().CanCapture,"RC2 independent timely second proof confirms after expired candidate");
        engine=new LiveStateEngine(clock);Observe();firstProof=engine.BeginRequest();engine.Complete(firstProof,Rows(clock.Now));
        Observe();var staleResponse=engine.BeginRequest();var started=clock.Now;for(int i=0;i<51;i++)Observe();engine.Complete(staleResponse,Rows(started));
        check(!engine.Status().CanCapture,"RC2 response older than five seconds remains rejected despite continuous physics");
        Bind();check(engine.Status().CanCapture,"RC2 fresh independent proofs recover after stale REST response");
        engine=new LiveStateEngine(clock);Observe();firstProof=engine.BeginRequest();engine.Complete(firstProof,Rows(clock.Now));
        Observe();var inFlight=engine.BeginRequest();started=clock.Now;Text(scoring,Layout.S_mTrackName,"Unknown track");Observe();engine.Complete(inFlight,Rows(started));
        check(engine.Status().Key is null&&!engine.Status().CanCapture,"RC2 response crossing native layout change cannot confirm old candidate");
    }
    sealed class RcClock(DateTimeOffset at):TimeProvider{public DateTimeOffset Now=at;public override DateTimeOffset GetUtcNow()=>Now;}
}
