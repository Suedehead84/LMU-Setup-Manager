using Lmu.SetupLibrary;
using Lmu.LiveCapture;
using Lmu.Diagnostics;
using Microsoft.Data.Sqlite;
using static Lmu.SetupLibrary.LibraryDatabase;

int count=0;
void Check(bool value,string message){if(!value)throw new Exception("FAIL "+message);count++;Console.WriteLine("PASS "+message);}
void Reject<T>(Action action,string message)where T:Exception {bool rejected=false;try{action();}catch(T){rejected=true;}Check(rejected,message);}
async Task RejectAsync<T>(Func<Task> action,string message)where T:Exception {bool rejected=false;try{await action();}catch(T){rejected=true;}Check(rejected,message);}
var root=Path.Combine(Path.GetTempPath(),"lmu-b1-tests-"+Guid.NewGuid());Directory.CreateDirectory(root);
try {
    var db=new LibraryDatabase(Path.Combine(root,"library.sqlite"));var service=new SetupLibraryService(db);
    var vehicle=new Vehicle("SYNTHETIC_MODEL","Synthetic GT3","GT3",new(10,10,10,12,"SYNTHETIC documented bounds"));
    var track=new Track("SYNTHETIC_TRACK","Synthetic Track");var layout=new TrackLayout(track.Id,"LAYOUT_A","Layout A");
    service.RegisterVehicle(vehicle);service.RegisterTrack(track);service.RegisterLayout(layout);
    service.RegisterVehicle(vehicle);service.RegisterTrack(track);service.RegisterLayout(layout);
    Check(service.Vehicles().Single()==vehicle && service.Tracks().Single()==track && service.Layouts(track.Id).Single()==layout,"catalog registration and idempotent reads");
    Reject<LibraryConflictException>(()=>service.RegisterVehicle(vehicle with{Name="conflicting label"}),"catalog IDs cannot silently overwrite existing metadata");
    Reject<LibraryValidationException>(()=>service.RegisterLayout(layout with{TrackId="missing"}),"layout must belong to an existing track");
    Reject<LibraryValidationException>(()=>service.RegisterVehicle(vehicle with{Id=" ",Limits=null}),"empty technical ID rejected");
    Reject<LibraryValidationException>(()=>service.RegisterVehicle(vehicle with{Id="proofless",Limits=new(3)}),"model-specific limits require evidence");
    var key=new SetupKey(vehicle.Id,track.Id,layout.Id);var draft=new SetupDraft("Safe Quali",new(7,7,7,6,51.25),"manual without LMU",PreferredUse:"Qualifying");
    var created=service.CreateManual(key,draft);
    Check(created.Current.Number==1 && created.Current.Content==draft && created.Origin==SetupOrigin.Manual,"manual create persists all five values, front bias, notes and purpose");
    Check(created.Current.Capture is null && service.LapReferences(created.Current.Id).Count==0,"manual setup has no invented capture, lap or best time");
    var reopened=new SetupLibraryService(new LibraryDatabase(db.FilePath)).Get(created.Id);
    Check(reopened.Current==created.Current && reopened.Key==key && reopened.CreatedAt==created.CreatedAt,"disk persistence across close/reopen");
    Reject<LibraryValidationException>(()=>service.CreateManual(key with{VehicleId="missing"},draft),"unknown model not guessed");
    Reject<LibraryValidationException>(()=>service.CreateManual(key with{LayoutId="missing"},draft),"unknown layout not guessed");
    foreach(var bad in new[]{draft with{Name=" "},draft with{Values=draft.Values with{TC=-1}},draft with{Values=draft.Values with{TCCut=11}},
        draft with{Values=draft.Values with{TCSlip=11}},draft with{Values=draft.Values with{ABS=13}},draft with{Values=draft.Values with{BrakeBiasFrontPercent=double.NaN}},
        draft with{Values=draft.Values with{BrakeBiasFrontPercent=double.PositiveInfinity}},draft with{Values=draft.Values with{BrakeBiasFrontPercent=100.01}},
        draft with{Values=draft.Values with{BrakeBiasFrontPercent=-.01}},draft with{Mode=(LibraryMode)999},draft with{Values=null!}})
        Reject<ArgumentException>(()=>service.CreateManual(key,bad),"invalid/missing manual input rejected atomically");
    Check(service.Search(new()).Count==1,"failed creates leave no orphan setup/version");
    service.RegisterVehicle(new("UNBOUNDED_MODEL","No proven maxima"));
    var unrestricted=service.CreateManual(key with{VehicleId="UNBOUNDED_MODEL"},draft with{Values=new(300,400,500,600,52)});
    Check(unrestricted.Current.Content.Values.TC==300,"unknown model limits are not replaced by an invented global 255 bound");
    var edited=service.Edit(created.Id,created.Current.Id,draft with{Name="Safe Race",Values=draft.Values with{TC=8},Notes="revision",PreferredUse="Race"});
    Check(edited.Versions.Count==2 && edited.Current.Number==2 && edited.Versions[0]==created.Current,"edit appends immutable full version, original values preserved");
    Reject<LibraryConflictException>(()=>service.Edit(created.Id,created.Current.Id,draft),"stale writer cannot overwrite current setup");
    Check(service.Get(created.Id).Current.Id==edited.Current.Id,"conflicting edit leaves head unchanged");
    var concurrent=service.CreateManual(key,draft with{Name="concurrency"});
    async Task<bool> Compete(int tc)=>await Task.Run(()=>{try{new SetupLibraryService(db).Edit(concurrent.Id,concurrent.Current.Id,draft with{Values=draft.Values with{TC=tc}});return true;}catch(LibraryConflictException){return false;}});
    var races=await Task.WhenAll(Compete(8),Compete(9));
    Check(races.Count(ok=>ok)==1 && service.Get(concurrent.Id).Versions.Count==2,"two concurrent writers commit exactly one revision; loser receives conflict");
    service.Delete(concurrent.Id,service.Get(concurrent.Id).Current.Id);
    var favorite=service.SetFavorite(created.Id,edited.Current.Id,true);
    Check(favorite.Current.Content.Favorite && !favorite.Versions[1].Content.Favorite && service.Search(new(FavoritesOnly:true)).Single().Id==created.Id,"favorite is versioned and searchable");
    var noChange=service.SetFavorite(created.Id,favorite.Current.Id,true);
    Check(noChange.Versions.Count==favorite.Versions.Count,"identical edit is a no-op");
    var renamed=service.Rename(created.Id,favorite.Current.Id,"Own ' name % _");
    Check(renamed.Id==created.Id && renamed.Current.Number==4 && renamed.Current.Content.Values==edited.Current.Content.Values,"rename preserves setup identity and values");
    var duplicate=service.Duplicate(created.Id,renamed.Current.Id,"Esport Quali");
    var savedAs=service.SaveAs(created.Id,renamed.Current.Id,"Test 1");
    Check(duplicate.Id!=created.Id && savedAs.Id!=duplicate.Id && duplicate.Current.Number==1 && duplicate.Origin==SetupOrigin.Copied &&
        duplicate.CopiedFromVersionId==renamed.Current.Id && !duplicate.Current.Content.Favorite,"duplicate/save-as create separate variants with source-version lineage");
    Check(service.Get(created.Id).Current.Id==renamed.Current.Id,"copy never changes source");
    Check(service.Search(new(vehicle.Id,track.Id,layout.Id)).Count==3,"exact vehicle/track/layout search returns multiple variants");
    Check(service.Search(new(NameContains:"' name % _")).Single().Id==created.Id,"parameterized literal name search handles SQL/wildcard characters");
    Reject<LibraryValidationException>(()=>service.Search(new(LayoutId:layout.Id)),"unscoped layout search rejected");
    service.RegisterLayout(new(track.Id,"LAYOUT_B","Layout B"));
    service.CreateManual(key with{LayoutId="LAYOUT_B"},draft);
    Check(service.Search(new(vehicle.Id,track.Id,"LAYOUT_B")).Count==1 && service.Search(new(vehicle.Id,track.Id,layout.Id)).Count==3,"separate layout identities never merge");
    Reject<LibraryConflictException>(()=>service.Delete(created.Id,edited.Current.Id),"stale delete rejected");
    service.Delete(created.Id,renamed.Current.Id);
    Reject<KeyNotFoundException>(()=>service.Get(created.Id),"soft-deleted setup hidden by default");
    var trash=service.Get(created.Id,true);
    Check(trash.DeletedAt is not null && trash.Versions.Count==4 && service.Search(new(IncludeDeleted:true)).Any(s=>s.Id==created.Id),"trash retains every version and can be listed");
    Reject<KeyNotFoundException>(()=>service.Edit(created.Id,renamed.Current.Id,draft),"deleted setup cannot be edited accidentally");
    var restored=service.Restore(created.Id,renamed.Current.Id);
    Check(restored.DeletedAt is null && restored.Current.Id==renamed.Current.Id,"restore recovers the same setup/version without loss");
    var backup=Path.Combine(root,"backup.sqlite");service.Backup(backup);
    Check(new SetupLibraryService(new LibraryDatabase(backup)).Get(created.Id).Current==restored.Current,"SQLite online backup reopens with complete committed data");
    Reject<IOException>(()=>service.Backup(backup),"backup never overwrites an existing target");
    Reject<IOException>(()=>service.Backup(db.FilePath),"backup cannot overwrite source database");
    using(var c=db.Connect()) {
        Check((string?)Scalar(c,null,"PRAGMA integrity_check")=="ok" && Convert.ToInt32(Scalar(c,null,"PRAGMA foreign_keys"))==1,"real SQLite integrity and foreign keys enabled");
        Reject<SqliteException>(()=>Execute(c,null,"UPDATE setup_versions SET tc=1"),"database forbids mutating historical versions");
        Reject<SqliteException>(()=>Execute(c,null,"DELETE FROM setup_versions"),"database forbids deleting historical versions");
    }
    var lap=new LapReference(Guid.NewGuid(),restored.Current.Id,115.27,LapValidity.Unknown,true,true,true,true,false,"SYNTHETIC explicit manual reference",LapAssignment.ExperimentalOpen,DateTimeOffset.UtcNow);
    service.AddLapReference(lap);
    Check(service.LapReferences(restored.Current.Id).Single()==lap && !lap.AutomaticallyEligibleForBestTime,"unknown/open lap keeps native time and coverage separate, never automatic best");
    Reject<LibraryValidationException>(()=>service.AddLapReference(lap with{Id=Guid.NewGuid(),SessionFixedSetup=true}),"Open experiment cannot masquerade as Fixed session");
    Reject<LibraryValidationException>(()=>service.AddLapReference(lap with{Id=Guid.NewGuid(),NativeTimeSeconds=null}),"confirmed timing requires a native time");
    Reject<LibraryValidationException>(()=>service.AddLapReference(lap with{Id=Guid.NewGuid(),NativeTimeSeconds=double.NaN}),"nonfinite lap time rejected");
    Reject<LibraryValidationException>(()=>service.AddLapReference(lap with{Id=Guid.NewGuid(),TelemetryFullyObserved=false}),"combined observation status cannot contradict telemetry coverage");
    Reject<LibraryValidationException>(()=>service.AddLapReference(lap with{Id=Guid.NewGuid(),Assignment=(LapAssignment)999}),"no unimplemented automatic official-history route");
    Reject<LibraryValidationException>(()=>service.AddLapReference(lap with{Id=Guid.NewGuid(),VersionId=Guid.NewGuid()}),"lap reference requires an actual immutable version");
    RejectAsync<LibraryValidationException>(()=>service.CaptureCurrent("No game"),"missing live reader does not fabricate values").GetAwaiter().GetResult();

    // Actual v1 -> v2 upgrade, backup, transaction rollback, future/foreign DB rejection.
    var oldPath=Path.Combine(root,"old","v1.sqlite");var oldDb=new LibraryDatabase(oldPath,1);var oldService=new SetupLibraryService(oldDb);
    oldService.RegisterVehicle(vehicle);oldService.RegisterTrack(track);oldService.RegisterLayout(layout);var oldSetup=oldService.CreateManual(key,draft);
    Reject<InvalidOperationException>(()=>new LibraryDatabase(oldPath,2,(_,_)=>throw new InvalidOperationException("SYNTHETIC migration failure")),"failed migration aborts transaction");
    using(var c=oldDb.Connect())Check(Convert.ToInt32(Scalar(c,null,"PRAGMA user_version"))==1 && Scalar(c,null,"SELECT name FROM sqlite_master WHERE name='lap_references'") is null,"failed migration leaves v1 schema intact");
    var upgraded=new LibraryDatabase(oldPath);var upgradedService=new SetupLibraryService(upgraded);
    Check(upgradedService.Get(oldSetup.Id).Current==oldSetup.Current && upgradedService.LapReferences(oldSetup.Current.Id).Count==0,"v1 data survives real v2 migration");
    Check(Directory.GetFiles(Path.Combine(root,"old","backups"),"*.sqlite").Length==2,"backup exists before each attempted upgrade, including failed migration");
    using(var c=upgraded.Connect())Check(Convert.ToInt32(Scalar(c,null,"SELECT count(*) FROM schema_migrations"))==LibraryDatabase.CurrentSchema,"versioned migration ledger persisted");
    new LibraryDatabase(oldPath);
    Check(Directory.GetFiles(Path.Combine(root,"old","backups"),"*.sqlite").Length==2,"opening current schema does not migrate/rebackup");
    using(var c=upgraded.Connect())Execute(c,null,"PRAGMA user_version=99");
    Reject<InvalidDataException>(()=>new LibraryDatabase(oldPath),"future schema refused without downgrade");
    using(var c=upgraded.Connect()){Check(Convert.ToInt32(Scalar(c,null,"PRAGMA user_version"))==99,"future schema unchanged");Execute(c,null,"PRAGMA user_version=2; UPDATE schema_migrations SET checksum='tampered' WHERE version=2");}
    Reject<InvalidDataException>(()=>new LibraryDatabase(oldPath),"migration checksum tampering refused");
    var foreign=Path.Combine(root,"foreign.sqlite");using(var c=new SqliteConnection("Data Source="+foreign+";Pooling=False")){c.Open();Execute(c,null,"CREATE TABLE other(value TEXT); INSERT INTO other VALUES('keep')");}
    Reject<InvalidDataException>(()=>new LibraryDatabase(foreign),"unrelated existing SQLite database not initialized over");
    using(var c=new SqliteConnection("Data Source="+foreign+";Pooling=False")){c.Open();Check((string?)Scalar(c,null,"SELECT value FROM other")=="keep","foreign user data remains intact after refusal");}

    // SYNTHETIC live samples only; no LMU process or external driver/profile fixture.
    var now=DateTimeOffset.UtcNow;var fakeClock=new FixedClock(now);
    var identity=new SessionIdentity(vehicle.Id,vehicle.Name,track.Id,track.Name,layout.Id,layout.Name,"SYNTHETIC catalog proof","Corroborated",null);
    Sample SampleAt(double et,DateTimeOffset at) {
        var f=new Dictionary<string,Datum>();
        foreach(var (k,v) in new (string,object?)[]{("GameRunning",true),("GameVersion",14200),("InRealtime",true),("PlayerControl",0),("InGarage",false),("FixedSetup",false),("GamePhase",5),
            ("RawVehicleModel",vehicle.Name),("RawTrackName",layout.Name),("PlayerVehicleFile","SYNTHETIC_ENTRY"),("PlayerSlotId",1),("SessionCode",5),("SessionStart",0f),
            ("TelemetryElapsed",et),("SessionElapsed",et),("TC",7),("TCCut",7),("TCSlip",7),("ABS",6),("BrakeBias",51.25),("TCMax",10),("TCCutMax",10),("TCSlipMax",10),("ABSMax",12)})f[k]=new(v,"SYNTHETIC");
        return new(at,"LIVE",f);
    }
    var before=SampleAt(10,now.AddMilliseconds(-100));var current=SampleAt(10.1,now);
    var observation=LiveSetupReader.FromSamples(before,current,identity,"SYNTHETIC-process",now);
    Check(observation.Evidence.SessionFixedSetup==false && observation.Values==draft.Values,"read-only adapter accepts complete corroborated Open-session capture");
    var captureService=new SetupLibraryService(db,new FixedReader(observation),fakeClock);
    var automatic=await captureService.CaptureCurrent("Safe Race");
    Check(automatic.Origin==SetupOrigin.Automatic && automatic.Current.Content.Mode==LibraryMode.FixedLab && automatic.Current.Capture?.SessionFixedSetup==false,"Fixed Lab is independent of actual Open session flag");
    Check(service.Get(created.Id).Current==restored.Current,"automatic capture never overwrites manual variant");
    var another=await captureService.CaptureCurrent("Safe Race");
    Check(another.Id!=automatic.Id,"repeated same-name capture creates a new variant instead of overwriting");
    var learned=observation with{Vehicle=new("UNBOUNDED_MODEL","No proven maxima")};
    await new SetupLibraryService(db,new FixedReader(learned),fakeClock).CaptureCurrent("Learned limits");
    Check(service.Vehicles().Single(v=>v.Id=="UNBOUNDED_MODEL").Limits==observation.NativeLimits,"native capture records proven bounds when model limits were previously unknown");
    Check(service.Get(unrestricted.Id).Current==unrestricted.Current,"learning bounds never rewrites an existing manual setup/version");
    Reject<LibraryValidationException>(()=>service.CreateManual(key with{VehicleId="UNBOUNDED_MODEL"},draft with{Values=draft.Values with{TC=11}}),"later manual input obeys newly measured model-specific bounds");
    var autoFavorite=service.SetFavorite(automatic.Id,automatic.Current.Id,true);
    Check(autoFavorite.Current.Capture==automatic.Current.Capture,"favorite retains unchanged electronics capture evidence");
    var manualRevision=service.Edit(automatic.Id,autoFavorite.Current.Id,autoFavorite.Current.Content with{Values=draft.Values with{TC=8}});
    Check(manualRevision.Current.Capture is null && manualRevision.Versions[0].Capture is not null,"manual electronics edit does not mislabel new values as live observed");
    await RejectAsync<LibraryValidationException>(()=>new SetupLibraryService(db,new FixedReader(observation with{Evidence=observation.Evidence with{ObservedAt=now.AddSeconds(-3)}}),fakeClock).CaptureCurrent("stale"),"stale observation rejected");
    await RejectAsync<LibraryValidationException>(()=>new SetupLibraryService(db,new FixedReader(observation with{Evidence=observation.Evidence with{SessionFixedSetup=null}}),fakeClock).CaptureCurrent("unknown"),"unknown actual session flag not invented");
    await RejectAsync<LibraryValidationException>(()=>new SetupLibraryService(db,new FixedReader(observation with{NativeLimits=new(0,10,10,12,"SYNTHETIC")}),fakeClock).CaptureCurrent("missing"),"native maximum zero remains unavailable");
    await RejectAsync<LibraryValidationException>(()=>new SetupLibraryService(db,new FixedReader(observation with{Values=observation.Values with{TC=11}}),fakeClock).CaptureCurrent("out of range"),"capture obeys measured model bounds");
    var isolated=observation with{Vehicle=observation.Vehicle with{Id="NEW_CAPTURE_MODEL"},Track=new("NEW_CAPTURE_TRACK","Synthetic new track"),Layout=new("NEW_CAPTURE_TRACK","NEW_CAPTURE_LAYOUT","New layout")};
    await RejectAsync<LibraryValidationException>(()=>new SetupLibraryService(db,new FixedReader(isolated),fakeClock).CaptureCurrent(" "),"failed capture rolls back catalog inserts and setup creation together");
    Check(!service.Vehicles().Any(v=>v.Id=="NEW_CAPTURE_MODEL")&&!service.Tracks().Any(v=>v.Id=="NEW_CAPTURE_TRACK"),"failed capture leaves no partial identity metadata");
    var fixedSession=observation with{Evidence=observation.Evidence with{SessionFixedSetup=true}};
    var fixedSaved=await new SetupLibraryService(db,new FixedReader(fixedSession),fakeClock).CaptureCurrent("Fixed capture");
    Check(fixedSaved.Current.Capture?.SessionFixedSetup==true && fixedSaved.Current.Content.Mode==LibraryMode.FixedLab,"actual Fixed session flag remains truthful and independent of library mode");
    foreach(var field in new[]{"TC","TCCut","TCSlip","ABS","BrakeBias","TCMax","FixedSetup"}) {
        var missing=current with{Fields=new(current.Fields)};missing.Fields[field]=new(null,"SYNTHETIC unavailable");
        Reject<LibraryValidationException>(()=>LiveSetupReader.FromSamples(before,missing,identity,"SYNTHETIC-process",now),"missing live "+field+" rejected without defaults");
    }
    Reject<LibraryValidationException>(()=>LiveSetupReader.FromSamples(before,current with{Mode="DEMO"},identity,"SYNTHETIC-process",now),"demo cannot become automatic real capture");
    Reject<LibraryValidationException>(()=>LiveSetupReader.FromSamples(before,current,SessionIdentity.Unknown("SYNTHETIC ambiguous"),"SYNTHETIC-process",now),"ambiguous identity cannot become automatic capture");
    Reject<LibraryValidationException>(()=>LiveSetupReader.FromSamples(before,current,identity,"SYNTHETIC-process",now.AddSeconds(3)),"adapter rejects stale sample");
    var switched=current with{Fields=new(current.Fields)};switched.Fields["PlayerVehicleFile"]=new("DIFFERENT_ENTRY","SYNTHETIC");
    Reject<LibraryValidationException>(()=>LiveSetupReader.FromSamples(before,switched,identity,"SYNTHETIC-process",now),"native entry switch invalidates capture pair");
    Console.WriteLine($"All {count} B1 assertions passed. Real temporary SQLite databases; SYNTHETIC live inputs, no new LMU drive.");
} finally {
    // Explicitly generated test directory only; all database connections have been disposed.
    var absolute=Path.GetFullPath(root);var temp=Path.GetFullPath(Path.GetTempPath());
    if(!absolute.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(absolute).StartsWith("lmu-b1-tests-"))throw new IOException("Unsafe test cleanup path");
    Directory.Delete(absolute,true);
}
sealed class FixedClock(DateTimeOffset at):TimeProvider {public override DateTimeOffset GetUtcNow()=>at;}
sealed class FixedReader(ObservedSetup observed):ILiveSetupReader {public Task<ObservedSetup> ReadCurrent(CancellationToken cancellationToken=default)=>Task.FromResult(observed);}
