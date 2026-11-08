using Lmu.Localization;
namespace Lmu.SetupLibrary;

// Application boundary for B2: no SQL, transport or game APIs in the public operations.
public sealed class SetupLibraryService : ISetupLibrary
{
    private readonly ISetupStore store;
    private readonly ILiveSetupReader? liveReader;
    private readonly TimeProvider clock;
    public SetupLibraryService(LibraryDatabase database,ILiveSetupReader? liveReader=null,TimeProvider? timeProvider=null)
    {
        clock=timeProvider??TimeProvider.System;store=new SqliteSetupStore(database,clock);this.liveReader=liveReader;
    }
    public void RegisterVehicle(Vehicle vehicle)=>store.RegisterVehicle(vehicle);
    public void RegisterTrack(Track track)=>store.RegisterTrack(track);
    public void RegisterLayout(TrackLayout layout)=>store.RegisterLayout(layout);
    public void UpdateVehicle(Vehicle expected,Vehicle replacement)=>store.UpdateVehicle(expected,replacement);
    public void UpdateTrack(Track expected,Track replacement)=>store.UpdateTrack(expected,replacement);
    public void UpdateLayout(TrackLayout expected,TrackLayout replacement)=>store.UpdateLayout(expected,replacement);
    public IReadOnlyList<Vehicle> Vehicles()=>store.Vehicles();
    public IReadOnlyList<Track> Tracks()=>store.Tracks();
    public IReadOnlyList<TrackLayout> Layouts(string trackId)=>store.Layouts(trackId);
    public SavedSetup CreateManual(SetupKey key,SetupDraft draft) {Validation.Draft(draft,null);return store.CreateManual(key,draft);}
    public SavedSetup Get(Guid id,bool includeDeleted=false)=>store.Get(id,includeDeleted);
    public IReadOnlyList<SavedSetup> Search(SetupQuery query)=>store.Search(query);
    public SavedSetup Edit(Guid id,Guid expectedVersion,SetupDraft draft) {Validation.Draft(draft,null);return store.Edit(id,expectedVersion,draft);}
    public SavedSetup CreateVersion(Guid id,Guid expectedVersion,SetupDraft draft) {Validation.Draft(draft,null);return store.CreateVersion(id,expectedVersion,draft);}
    public SavedSetup Rename(Guid id,Guid expectedVersion,string name)=>store.Rename(id,expectedVersion,name);
    public SavedSetup SetFavorite(Guid id,Guid expectedVersion,bool favorite)=>store.SetFavorite(id,expectedVersion,favorite);
    public SavedSetup Duplicate(Guid id,Guid expectedVersion,string newName)=>store.Duplicate(id,expectedVersion,newName);
    public SavedSetup SaveAs(Guid id,Guid expectedVersion,string newName)=>store.SaveAs(id,expectedVersion,newName);
    public SavedSetup CopyVersion(Guid id,Guid versionId,string newName,SetupKey? targetKey=null)=>store.CopyVersion(id,versionId,newName,targetKey);
    public void Delete(Guid id,Guid expectedVersion)=>store.Delete(id,expectedVersion);
    public SavedSetup Restore(Guid id,Guid expectedVersion)=>store.Restore(id,expectedVersion);
    public void Backup(string newFilePath)=>store.Backup(newFilePath);
    public LapReference AddLapReference(LapReference reference)=>store.AddLapReference(reference);
    public IReadOnlyList<LapReference> LapReferences(Guid versionId)=>store.LapReferences(versionId);
    public CatalogImportResult ImportCatalog(CatalogBatch batch)=>store.ImportCatalog(batch);
    public Task<SavedSetup> CaptureCurrent(string name,string? preferredUse=null,CancellationToken cancellationToken=default)
        =>CaptureNew(name,SetupCategory.FixedLab,preferredUse,cancellationToken);
    public async Task<SavedSetup> CaptureNew(string name,SetupCategory category,string? preferredUse=null,CancellationToken cancellationToken=default)
    {
        var snapshot=await ReadObservation(cancellationToken);
        return store.CreateCaptured(snapshot,name,preferredUse,category);
    }
    public async Task<SavedSetup> CaptureVersion(Guid id,Guid expectedVersion,CancellationToken cancellationToken=default)
    {
        var snapshot=await ReadObservation(cancellationToken);
        return store.UpdateCaptured(id,expectedVersion,snapshot);
    }
    private async Task<ObservedSetup> ReadObservation(CancellationToken cancellationToken)
    {
        if(liveReader is null)throw new LibraryValidationException(UiText.C("library.noLiveSourceConfiguredManualManagementWorksIndependently"));
        var snapshot=await liveReader.ReadCurrent(cancellationToken);ValidateObservation(snapshot,clock.GetUtcNow());return snapshot;
    }
    public static void ValidateObservation(ObservedSetup snapshot,DateTimeOffset now)
    {
        if(snapshot is null || snapshot.Evidence is null || snapshot.Values is null || snapshot.NativeLimits is null ||
            snapshot.Vehicle is null || snapshot.Track is null || snapshot.Layout is null || snapshot.Evidence.SessionFixedSetup is null)
            throw new LibraryValidationException(UiText.C("library.noCompleteLiveObservation"));
        var age=now-snapshot.Evidence.ObservedAt;
        if(age<TimeSpan.Zero || age>TimeSpan.FromSeconds(2))throw new LibraryValidationException(UiText.C("library.liveObservationIsNotFresh"));
        Validation.Text(snapshot.Evidence.Source,UiText.C("library.liveDataSource"),1024);
        Validation.Limits(snapshot.NativeLimits);Validation.Values(snapshot.Values,snapshot.NativeLimits);
        if(new[]{snapshot.NativeLimits.TC,snapshot.NativeLimits.TCCut,snapshot.NativeLimits.TCSlip,snapshot.NativeLimits.ABS}.Any(m=>m is null or <=0))
            throw new LibraryValidationException(UiText.C("library.notAllNativeElectronicsLimitsAreAvailable"));
        if(snapshot.Layout.TrackId!=snapshot.Track.Id)throw new LibraryValidationException(UiText.C("library.layoutDoesNotBelongToTheActiveTrack"));
        Validation.Text(snapshot.Vehicle.Id,UiText.C("library.modelId"));Validation.Text(snapshot.Vehicle.Name,UiText.C("library.modelName"));
        Validation.Text(snapshot.Track.Id,UiText.C("library.trackId"));Validation.Text(snapshot.Track.Name,UiText.C("library.trackName"));
        Validation.Text(snapshot.Layout.Id,UiText.C("library.layoutId"));Validation.Text(snapshot.Layout.Name,"Layoutname");
    }
}

