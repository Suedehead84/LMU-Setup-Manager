using Lmu.Localization;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;
[assembly:InternalsVisibleTo("Lmu.SetupLibrary.Tests")]
[assembly:InternalsVisibleTo("Lmu.Web.Tests")]
[assembly:InternalsVisibleTo("Lmu.B23.Tests")]
namespace Lmu.SetupLibrary;

public sealed partial class LibraryDatabase
{
    public const int CurrentSchema=4;
    private const int ApplicationId=0x4c4d5546;
    public string FilePath {get;}
    public static string DefaultPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LmuFixedSetupManager","library","setups.sqlite");
    public LibraryDatabase(string filePath):this(filePath,CurrentSchema){}
    internal LibraryDatabase(string filePath,int target,Action<SqliteConnection,SqliteTransaction>? beforeCommit=null)
    {
        FilePath=Path.GetFullPath(filePath);Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        using var c=Connect(create:true);
        int version=Convert.ToInt32(Scalar(c,null,"PRAGMA user_version"));
        int app=Convert.ToInt32(Scalar(c,null,"PRAGMA application_id"));
        if(app!=ApplicationId && (app!=0 || version!=0 || Convert.ToInt32(Scalar(c,null,"SELECT count(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'"))!=0))
            throw new InvalidDataException(UiText.C("library.notAnLmuSetupLibraryExistingFileWill"));
        if(version>CurrentSchema || version>target)throw new InvalidDataException(UiText.C("library.databaseIsNewerThanThisApplicationNoDowngrade"));
        if(version>0) {
            for(int i=1;i<=version;i++)if((string?)Scalar(c,null,"SELECT checksum FROM schema_migrations WHERE version=$v",("$v",i))!=Checksum(Migrations[i-1]))
                throw new InvalidDataException(UiText.C("library.migrationEvidenceIsMissingOrHasChanged"));
        }
        if(version<target) {
            if(version>0) {
                string directory=Path.Combine(Path.GetDirectoryName(FilePath)!,"backups");Directory.CreateDirectory(directory);
                BackupConnection(c,Path.Combine(directory,$"before-v{version}-to-v{target}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffffff}-{Guid.NewGuid():N}.sqlite"));
            }
            using var tx=c.BeginTransaction();
            // Recheck under SQLite's write lock; concurrent migrators must never apply twice.
            if(Convert.ToInt32(Scalar(c,tx,"PRAGMA user_version"))!=version)throw new LibraryConflictException(UiText.C("library.migrationRanConcurrentlyReopenTheDatabase"));
            Execute(c,tx,$"PRAGMA application_id={ApplicationId}");
            for(int i=version;i<target;i++) {
                Execute(c,tx,Migrations[i]);
                Execute(c,tx,"INSERT INTO schema_migrations VALUES($v,$h,$at)",("$v",i+1),("$h",Checksum(Migrations[i])),("$at",DateTimeOffset.UtcNow.ToString("O")));
                Execute(c,tx,$"PRAGMA user_version={i+1}");
            }
            beforeCommit?.Invoke(c,tx);tx.Commit();
        }
        Execute(c,null,"PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL");
    }
    internal SqliteConnection Connect(bool create=false)
    {
        var c=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=FilePath,Mode=create?SqliteOpenMode.ReadWriteCreate:SqliteOpenMode.ReadWrite,
            Pooling=false,ForeignKeys=true,DefaultTimeout=5}.ToString());
        try{c.Open();Execute(c,null,"PRAGMA synchronous=FULL");return c;}catch{c.Dispose();throw;}
    }
    public void Backup(string newFilePath)
    {
        using var c=Connect();BackupConnection(c,Path.GetFullPath(newFilePath));
    }
    private static void BackupConnection(SqliteConnection source,string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using(var reservation=new FileStream(target,FileMode.CreateNew,FileAccess.Write)){}
        try {
            using var destination=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=target,Pooling=false}.ToString());
            destination.Open();source.BackupDatabase(destination);
            if((string?)Scalar(destination,null,"PRAGMA integrity_check")!="ok")throw new IOException(UiText.C("library.backupIntegrityCheckFailed"));
        }catch{File.Delete(target);throw;}
    }
    internal static SqliteCommand Command(SqliteConnection c,SqliteTransaction? tx,string sql,params (string,object?)[] values)
    {
        var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;
        foreach(var (name,value) in values)cmd.Parameters.AddWithValue(name,value??DBNull.Value);return cmd;
    }
    internal static void Execute(SqliteConnection c,SqliteTransaction? tx,string sql,params (string,object?)[] values)
    {using var cmd=Command(c,tx,sql,values);cmd.ExecuteNonQuery();}
    internal static object? Scalar(SqliteConnection c,SqliteTransaction? tx,string sql,params (string,object?)[] values)
    {using var cmd=Command(c,tx,sql,values);var v=cmd.ExecuteScalar();return v is DBNull?null:v;}
    private static string Checksum(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static readonly string[] Migrations=[
        """
        CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY,checksum TEXT NOT NULL,applied_at TEXT NOT NULL);
        CREATE TABLE vehicles(id TEXT PRIMARY KEY,name TEXT NOT NULL,class TEXT,tc_max INTEGER CHECK(tc_max>=0),cut_max INTEGER CHECK(cut_max>=0),slip_max INTEGER CHECK(slip_max>=0),abs_max INTEGER CHECK(abs_max>=0),limits_evidence TEXT);
        CREATE TABLE tracks(id TEXT PRIMARY KEY,name TEXT NOT NULL);
        CREATE TABLE layouts(track_id TEXT NOT NULL REFERENCES tracks(id),id TEXT NOT NULL,name TEXT NOT NULL,PRIMARY KEY(track_id,id));
        CREATE TABLE setups(id TEXT PRIMARY KEY,vehicle_id TEXT NOT NULL REFERENCES vehicles(id),track_id TEXT NOT NULL,layout_id TEXT NOT NULL,
          origin TEXT NOT NULL CHECK(origin IN ('Manual','Automatic','Copied')),copied_from_version TEXT REFERENCES setup_versions(id),created_at TEXT NOT NULL,deleted_at TEXT,
          FOREIGN KEY(track_id,layout_id) REFERENCES layouts(track_id,id));
        CREATE TABLE setup_versions(id TEXT PRIMARY KEY,setup_id TEXT NOT NULL REFERENCES setups(id),number INTEGER NOT NULL CHECK(number>0),change TEXT NOT NULL,
          name TEXT NOT NULL CHECK(length(trim(name))>0),tc INTEGER NOT NULL CHECK(tc>=0),tc_cut INTEGER NOT NULL CHECK(tc_cut>=0),tc_slip INTEGER NOT NULL CHECK(tc_slip>=0),abs INTEGER NOT NULL CHECK(abs>=0),brake_bias_front REAL NOT NULL CHECK(brake_bias_front BETWEEN 0 AND 100),
          notes TEXT NOT NULL,favorite INTEGER NOT NULL CHECK(favorite IN (0,1)),preferred_use TEXT,mode TEXT NOT NULL CHECK(mode='FixedLab'),created_at TEXT NOT NULL,
          captured_at TEXT,session_fixed INTEGER CHECK(session_fixed IN (0,1)),capture_source TEXT,UNIQUE(setup_id,number));
        CREATE INDEX setup_lookup ON setups(vehicle_id,track_id,layout_id);
        CREATE TRIGGER versions_no_update BEFORE UPDATE ON setup_versions BEGIN SELECT RAISE(ABORT,'Setup versions are immutable'); END;
        CREATE TRIGGER versions_no_delete BEFORE DELETE ON setup_versions BEGIN SELECT RAISE(ABORT,'Setup versions are immutable'); END;
        """,
        """
        CREATE TABLE lap_references(id TEXT PRIMARY KEY,version_id TEXT NOT NULL REFERENCES setup_versions(id),native_time REAL CHECK(native_time>0),validity TEXT NOT NULL CHECK(validity IN ('Unknown','Valid','Invalid')),
          native_end INTEGER NOT NULL CHECK(native_end IN (0,1)),telemetry_complete INTEGER NOT NULL CHECK(telemetry_complete IN (0,1)),fully_observed INTEGER NOT NULL CHECK(fully_observed IN (0,1)),timing_confirmed INTEGER NOT NULL CHECK(timing_confirmed IN (0,1)),
          session_fixed INTEGER CHECK(session_fixed IN (0,1)),data_source TEXT NOT NULL,assignment TEXT NOT NULL CHECK(assignment IN ('Manual','ExperimentalOpen')),recorded_at TEXT NOT NULL);
        CREATE INDEX lap_version_lookup ON lap_references(version_id);
        CREATE TRIGGER laps_no_update BEFORE UPDATE ON lap_references BEGIN SELECT RAISE(ABORT,'Lap evidence is immutable'); END;
        CREATE TRIGGER laps_no_delete BEFORE DELETE ON lap_references BEGIN SELECT RAISE(ABORT,'Lap evidence is immutable'); END;
        """,
        """
        ALTER TABLE setup_versions ADD COLUMN category TEXT NOT NULL DEFAULT 'FixedLab' CHECK(category IN ('FixedLab','Fixed','Open'));
        """,
        """
        CREATE TABLE manual_vehicle_bindings(component_id TEXT NOT NULL,candidate_name TEXT NOT NULL,vehicle_id TEXT NOT NULL UNIQUE REFERENCES vehicles(id),catalog_signature TEXT NOT NULL,source TEXT NOT NULL,selected_at TEXT NOT NULL,PRIMARY KEY(component_id,candidate_name));
        """
    ];
}
