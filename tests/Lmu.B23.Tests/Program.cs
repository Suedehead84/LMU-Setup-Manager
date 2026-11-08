using Lmu.SetupLibrary;
using Lmu.LiveCapture;
using Lmu.Diagnostics;
using Lmu.Web;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using static Lmu.SetupLibrary.LibraryDatabase;

if(args.FirstOrDefault()=="--restore-crash") {
    var crashService=new LibraryRestoreService(new LibraryDatabase(args[1]));using var input=File.OpenRead(args[2]);var p=await crashService.Stage(input);
    crashService.Confirm(p.Id,"BIBLIOTHEK ERSETZEN",point=>{if(point=="after-clear")Environment.Exit(23);});return;
}
int count=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
void Reject<T>(Action work,string name)where T:Exception{try{work();}catch(T){Check(true,name);return;}throw new Exception("FAIL "+name);}
async Task RejectAsync<T>(Func<Task> work,string name)where T:Exception{try{await work();}catch(T){Check(true,name);return;}throw new Exception("FAIL "+name);}
string root=Path.Combine(Path.GetTempPath(),"lmu-b23-tests-"+Guid.NewGuid());Directory.CreateDirectory(root);
try {
    var key=new SetupKey("SYNTHETIC_CAR","SYNTHETIC_TRACK","A");
    SavedSetup Seed(LibraryDatabase db,string name){var s=new SetupLibraryService(db);s.RegisterVehicle(new(key.VehicleId,"Synthetic car"));s.RegisterTrack(new(key.TrackId,"Synthetic track"));s.RegisterLayout(new(key.TrackId,key.LayoutId,"Layout A"));return s.CreateManual(key,new(name,new(7,8,7,6,51.25)));}
    var liveDb=new LibraryDatabase(Path.Combine(root,"live.sqlite"));var live=new SetupLibraryService(liveDb);var existing=Seed(liveDb,"Keep current until confirmed");
    var oldDb=new LibraryDatabase(Path.Combine(root,"v3.sqlite"),3);var original=Seed(oldDb,"Restored backup setup");var oldService=new SetupLibraryService(oldDb);oldService.SetFavorite(original.Id,original.Current.Id,true);
    string backup=Path.Combine(root,"source-backup.sqlite");oldDb.Backup(backup);var sourceHash=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(backup));
    var restore=new LibraryRestoreService(liveDb);
    async Task<RestorePreview> Stage(string? file=null){using var stream=File.OpenRead(file??backup);return await restore.Stage(stream);}
    string baseline=liveDb.ContentSignature();var preview=await Stage();
    Check(preview.OriginalSchema==3&&preview.TargetSchema==4&&preview.Setups==1&&preview.Versions==2,"v3 backup preview migrates private copy and reports real counts");
    Check(liveDb.ContentSignature()==baseline&&System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(backup)).SequenceEqual(sourceHash),"preview changes neither current library nor uploaded original");
    Reject<LibraryValidationException>(()=>restore.Confirm(preview.Id,"yes"),"explicit replacement phrase required");
    foreach(var point in new[]{"after-backup","after-clear","before-commit"}) {
        Reject<IOException>(()=>restore.Confirm(preview.Id,"BIBLIOTHEK ERSETZEN",stage=>{if(stage==point)throw new IOException("SYNTHETIC interruption");}),"simulated restore interruption rolls back: "+point);
        Check(liveDb.ContentSignature()==baseline&&live.Get(existing.Id).Current.Id==existing.Current.Id,"old data and schema preserved after interruption: "+point);
    }
    var processPath=Environment.ProcessPath!;var start=new System.Diagnostics.ProcessStartInfo(processPath){UseShellExecute=false,CreateNoWindow=true};
    if(Path.GetFileNameWithoutExtension(processPath).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(Clock).Assembly.Location);
    foreach(var argument in new[]{"--restore-crash",liveDb.FilePath,backup})start.ArgumentList.Add(argument);
    using(var child=System.Diagnostics.Process.Start(start)!) {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));await child.WaitForExitAsync(timeout.Token);Check(child.ExitCode==23,"restore process terminates inside uncommitted replacement transaction");
    }
    Check(liveDb.ContentSignature()==baseline,"SQLite recovery preserves old library after actual process exit without disposal");
    var result=restore.Confirm(preview.Id,"BIBLIOTHEK ERSETZEN");
    Check(live.Search(new()).Single().Id==original.Id&&live.Get(original.Id).Versions.Count==2,"restore replaces whole library with immutable historical IDs intact");
    Check(!live.Search(new()).Any(s=>s.Id==existing.Id)&&File.Exists(Path.Combine(root,"backups",result.SafetyBackup)),"restore does not merge and always creates safety backup");
    Check(new SetupLibraryService(new LibraryDatabase(liveDb.FilePath)).Get(original.Id).Current.Content.Favorite,"reopened services read restored state without process restart");
    Reject<LibraryConflictException>(()=>restore.Confirm(preview.Id,"BIBLIOTHEK ERSETZEN"),"restore confirmation cannot be replayed");
    preview=await Stage();var edited=live.Rename(original.Id,live.Get(original.Id).Current.Id,"Changed after preview");baseline=liveDb.ContentSignature();
    Reject<LibraryConflictException>(()=>restore.Confirm(preview.Id,"BIBLIOTHEK ERSETZEN"),"concurrent library edit invalidates restore preview");
    Check(liveDb.ContentSignature()==baseline&&live.Get(original.Id).Current.Id==edited.Current.Id,"concurrent update survives rejected restore");restore.Discard(preview.Id);
    preview=await Stage();string staged=Path.Combine(root,".restore-staging",preview.Id,"candidate.sqlite");
    using(var c=new SqliteConnection("Data Source="+staged+";Pooling=False")){c.Open();Execute(c,null,"CREATE TABLE tampered(value TEXT)");}
    Reject<LibraryValidationException>(()=>restore.Confirm(preview.Id,"BIBLIOTHEK ERSETZEN"),"modified staged file rejected before replacement");restore.Discard(preview.Id);
    foreach(var version in new[]{1,2}){var db=new LibraryDatabase(Path.Combine(root,$"old-{version}.sqlite"),version);Seed(db,"Old schema "+version);string file=Path.Combine(root,$"backup-{version}.sqlite");db.Backup(file);var p=await Stage(file);Check(p.OriginalSchema==version&&p.TargetSchema==4,"supported backup schema migrates: v"+version);restore.Discard(p.Id);}
    restore.MigrationFault=(_,_)=>throw new IOException("SYNTHETIC failed staging migration");await RejectAsync<IOException>(()=>Stage(),"failed staging migration never touches current database");restore.MigrationFault=null;
    Check(liveDb.ContentSignature()==baseline,"failed migration keeps entire live library unchanged");
    async Task Bad(string name,Action<SqliteConnection> mutate){string file=Path.Combine(root,name+".sqlite");File.Copy(backup,file);using(var c=new SqliteConnection("Data Source="+file+";Pooling=False")){c.Open();mutate(c);}await RejectAsync<LibraryValidationException>(()=>Stage(file),"reject manipulated backup: "+name);}
    await Bad("extra-trigger",c=>Execute(c,null,"CREATE TRIGGER exfil AFTER INSERT ON tracks BEGIN SELECT load_extension('evil'); END"));
    await Bad("future",c=>Execute(c,null,"PRAGMA user_version=999"));
    await Bad("foreign",c=>Execute(c,null,"PRAGMA application_id=123"));
    await Bad("ledger",c=>Execute(c,null,"UPDATE schema_migrations SET checksum='fake' WHERE version=1"));
    await Bad("schema",c=>Execute(c,null,"ALTER TABLE vehicles ADD COLUMN extra TEXT"));
    await Bad("foreign-keys",c=>Execute(c,null,"PRAGMA foreign_keys=OFF; DELETE FROM tracks"));
    await Bad("typed-value",c=>Execute(c,null,"DROP TRIGGER versions_no_update; UPDATE setup_versions SET tc='not a number'; CREATE TRIGGER versions_no_update BEFORE UPDATE ON setup_versions BEGIN SELECT RAISE(ABORT,'Setup versions are immutable'); END"));
    await Bad("constraint",c=>Execute(c,null,"PRAGMA ignore_check_constraints=ON; DROP TRIGGER versions_no_update; UPDATE setup_versions SET tc=-1; CREATE TRIGGER versions_no_update BEFORE UPDATE ON setup_versions BEGIN SELECT RAISE(ABORT,'Setup versions are immutable'); END"));
    string broken=Path.Combine(root,"broken.sqlite");await File.WriteAllTextAsync(broken,"not a SQLite database");await RejectAsync<LibraryValidationException>(()=>Stage(broken),"reject corrupt/non-SQLite file");
    Check(liveDb.ContentSignature()==baseline,"every malformed restore preserves current data");
    using(var large=new MemoryStream(new byte[(int)LibraryRestoreService.MaximumBytes+1]))await RejectAsync<LibraryValidationException>(()=>restore.Stage(large),"oversized backup rejected before SQLite processing");
    using(var cancel=new CancellationTokenSource()){cancel.Cancel();using var input=File.OpenRead(backup);await RejectAsync<OperationCanceledException>(()=>restore.Stage(input,cancel.Token),"cancelled upload leaves no staged partial candidate");}
    var clock=new Clock(DateTimeOffset.UtcNow);var expiry=new LibraryRestoreService(liveDb,clock);using(var f=File.OpenRead(backup)){var p=await expiry.Stage(f);clock.Now=clock.Now.AddMinutes(16);Reject<LibraryConflictException>(()=>expiry.Confirm(p.Id,"BIBLIOTHEK ERSETZEN"),"bounded restore preview expires safely");expiry.Discard(p.Id);}

    var vehicleJson=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/vehicles.json"));var trackJson=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/tracks.json"));
    clock.Now=DateTimeOffset.UtcNow;var transport=new Transport(clock,vehicleJson,trackJson);var imports=new CatalogImportService(transport,live,clock);
    var cars=await imports.Preview("vehicles");var ferrari=cars.Choices.Single(c=>c.Id=="SYNTHETIC_DAWN");
    Check(ferrari.Candidates?.Select(c=>c.Name).SequenceEqual(new[]{"SYNTHETIC Dawn","SYNTHETIC Dawn Evo"})==true,"real Dawn package exposes exact two model candidates");
    Check(ferrari.Candidates!.All(c=>c.Class=="GT3")&&!ferrari.Selectable,"verified Dawn classes retained, ambiguous root remains blocked");
    var selected=new[]{ferrari.Id};var signatures=cars.Choices.ToDictionary(c=>c.Id,c=>c.Signature!);
    clock.Now=clock.Now.AddHours(4);var reviewed=await imports.Review(new("vehicles",selected,signatures));
    Check(!reviewed.CanImport&&reviewed.SelectedIds.SequenceEqual(selected),"selection survives hours; no implicit ambiguous assignment");
    var manual=new Dictionary<string,string>{{ferrari.Id,ferrari.Candidates![1].Id}};
    reviewed=await imports.Review(new("vehicles",selected,signatures,manual));
    Check(reviewed.CanImport&&reviewed.Changed.Count==0,"fresh review accepts unchanged explicitly chosen candidate after hours");
    var imported=await imports.Confirm(new(reviewed.Preview.Id,selected,manual));
    var binding=liveDb.ManualBindings().Single();
    Check(imported.VehiclesAdded==1&&binding.ComponentId==ferrari.Id&&binding.CandidateName=="SYNTHETIC Dawn Evo"&&binding.Source.Contains("MANUAL"),"manual candidate and origin persist under separate technical ID");
    Check(live.Vehicles().Single(c=>c.Id==binding.VehicleId).Name=="SYNTHETIC Dawn Evo"&&binding.VehicleId!=ferrari.Id,"manual variant never overwrites component identity");
    Check(new LibraryDatabase(liveDb.FilePath).ManualBindings().Single()==binding,"manual provenance survives reopening");
    var existingPreview=await imports.Preview("vehicles");Check(existingPreview.Choices.Single(c=>c.Id==ferrari.Id).Candidates!.Single(c=>c.Id==binding.VehicleId).Exists,"already imported manual candidate marked in new preview");
    Check(existingPreview.Choices.Single(c=>c.Id==ferrari.Id).Signature==ferrari.Signature,"personal import status does not change source integrity signature");
    var native=IdentityResolver.ReadVehicleCatalog(vehicleJson).First(c=>c.Package==ferrari.Id);
    Check(IdentityResolver.ResolveActive(new(native.Model,native.Stem,"SYNTHETIC Circuit Alpha","SYNTHETIC","5","False","1","0",10),vehicleJson,trackJson).SetupKey is null,"manual Dawn choice does not weaken Phase A live ambiguity gate");
    var bmw=cars.Choices.Single(c=>c.Name=="SYNTHETIC Aster GT3");
    var changed=JsonNode.Parse(vehicleJson)!.AsArray();foreach(var row in changed.Where(n=>n!["fullPathTree"]!.GetValue<string>().EndsWith("SYNTHETIC Aster GT3")))row!["fullPathTree"]=row["fullPathTree"]!.GetValue<string>().Replace(", GT3,",", VERIFIED_TEST_CLASS,");
    transport.Vehicles=changed.ToJsonString();var review=await imports.Review(new("vehicles",[bmw.Id],signatures));
    Check(review.CanImport&&review.Changed.SequenceEqual(new[]{bmw.Id}),"changed selected metadata reported for explicit reconfirmation");
    transport.Vehicles=vehicleJson;await RejectAsync<LibraryConflictException>(()=>imports.Confirm(new(review.Preview.Id,[bmw.Id])),"change between review and commit rejected");
    transport.Vehicles="[]";await RejectAsync<LmuSourceException>(()=>imports.Review(new("vehicles",[bmw.Id],signatures)),"missing/empty current catalog never trusts old IDs");
    transport.Vehicles=JsonSerializer.Serialize(JsonNode.Parse(vehicleJson)!.AsArray().Where(n=>!n!["fullPathTree"]!.GetValue<string>().EndsWith("SYNTHETIC Aster GT3")));review=await imports.Review(new("vehicles",[bmw.Id],signatures));Check(!review.CanImport&&review.Missing.SequenceEqual(new[]{bmw.Id}),"vanished model removed transparently and cannot import");transport.Vehicles=vehicleJson;

    // Authenticated upload + explicit confirmation over real HTTP, isolated DB only.
    var options=new ServerOptions(liveDb.FilePath,0,WebRoot:Path.GetFullPath("src/Lmu.Web/wwwroot"));await using var app=WebHostFactory.Create(options,catalogTransport:transport,startLiveWorker:false,timeProvider:clock);await app.StartAsync();
    try {
        var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();using var client=new HttpClient{BaseAddress=new(address)};
        var bytes=File.ReadAllBytes(backup);
        using(var r=await client.PostAsync("/api/restores/preview",new ByteArrayContent(bytes)))Check(r.StatusCode==HttpStatusCode.Unauthorized,"restore upload requires access grant");
        var pair=await client.PostAsJsonAsync("/api/session",new{key=options.AccessKey});var session=await pair.Content.ReadFromJsonAsync<JsonElement>();
        using(var r=await client.PostAsync("/api/restores/preview",new ByteArrayContent(bytes)))Check(r.StatusCode==HttpStatusCode.Forbidden,"restore upload requires CSRF");
        client.DefaultRequestHeaders.Add("X-LMU-CSRF",session.GetProperty("csrf").GetString());
        var oldEndpointPreview=await client.GetFromJsonAsync<ImportPreview>("/api/lmu/catalog/vehicles");transport.Vehicles=changed.ToJsonString();
        using(var oldEndpoint=await client.PostAsJsonAsync("/api/lmu/catalog/import",new CatalogSelection(oldEndpointPreview!.Id,[bmw.Id])))Check(oldEndpoint.StatusCode==HttpStatusCode.Conflict,"legacy import HTTP route also revalidates fresh catalog before writing");transport.Vehicles=vehicleJson;
        var largeDb=new LibraryDatabase(Path.Combine(root,"large.sqlite"));var largeService=new SetupLibraryService(largeDb);Seed(largeDb,"Large backup");for(int i=0;i<12;i++)largeService.CreateManual(key,new("Large "+i,new(1,2,3,4,52),Notes:new string('x',16000)));var largeFile=Path.Combine(root,"large-backup.sqlite");largeDb.Backup(largeFile);
        using(var largeResponse=await client.PostAsync("/api/restores/preview",new ByteArrayContent(File.ReadAllBytes(largeFile)))){Check(new FileInfo(largeFile).Length>128*1024&&largeResponse.IsSuccessStatusCode,"authenticated backup upload over ordinary 128 KiB request limit is supported");var largePreview=await largeResponse.Content.ReadFromJsonAsync<RestorePreview>();using var discard=await client.DeleteAsync("/api/restores/"+largePreview!.Id);Check(discard.IsSuccessStatusCode,"unused restore preview can be explicitly discarded");}
        using(var request=new HttpRequestMessage(HttpMethod.Post,"/api/restores/preview"){Content=new ByteArrayContent(bytes)}){request.Headers.Add("Origin","http://foreign.example");using var response=await client.SendAsync(request);Check(response.StatusCode==HttpStatusCode.Forbidden,"restore rejects foreign Origin");}
        using var uploaded=await client.PostAsync("/api/restores/preview",new ByteArrayContent(bytes));var p=await uploaded.Content.ReadFromJsonAsync<RestorePreview>();Check(uploaded.IsSuccessStatusCode&&p?.Setups==1,"HTTP upload validates and previews actual backup");
        using(var r=await client.PostAsJsonAsync("/api/restores/confirm",new RestoreConfirmRequest(p!.Id,"yes")))Check(r.StatusCode==HttpStatusCode.BadRequest,"HTTP restore rejects unconfirmed replacement");
        using(var r=await client.PostAsJsonAsync("/api/restores/confirm",new RestoreConfirmRequest(p!.Id,"BIBLIOTHEK ERSETZEN")))Check(r.IsSuccessStatusCode,"HTTP explicit restore commits successfully");
        var rows=await client.GetFromJsonAsync<JsonElement>("/api/setups");Check(rows.GetArrayLength()==1&&rows[0].GetProperty("name").GetString()=="Restored backup setup","same HTTP services immediately show restored library");
        using(var r=await client.PostAsync("/api/restores/preview",new ByteArrayContent([1,2,3])))Check(r.StatusCode==HttpStatusCode.BadRequest,"HTTP invalid file rejected without replacement");
    }finally{await app.StopAsync();}
    Console.WriteLine($"All {count} B2.3 assertions passed. Isolated SQLite + HTTP; no user database or LMU writes.");
}finally{if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("lmu-b23-tests-"))throw new IOException("Unsafe cleanup");Directory.Delete(root,true);}
sealed class Clock(DateTimeOffset at):TimeProvider{public DateTimeOffset Now=at;public override DateTimeOffset GetUtcNow()=>Now;}
sealed class Transport(Clock clock,string vehicles,string tracks):ILmuCatalogTransport{public string Vehicles=vehicles;public Task<RestCapture[]> Read(CancellationToken token)=>Task.FromResult<RestCapture[]>([new(clock.Now,"http://127.0.0.1:6397/rest/sessions/getAllVehicles",200,Vehicles,null,clock.Now),new(clock.Now,"http://127.0.0.1:6397/rest/sessions/getTracksAll",200,tracks,null,clock.Now)]);}
