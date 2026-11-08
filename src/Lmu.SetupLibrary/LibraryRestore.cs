using Lmu.Localization;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;
namespace Lmu.SetupLibrary;

public sealed record RestorePreview(string Id,int OriginalSchema,int TargetSchema,int Vehicles,int Tracks,int Layouts,int Setups,int Versions,IReadOnlyList<string> SetupNames,DateTimeOffset ExpiresAt);
public sealed record RestoreResult(string SafetyBackup,bool RestartRequired=false);

// Upload paths and confirmation tokens are server-owned. No client-supplied filesystem path.
public sealed class LibraryRestoreService(LibraryDatabase database,TimeProvider? timeProvider=null)
{
    public const long MaximumBytes=64*1024*1024;
    private readonly TimeProvider clock=timeProvider??TimeProvider.System;
    private readonly object gate=new();
    internal Action<SqliteConnection,SqliteTransaction>? MigrationFault {get;set;}
    private sealed record Pending(string File,string Hash,string Baseline,RestorePreview Preview);
    private readonly Dictionary<string,Pending> pending=[];
    public async Task<RestorePreview> Stage(Stream input,CancellationToken cancellationToken=default)
    {
        string id=Guid.NewGuid().ToString("N");string directory=Path.Combine(Path.GetDirectoryName(database.FilePath)!,".restore-staging",id);Directory.CreateDirectory(directory);
        string file=Path.Combine(directory,"candidate.sqlite");
        try {
            await using(var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                var buffer=new byte[65536];long total=0;int n;
                while((n=await input.ReadAsync(buffer,cancellationToken))>0){total+=n;if(total>MaximumBytes)throw new LibraryValidationException(UiText.C("library.theBackupMustNotExceed64Mib"));await output.WriteAsync(buffer.AsMemory(0,n),cancellationToken);}
            }
            int original=LibraryDatabase.ValidateRestoreFile(file);
            // Migrations touch only the private staging copy, never the uploaded original or live DB.
            var staged=new LibraryDatabase(file,LibraryDatabase.CurrentSchema,MigrationFault);
            LibraryDatabase.ValidateRestoreFile(file);
            var library=new SetupLibraryService(staged);var setups=library.Search(new(IncludeDeleted:true));
            foreach(var v in library.Vehicles()){Validation.Text(v.Id,UiText.C("library.modelId"));Validation.Text(v.Name,UiText.C("library.modelName"));if(v.Class is not null)Validation.Text(v.Class,UiText.C("library.vehicleClass"));Validation.Limits(v.Limits);}
            foreach(var t in library.Tracks()){Validation.Text(t.Id,UiText.C("library.trackId"));Validation.Text(t.Name,UiText.C("library.trackName"));foreach(var l in library.Layouts(t.Id)){Validation.Text(l.Id,UiText.C("library.layoutId"));Validation.Text(l.Name,"Layoutname");}}
            foreach(var b in staged.ManualBindings()){Validation.Text(b.ComponentId,UiText.C("library.component"));Validation.Text(b.CandidateName,UiText.C("library.candidate"));Validation.Text(b.CatalogSignature,UiText.C("library.catalogEvidence"));Validation.Text(b.Source,UiText.C("library.origin"),1024);}
            foreach(var setup in setups) {
                if(setup.Versions.Select(v=>v.Number).Where((n,i)=>n!=i+1).Any())throw new LibraryValidationException(UiText.C("library.invalidVersionSequenceInBackup"));
                foreach(var version in setup.Versions){Validation.Draft(version.Content,null);_ = library.LapReferences(version.Id);}
            }
            var preview=new RestorePreview(id,original,LibraryDatabase.CurrentSchema,library.Vehicles().Count,library.Tracks().Count,library.Tracks().Sum(t=>library.Layouts(t.Id).Count),setups.Count,setups.Sum(s=>s.Versions.Count),setups.Take(20).Select(s=>s.Current.Content.Name).ToArray(),clock.GetUtcNow().AddMinutes(15));
            var entry=new Pending(file,Hash(file),database.ContentSignature(),preview);
            lock(gate) {
                foreach(var old in pending.Where(p=>p.Value.Preview.ExpiresAt<clock.GetUtcNow()).Select(p=>p.Key).ToArray()){DeleteStage(pending[old].File);pending.Remove(old);}
                if(pending.Count>=3)throw new LibraryConflictException(UiText.C("library.threeRestorePreviewsAreOpenDiscardAPreview"));pending.Add(id,entry);
            }
            return preview;
        }catch(Exception e)when(e is SqliteException or InvalidDataException or LibraryValidationException or JsonException or FormatException or OverflowException) {
            DeleteStage(file);throw new LibraryValidationException(UiText.C("library.backupRejectedInvalidSqliteDataUnexpectedApplicationSchema"));
        }catch{DeleteStage(file);throw;}
    }
    public RestoreResult Confirm(string id,string confirmation,Action<string>? fault=null)
    {
        lock(gate) {
            if(!pending.TryGetValue(id,out var entry)||entry.Preview.ExpiresAt<clock.GetUtcNow())throw new LibraryConflictException(UiText.C("library.restorePreviewExpiredVerifyTheBackupAgain"));
            if(confirmation is not ("BIBLIOTHEK ERSETZEN" or "REPLACE LIBRARY"))throw new LibraryValidationException(UiText.C("library.explicitConfirmationBibliothekErsetzenRequired"));
            if(Hash(entry.File)!=entry.Hash)throw new LibraryValidationException(UiText.C("library.verifiedBackupFileHasChanged"));
            LibraryDatabase.ValidateRestoreFile(entry.File);
            string backup=database.ReplaceFrom(entry.File,entry.Baseline,fault);
            pending.Remove(id);DeleteStage(entry.File);return new(Path.GetFileName(backup));
        }
    }
    public void Discard(string id){lock(gate){if(pending.Remove(id,out var value))DeleteStage(value.File);}}
    private static string Hash(string file){using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream));}
    private static void DeleteStage(string file) {
        // Only the private directory generated above, never user input or a computed ancestor.
        var dir=Path.GetDirectoryName(Path.GetFullPath(file))!;
        if(Path.GetFileName(Path.GetDirectoryName(dir))!=".restore-staging"||!Guid.TryParseExact(Path.GetFileName(dir),"N",out _))throw new IOException(UiText.C("library.unsafeStagingPath"));
        if(Directory.Exists(dir))Directory.Delete(dir,true);
    }
}

public sealed partial class LibraryDatabase
{
    public IReadOnlyList<ManualVehicleBinding> ManualBindings() {
        using var c=Connect();using var cmd=Command(c,null,"SELECT * FROM manual_vehicle_bindings ORDER BY component_id,candidate_name");using var r=cmd.ExecuteReader();var list=new List<ManualVehicleBinding>();
        while(r.Read())list.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),DateTimeOffset.Parse(r.GetString(5))));return list;
    }
    private static readonly string[] RestoreTables=["schema_migrations","vehicles","tracks","layouts","setups","setup_versions","lap_references","manual_vehicle_bindings"];
    private static SqliteConnection ReadOnly(string file) {
        var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=5}.ToString());
        try{c.Open();Execute(c,null,"PRAGMA trusted_schema=OFF; PRAGMA query_only=ON");return c;}catch{c.Dispose();throw;}
    }
    private static string Schema(SqliteConnection c) {
        using var cmd=Command(c,null,"SELECT type,name,tbl_name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name");using var rows=cmd.ExecuteReader();var result=new List<object[]>();while(rows.Read()){var values=new object[4];rows.GetValues(values);result.Add(values);}return JsonSerializer.Serialize(result);
    }
    internal static int ValidateRestoreFile(string file)
    {
        var info=new FileInfo(file);if(!info.Exists||info.Length<100||info.Length>LibraryRestoreService.MaximumBytes)throw new InvalidDataException(UiText.C("library.invalidFileSize"));
        using var c=ReadOnly(file);
        int version=Convert.ToInt32(Scalar(c,null,"PRAGMA user_version"));
        if(Convert.ToInt32(Scalar(c,null,"PRAGMA application_id"))!=ApplicationId||version<1||version>CurrentSchema)throw new InvalidDataException(UiText.C("library.differentApplicationOrFutureVersion"));
        // Reject every extra/altered trigger, view, index, virtual table or column BEFORE migrating/querying user tables.
        string expected=Path.Combine(Path.GetDirectoryName(file)!,"expected-"+Guid.NewGuid().ToString("N")+".sqlite");
        try{new LibraryDatabase(expected,version);using var reference=ReadOnly(expected);if(Schema(c)!=Schema(reference))throw new InvalidDataException(UiText.C("library.schemaHasBeenTamperedWith"));}
        finally{foreach(var suffix in new[]{"","-wal","-shm"})if(File.Exists(expected+suffix))File.Delete(expected+suffix);}
        if(Convert.ToInt32(Scalar(c,null,"SELECT count(*) FROM schema_migrations"))!=version)throw new InvalidDataException(UiText.C("library.invalidMigrationLedger"));
        for(int i=1;i<=version;i++)if((string?)Scalar(c,null,"SELECT checksum FROM schema_migrations WHERE version=$v",("$v",i))!=Checksum(Migrations[i-1]))throw new InvalidDataException(UiText.C("library.migrationTampered"));
        using(var cmd=Command(c,null,"PRAGMA integrity_check")){using var r=cmd.ExecuteReader();if(!r.Read()||r.GetString(0)!="ok"||r.Read())throw new InvalidDataException(UiText.C("library.sqliteIntegrityCheckFailed"));}
        using(var cmd=Command(c,null,"PRAGMA foreign_key_check")){using var r=cmd.ExecuteReader();if(r.Read())throw new InvalidDataException(UiText.C("library.foreignKeyCheckFailed"));}
        // Check the value domain explicitly as well; do not rely solely on SQLite CHECK diagnostics.
        if(Convert.ToInt32(Scalar(c,null,"SELECT count(*) FROM setup_versions WHERE tc<0 OR tc_cut<0 OR tc_slip<0 OR abs<0 OR brake_bias_front NOT BETWEEN 0 AND 100 OR length(trim(name))=0 OR number<1 OR favorite NOT IN (0,1) OR mode<>'FixedLab'"))!=0)throw new InvalidDataException(UiText.C("library.invalidSetupValues"));
        if(Convert.ToInt32(Scalar(c,null,"SELECT count(*) FROM setup_versions WHERE typeof(tc)<>'integer' OR typeof(tc_cut)<>'integer' OR typeof(tc_slip)<>'integer' OR typeof(abs)<>'integer' OR typeof(brake_bias_front) NOT IN ('real','integer') OR ((captured_at IS NULL)<>(capture_source IS NULL)) OR (captured_at IS NOT NULL AND session_fixed IS NULL)"))!=0)throw new InvalidDataException(UiText.C("library.invalidValueSourceTypes"));
        return version;
    }
    public string ContentSignature(){using var c=Connect();using var tx=c.BeginTransaction(deferred:true);return ContentSignature(c,tx,"main");}
    private static string ContentSignature(SqliteConnection c,SqliteTransaction? tx,string schema)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var table in RestoreTables) {
            using var cmd=Command(c,tx,$"SELECT * FROM {schema}.{table} ORDER BY "+(table=="layouts"?"track_id,id":table=="manual_vehicle_bindings"?"component_id,candidate_name":table=="schema_migrations"?"version":"id"));
            using var rows=cmd.ExecuteReader();while(rows.Read()){var data=new object[rows.FieldCount];rows.GetValues(data);hash.AppendData(System.Text.Encoding.UTF8.GetBytes(table+JsonSerializer.Serialize(data)+"\n"));}
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    internal string ReplaceFrom(string source,string baseline,Action<string>? fault)
    {
        string backup=Path.Combine(Path.GetDirectoryName(FilePath)!,"backups",$"before-restore-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffffff}-{Guid.NewGuid():N}.sqlite");
        Backup(backup);fault?.Invoke("after-backup");
        using var c=Connect();Execute(c,null,"PRAGMA foreign_keys=OFF; PRAGMA trusted_schema=OFF");
        Execute(c,null,"ATTACH DATABASE $source AS incoming",("$source",source));
        using var tx=c.BeginTransaction(); // immediate write lock: every writer sees either the old or the new committed library.
        if(ContentSignature(c,tx,"main")!=baseline||ContentSignature(c,tx,"incoming") is not {} incoming)throw new LibraryConflictException(UiText.C("library.libraryChangedSinceTheRestorePreviewVerifyThe"));
        using(var safe=ReadOnly(backup))if(ContentSignature(safe,null,"main")!=baseline)throw new LibraryConflictException(UiText.C("library.libraryChangedDuringBackupPrepareTheRestoreAgain"));
        foreach(var table in RestoreTables.Reverse())Execute(c,tx,$"DROP TABLE main.{table}");
        fault?.Invoke("after-clear");
        foreach(var migration in Migrations)Execute(c,tx,migration);
        foreach(var table in RestoreTables)Execute(c,tx,$"INSERT INTO main.{table} SELECT * FROM incoming.{table}");
        Execute(c,tx,$"PRAGMA user_version={CurrentSchema}; PRAGMA application_id={ApplicationId}");
        if(ContentSignature(c,tx,"main")!=incoming)throw new InvalidDataException(UiText.C("library.restoreComparisonFailed"));
        using(var cmd=Command(c,tx,"PRAGMA main.foreign_key_check")){using var rows=cmd.ExecuteReader();if(rows.Read())throw new InvalidDataException(UiText.C("library.restoreForeignKeyCheckFailed"));}
        fault?.Invoke("before-commit");tx.Commit();return backup;
    }
}
