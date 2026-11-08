using Lmu.Localization;
namespace Lmu.SetupLibrary;

public enum LibraryMode { FixedLab }
// User's library classification, never an assertion about LMU's session flag.
public enum SetupCategory { FixedLab, Fixed, Open }
public enum SetupOrigin { Manual, Automatic, Copied }
public enum LapAssignment { Manual, ExperimentalOpen }
public enum LapValidity { Unknown, Valid, Invalid }
public sealed record AidLimits(int? TC=null,int? TCCut=null,int? TCSlip=null,int? ABS=null,string? Evidence=null);
public sealed record Vehicle(string Id,string Name,string? Class=null,AidLimits? Limits=null);
public sealed record Track(string Id,string Name);
public sealed record TrackLayout(string TrackId,string Id,string Name);
public sealed record SetupKey(string VehicleId,string TrackId,string LayoutId);
public sealed record Electronics(int TC,int TCCut,int TCSlip,int ABS,double BrakeBiasFrontPercent);
public sealed record SetupDraft(string Name,Electronics Values,string Notes="",bool Favorite=false,
    string? PreferredUse=null,LibraryMode Mode=LibraryMode.FixedLab,SetupCategory Category=SetupCategory.FixedLab);
public sealed record CaptureEvidence(DateTimeOffset ObservedAt,bool? SessionFixedSetup,string Source);
public sealed record SetupVersion(Guid Id,Guid SetupId,int Number,string Change,SetupDraft Content,
    DateTimeOffset CreatedAt,CaptureEvidence? Capture);
public sealed record SavedSetup(Guid Id,SetupKey Key,SetupOrigin Origin,Guid? CopiedFromVersionId,
    DateTimeOffset CreatedAt,DateTimeOffset? DeletedAt,SetupVersion Current,IReadOnlyList<SetupVersion> Versions)
{
    public DateTimeOffset UpdatedAt=>Current.CreatedAt;
}
public sealed record SetupQuery(string? VehicleId=null,string? TrackId=null,string? LayoutId=null,
    string? NameContains=null,bool FavoritesOnly=false,bool IncludeDeleted=false);
// Explicit assignments only. B1 has no automatic official best-time ingestion/ranking.
public sealed record LapReference(Guid Id,Guid VersionId,double? NativeTimeSeconds,LapValidity Validity,
    bool NativeEndBoundaryObserved,bool TelemetryFullyObserved,bool FullyObserved,bool TimingConfirmed,
    bool? SessionFixedSetup,string DataSource,LapAssignment Assignment,DateTimeOffset RecordedAt)
{
    public bool AutomaticallyEligibleForBestTime=>false;
}
public sealed record ObservedSetup(Vehicle Vehicle,Track Track,TrackLayout Layout,Electronics Values,
    AidLimits NativeLimits,CaptureEvidence Evidence);
public sealed record ManualVehicleBinding(string ComponentId,string CandidateName,string VehicleId,string CatalogSignature,string Source,DateTimeOffset SelectedAt);
public sealed record CatalogBatch(IReadOnlyList<Vehicle> Vehicles,IReadOnlyList<Track> Tracks,IReadOnlyList<TrackLayout> Layouts,IReadOnlyList<ManualVehicleBinding>? ManualBindings=null);
public sealed record CatalogImportResult(int VehiclesAdded,int TracksAdded,int LayoutsAdded,int ExistingPreserved);
public interface ILiveSetupReader { Task<ObservedSetup> ReadCurrent(CancellationToken cancellationToken=default); }

public interface ISetupLibrary
{
    void RegisterVehicle(Vehicle vehicle);
    void RegisterTrack(Track track);
    void RegisterLayout(TrackLayout layout);
    void UpdateVehicle(Vehicle expected,Vehicle replacement);
    void UpdateTrack(Track expected,Track replacement);
    void UpdateLayout(TrackLayout expected,TrackLayout replacement);
    CatalogImportResult ImportCatalog(CatalogBatch batch);
    IReadOnlyList<Vehicle> Vehicles();
    IReadOnlyList<Track> Tracks();
    IReadOnlyList<TrackLayout> Layouts(string trackId);
    SavedSetup CreateManual(SetupKey key,SetupDraft draft);
    Task<SavedSetup> CaptureCurrent(string name,string? preferredUse=null,CancellationToken cancellationToken=default);
    Task<SavedSetup> CaptureNew(string name,SetupCategory category,string? preferredUse=null,CancellationToken cancellationToken=default);
    Task<SavedSetup> CaptureVersion(Guid id,Guid expectedVersion,CancellationToken cancellationToken=default);
    SavedSetup Get(Guid id,bool includeDeleted=false);
    IReadOnlyList<SavedSetup> Search(SetupQuery query);
    SavedSetup Edit(Guid id,Guid expectedVersion,SetupDraft draft);
    SavedSetup CreateVersion(Guid id,Guid expectedVersion,SetupDraft draft);
    SavedSetup Rename(Guid id,Guid expectedVersion,string name);
    SavedSetup SetFavorite(Guid id,Guid expectedVersion,bool favorite);
    SavedSetup Duplicate(Guid id,Guid expectedVersion,string newName);
    SavedSetup SaveAs(Guid id,Guid expectedVersion,string newName);
    SavedSetup CopyVersion(Guid id,Guid versionId,string newName,SetupKey? targetKey=null);
    void Delete(Guid id,Guid expectedVersion);
    SavedSetup Restore(Guid id,Guid expectedVersion);
    void Backup(string newFilePath);
    LapReference AddLapReference(LapReference reference);
    IReadOnlyList<LapReference> LapReferences(Guid versionId);
}
public sealed class LibraryConflictException(string message):InvalidOperationException(message);
public sealed class LibraryValidationException(string message):ArgumentException(message);

internal static class Validation
{
    public static string Text(string? value,string field,int maximum=256)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Length>maximum)throw new LibraryValidationException(UiText.C("library.textWith1CharactersRequired",(field),(maximum)));
        return value;
    }
    public static void Limits(AidLimits? limits)
    {
        if(limits is null)return;
        var values=new[]{limits.TC,limits.TCCut,limits.TCSlip,limits.ABS};
        if(values.Any(n=>n<0))throw new LibraryValidationException(UiText.C("library.negativeElectronicsLimit"));
        if(values.Any(n=>n.HasValue))Text(limits.Evidence,UiText.C("library.evidenceOfVehicleSpecificLimits"),1024);
    }
    public static void Draft(SetupDraft draft,AidLimits? limits)
    {
        ArgumentNullException.ThrowIfNull(draft);ArgumentNullException.ThrowIfNull(draft.Values);
        Text(draft.Name,UiText.C("library.setupName"));if(draft.Notes is null||draft.Notes.Length>16000)throw new LibraryValidationException(UiText.C("library.notesMaximum16000Characters"));
        if(draft.PreferredUse is not null)Text(draft.PreferredUse,UiText.C("library.preferredUse"),128);
        if(draft.Mode!=LibraryMode.FixedLab)throw new LibraryValidationException(UiText.C("library.b1SupportsFixedLabOnly"));
        if(!Enum.IsDefined(draft.Category))throw new LibraryValidationException(UiText.C("library.unknownLibraryCategory"));
        Values(draft.Values,limits);
    }
    public static void Values(Electronics values,AidLimits? limits)
    {
        foreach(var (name,value,max) in new[]{("TC",values.TC,limits?.TC),("TC Cut",values.TCCut,limits?.TCCut),
            ("TC Slip",values.TCSlip,limits?.TCSlip),("ABS",values.ABS,limits?.ABS)})
            if(value<0 || max is {} m && value>m)throw new LibraryValidationException(UiText.C("library.negativeLevelOrVerifiedVehicleLimitExceeded",(name)));
        if(!double.IsFinite(values.BrakeBiasFrontPercent)||values.BrakeBiasFrontPercent is <0 or >100)
            throw new LibraryValidationException(UiText.C("library.invalidBrakeBias"));
    }
}
internal interface ISetupStore
{
    void RegisterVehicle(Vehicle vehicle);
    void RegisterTrack(Track track);
    void RegisterLayout(TrackLayout layout);
    void UpdateVehicle(Vehicle expected,Vehicle replacement);
    void UpdateTrack(Track expected,Track replacement);
    void UpdateLayout(TrackLayout expected,TrackLayout replacement);
    CatalogImportResult ImportCatalog(CatalogBatch batch);
    IReadOnlyList<Vehicle> Vehicles();
    IReadOnlyList<Track> Tracks();
    IReadOnlyList<TrackLayout> Layouts(string trackId);
    SavedSetup CreateManual(SetupKey key,SetupDraft draft);
    SavedSetup CreateCaptured(ObservedSetup snapshot,string name,string? preferredUse,SetupCategory category=SetupCategory.FixedLab);
    SavedSetup UpdateCaptured(Guid id,Guid expectedVersion,ObservedSetup snapshot);
    SavedSetup Get(Guid id,bool includeDeleted=false);
    IReadOnlyList<SavedSetup> Search(SetupQuery query);
    SavedSetup Edit(Guid id,Guid expectedVersion,SetupDraft draft);
    SavedSetup CreateVersion(Guid id,Guid expectedVersion,SetupDraft draft);
    SavedSetup Rename(Guid id,Guid expectedVersion,string name);
    SavedSetup SetFavorite(Guid id,Guid expectedVersion,bool favorite);
    SavedSetup Duplicate(Guid id,Guid expectedVersion,string newName);
    SavedSetup SaveAs(Guid id,Guid expectedVersion,string newName);
    SavedSetup CopyVersion(Guid id,Guid versionId,string newName,SetupKey? targetKey=null);
    void Delete(Guid id,Guid expectedVersion);
    SavedSetup Restore(Guid id,Guid expectedVersion);
    void Backup(string newFilePath);
    LapReference AddLapReference(LapReference reference);
    IReadOnlyList<LapReference> LapReferences(Guid versionId);
}

