using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lmu.Web;
using Lmu.SetupLibrary;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using static Lmu.SetupLibrary.LibraryDatabase;

int count=0;
void Check(bool ok,string label){if(!ok)throw new Exception("FAIL "+label);count++;Console.WriteLine("PASS "+label);}
var root=Path.Combine(Path.GetTempPath(),"lmu-b2-tests-"+Guid.NewGuid());Directory.CreateDirectory(root);
var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);json.Converters.Add(new JsonStringEnumConverter());
try {
    string path=Path.Combine(root,"library.sqlite");
    var legacy=new LibraryDatabase(path,2);var old=new SetupLibraryService(legacy);
    old.RegisterVehicle(new("SYNTHETIC_CAR","Synthetic car","GT3",new(10,10,10,12,"SYNTHETIC limits")));
    old.RegisterTrack(new("SYNTHETIC_TRACK","Synthetic track"));old.RegisterLayout(new("SYNTHETIC_TRACK","A","Layout A"));
    var key=new SetupKey("SYNTHETIC_CAR","SYNTHETIC_TRACK","A");var draft=new SetupDraft("Legacy",new(7,7,7,6,51.25));
    var prior=old.CreateManual(key,draft);var previousHashes=new List<string>();using(var c=legacy.Connect())for(int i=1;i<=2;i++)previousHashes.Add((string)Scalar(c,null,"SELECT checksum FROM schema_migrations WHERE version=$v",("$v",i))!);
    var options=new ServerOptions(path,0,WebRoot:Path.GetFullPath("src/Lmu.Web/wwwroot"));
    await using var app=WebHostFactory.Create(options,startLiveWorker:false);await app.StartAsync();
    try {
        var url=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new(url)};
        async Task<HttpResponseMessage> Send(HttpMethod method,string route,object? body=null)=>await client.SendAsync(new HttpRequestMessage(method,route){Content=body is null?null:JsonContent.Create(body,options:json)});
        async Task<JsonElement> Good(HttpMethod method,string route,object? body=null) {
            using var r=await Send(method,route,body);var text=await r.Content.ReadAsStringAsync();if(!r.IsSuccessStatusCode)throw new Exception($"{route}: {r.StatusCode} {text}");
            return string.IsNullOrWhiteSpace(text)?default:JsonDocument.Parse(text).RootElement.Clone();
        }
        async Task<SavedSetup> Setup(HttpMethod method,string route,object? body=null)=>(await Good(method,route,body)).Deserialize<SavedSetup>(json)!;
        Check((await client.GetAsync("/api/setups")).StatusCode==HttpStatusCode.Unauthorized,"API requires local access grant before exposing library");
        using(var index=await client.GetAsync("/")){Check(index.IsSuccessStatusCode&&(await index.Content.ReadAsStringAsync()).Contains("lang=\"de\""),"static German application shell served");Check(index.Headers.Contains("Content-Security-Policy"),"UI has self-only script policy and frame protection");}
        Check((await client.GetStringAsync("/app.css")).Contains("max-width:500px"),"responsive assets served without external CDN");
        Check((await Send(HttpMethod.Post,"/api/session",new {key="wrong"})).StatusCode==HttpStatusCode.Unauthorized,"wrong pairing key rejected");
        var paired=await Good(HttpMethod.Post,"/api/session",new {key=options.AccessKey});Check(paired.GetProperty("authenticated").GetBoolean(),"correct pairing key establishes cookie session");
        Check((await Send(HttpMethod.Post,"/api/backups",new{})).StatusCode==HttpStatusCode.Forbidden,"write without CSRF header rejected");
        client.DefaultRequestHeaders.Add("X-LMU-CSRF",paired.GetProperty("csrf").GetString());
        using(var hostile=new HttpRequestMessage(HttpMethod.Post,"/api/backups"){Content=JsonContent.Create(new{})}){hostile.Headers.Add("Origin","http://unrelated.example");Check((await client.SendAsync(hostile)).StatusCode==HttpStatusCode.Forbidden,"foreign browser origin rejected even with valid access cookie");}
        var migrated=await Setup(HttpMethod.Get,"/api/setups/"+prior.Id);
        Check(migrated.Current.Id==prior.Current.Id&&migrated.Current.Content.Category==SetupCategory.FixedLab,"v2 library migration preserves old version IDs/values and defaults category to Fixed Lab");
        using(var c=new LibraryDatabase(path).Connect())Check(previousHashes.Select((hash,i)=>hash==(string?)Scalar(c,null,"SELECT checksum FROM schema_migrations WHERE version=$v",("$v",i+1))).All(x=>x),"v1/v2 migration SQL checksums remain unchanged");
        Check(Directory.GetFiles(Path.Combine(root,"backups"),"before-*.sqlite").Length==1,"existing v2 library backed up before v3 migration");
        var stats=await Good(HttpMethod.Get,"/api/dashboard");Check(stats.GetProperty("activeCount").GetInt32()==1,"dashboard reflects actual SQLite setup count");
        await Good(HttpMethod.Post,"/api/catalog/tracks",new Track("OTHER_TRACK","Second track"));
        await Good(HttpMethod.Post,"/api/catalog/layouts",new TrackLayout("OTHER_TRACK","A","Different A"));
        var cat=await Good(HttpMethod.Get,"/api/catalog");Check(cat.GetProperty("layouts").GetArrayLength()==2,"catalog API preserves track-scoped layout identities");
        var initial=await Setup(HttpMethod.Post,"/api/setups",new CreateSetupRequest(key,draft with{Name="Safe Quali",PreferredUse="Esport Quali",Category=SetupCategory.Fixed,Notes="wet conditions"}));
        Check(initial.Current.Content.Category==SetupCategory.Fixed&&initial.Current.Capture is null,"manual Fixed category is saved without inventing actual FixedSetup or live capture");
        Check((await Good(HttpMethod.Get,"/api/setups?q=wet")).GetArrayLength()==1,"free text searches notes");
        Check((await Good(HttpMethod.Get,"/api/setups?q=synthetic%20car")).GetArrayLength()==2,"free text searches vehicle labels");
        Check((await Good(HttpMethod.Get,"/api/setups?q=synthetic%20track")).GetArrayLength()==2,"free text searches track labels");
        var originalVehicle=new Vehicle("SYNTHETIC_CAR","Synthetic car","GT3",new(10,10,10,12,"SYNTHETIC limits"));
        var renamedVehicle=originalVehicle with{Name="Renamed car",Class="GT3 test"};
        await Good(HttpMethod.Put,"/api/catalog/vehicles",new CatalogChange<Vehicle>(originalVehicle,renamedVehicle));
        Check((await Good(HttpMethod.Get,"/api/setups?q=Renamed%20car")).GetArrayLength()==2,"explicit vehicle metadata editing preserves technical setup bindings");
        Check((await Send(HttpMethod.Put,"/api/catalog/vehicles",new CatalogChange<Vehicle>(originalVehicle,renamedVehicle))).StatusCode==HttpStatusCode.Conflict,"stale catalog edit cannot overwrite another change");
        Check((await Send(HttpMethod.Put,"/api/catalog/vehicles",new CatalogChange<Vehicle>(renamedVehicle,renamedVehicle with{Limits=null}))).StatusCode==HttpStatusCode.BadRequest,"catalog label edit cannot weaken proven vehicle limits");
        await Good(HttpMethod.Put,"/api/catalog/tracks",new CatalogChange<Track>(new(key.TrackId,"Synthetic track"),new(key.TrackId,"Renamed track")));
        Check((await Good(HttpMethod.Get,"/api/setups?q=Renamed%20track")).GetArrayLength()==2,"track display label can change without rebinding layouts");
        await Good(HttpMethod.Put,"/api/catalog/layouts",new CatalogChange<TrackLayout>(new(key.TrackId,"A","Layout A"),new(key.TrackId,"A","Renamed layout")));
        Check((await Good(HttpMethod.Get,"/api/setups?q=Renamed%20layout")).GetArrayLength()==2,"layout display name is editable independently of technical layout identity");
        Check((await Good(HttpMethod.Get,"/api/setups?category=Fixed")).GetArrayLength()==1,"category filter uses persisted classification");
        Check((await Good(HttpMethod.Get,"/api/setups?vehicle=SYNTHETIC_CAR&track=SYNTHETIC_TRACK&layout=A")).GetArrayLength()==2,"identity filters bind exact model/track/layout");
        using(var missing=await client.PostAsync("/api/setups",new StringContent("{\"key\":{\"vehicleId\":\"SYNTHETIC_CAR\",\"trackId\":\"SYNTHETIC_TRACK\",\"layoutId\":\"A\"},\"draft\":{\"name\":\"missing\",\"values\":{\"tcCut\":7,\"tcSlip\":7,\"abs\":6,\"brakeBiasFrontPercent\":51.25}}}",System.Text.Encoding.UTF8,"application/json")))Check(missing.StatusCode==HttpStatusCode.BadRequest,"HTTP model binding rejects missing TC instead of defaulting to zero");
        Check((await Send(HttpMethod.Post,"/api/setups",new CreateSetupRequest(key,draft with{Values=draft.Values with{TC=11}}))).StatusCode==HttpStatusCode.BadRequest,"API obeys proven model limits");
        Check((await Send(HttpMethod.Post,"/api/setups",new CreateSetupRequest(key,draft with{Category=(SetupCategory)999}))).StatusCode==HttpStatusCode.BadRequest,"unknown category rejected");
        Check((await Send(HttpMethod.Get,"/api/setups?layout=A")).StatusCode==HttpStatusCode.BadRequest,"unscoped layout query rejected");
        Check((await Good(HttpMethod.Get,"/api/setups")).GetArrayLength()==2,"invalid API creates leave no partial setup");
        var edited=await Setup(HttpMethod.Put,"/api/setups/"+initial.Id,new EditSetupRequest(initial.Current.Id,initial.Current.Content with{Values=draft.Values with{TC=8},Category=SetupCategory.Open}));
        Check(edited.Current.Number==2&&edited.Versions[0].Content.Category==SetupCategory.Fixed&&edited.Current.Content.Category==SetupCategory.Open,"editing creates immutable category/value revision");
        Check((await Send(HttpMethod.Put,"/api/setups/"+initial.Id,new EditSetupRequest(initial.Current.Id,draft))).StatusCode==HttpStatusCode.Conflict,"stale browser edit returns 409 without overwriting");
        var forced=await Setup(HttpMethod.Post,$"/api/setups/{edited.Id}/versions",new EditSetupRequest(edited.Current.Id,edited.Current.Content));
        Check(forced.Current.Number==3&&forced.Current.Content==edited.Current.Content,"explicit new-version action works even with identical values");
        var comparison=await Good(HttpMethod.Get,$"/api/setups/{initial.Id}/compare?left={initial.Current.Id}&right={edited.Current.Id}");
        Check(comparison.GetProperty("differences").EnumerateArray().Any(d=>d.GetProperty("field").GetString()=="TC"),"version comparison shows value differences");
        Check((await Send(HttpMethod.Get,$"/api/setups/{initial.Id}/compare?left={prior.Current.Id}&right={edited.Current.Id}")).StatusCode==HttpStatusCode.BadRequest,"comparison cannot mix another setup's version");
        var copy=await Setup(HttpMethod.Post,$"/api/setups/{initial.Id}/copy",new CopyRequest(initial.Current.Id,"Old base"));
        Check(copy.Current.Content.Values.TC==7&&copy.Current.Content.Category==SetupCategory.Fixed&&copy.CopiedFromVersionId==initial.Current.Id,"old version becomes independent variant with correct lineage");
        var reassigned=await Setup(HttpMethod.Post,$"/api/setups/{initial.Id}/copy",new CopyRequest(initial.Current.Id,"Other layout",new(key.VehicleId,"OTHER_TRACK","A")));
        Check(reassigned.Key.TrackId=="OTHER_TRACK"&&reassigned.Id!=initial.Id&&reassigned.Current.Capture is null,"changing assignment creates new setup and cannot mislabel live evidence");
        var fav=await Setup(HttpMethod.Post,$"/api/setups/{initial.Id}/favorite",new FavoriteRequest(forced.Current.Id,true));
        Check((await Good(HttpMethod.Get,"/api/setups?favorites=true")).GetArrayLength()==1,"favorites API/filter is versioned");
        await Good(HttpMethod.Post,$"/api/setups/{initial.Id}/delete",new VersionRequest(fav.Current.Id));
        Check((await Good(HttpMethod.Get,"/api/setups?trash=only")).GetArrayLength()==1,"trash lists only deleted setups");
        Check((await Good(HttpMethod.Get,"/api/setups?trash=include")).GetArrayLength()==4,"include-trash filter retains every variant");
        var deleted=await Setup(HttpMethod.Get,"/api/setups/"+initial.Id);Check(deleted.DeletedAt is not null&&deleted.Versions.Count==4,"deleted detail retains full history");
        Check((await Send(HttpMethod.Put,"/api/setups/"+initial.Id,new EditSetupRequest(fav.Current.Id,draft))).StatusCode==HttpStatusCode.NotFound,"deleted setup cannot be edited");
        var restored=await Setup(HttpMethod.Post,$"/api/setups/{initial.Id}/restore",new VersionRequest(fav.Current.Id));
        Check(restored.DeletedAt is null&&restored.Current.Id==fav.Current.Id,"restore retains original setup/version identities");
        var backup=await Good(HttpMethod.Post,"/api/backups",new{});
        var bytes=await client.GetByteArrayAsync(backup.GetProperty("downloadUrl").GetString());
        Check(System.Text.Encoding.ASCII.GetString(bytes,0,15)=="SQLite format 3","backup download is an actual SQLite database");
        var backupPath=Path.Combine(root,"download.sqlite");await File.WriteAllBytesAsync(backupPath,bytes);
        Check(new SetupLibraryService(new LibraryDatabase(backupPath)).Get(initial.Id).Versions.Count==4,"downloaded backup reopens with complete persisted history");
        Check(backup.GetProperty("restoreReleased").GetBoolean(),"restore available only through separate validated preview/confirmation workflow");
        Check((await client.PostAsJsonAsync("/api/restore",new{})).StatusCode==HttpStatusCode.NotFound,"no destructive database restore endpoint");
        Check((await client.GetAsync("/api/backups/"+Guid.NewGuid())).StatusCode==HttpStatusCode.NotFound,"backup download cannot access arbitrary file paths");
        Console.WriteLine($"All {count} B2 assertions passed. Real localhost HTTP and temporary SQLite; no LMU live test.");
    }finally{await app.StopAsync();}
}finally {
    string full=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath());
    if(!full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("lmu-b2-tests-"))throw new IOException("Unsafe cleanup");
    Directory.Delete(full,true);
}
