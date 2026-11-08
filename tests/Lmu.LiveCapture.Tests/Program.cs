using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lmu.Diagnostics;
using Lmu.LiveCapture;
using Lmu.SetupLibrary;
using Lmu.Web;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

if(args.Contains("--rc2-passive")){await Rc2CaptureTests.Passive();return;}
if(args.Contains("--ui-fixture")){await BrowserFixture.Run();return;}
if(args.Contains("--b26-ui-fixture")){await BrowserFixture.Run(true);return;}
int count=0;
void Check(bool ok,string label){if(!ok)throw new Exception("FAIL "+label);count++;Console.WriteLine("PASS "+label);}
void Reject<T>(Action action,string label)where T:Exception{try{action();}catch(T){Check(true,label);return;}throw new Exception("FAIL "+label);}
async Task RejectAsync<T>(Func<Task> action,string label)where T:Exception{try{await action();}catch(T){Check(true,label);return;}throw new Exception("FAIL "+label);}
var root=Path.Combine(Path.GetTempPath(),"lmu-b22-tests-"+Guid.NewGuid());Directory.CreateDirectory(root);
var clock=new Clock(DateTimeOffset.UtcNow);
var vehicles=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/vehicles.json"));
var tracks=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/tracks.json"));
var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);json.Converters.Add(new JsonStringEnumConverter());
RestCapture[] Captures()=>[new(clock.Now,"http://127.0.0.1:6397/rest/sessions/getAllVehicles",200,vehicles,null,clock.Now),new(clock.Now,"http://127.0.0.1:6397/rest/sessions/getTracksAll",200,tracks,null,clock.Now)];
try {
    await Rc2CaptureTests.Run(Check,root,vehicles,tracks);
    var path=Path.Combine(root,"library.sqlite");var library=new SetupLibraryService(new LibraryDatabase(path));
    var transport=new Transport{Rows=Captures()};var imports=new CatalogImportService(transport,library,clock);
    var v=await imports.Preview("vehicles");var t=await imports.Preview("tracks");
    Check(v.SourceEntries==13&&t.SourceEntries==5,"synthetic catalogs parse all 13 vehicle and 5 layout entries");
    var bmw=v.Choices.Single(c=>c.Name=="SYNTHETIC Aster GT3");var monza=t.Choices.Single(c=>c.LayoutId=="SYNTHETIC_ALPHA_GP");var grande=t.Choices.Single(c=>c.LayoutId=="SYNTHETIC_ALPHA_ALT");
    Check(bmw.Selectable&&bmw.SourceEntries>1,"BMW teams and liveries deduplicate to one selectable component");
    Check(monza.TrackId==grande.TrackId&&monza.Id!=grande.Id&&monza.LayoutId!=grande.LayoutId,"Monza variants retain shared parent and distinct exact scene IDs");
    Check(v.Choices.Any(c=>!c.Selectable&&c.Name.Contains("Dawn")),"ambiguous Dawn component variants are explicitly blocked");
    Reject<LibraryValidationException>(()=>imports.Import(v.Id,[v.Choices.First(c=>!c.Selectable).Id]),"ambiguous group cannot be imported");
    library.RegisterVehicle(new(bmw.Id,"My BMW label","Custom class"));library.RegisterTrack(new(monza.TrackId!,"My Monza"));library.RegisterLayout(new(monza.TrackId!,monza.LayoutId!,"My GP"));
    var result=imports.Import(v.Id,[bmw.Id,bmw.Id]);Check(result.VehiclesAdded==0&&result.ExistingPreserved==1,"duplicate selections do not duplicate existing manual model");
    result=imports.Import(t.Id,[monza.Id,grande.Id]);Check(result.TracksAdded==0&&result.LayoutsAdded==1,"existing parent and layout retained while sibling layout imports");
    Check(library.Vehicles().Single().Name=="My BMW label"&&library.Layouts(monza.TrackId!).Single(l=>l.Id==monza.LayoutId).Name=="My GP","custom labels preserved verbatim");
    Check(new SetupLibraryService(new LibraryDatabase(path)).Layouts(monza.TrackId!).Count==2,"import persists across database/service restart");
    Reject<LibraryValidationException>(()=>imports.Import(v.Id,["fabricated"]),"unknown client selection rejected");
    Reject<LibraryValidationException>(()=>imports.Import(v.Id,[]),"empty selection rejected");
    Reject<LibraryValidationException>(()=>library.ImportCatalog(new([new("ATOMIC","Would roll back")],[],[new("missing","X","Invalid")])),"invalid layout aborts whole import transaction");
    Check(!library.Vehicles().Any(x=>x.Id=="ATOMIC"),"failed import leaves no partial model insert");
    clock.Now=clock.Now.AddSeconds(61);Reject<LibraryConflictException>(()=>imports.Import(v.Id,[bmw.Id]),"expired preview rejected");clock.Now=clock.Now.AddSeconds(-61);
    foreach(var bad in new[]{"[]","{}","[{}]","not JSON"}){transport.Rows=Captures();transport.Rows[0]=transport.Rows[0] with{Body=bad};await RejectAsync<LmuSourceException>(()=>imports.Preview("vehicles"),"malformed catalog rejected: "+bad);}
    transport.Rows=[];await RejectAsync<LmuSourceException>(()=>imports.Preview("vehicles"),"missing REST response is explicit source error");
    transport.Rows=Captures();transport.Rows[0]=transport.Rows[0] with{Status=500};await RejectAsync<LmuSourceException>(()=>imports.Preview("vehicles"),"HTTP failure not treated as empty catalog");
    transport.Rows=Captures();transport.Rows[0]=transport.Rows[0] with{Status=null,Body=null,Error="SYNTHETIC timeout"};await RejectAsync<LmuSourceException>(()=>imports.Preview("vehicles"),"timeout not treated as available catalog");
    transport.Rows=Captures();transport.Rows[0]=transport.Rows[0] with{RequestedAt=clock.Now.AddSeconds(-6)};await RejectAsync<LmuSourceException>(()=>imports.Preview("vehicles"),"old catalog response rejected");transport.Rows=Captures();

    var entry=IdentityResolver.ReadVehicleCatalog(vehicles).First(e=>e.Package==bmw.Id);
    Sample At(double elapsed,string? model=null,string? file=null,string? layout=null,bool fixedSetup=false) {
        var f=new Dictionary<string,Datum>();
        foreach(var (k,value) in new (string,object?)[]{("GameRunning",true),("GameVersion",14200),("InRealtime",true),("PlayerControl",0),("InGarage",false),("FixedSetup",fixedSetup),("GamePhase",5),
            ("RawVehicleModel",model??entry.Model),("RawTrackName",layout??monza.Name),("PlayerVehicleFile",file??entry.Stem),("PlayerSlotId",1),("SessionCode",5),("Session","Qualifying"),("SessionStart",0f),
            ("TelemetryElapsed",elapsed),("SessionElapsed",elapsed),("TC",7),("TCCut",8),("TCSlip",7),("ABS",6),("BrakeBias",51.25),("TCMax",10),("TCCutMax",10),("TCSlipMax",10),("ABSMax",12)})f[k]=new(value,"SYNTHETIC");
        return new(clock.Now,"LIVE",f);
    }
    var engine=new LiveStateEngine(clock);double et=10;
    void Observe(Sample? sample=null,string process="SYNTHETIC-1"){clock.Now=clock.Now.AddMilliseconds(100);et+=.1;var s=sample??At(et);engine.Observe(new(true,process,s with{ObservedAt=clock.Now}));}
    void Bind(){for(int i=0;i<2;i++){Observe();var ticket=engine.BeginRequest();engine.Complete(ticket,Captures());}}
    Check(engine.Status().GameRunning is null&&!engine.Status().CanCapture,"no frame has unknown process and no invented live values");
    engine.Observe(new(false,"",At(et)));Check(engine.Status().GameRunning==false&&engine.Status().Values.TC is null,"stopped LMU clears readings");
    Observe();var ticket1=engine.BeginRequest();engine.Complete(ticket1,Captures());Check(engine.Status().Key is null,"one catalog confirmation cannot unlock matching");
    Observe();var ticket2=engine.BeginRequest();engine.Complete(ticket2,Captures());var status=engine.Status();
    Check(status.Key==new SetupKey(bmw.Id,monza.TrackId!,monza.LayoutId!)&&status.CanCapture,"two corroborated catalogs bind exact Phase A model/component/scene identity");
    Check(status.Values.TCCut==8&&status.SessionFixedSetup==false,"fresh actual Open-session electronics displayed independently");
    var originalToken=status.ContextToken!;
    var snapshot=engine.ReadForContext(originalToken);Check(snapshot.Values.TCCut==8,"capture reads server sample, not submitted values");
    var capture=new SetupLibraryService(new LibraryDatabase(path),engine.Reader(originalToken),clock);
    var saved=await capture.CaptureNew("Live Fixed Lab",SetupCategory.FixedLab);
    Check(saved.Origin==SetupOrigin.Automatic&&saved.Current.Capture?.SessionFixedSetup==false&&saved.Current.Content.Category==SetupCategory.FixedLab,"Fixed Lab capture allowed in Open session with true provenance");
    Check(library.Vehicles().Single().Name=="My BMW label","live capture preserves imported/manual display name");
    var other=await capture.CaptureNew("Live Open variant",SetupCategory.Open);
    Check(other.Id!=saved.Id&&other.Current.Content.Category==SetupCategory.Open,"explicit live variant is separate setup and category");
    var key=saved.Key;var manual=library.CreateManual(key,new("Manual target",new(1,2,3,4,52),Notes:"Keep notes",PreferredUse:"Keep profile",Favorite:true,Category:SetupCategory.Fixed));
    var revised=await capture.CaptureVersion(manual.Id,manual.Current.Id);
    Check(revised.Current.Number==2&&revised.Versions[0]==manual.Current&&revised.Current.Change=="LiveCaptured","live update appends immutable version; original untouched");
    Check(revised.Current.Content.Name=="Manual target"&&revised.Current.Content.Notes=="Keep notes"&&revised.Current.Content.Favorite&&revised.Current.Content.Category==SetupCategory.Fixed&&revised.Current.Capture is not null,"live version preserves metadata/category and records observation evidence");
    await RejectAsync<LibraryConflictException>(()=>capture.CaptureVersion(manual.Id,manual.Current.Id),"stale expected version cannot overwrite");
    var wrong=library.CreateManual(key with{LayoutId=grande.LayoutId!},new("Wrong layout",new(1,2,3,4,52)));
    await RejectAsync<LibraryConflictException>(()=>capture.CaptureVersion(wrong.Id,wrong.Current.Id),"live version cannot change target layout identity");
    var web=new LibraryWebService(library);Check(web.List(key.VehicleId,key.TrackId,key.LayoutId).Count==3,"exact matching includes all categories and excludes sibling layout");
    Observe(At(et+.1) with{Fields=new(At(et+.1).Fields){["TC"]=new(null,"SYNTHETIC unavailable")}});Check(!engine.Status().CanCapture&&engine.Status().Values.TC is null,"missing aid never defaults to zero or captures");
    Observe();Observe();Check(engine.Status().CanCapture,"complete fresh pair recovers after missing aid");
    clock.Now=clock.Now.AddSeconds(3);Check(engine.Status().Key is null&&engine.Status().Values.TC is null&&!engine.Status().CanCapture,"stale sample clears matching and all readings");Reject<LibraryConflictException>(()=>engine.ReadForContext(originalToken),"old UI capture rejected after stale telemetry");
    Bind();var tokenBeforeSwitch=engine.Status().ContextToken!;
    Observe(At(et+.1,layout:grande.Name));Check(engine.Status().Key is null&&!engine.Status().CanCapture,"layout switch immediately clears old binding");Reject<LibraryConflictException>(()=>engine.ReadForContext(tokenBeforeSwitch),"old dashboard token rejected after layout switch");
    Observe();Bind();Check(engine.Status().Key?.LayoutId==monza.LayoutId,"returning layout requires new catalog confirmations");
    Observe(At(et+.1,fixedSetup:true));Check(engine.Status().Key is null&&engine.Status().SessionFixedSetup==true,"Fixed/Open switch invalidates old identity and reports actual new flag");
    Observe();Bind();Observe(At(et+.1,model:"DIFFERENT",file:"OTHER"));Check(engine.Status().Key is null,"vehicle switch cannot inherit old identity");
    Observe();Bind();Observe(process:"SYNTHETIC-2");Check(engine.Status().Key is null,"new game process requires revalidation");
    Observe();Bind();Observe(At(et+.1) with{Fields=new(At(et+.1).Fields){["InGarage"]=new(true,"SYNTHETIC")}});Check(!engine.Status().Fresh&&engine.Status().Values.TC is null,"garage clears readings instead of displaying current values");
    Observe();Bind();Observe(At(et) with{Fields=new(At(et).Fields){["TelemetryElapsed"]=new(et,"SYNTHETIC")}});Observe(At(et-.1));Check(!engine.Status().Fresh,"stopped physics progression does not claim fresh telemetry");
    Observe();Bind();

    // Real HTTP + SQLite. SYNTHETIC live source, synthetic catalogs. LAN binding is explicit.
    var options=new ServerOptions(path,0,Lan:true,WebRoot:Path.GetFullPath("src/Lmu.Web/wwwroot"));
    await using var app=WebHostFactory.Create(options,engine,transport,startLiveWorker:false,timeProvider:clock);await app.StartAsync();
    try {
        var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().Replace("0.0.0.0","127.0.0.1");
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new(address)};
        async Task<HttpResponseMessage> Send(HttpMethod method,string route,object? body=null)=>await client.SendAsync(new(method,route){Content=body is null?null:JsonContent.Create(body,options:json)});
        async Task<JsonElement> Good(HttpMethod method,string route,object? body=null){using var r=await Send(method,route,body);var bodyText=await r.Content.ReadAsStringAsync();if(!r.IsSuccessStatusCode)throw new Exception(route+" "+r.StatusCode+" "+bodyText);return JsonDocument.Parse(bodyText).RootElement.Clone();}
        foreach(var route in new[]{"/api/live","/api/lmu/catalog/vehicles","/api/lmu/catalog/tracks"})Check((await Send(HttpMethod.Get,route)).StatusCode==HttpStatusCode.Unauthorized,"LAN live/catalog read requires access grant: "+route);
        Check((await Send(HttpMethod.Post,"/api/live/setups",new{})).StatusCode==HttpStatusCode.Unauthorized,"LAN live writes reject anonymous clients");
        var session=await Good(HttpMethod.Post,"/api/session",new{key=options.AccessKey});
        Check((await Send(HttpMethod.Post,"/api/lmu/catalog/import",new{})).StatusCode==HttpStatusCode.Forbidden,"catalog writes require CSRF with valid access cookie");
        Check((await Send(HttpMethod.Post,"/api/live/setups",new{})).StatusCode==HttpStatusCode.Forbidden,"live writes require CSRF with valid access cookie");
        client.DefaultRequestHeaders.Add("X-LMU-CSRF",session.GetProperty("csrf").GetString());
        using(var foreign=new HttpRequestMessage(HttpMethod.Post,"/api/live/setups"){Content=JsonContent.Create(new{})}){foreign.Headers.Add("Origin","http://evil.example");Check((await client.SendAsync(foreign)).StatusCode==HttpStatusCode.Forbidden,"foreign Origin rejected on new capture route");}
        Observe();Bind();var live=await Good(HttpMethod.Get,"/api/live");var matches=live.GetProperty("matchingSetups");
        Check(matches.GetArrayLength()==3&&matches[0].GetProperty("favorite").GetBoolean(),"HTTP matching returns exact identities with favorites first");
        transport.Rows=Captures();
        var preview=(await Good(HttpMethod.Get,"/api/lmu/catalog/vehicles")).Deserialize<ImportPreview>(json)!;
        Check(preview.Choices.Single(c=>c.Id==bmw.Id).Exists,"HTTP preview marks existing exact model");
        var imported=await Good(HttpMethod.Post,"/api/lmu/catalog/import",new CatalogSelection(preview.Id,[bmw.Id]));Check(imported.GetProperty("existingPreserved").GetInt32()==1,"HTTP import preserves existing record");
        var token=live.GetProperty("status").GetProperty("contextToken").GetString()!;
        var httpSaved=(await Good(HttpMethod.Post,"/api/live/setups",new LiveCreateRequest(token,"HTTP capture",SetupCategory.Open))).Deserialize<SavedSetup>(json)!;
        Check(httpSaved.Current.Capture is not null&&httpSaved.Current.Content.Category==SetupCategory.Open,"HTTP capture persists values/evidence and chosen category");
        var httpVersion=(await Good(HttpMethod.Post,$"/api/live/setups/{httpSaved.Id}/versions",new LiveVersionRequest(token,httpSaved.Current.Id))).Deserialize<SavedSetup>(json)!;
        Check(httpVersion.Current.Number==2&&httpVersion.Current.Change=="LiveCaptured","HTTP live update creates explicit new version");
        Check((await Send(HttpMethod.Post,$"/api/live/setups/{httpSaved.Id}/versions",new LiveVersionRequest(token,httpSaved.Current.Id))).StatusCode==HttpStatusCode.Conflict,"HTTP concurrent update returns conflict");
        Check((await Send(HttpMethod.Post,"/api/live/setups",new LiveCreateRequest("old-token","Rejected",SetupCategory.Fixed))).StatusCode==HttpStatusCode.Conflict,"HTTP stale context rejected");
        engine.Observe(new(false,"",At(et)));var noGame=await Good(HttpMethod.Get,"/api/live");
        Check(noGame.GetProperty("matchingSetups").GetArrayLength()==0&&noGame.GetProperty("status").GetProperty("values").GetProperty("tc").ValueKind==JsonValueKind.Null,"HTTP stopped game returns no stale matching or aid");
        var offline=(await Good(HttpMethod.Post,"/api/setups",new CreateSetupRequest(key,new("Offline manual",new(1,2,3,4,52))))).Deserialize<SavedSetup>(json)!;
        Check(offline.Current.Capture is null,"manual HTTP save remains available without LMU");
        transport.Rows=[];Check((await Send(HttpMethod.Get,"/api/lmu/catalog/tracks")).StatusCode==HttpStatusCode.ServiceUnavailable,"HTTP unavailable source returns 503, not empty catalog");
    }finally{await app.StopAsync();}
    Console.WriteLine($"All {count} B2.2 assertions passed. Synthetic catalogs + SQLite/HTTP; SYNTHETIC native samples, no LMU drive claimed.");
}finally {
    if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("lmu-b22-tests-"))throw new IOException("Unsafe cleanup path");
    Directory.Delete(root,true);
}
sealed class Clock(DateTimeOffset at):TimeProvider{public DateTimeOffset Now=at;public override DateTimeOffset GetUtcNow()=>Now;}
sealed class Transport:ILmuCatalogTransport{public RestCapture[] Rows=[];public Task<RestCapture[]> Read(CancellationToken token)=>Task.FromResult(Rows);}
