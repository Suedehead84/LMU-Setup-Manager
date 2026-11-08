using Lmu.Localization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lmu.SetupLibrary;
namespace Lmu.LiveCapture;

public sealed record CatalogReviewRequest(string Kind,IReadOnlyList<string> SelectedIds,IReadOnlyDictionary<string,string> ExpectedSignatures,IReadOnlyDictionary<string,string>? ManualSelections=null);
public sealed record CatalogReview(ImportPreview Preview,IReadOnlyList<string> SelectedIds,IReadOnlyDictionary<string,string> ManualSelections,IReadOnlyList<string> Changed,IReadOnlyList<string> Missing,bool CanImport);
public sealed record CatalogConfirmRequest(string PreviewId,IReadOnlyList<string> SelectedIds,IReadOnlyDictionary<string,string>? ManualSelections=null);
public sealed partial class CatalogImportService
{
    private static string Digest(object data)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data))));
    private static string ChoiceSignature(ImportChoice c)=>Digest(new object?[]{c.Id,c.Name,c.Class,c.TrackId,c.TrackName,c.Detail,c.LayoutId,c.SourceEntries,c.Selectable,c.Reason,c.Candidates?.Select(x=>new object?[]{x.Id,x.Name,x.Class,x.SourceEntries,x.Signature}).ToArray()});
    public async Task<CatalogReview> Review(CatalogReviewRequest request,CancellationToken token=default)
    {
        if(request.SelectedIds is null||request.SelectedIds.Count==0||request.ExpectedSignatures is null)throw new LibraryValidationException(UiText.C("live.selectionAndCatalogEvidenceRequired"));
        var preview=await Preview(request.Kind,token);var ids=request.SelectedIds.Distinct().ToArray();
        var selected=preview.Choices.Where(c=>ids.Contains(c.Id)).ToArray();
        var missing=ids.Except(selected.Select(c=>c.Id)).ToArray();
        var changed=selected.Where(c=>!request.ExpectedSignatures.TryGetValue(c.Id,out var prior)||prior!=c.Signature).Select(c=>c.Id).ToArray();
        var manual=request.ManualSelections??new Dictionary<string,string>();
        bool valid=selected.All(c=>c.Selectable||manual.TryGetValue(c.Id,out var candidate)&&c.Candidates?.Any(x=>x.Id==candidate)==true);
        return new(preview,selected.Select(c=>c.Id).ToArray(),manual,changed,missing,missing.Length==0&&valid);
    }
    public async Task<CatalogImportResult> Confirm(CatalogConfirmRequest request,CancellationToken token=default)
    {
        ImportPreview prior;
        lock(gate){if(!previews.TryGetValue(request.PreviewId,out prior!)||clock.GetUtcNow()-prior.ObservedAt>TimeSpan.FromMinutes(1)||clock.GetUtcNow()<prior.ObservedAt)throw new LibraryConflictException(UiText.C("live.importReviewExpiredYourSelectionIsRetainedReview"));}
        var reviewed=await Review(new(prior.Kind,request.SelectedIds,prior.Choices.ToDictionary(c=>c.Id,c=>c.Signature!),request.ManualSelections),token);
        if(!reviewed.CanImport||reviewed.Changed.Count>0)throw new LibraryConflictException(UiText.C("live.catalogChangedSinceTheLastConfirmationRetainThe"));
        var selected=reviewed.Preview.Choices.Where(c=>reviewed.SelectedIds.Contains(c.Id)).ToArray();
        var cars=new List<Vehicle>();var bindings=new List<ManualVehicleBinding>();
        if(prior.Kind=="vehicles")foreach(var c in selected) {
            if(c.Selectable)cars.Add(new(c.Id,c.Name,c.Class));
            else {
                var candidate=c.Candidates!.Single(x=>x.Id==reviewed.ManualSelections[c.Id]);
                cars.Add(new(candidate.Id,candidate.Name,candidate.Class));
                bindings.Add(new(c.Id,candidate.Name,candidate.Id,candidate.Signature,"Explicit user selection from verified LMU content catalog; MANUAL, never live-confirmed",clock.GetUtcNow()));
            }
        }
        var tracks=prior.Kind=="tracks"?selected.GroupBy(c=>c.TrackId!).Select(g=>new Track(g.Key,g.First().TrackName!)).ToArray():[];
        var layouts=prior.Kind=="tracks"?selected.Select(c=>new TrackLayout(c.TrackId!,c.LayoutId!,c.Name+(c.Detail!=c.Name?" · "+c.Detail:""))).ToArray():[];
        return library.ImportCatalog(new(cars,tracks,layouts,bindings));
    }
}
