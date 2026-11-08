using Lmu.Localization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lmu.LiveCapture;
using Lmu.SetupLibrary;

if(args.Length==1&&args[0]=="--help") {
    Console.WriteLine("Phase B1 local JSON test interface (no web UI): --db <library.sqlite> --request <request.json>\nRequest: {\"Action\":\"setup.search\",\"Data\":{}}. Details: docs/PHASE_B1.md");return 0;
}
var options=new JsonSerializerOptions {WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,RespectRequiredConstructorParameters=true};
options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues:false));
try {
    if(args.Length!=4||args[0]!="--db"||args[2]!="--request")throw new ArgumentException("Erwartet: --db <library.sqlite> --request <request.json>");
    using var input=JsonDocument.Parse(File.ReadAllText(args[3]));
    var service=new SetupLibraryService(new LibraryDatabase(args[1]),new LiveSetupReader());
    var requests=input.RootElement.ValueKind==JsonValueKind.Array?input.RootElement.EnumerateArray().Select(e=>e.Clone()).ToArray():[input.RootElement.Clone()];
    var results=new List<object?>();
    foreach(var request in requests) {
        var action=request.GetProperty("Action").GetString();var data=request.GetProperty("Data");
        T Get<T>()=>data.Deserialize<T>(options)??throw new ArgumentException(UiText.C("diagnostics.requestDataMissing"));
        object? result;
        switch(action) {
            case "catalog.vehicle":service.RegisterVehicle(Get<Vehicle>());result=new {Saved=true};break;
            case "catalog.track":service.RegisterTrack(Get<Track>());result=new {Saved=true};break;
            case "catalog.layout":service.RegisterLayout(Get<TrackLayout>());result=new {Saved=true};break;
            case "catalog.read":result=new {Vehicles=service.Vehicles(),Tracks=service.Tracks(),Layouts=service.Tracks().SelectMany(t=>service.Layouts(t.Id))};break;
            case "setup.create":var create=Get<Create>();result=service.CreateManual(create.Key,create.Draft);break;
            case "setup.get":var get=Get<GetSetup>();result=service.Get(get.Id,get.IncludeDeleted);break;
            case "setup.search":result=service.Search(Get<SetupQuery>());break;
            case "setup.edit":var edit=Get<Edit>();result=service.Edit(edit.Id,edit.ExpectedVersion,edit.Draft);break;
            case "setup.rename":var rename=Get<Named>();result=service.Rename(rename.Id,rename.ExpectedVersion,rename.Name);break;
            case "setup.duplicate":var copy=Get<Named>();result=service.Duplicate(copy.Id,copy.ExpectedVersion,copy.Name);break;
            case "setup.saveAs":var save=Get<Named>();result=service.SaveAs(save.Id,save.ExpectedVersion,save.Name);break;
            case "setup.favorite":var favorite=Get<Favorite>();result=service.SetFavorite(favorite.Id,favorite.ExpectedVersion,favorite.Value);break;
            case "setup.delete":var delete=Get<Versioned>();service.Delete(delete.Id,delete.ExpectedVersion);result=new {MovedToTrash=true};break;
            case "setup.restore":var restore=Get<Versioned>();result=service.Restore(restore.Id,restore.ExpectedVersion);break;
            case "setup.capture":var capture=Get<Capture>();result=await service.CaptureCurrent(capture.Name,capture.PreferredUse);break;
            case "backup":service.Backup(Get<Backup>().Path);result=new {Saved=true};break;
            case "lap.addReference":result=service.AddLapReference(Get<LapReference>());break;
            case "lap.listReferences":result=service.LapReferences(Get<Version>().VersionId);break;
            default:throw new ArgumentException(UiText.C("diagnostics.unknownAction")+action);
        }results.Add(new {Action=action,Result=result});
    }
    Console.WriteLine(JsonSerializer.Serialize(results,options));return 0;
}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException or KeyNotFoundException or JsonException or Microsoft.Data.Sqlite.SqliteException) {
    Console.Error.WriteLine(JsonSerializer.Serialize(new {Error=e.Message,Type=e.GetType().Name},options));return 2;
}
record Create(SetupKey Key,SetupDraft Draft);
record GetSetup(Guid Id,bool IncludeDeleted=false);
record Edit(Guid Id,Guid ExpectedVersion,SetupDraft Draft);
record Named(Guid Id,Guid ExpectedVersion,string Name);
record Favorite(Guid Id,Guid ExpectedVersion,bool Value);
record Versioned(Guid Id,Guid ExpectedVersion);
record Capture(string Name,string? PreferredUse=null);
record Backup(string Path);
record Version(Guid VersionId);
