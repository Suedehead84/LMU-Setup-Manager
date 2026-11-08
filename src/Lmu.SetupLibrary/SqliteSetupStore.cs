using Lmu.Localization;
using Microsoft.Data.Sqlite;
using static Lmu.SetupLibrary.LibraryDatabase;
namespace Lmu.SetupLibrary;

internal sealed class SqliteSetupStore(LibraryDatabase database,TimeProvider clock):ISetupStore
{

    private T Read<T>(Func<SqliteConnection,SqliteTransaction,T> action)
    {using var c=database.Connect();using var tx=c.BeginTransaction(deferred:true);var result=action(c,tx);tx.Commit();return result;}
    private T Write<T>(Func<SqliteConnection,SqliteTransaction,T> action)
    {using var c=database.Connect();using var tx=c.BeginTransaction();var result=action(c,tx);tx.Commit();return result;}
    private void Write(Action<SqliteConnection,SqliteTransaction> action)=>Write((c,t)=>{action(c,t);return 0;});
    public void RegisterVehicle(Vehicle vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);Validation.Text(vehicle.Id,UiText.C("library.vehicleId"));Validation.Text(vehicle.Name,UiText.C("library.vehicleName"));
        if(vehicle.Class is not null)Validation.Text(vehicle.Class,UiText.C("library.vehicleClass"));Validation.Limits(vehicle.Limits);
        Write((c,t)=>InsertVehicle(c,t,vehicle));
    }
    private static void InsertVehicle(SqliteConnection c,SqliteTransaction t,Vehicle v)
    {
        var old=ReadVehicles(c,t).SingleOrDefault(x=>x.Id==v.Id);
        if(old is not null){if(old!=v)throw new LibraryConflictException(UiText.C("library.vehicleIdExistsWithDifferentMetadataNoSilent"));return;}
        Execute(c,t,"INSERT INTO vehicles VALUES($id,$n,$class,$tc,$cut,$slip,$abs,$proof)",("$id",v.Id),("$n",v.Name),("$class",v.Class),
            ("$tc",v.Limits?.TC),("$cut",v.Limits?.TCCut),("$slip",v.Limits?.TCSlip),("$abs",v.Limits?.ABS),("$proof",v.Limits?.Evidence));
    }
    public void RegisterTrack(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);Validation.Text(track.Id,UiText.C("library.trackId"));Validation.Text(track.Name,UiText.C("library.trackName"));
        Write((c,t)=>InsertTrack(c,t,track));
    }
    private static void InsertTrack(SqliteConnection c,SqliteTransaction t,Track track)
    {
        var old=(string?)Scalar(c,t,"SELECT name FROM tracks WHERE id=$id",("$id",track.Id));
        if(old is not null){if(old!=track.Name)throw new LibraryConflictException(UiText.C("library.trackIdExistsWithADifferentName"));return;}
        Execute(c,t,"INSERT INTO tracks VALUES($id,$n)",("$id",track.Id),("$n",track.Name));
    }
    public void RegisterLayout(TrackLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);Validation.Text(layout.TrackId,UiText.C("library.trackId"));Validation.Text(layout.Id,UiText.C("library.layoutId"));Validation.Text(layout.Name,"Layoutname");
        Write((c,t)=>InsertLayout(c,t,layout));
    }
    private static void InsertLayout(SqliteConnection c,SqliteTransaction t,TrackLayout layout)
    {
        if(Scalar(c,t,"SELECT id FROM tracks WHERE id=$id",("$id",layout.TrackId)) is null)throw new LibraryValidationException(UiText.C("library.trackNotRegistered"));
        var old=(string?)Scalar(c,t,"SELECT name FROM layouts WHERE track_id=$t AND id=$l",("$t",layout.TrackId),("$l",layout.Id));
        if(old is not null){if(old!=layout.Name)throw new LibraryConflictException(UiText.C("library.layoutIdExistsWithADifferentName"));return;}
        Execute(c,t,"INSERT INTO layouts VALUES($t,$l,$n)",("$t",layout.TrackId),("$l",layout.Id),("$n",layout.Name));
    }
    public IReadOnlyList<Vehicle> Vehicles()=>Read((c,t)=>ReadVehicles(c,t));
    public void UpdateVehicle(Vehicle expected,Vehicle replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);ArgumentNullException.ThrowIfNull(replacement);Validation.Text(replacement.Name,UiText.C("library.vehicleName"));
        if(replacement.Class is not null)Validation.Text(replacement.Class,UiText.C("library.vehicleClass"));
        if(expected.Id!=replacement.Id||expected.Limits!=replacement.Limits)throw new LibraryValidationException(UiText.C("library.technicalIdAndVerifiedLimitsRemainUnchanged"));
        Write((c,t)=>{
            if(ReadVehicles(c,t).SingleOrDefault(v=>v.Id==expected.Id)!=expected)throw new LibraryConflictException(UiText.C("library.vehicleMetadataHasChangedReload"));
            Execute(c,t,"UPDATE vehicles SET name=$n,class=$class WHERE id=$id",("$n",replacement.Name),("$class",replacement.Class),("$id",expected.Id));
        });
    }
    public void UpdateTrack(Track expected,Track replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);ArgumentNullException.ThrowIfNull(replacement);Validation.Text(replacement.Name,UiText.C("library.trackName"));
        if(expected.Id!=replacement.Id)throw new LibraryValidationException(UiText.C("library.technicalTrackIdRemainsUnchanged"));
        Write((c,t)=>{
            if((string?)Scalar(c,t,"SELECT name FROM tracks WHERE id=$id",("$id",expected.Id))!=expected.Name)throw new LibraryConflictException(UiText.C("library.trackMetadataHasChangedReload"));
            Execute(c,t,"UPDATE tracks SET name=$n WHERE id=$id",("$n",replacement.Name),("$id",expected.Id));
        });
    }
    public void UpdateLayout(TrackLayout expected,TrackLayout replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);ArgumentNullException.ThrowIfNull(replacement);Validation.Text(replacement.Name,"Layoutname");
        if(expected.Id!=replacement.Id||expected.TrackId!=replacement.TrackId)throw new LibraryValidationException(UiText.C("library.technicalLayoutBindingRemainsUnchanged"));
        Write((c,t)=>{
            if((string?)Scalar(c,t,"SELECT name FROM layouts WHERE track_id=$t AND id=$id",("$t",expected.TrackId),("$id",expected.Id))!=expected.Name)throw new LibraryConflictException(UiText.C("library.layoutMetadataHasChangedReload"));
            Execute(c,t,"UPDATE layouts SET name=$n WHERE track_id=$t AND id=$id",("$n",replacement.Name),("$t",expected.TrackId),("$id",expected.Id));
        });
    }
    private static IReadOnlyList<Vehicle> ReadVehicles(SqliteConnection c,SqliteTransaction t)
    {
        using var cmd=Command(c,t,"SELECT id,name,class,tc_max,cut_max,slip_max,abs_max,limits_evidence FROM vehicles ORDER BY name,id");
        using var r=cmd.ExecuteReader();var result=new List<Vehicle>();
        while(r.Read()) {
            int? N(int i)=>r.IsDBNull(i)?null:r.GetInt32(i);
            var limits=new AidLimits(N(3),N(4),N(5),N(6),Text(r,7));
            result.Add(new(r.GetString(0),r.GetString(1),Text(r,2),limits==new AidLimits()?null:limits));
        }return result;
    }
    public IReadOnlyList<Track> Tracks()=>Read((c,t)=>{
        using var cmd=Command(c,t,"SELECT id,name FROM tracks ORDER BY name,id");using var r=cmd.ExecuteReader();var list=new List<Track>();
        while(r.Read())list.Add(new(r.GetString(0),r.GetString(1)));return (IReadOnlyList<Track>)list;
    });
    public IReadOnlyList<TrackLayout> Layouts(string trackId)=>Read((c,t)=>{
        using var cmd=Command(c,t,"SELECT track_id,id,name FROM layouts WHERE track_id=$t ORDER BY name,id",("$t",trackId));
        using var r=cmd.ExecuteReader();var list=new List<TrackLayout>();while(r.Read())list.Add(new(r.GetString(0),r.GetString(1),r.GetString(2)));return (IReadOnlyList<TrackLayout>)list;
    });
    public SavedSetup CreateManual(SetupKey key,SetupDraft draft)=>Write((c,t)=>Create(c,t,key,draft,SetupOrigin.Manual,null,null));
    public SavedSetup CreateCaptured(ObservedSetup snapshot,string name,string? preferredUse,SetupCategory category=SetupCategory.FixedLab)
    {
        var key=new SetupKey(snapshot.Vehicle.Id,snapshot.Track.Id,snapshot.Layout.Id);
        return Write((c,t)=>{
            SetupLibraryService.ValidateObservation(snapshot,clock.GetUtcNow());
            // Existing user catalog entries are never overwritten by a capture.
            if(Scalar(c,t,"SELECT id FROM vehicles WHERE id=$id",("$id",key.VehicleId)) is null)InsertVehicle(c,t,snapshot.Vehicle with {Limits=snapshot.NativeLimits});
            else if(ReadVehicles(c,t).Single(v=>v.Id==key.VehicleId).Limits is null)
                Execute(c,t,"UPDATE vehicles SET tc_max=$tc,cut_max=$cut,slip_max=$slip,abs_max=$abs,limits_evidence=$proof WHERE id=$id",
                    ("$tc",snapshot.NativeLimits.TC),("$cut",snapshot.NativeLimits.TCCut),("$slip",snapshot.NativeLimits.TCSlip),("$abs",snapshot.NativeLimits.ABS),
                    ("$proof",snapshot.NativeLimits.Evidence),("$id",key.VehicleId));
            if(Scalar(c,t,"SELECT id FROM tracks WHERE id=$id",("$id",key.TrackId)) is null)InsertTrack(c,t,snapshot.Track);
            if(Scalar(c,t,"SELECT id FROM layouts WHERE track_id=$t AND id=$l",("$t",key.TrackId),("$l",key.LayoutId)) is null)InsertLayout(c,t,snapshot.Layout);
            return Create(c,t,key,new(name,snapshot.Values,PreferredUse:preferredUse,Category:category),SetupOrigin.Automatic,null,snapshot.Evidence);
        });
    }
    public SavedSetup UpdateCaptured(Guid id,Guid expectedVersion,ObservedSetup snapshot)=>Write((c,t)=>{
        SetupLibraryService.ValidateObservation(snapshot,clock.GetUtcNow());
        var old=Load(c,t,id,false);Expected(old,expectedVersion);
        if(old.Key!=new SetupKey(snapshot.Vehicle.Id,snapshot.Track.Id,snapshot.Layout.Id))throw new LibraryConflictException(UiText.C("library.liveVehicleOrLayoutDoesNotExactlyMatch"));
        var draft=old.Current.Content with {Values=snapshot.Values};
        Validation.Draft(draft,ReadVehicles(c,t).Single(v=>v.Id==old.Key.VehicleId).Limits);
        InsertVersion(c,t,id,old.Current.Number+1,"LiveCaptured",draft,snapshot.Evidence,clock.GetUtcNow());return Load(c,t,id,false);
    });
    public CatalogImportResult ImportCatalog(CatalogBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);ArgumentNullException.ThrowIfNull(batch.Vehicles);ArgumentNullException.ThrowIfNull(batch.Tracks);ArgumentNullException.ThrowIfNull(batch.Layouts);
        foreach(var v in batch.Vehicles){Validation.Text(v.Id,UiText.C("library.modelId"));Validation.Text(v.Name,UiText.C("library.modelName"));if(v.Class is not null)Validation.Text(v.Class,UiText.C("library.vehicleClass"));Validation.Limits(v.Limits);}
        foreach(var t in batch.Tracks){Validation.Text(t.Id,UiText.C("library.trackId"));Validation.Text(t.Name,UiText.C("library.trackName"));}
        foreach(var l in batch.Layouts){Validation.Text(l.TrackId,UiText.C("library.trackId"));Validation.Text(l.Id,UiText.C("library.layoutId"));Validation.Text(l.Name,"Layoutname");}
        if(batch.Vehicles.Select(v=>v.Id).Distinct().Count()!=batch.Vehicles.Count||batch.Tracks.Select(t=>t.Id).Distinct().Count()!=batch.Tracks.Count||
            batch.Layouts.Select(l=>(l.TrackId,l.Id)).Distinct().Count()!=batch.Layouts.Count)throw new LibraryValidationException(UiText.C("library.duplicateTechnicalKeysInImport"));
        return Write((c,t)=>{
            int cars=0,tracks=0,layouts=0,kept=0;
            foreach(var v in batch.Vehicles)if(Scalar(c,t,"SELECT id FROM vehicles WHERE id=$id",("$id",v.Id)) is null){InsertVehicle(c,t,v);cars++;}else kept++;
            foreach(var track in batch.Tracks)if(Scalar(c,t,"SELECT id FROM tracks WHERE id=$id",("$id",track.Id)) is null){InsertTrack(c,t,track);tracks++;}else kept++;
            foreach(var l in batch.Layouts) {
                if(Scalar(c,t,"SELECT id FROM tracks WHERE id=$id",("$id",l.TrackId)) is null)throw new LibraryValidationException(UiText.C("library.importedLayoutHasNoRegisteredTrack"));
                if(Scalar(c,t,"SELECT id FROM layouts WHERE track_id=$t AND id=$l",("$t",l.TrackId),("$l",l.Id)) is null){InsertLayout(c,t,l);layouts++;}else kept++;
            }
            foreach(var b in batch.ManualBindings??[]) {
                Validation.Text(b.ComponentId,UiText.C("library.componentId"));Validation.Text(b.CandidateName,UiText.C("library.candidate"));Validation.Text(b.CatalogSignature,UiText.C("library.catalogEvidence"));Validation.Text(b.Source,UiText.C("library.origin"),1024);
                var prior=Scalar(c,t,"SELECT catalog_signature FROM manual_vehicle_bindings WHERE component_id=$c AND candidate_name=$n",("$c",b.ComponentId),("$n",b.CandidateName));
                if(prior is null)Execute(c,t,"INSERT INTO manual_vehicle_bindings VALUES($c,$n,$v,$h,$s,$at)",("$c",b.ComponentId),("$n",b.CandidateName),("$v",b.VehicleId),("$h",b.CatalogSignature),("$s",b.Source),("$at",b.SelectedAt.ToString("O")));
                else if((string)prior!=b.CatalogSignature)throw new LibraryConflictException(UiText.C("library.manualCatalogEvidenceHasChangedExistingMappingRemains"));
            }
            return new CatalogImportResult(cars,tracks,layouts,kept);
        });
    }
    private SavedSetup Create(SqliteConnection c,SqliteTransaction t,SetupKey key,SetupDraft draft,SetupOrigin origin,Guid? copied,CaptureEvidence? capture)
    {
        ArgumentNullException.ThrowIfNull(key);Validation.Text(key.VehicleId,UiText.C("library.modelId"));Validation.Text(key.TrackId,UiText.C("library.trackId"));Validation.Text(key.LayoutId,UiText.C("library.layoutId"));
        var vehicle=ReadVehicles(c,t).SingleOrDefault(v=>v.Id==key.VehicleId)??throw new LibraryValidationException(UiText.C("library.vehicleNotRegistered"));
        if(Scalar(c,t,"SELECT id FROM layouts WHERE track_id=$t AND id=$l",("$t",key.TrackId),("$l",key.LayoutId)) is null)throw new LibraryValidationException(UiText.C("library.trackLayoutNotRegistered"));
        Validation.Draft(draft,vehicle.Limits);var id=Guid.NewGuid();var at=clock.GetUtcNow();
        Execute(c,t,"INSERT INTO setups VALUES($id,$v,$t,$l,$o,$copy,$at,NULL)",("$id",id.ToString()),("$v",key.VehicleId),("$t",key.TrackId),("$l",key.LayoutId),
            ("$o",origin.ToString()),("$copy",copied?.ToString()),("$at",at.ToString("O")));
        InsertVersion(c,t,id,1,"Created",draft,capture,at);return Load(c,t,id,false);
    }
    public SavedSetup Get(Guid id,bool includeDeleted=false)=>Read((c,t)=>Load(c,t,id,includeDeleted));
    private static SavedSetup Load(SqliteConnection c,SqliteTransaction t,Guid id,bool deleted)
    {
        SetupKey key;SetupOrigin origin;Guid? copied;DateTimeOffset created;DateTimeOffset? deletedAt;
        using(var cmd=Command(c,t,"SELECT vehicle_id,track_id,layout_id,origin,copied_from_version,created_at,deleted_at FROM setups WHERE id=$id",("$id",id.ToString()))) {
            using var r=cmd.ExecuteReader();if(!r.Read())throw new KeyNotFoundException(UiText.C("library.setupNotFound"));
            key=new(r.GetString(0),r.GetString(1),r.GetString(2));origin=Enum.Parse<SetupOrigin>(r.GetString(3));
            copied=Text(r,4) is {} copy?Guid.Parse(copy):null;created=DateTimeOffset.Parse(r.GetString(5));deletedAt=Text(r,6) is {} d?DateTimeOffset.Parse(d):null;
        }
        if(deletedAt is not null&&!deleted)throw new KeyNotFoundException(UiText.C("library.setupInTrash"));
        using var versions=Command(c,t,"SELECT * FROM setup_versions WHERE setup_id=$id ORDER BY number",("$id",id.ToString()));using var rows=versions.ExecuteReader();
        var list=new List<SetupVersion>();while(rows.Read()) {
            string S(string k)=>rows.GetString(rows.GetOrdinal(k));string? N(string k)=>Text(rows,rows.GetOrdinal(k));
            var values=new Electronics(rows.GetInt32(rows.GetOrdinal("tc")),rows.GetInt32(rows.GetOrdinal("tc_cut")),rows.GetInt32(rows.GetOrdinal("tc_slip")),rows.GetInt32(rows.GetOrdinal("abs")),rows.GetDouble(rows.GetOrdinal("brake_bias_front")));
            bool hasCategory=Enumerable.Range(0,rows.FieldCount).Any(i=>rows.GetName(i)=="category");
            var draft=new SetupDraft(S("name"),values,S("notes"),rows.GetBoolean(rows.GetOrdinal("favorite")),N("preferred_use"),Enum.Parse<LibraryMode>(S("mode")),
                hasCategory?Enum.Parse<SetupCategory>(S("category")):SetupCategory.FixedLab);
            CaptureEvidence? capture=N("captured_at") is {} at?new(DateTimeOffset.Parse(at),rows.IsDBNull(rows.GetOrdinal("session_fixed"))?null:rows.GetBoolean(rows.GetOrdinal("session_fixed")),S("capture_source")):null;
            list.Add(new(Guid.Parse(S("id")),id,rows.GetInt32(rows.GetOrdinal("number")),S("change"),draft,DateTimeOffset.Parse(S("created_at")),capture));
        }
        if(list.Count==0)throw new InvalidDataException(UiText.C("library.setupHasNoVersion"));return new(id,key,origin,copied,created,deletedAt,list[^1],list.AsReadOnly());
    }
    public IReadOnlyList<SavedSetup> Search(SetupQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if(query.LayoutId is not null&&query.TrackId is null)throw new LibraryValidationException(UiText.C("library.layoutSearchRequiresTheSeparateTrackId"));
        return Read((c,t)=>{
            using var cmd=Command(c,t,"""
                SELECT s.id FROM setups s JOIN setup_versions v ON v.setup_id=s.id
                WHERE v.number=(SELECT max(number) FROM setup_versions WHERE setup_id=s.id)
                AND ($vehicle IS NULL OR s.vehicle_id=$vehicle) AND ($track IS NULL OR s.track_id=$track) AND ($layout IS NULL OR s.layout_id=$layout)
                AND ($name IS NULL OR instr(lower(v.name),lower($name))>0) AND ($favorite=0 OR v.favorite=1) AND ($deleted=1 OR s.deleted_at IS NULL)
                ORDER BY v.name,s.id
                """,("$vehicle",query.VehicleId),("$track",query.TrackId),("$layout",query.LayoutId),("$name",query.NameContains),("$favorite",query.FavoritesOnly),("$deleted",query.IncludeDeleted));
            var ids=new List<Guid>();using(var r=cmd.ExecuteReader())while(r.Read())ids.Add(Guid.Parse(r.GetString(0)));
            return (IReadOnlyList<SavedSetup>)ids.Select(id=>Load(c,t,id,query.IncludeDeleted)).ToArray();
        });
    }
    private static void Expected(SavedSetup setup,Guid expected)
    {if(setup.Current.Id!=expected)throw new LibraryConflictException(UiText.C("library.setupHasChangedReloadInsteadOfOverwriting"));}
    public SavedSetup Edit(Guid id,Guid expectedVersion,SetupDraft draft)=>Change(id,expectedVersion,_=>draft,"Edited");
    public SavedSetup CreateVersion(Guid id,Guid expectedVersion,SetupDraft draft)=>Change(id,expectedVersion,_=>draft,"VersionCreated",true);
    public SavedSetup Rename(Guid id,Guid expectedVersion,string name)=>Change(id,expectedVersion,d=>d with {Name=name},"Renamed");
    public SavedSetup SetFavorite(Guid id,Guid expectedVersion,bool favorite)=>Change(id,expectedVersion,d=>d with {Favorite=favorite},"FavoriteChanged");
    private SavedSetup Change(Guid id,Guid expected,Func<SetupDraft,SetupDraft> change,string kind,bool force=false)=>Write((c,t)=>{
        var old=Load(c,t,id,false);Expected(old,expected);var draft=change(old.Current.Content);
        var vehicle=ReadVehicles(c,t).Single(v=>v.Id==old.Key.VehicleId);Validation.Draft(draft,vehicle.Limits);
        if(!force&&draft==old.Current.Content)return old;
        // Renaming/favoriting preserves capture evidence; changed electronics are a manual revision.
        var capture=draft.Values==old.Current.Content.Values?old.Current.Capture:null;
        InsertVersion(c,t,id,old.Current.Number+1,kind,draft,capture,clock.GetUtcNow());return Load(c,t,id,false);
    });
    public SavedSetup Duplicate(Guid id,Guid expectedVersion,string newName)=>Write((c,t)=>{
        var old=Load(c,t,id,false);Expected(old,expectedVersion);
        return Create(c,t,old.Key,old.Current.Content with {Name=newName,Favorite=false},SetupOrigin.Copied,old.Current.Id,old.Current.Capture);
    });
    public SavedSetup SaveAs(Guid id,Guid expectedVersion,string newName)=>Duplicate(id,expectedVersion,newName);
    public SavedSetup CopyVersion(Guid id,Guid versionId,string newName,SetupKey? targetKey=null)=>Write((c,t)=>{
        var source=Load(c,t,id,true);
        var version=source.Versions.SingleOrDefault(v=>v.Id==versionId)??throw new LibraryValidationException(UiText.C("library.versionDoesNotBelongToThisSetup"));
        var key=targetKey??source.Key;
        return Create(c,t,key,version.Content with {Name=newName,Favorite=false},SetupOrigin.Copied,version.Id,key==source.Key?version.Capture:null);
    });
    public void Delete(Guid id,Guid expectedVersion)=>Write((c,t)=>{
        var old=Load(c,t,id,false);Expected(old,expectedVersion);
        Execute(c,t,"UPDATE setups SET deleted_at=$at WHERE id=$id",("$at",clock.GetUtcNow().ToString("O")),("$id",id.ToString()));
    });
    public SavedSetup Restore(Guid id,Guid expectedVersion)=>Write((c,t)=>{
        var old=Load(c,t,id,true);Expected(old,expectedVersion);
        if(old.DeletedAt is null)throw new LibraryConflictException(UiText.C("library.setupIsNotDeleted"));
        Execute(c,t,"UPDATE setups SET deleted_at=NULL WHERE id=$id",("$id",id.ToString()));return Load(c,t,id,false);
    });
    private static void InsertVersion(SqliteConnection c,SqliteTransaction t,Guid id,int number,string change,SetupDraft d,CaptureEvidence? capture,DateTimeOffset at)
    {
        bool hasCategory=Convert.ToInt32(Scalar(c,t,"SELECT count(*) FROM pragma_table_info('setup_versions') WHERE name='category'"))==1;
        if(!hasCategory&&d.Category!=SetupCategory.FixedLab)throw new LibraryValidationException(UiText.C("library.categoryRequiresSchemaV3"));
        string sql="""
            INSERT INTO setup_versions(id,setup_id,number,change,name,tc,tc_cut,tc_slip,abs,brake_bias_front,notes,favorite,preferred_use,mode,created_at,captured_at,session_fixed,capture_source)
            VALUES($id,$setup,$number,$change,$name,$tc,$cut,$slip,$abs,$bias,$notes,$favorite,$use,$mode,$at,$captured,$fixed,$source)
            """;
        if(hasCategory)sql=sql.Replace("capture_source)","capture_source,category)").Replace("$source)","$source,$category)");
        Execute(c,t,sql,("$category",d.Category.ToString()),("$id",Guid.NewGuid().ToString()),("$setup",id.ToString()),("$number",number),("$change",change),("$name",d.Name),("$tc",d.Values.TC),
            ("$cut",d.Values.TCCut),("$slip",d.Values.TCSlip),("$abs",d.Values.ABS),("$bias",d.Values.BrakeBiasFrontPercent),("$notes",d.Notes),
            ("$favorite",d.Favorite),("$use",d.PreferredUse),("$mode",d.Mode.ToString()),("$at",at.ToString("O")),("$captured",capture?.ObservedAt.ToString("O")),
            ("$fixed",capture?.SessionFixedSetup),("$source",capture?.Source));
    }
    public void Backup(string newFilePath)=>database.Backup(newFilePath);
    public LapReference AddLapReference(LapReference r)
    {
        ArgumentNullException.ThrowIfNull(r);Validation.Text(r.DataSource,"Rundenquelle",1024);
        if(r.Id==Guid.Empty || r.VersionId==Guid.Empty || !Enum.IsDefined(r.Validity)||!Enum.IsDefined(r.Assignment) ||
            r.NativeTimeSeconds is {} seconds&&(!double.IsFinite(seconds)||seconds<=0) || r.TimingConfirmed&&r.NativeTimeSeconds is null ||
            r.FullyObserved&&(!r.TelemetryFullyObserved||!r.NativeEndBoundaryObserved||!r.TimingConfirmed) ||
            r.Assignment==LapAssignment.ExperimentalOpen&&r.SessionFixedSetup!=false)
            throw new LibraryValidationException(UiText.C("library.invalidLapReferenceOpenExperimentRequiresAConfirmed"));
        Write((c,t)=>{
            if(Scalar(c,t,"SELECT id FROM setup_versions WHERE id=$id",("$id",r.VersionId.ToString())) is null)throw new LibraryValidationException(UiText.C("library.setupVersionDoesNotExist"));
            Execute(c,t,"INSERT INTO lap_references VALUES($id,$v,$time,$valid,$end,$telemetry,$full,$timing,$fixed,$source,$assignment,$at)",
                ("$id",r.Id.ToString()),("$v",r.VersionId.ToString()),("$time",r.NativeTimeSeconds),("$valid",r.Validity.ToString()),("$end",r.NativeEndBoundaryObserved),
                ("$telemetry",r.TelemetryFullyObserved),("$full",r.FullyObserved),("$timing",r.TimingConfirmed),("$fixed",r.SessionFixedSetup),("$source",r.DataSource),
                ("$assignment",r.Assignment.ToString()),("$at",r.RecordedAt.ToString("O")));
        });return r;
    }
    public IReadOnlyList<LapReference> LapReferences(Guid versionId)=>Read((c,t)=>{
        using var cmd=Command(c,t,"SELECT * FROM lap_references WHERE version_id=$v ORDER BY recorded_at,id",("$v",versionId.ToString()));
        using var r=cmd.ExecuteReader();var list=new List<LapReference>();
        while(r.Read())list.Add(new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),r.IsDBNull(2)?null:r.GetDouble(2),Enum.Parse<LapValidity>(r.GetString(3)),
            r.GetBoolean(4),r.GetBoolean(5),r.GetBoolean(6),r.GetBoolean(7),r.IsDBNull(8)?null:r.GetBoolean(8),r.GetString(9),Enum.Parse<LapAssignment>(r.GetString(10)),DateTimeOffset.Parse(r.GetString(11))));
        return (IReadOnlyList<LapReference>)list;
    });
    private static string? Text(SqliteDataReader reader,int index)=>reader.IsDBNull(index)?null:reader.GetString(index);
}

