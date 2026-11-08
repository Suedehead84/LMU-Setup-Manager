using Lmu.Localization;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Lmu.Diagnostics;
using Lmu.SetupLibrary;
namespace Lmu.LiveCapture;

public sealed record ImportChoice(string Id,string Name,string? Class,string? TrackId,string? TrackName,string? Detail,
    int SourceEntries,bool Selectable,string? Reason,bool Exists,string? LayoutId=null,IReadOnlyList<ManualCandidate>? Candidates=null,string? Signature=null);
public sealed record ManualCandidate(string Id,string Name,string? Class,int SourceEntries,string Signature,bool Exists=false);
public sealed record ImportPreview(string Id,string Kind,DateTimeOffset ObservedAt,int SourceEntries,IReadOnlyList<ImportChoice> Choices);
public sealed partial class CatalogImportService(ILmuCatalogTransport transport,ISetupLibrary library,TimeProvider? timeProvider=null)
{
    private readonly TimeProvider clock=timeProvider??TimeProvider.System;private readonly object gate=new();
    private readonly Dictionary<string,ImportPreview> previews=[];
    public async Task<ImportPreview> Preview(string kind,CancellationToken token=default)
    {
        if(kind is not ("vehicles" or "tracks"))throw new LibraryValidationException(UiText.C("live.unknownImportType"));
        var captures=await transport.Read(token);var path=kind=="vehicles"?"/rest/sessions/getAllVehicles":"/rest/sessions/getTracksAll";
        var relevant=captures.Where(c=>Uri.TryCreate(c.Url,UriKind.Absolute,out var u)&&u.AbsolutePath==path).ToArray();
        var now=clock.GetUtcNow();
        if(relevant.Length!=1||relevant[0].Status!=200||relevant[0].Body is null||relevant[0].ObservedAt>now||now-relevant[0].RequestedAt>TimeSpan.FromSeconds(5)||relevant[0].RequestedAt>relevant[0].ObservedAt)
            throw new LmuSourceException(UiText.C("live.lmuContentCatalogUnavailableStartLmuAndCheck"));
        ImportPreview preview;
        try{preview=Build(kind,relevant[0].Body!,relevant[0].ObservedAt,library);}
        catch(Exception e)when(e is FormatException or JsonException){throw new LmuSourceException(UiText.C("live.lmuCatalogIsEmptyInvalidOrHasAn"));}
        lock(gate){foreach(var old in previews.Where(p=>now-p.Value.ObservedAt>TimeSpan.FromMinutes(1)).Select(p=>p.Key).ToArray())previews.Remove(old);if(previews.Count>=16)previews.Remove(previews.Keys.First());previews[preview.Id]=preview;}
        return preview;
    }
    public static ImportPreview Build(string kind,string json,DateTimeOffset at,ISetupLibrary library)
    {
        var choices=new List<ImportChoice>();int total;
        if(kind=="vehicles") {
            var rows=IdentityResolver.ReadVehicleCatalog(json);total=rows.Count;var existing=library.Vehicles().Select(v=>v.Id).ToHashSet();
            foreach(var group in rows.GroupBy(v=>v.Package,StringComparer.OrdinalIgnoreCase)) {
                var names=group.Select(v=>v.Model).Distinct().ToArray();var classes=group.Select(v=>v.Class).Distinct().ToArray();
                bool unique=names.Length==1&&group.Select(v=>v.Package).Distinct().Count()==1;
                var label=string.Join(" / ",names);
                bool sharedLabel=rows.Any(v=>!v.Package.Equals(group.Key,StringComparison.OrdinalIgnoreCase)&&names.Contains(v.Model));
                choices.Add(new(group.First().Package,label,classes.Length==1?classes[0]:null,null,null,sharedLabel?group.First().Package:null,group.Count(),unique,
                    unique?null:UiText.C("live.multipleModelVariants"),existing.Contains(group.First().Package)));
            }
        } else if(kind=="tracks") {
            var rows=IdentityResolver.ReadTrackCatalog(json);total=rows.Count;
            var existing=library.Tracks().SelectMany(t=>library.Layouts(t.Id)).Select(l=>(l.TrackId,l.Id)).ToHashSet();
            foreach(var group in rows.GroupBy(t=>(t.Package,t.Scene))) {
                var first=group.First();bool unique=group.Select(t=>(t.Name,t.LayoutName,t.EventName)).Distinct().Count()==1&&rows.Where(t=>t.Package==first.Package).Select(t=>t.Name).Distinct().Count()==1;
                string selectionId=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]{first.Package,first.Scene}))));
                choices.Add(new(selectionId,first.LayoutName,null,first.Package,first.Name,
                    first.EventName is {} ev&&ev!=first.LayoutName?ev:first.Scene,group.Count(),unique,unique?null:UiText.C("live.conflictingTrackLayoutMetadata"),existing.Contains((first.Package,first.Scene)),first.Scene));
            }
        }else throw new LibraryValidationException(UiText.C("live.unknownImportType"));
        if(kind=="vehicles") {
            var rows=IdentityResolver.ReadVehicleCatalog(json);
            var registered=library.Vehicles().Select(v=>v.Id).ToHashSet();
            choices=choices.Select(c=>c.Selectable?c:c with {Candidates=rows.Where(r=>r.Package==c.Id).GroupBy(r=>r.Model).Select(g=>{
                string id="manual-model-"+Digest(new[]{c.Id,g.Key});var cls=g.Select(r=>r.Class).Distinct().ToArray();
                return new ManualCandidate(id,g.Key,cls.Length==1?cls[0]:null,g.Count(),Digest(new object?[]{c.Id,g.Key,cls,g.Count()}),registered.Contains(id));
            }).OrderBy(x=>x.Name).ToArray()}).ToList();
        }
        return new(Guid.NewGuid().ToString("N"),kind,at,total,choices.Select(c=>c with{Signature=ChoiceSignature(c)}).OrderBy(c=>c.TrackName??c.Name).ThenBy(c=>c.Name).ThenBy(c=>c.Id).ToArray());
    }
    public CatalogImportResult Import(string previewId,IReadOnlyList<string> selectedIds)
    {
        ImportPreview preview;
        lock(gate){if(!previews.TryGetValue(previewId,out preview!)||clock.GetUtcNow()-preview.ObservedAt>TimeSpan.FromMinutes(1)||clock.GetUtcNow()<preview.ObservedAt)
            throw new LibraryConflictException(UiText.C("live.catalogPreviewExpired"));}
        if(selectedIds is null||selectedIds.Count==0)throw new LibraryValidationException(UiText.C("live.selectAtLeastOneUnambiguousEntry"));
        var ids=selectedIds.ToHashSet();var selected=preview.Choices.Where(c=>ids.Contains(c.Id)).ToArray();
        if(ids.Count!=selected.Length||selected.Any(c=>!c.Selectable))throw new LibraryValidationException(UiText.C("live.unknownOrAmbiguousCatalogSelection"));
        var cars=preview.Kind=="vehicles"?selected.Select(c=>new Vehicle(c.Id,c.Name,c.Class)).ToArray():[];
        var tracks=preview.Kind=="tracks"?selected.GroupBy(c=>c.TrackId!).Select(g=>new Track(g.Key,g.First().TrackName!)).ToArray():[];
        var layouts=preview.Kind=="tracks"?selected.Select(c=>new TrackLayout(c.TrackId!,c.LayoutId!,c.Detail!=c.Name?c.Name+" · "+c.Detail:c.Name)).ToArray():[];
        return library.ImportCatalog(new(cars,tracks,layouts));
    }
}
