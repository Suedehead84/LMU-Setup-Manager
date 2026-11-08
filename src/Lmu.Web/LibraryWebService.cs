using Lmu.Localization;
using Lmu.SetupLibrary;
namespace Lmu.Web;

public sealed record SetupCard(Guid Id,SetupKey Key,string Name,string Vehicle,string Track,string Layout,SetupCategory Category,
    string? Profile,bool Favorite,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt,SetupOrigin Origin,string Notes,DateTimeOffset? DeletedAt,Guid VersionId,int VersionNumber,bool LiveCaptured=false,Electronics? Values=null);
public sealed class LibraryWebService(ISetupLibrary library)
{
    private static string VehicleLabel(Vehicle v,IEnumerable<Vehicle> all)=>all.Count(x=>x.Name==v.Name)>1?v.Name+" · "+v.Id:v.Name;
    public object Catalog(){var cars=library.Vehicles();return new {Vehicles=cars.Select(v=>new{v.Id,v.Name,v.Class,v.Limits,DisplayName=VehicleLabel(v,cars)}),Tracks=library.Tracks(),Layouts=library.Tracks().SelectMany(t=>library.Layouts(t.Id)).ToArray()};}
    public IReadOnlyList<SetupCard> List(string? vehicle=null,string? track=null,string? layout=null,string? category=null,string? q=null,bool favorites=false,string trash="exclude")
    {
        if(trash is not ("exclude" or "include" or "only"))throw new LibraryValidationException(UiText.C("api.invalidTrashFilter"));
        SetupCategory? filter=null;
        if(category is not null){if(!Enum.TryParse<SetupCategory>(category,false,out var parsed)||!Enum.IsDefined(parsed)||char.IsDigit(category[0]))throw new LibraryValidationException(UiText.C("api.unknownCategory"));filter=parsed;}
        var cars=library.Vehicles().ToDictionary(v=>v.Id);var tracks=library.Tracks().ToDictionary(t=>t.Id);
        var layouts=tracks.Keys.SelectMany(t=>library.Layouts(t)).ToDictionary(l=>(l.TrackId,l.Id));
        var result=library.Search(new(vehicle,track,layout,FavoritesOnly:favorites,IncludeDeleted:trash!="exclude"))
            .Where(s=>trash!="only"||s.DeletedAt is not null)
            .Where(s=>filter is null||s.Current.Content.Category==filter)
            .Select(s=>new SetupCard(s.Id,s.Key,s.Current.Content.Name,VehicleLabel(cars[s.Key.VehicleId],cars.Values),tracks[s.Key.TrackId].Name,layouts[(s.Key.TrackId,s.Key.LayoutId)].Name,
                s.Current.Content.Category,s.Current.Content.PreferredUse,s.Current.Content.Favorite,s.CreatedAt,s.UpdatedAt,s.Origin,s.Current.Content.Notes,s.DeletedAt,s.Current.Id,s.Current.Number,s.Current.Capture is not null,s.Current.Content.Values));
        if(!string.IsNullOrWhiteSpace(q))result=result.Where(s=>new[]{s.Name,s.Vehicle,s.Track,s.Layout,s.Notes,s.Profile}.Any(v=>v?.Contains(q,StringComparison.OrdinalIgnoreCase)==true));
        return result.OrderByDescending(s=>s.UpdatedAt).ThenBy(s=>s.Name).ToArray();
    }
    public object Dashboard()
    {
        var setups=List();return new {ActiveCount=setups.Count,FavoriteCount=setups.Count(s=>s.Favorite),
            Recent=setups.Take(6),Favorites=setups.Where(s=>s.Favorite).Take(6)};
    }
    public object Compare(Guid id,Guid left,Guid right)
    {
        var setup=library.Get(id,true);
        SetupVersion Version(Guid version)=>setup.Versions.SingleOrDefault(v=>v.Id==version)??throw new LibraryValidationException(UiText.C("api.versionDoesNotBelongToSetup"));
        var a=Version(left);var b=Version(right);
        var differences=new List<object>();
        void Diff(string field,object? before,object? after){if(!Equals(before,after))differences.Add(new {Field=field,Before=before,After=after});}
        Diff(UiText.C("api.name"),a.Content.Name,b.Content.Name);Diff("TC",a.Content.Values.TC,b.Content.Values.TC);Diff("TC Cut",a.Content.Values.TCCut,b.Content.Values.TCCut);
        Diff("TC Slip",a.Content.Values.TCSlip,b.Content.Values.TCSlip);Diff("ABS",a.Content.Values.ABS,b.Content.Values.ABS);
        Diff(UiText.C("library.frontBrakeBias"),a.Content.Values.BrakeBiasFrontPercent,b.Content.Values.BrakeBiasFrontPercent);
        Diff(UiText.C("api.notes"),a.Content.Notes,b.Content.Notes);Diff("Favorit",a.Content.Favorite,b.Content.Favorite);
        Diff(UiText.C("api.profile"),a.Content.PreferredUse,b.Content.PreferredUse);Diff(UiText.C("api.category"),a.Content.Category.ToString(),b.Content.Category.ToString());
        return new {Left=a,Right=b,Differences=differences};
    }
}
