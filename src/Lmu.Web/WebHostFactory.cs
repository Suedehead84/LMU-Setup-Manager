using Lmu.Localization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Lmu.SetupLibrary;
using Lmu.LiveCapture;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace Lmu.Web;
public sealed record ServerOptions(string DatabasePath,int Port=5087,bool Lan=false,string? WebRoot=null)
{
    public string AccessKey {get;init;}=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public string Csrf {get;init;}=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public IPAddress[]? ListenAddresses {get;init;}
}
public sealed record CreateSetupRequest(SetupKey Key,SetupDraft Draft);
public sealed record EditSetupRequest(Guid ExpectedVersion,SetupDraft Draft);
public sealed record VersionRequest(Guid ExpectedVersion);
public sealed record FavoriteRequest(Guid ExpectedVersion,bool Favorite);
public sealed record CopyRequest(Guid VersionId,string Name,SetupKey? TargetKey=null);
public sealed record PairRequest(string Key);
public sealed record RestoreConfirmRequest(string PreviewId,string Confirmation);
public sealed record CatalogChange<T>(T Expected,T Replacement);
public sealed record CatalogSelection(string PreviewId,IReadOnlyList<string> SelectedIds);
public sealed record LiveCreateRequest(string ContextToken,string Name,SetupCategory Category,string? PreferredUse=null);
public sealed record LiveVersionRequest(string ContextToken,Guid ExpectedVersion);

public static class WebHostFactory
{
    private const string Cookie="lmu-library-access";
    private static bool Equal(string a,string b)=>CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a),Encoding.UTF8.GetBytes(b));
    public static WebApplication Create(ServerOptions options,LiveStateEngine? liveState=null,ILmuCatalogTransport? catalogTransport=null,bool startLiveWorker=true,TimeProvider? timeProvider=null,IManagerBridge? manager=null)
    {
        var root=options.WebRoot??Path.Combine(AppContext.BaseDirectory,"wwwroot");
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions {Args=[],WebRootPath=root,ContentRootPath=Path.GetDirectoryName(root)!});
        builder.Logging.ClearProviders();builder.Logging.AddSimpleConsole(c=>c.SingleLine=true);builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(k=>{foreach(var address in options.ListenAddresses??[options.Lan?IPAddress.Any:IPAddress.Loopback])k.Listen(address,options.Port);k.Limits.MaxRequestBodySize=128*1024;});
        builder.Services.ConfigureHttpJsonOptions(o=>{
            var resolver=new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(info=>{
                foreach(var property in info.Properties){
                    bool systemText=property.Name is "Error" or "error" or "Message" or "message" or "Reason" or "reason" or "Detail" or "detail" or "Transport" or "transport" or "FirewallStatus" or "firewallStatus" or "Notice" or "notice" or "Field" or "field";
                    if(info.Type==typeof(NetworkChoice)&&(property.Name is "Profile" or "profile"))systemText=true;
                    if(systemText&&property.PropertyType==typeof(string)&&property.Get is {} getter)property.Get=value=>getter(value) is string text?UiText.Localize(text,UiText.RequestLanguage):null;
                }
            });
            o.SerializerOptions.TypeInfoResolver=resolver;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues:false));
            o.SerializerOptions.RespectRequiredConstructorParameters=true;
            o.SerializerOptions.UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow;
        });
        builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o=>o.ThrowOnBadRequest=true);
        builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("pair",ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString()??"local",_=>new(){PermitLimit=20,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));});
        var db=new LibraryDatabase(options.DatabasePath);ISetupLibrary library=new SetupLibraryService(db);
        builder.Services.AddSingleton(library);builder.Services.AddSingleton<LibraryWebService>();
        builder.Services.AddSingleton(new LibraryRestoreService(db,timeProvider));
        var live=liveState??new LiveStateEngine();
        builder.Services.AddSingleton(live);
        builder.Services.AddSingleton<LiveHealth>();
        if(catalogTransport is null)builder.Services.AddSingleton<ILmuCatalogTransport,LmuCatalogTransport>();else builder.Services.AddSingleton(catalogTransport);
        builder.Services.AddSingleton<INativeFrameSource,NativeFrameSource>();
        builder.Services.AddSingleton(timeProvider??TimeProvider.System);
        builder.Services.AddSingleton<CatalogImportService>();
        if(startLiveWorker)builder.Services.AddHostedService<LmuLiveWorker>();
        var app=builder.Build();
        bool Local(HttpContext c)=>c.Connection.RemoteIpAddress is {} ip&&IPAddress.IsLoopback(ip);
        bool Owner(HttpContext c)=>ManagedSecurity.OwnerAllowed(manager is not null,Local(c))&&(Equal(c.Request.Cookies[Cookie]??"",options.AccessKey)||Equal(c.Request.Headers.Authorization.ToString(),"Bearer "+options.AccessKey));
        var remoteAuthorizationKey=new object();
        bool Auth(HttpContext c)=>manager is not null&&!Local(c)?c.Items[remoteAuthorizationKey] is true:Owner(c)||manager is not null&&manager.AuthenticateDevice(c.Request.Cookies[Cookie]??"")||manager is not null&&c.Request.Headers.Authorization.ToString().StartsWith("Bearer ",StringComparison.Ordinal)&&manager.AuthenticateDevice(c.Request.Headers.Authorization.ToString()[7..]);
        app.Use(async(ctx,next)=>{
            var languageCode=ctx.Request.Headers["X-LMU-Language"].ToString();if(string.IsNullOrEmpty(languageCode)&&(ctx.Request.Method is "GET" or "HEAD"))languageCode=ctx.Request.Query["uiLanguage"].ToString();
            using var language=UiText.UseRequestLanguage(languageCode);
            ctx.Response.Headers["X-Content-Type-Options"]="nosniff";ctx.Response.Headers["Referrer-Policy"]="no-referrer";
            ctx.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            ctx.Response.Headers.CacheControl="no-store";
            string host=ctx.Request.Host.Host;
            if(manager is not null&&!Local(ctx)){
                if(ctx.Connection.RemoteIpAddress is not {} remote){ctx.Response.StatusCode=403;return;}
                var header=ctx.Request.Headers.Authorization.ToString();
                var authorization=manager.CheckRemote(remote,ctx.Request.Cookies[Cookie]??"",header.StartsWith("Bearer ",StringComparison.Ordinal)?header[7..]:"");
                if(!authorization.Allowed){ctx.Response.StatusCode=403;return;}
                ctx.Items[remoteAuthorizationKey]=authorization.Authenticated;
            }
            if(host!="localhost"&&!IPAddress.TryParse(host,out _)){ctx.Response.StatusCode=400;return;}
            string origin=ctx.Request.Headers.Origin.ToString();
            if(origin.Length>0&&origin!=$"{ctx.Request.Scheme}://{ctx.Request.Host}"){ctx.Response.StatusCode=403;await ctx.Response.WriteAsJsonAsync(new {Error=UiText.C("api.foreignBrowserOriginIsNotAllowed")});return;}
            bool publicLanguage=(ctx.Request.Method is "GET" or "HEAD")&&ctx.Request.Path.StartsWithSegments("/api/languages");
            if(ctx.Request.Path.StartsWithSegments("/api")&&ctx.Request.Path!="/api/session"&&!publicLanguage) {
                if(!Auth(ctx)){ctx.Response.StatusCode=401;await ctx.Response.WriteAsJsonAsync(new {Error=UiText.C("api.connectToTheLibraryUsingTheInvitationLink")});return;}
                if(manager is not null&&!Local(ctx))manager.RecordRemoteContact();
                if(ctx.Request.Method is not ("GET" or "HEAD")&&!Equal(ctx.Request.Headers["X-LMU-CSRF"].ToString(),options.Csrf)) {
                    ctx.Response.StatusCode=403;await ctx.Response.WriteAsJsonAsync(new {Error=UiText.C("api.securityTokenMissingReloadThePage")});return;
                }
            }
            if(ctx.Request.Path.StartsWithSegments("/api/manager")&&manager is not null&&!Owner(ctx)){ctx.Response.StatusCode=403;return;}
            if(ctx.Request.Path=="/api/restores/preview"&&ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is {} limit)limit.MaxRequestBodySize=LibraryRestoreService.MaximumBytes;
            try{await next();}
            catch(Exception e)when(e is ArgumentException or KeyNotFoundException or LibraryConflictException or BadHttpRequestException or JsonException or Microsoft.Data.Sqlite.SqliteException or IOException or LmuSourceException) {
                if(ctx.Response.HasStarted)throw;
                ctx.Response.StatusCode=e switch {KeyNotFoundException=>404,LibraryConflictException=>409,BadHttpRequestException b=>b.StatusCode,Microsoft.Data.Sqlite.SqliteException=>409,IOException or LmuSourceException=>503,_=>400};
                string message=e is BadHttpRequestException or JsonException?UiText.C("api.invalidOrMissingRequiredValuesEnterAllFive"):
                    e is Microsoft.Data.Sqlite.SqliteException?UiText.C("api.databaseConflictReloadAndTryAgain"):e is IOException?UiText.C("api.fileAccessFailedCheckTheLocationAndAvailable"):e.Message;
                await ctx.Response.WriteAsJsonAsync(new {Error=message});
            }
        });
        app.UseRateLimiter();app.UseDefaultFiles();app.UseStaticFiles();
        app.MapGet("/api/languages",()=>new{Languages=UiText.Catalog.Languages,SecurityKeys=UiText.Catalog.SecurityKeys});
        app.MapGet("/api/languages/{code}",(string code)=>{var doc=UiText.Catalog.Document(code);var info=UiText.Catalog.Languages.Single(l=>string.Equals(l.Code,doc.Info.Code,StringComparison.Ordinal));return new{Meta=new{info.Code,info.NativeName,info.Name,info.Direction,Status=info.Complete?"complete":"partial"},doc.Messages};});
        app.MapGet("/api/session",(HttpContext c)=>{bool authenticated=Auth(c);return Results.Json(new {Authenticated=authenticated,Csrf=authenticated?options.Csrf:null});});
        app.MapGet("/api/manager/health",(HttpContext c,LiveHealth health)=>Owner(c)?Results.Json(health.Snapshot()):Results.StatusCode(403));
        app.MapPost("/api/session",(PairRequest r,HttpContext c)=>{
            string? grant=(manager is null||Local(c))&&Equal(r.Key??"",options.AccessKey)?options.AccessKey:manager?.RedeemInvitation(r.Key??"");
            if(grant is null)return Results.Json(new {Error=UiText.C("api.invalidExpiredOrAlreadyRedeemedAccessCode")},statusCode:401);
            c.Response.Cookies.Append(Cookie,grant,new CookieOptions {HttpOnly=true,SameSite=SameSiteMode.Strict,IsEssential=true,MaxAge=grant==options.AccessKey?TimeSpan.FromHours(8):TimeSpan.FromDays(30),Path="/"});
            return Results.Ok(new {Authenticated=true,Csrf=options.Csrf});
        }).RequireRateLimiting("pair");
        app.MapGet("/api/manager",()=>manager is null?Results.Ok(new{Managed=false}):Results.Ok(manager.View()));
        app.MapPost("/api/manager/settings",(ManagerSettings r)=>{if(manager is null)return Results.Conflict(new{Error=UiText.C("api.theseSettingsRequireTheWindowsTrayApplication")});manager.RequestSettings(r);return Results.Accepted(value:new{Message=UiText.C("api.changeRequestedWaitForTheActualStatusReopen")});});
        app.MapPost("/api/manager/home-network",(HomeNetworkRequest r)=>{if(manager is null)return Results.Conflict();manager.RequestHomeNetwork(r);return Results.Accepted();});
        app.MapPost("/api/manager/preferences",(ManagerPreferences r)=>{if(manager is null)return Results.Conflict();manager.RequestPreferences(r);return Results.Accepted();});
        app.MapPost("/api/manager/invitations",(DeviceInvitation r)=>manager is null?Results.Conflict():Results.Ok(manager.Invite(r.Name)));
        app.MapGet("/api/manager/invitations/{id:guid}",(Guid id)=>manager is null?Results.Conflict():Results.Ok(manager.InvitationStatus(id)));
        app.MapPost("/api/manager/firewall-help",(FirewallHelpRequest r)=>manager is null?Results.Conflict():Results.Ok(manager.FirewallHelp(r)));
        app.MapDelete("/api/manager/devices/{id:guid}",(Guid id)=>{if(manager is null)return Results.Conflict();manager.Revoke(id);return Results.NoContent();});
        app.MapGet("/api/catalog",(LibraryWebService service)=>service.Catalog());
        app.MapGet("/api/catalog/manual-bindings",()=>db.ManualBindings());
        app.MapPost("/api/catalog/vehicles",(Vehicle v,ISetupLibrary l)=>{l.RegisterVehicle(v);return Results.Ok(v);});
        app.MapPost("/api/catalog/tracks",(Track t,ISetupLibrary l)=>{l.RegisterTrack(t);return Results.Ok(t);});
        app.MapPost("/api/catalog/layouts",(TrackLayout t,ISetupLibrary l)=>{l.RegisterLayout(t);return Results.Ok(t);});
        app.MapPut("/api/catalog/vehicles",(CatalogChange<Vehicle> r,ISetupLibrary l)=>{l.UpdateVehicle(r.Expected,r.Replacement);return Results.Ok(r.Replacement);});
        app.MapPut("/api/catalog/tracks",(CatalogChange<Track> r,ISetupLibrary l)=>{l.UpdateTrack(r.Expected,r.Replacement);return Results.Ok(r.Replacement);});
        app.MapPut("/api/catalog/layouts",(CatalogChange<TrackLayout> r,ISetupLibrary l)=>{l.UpdateLayout(r.Expected,r.Replacement);return Results.Ok(r.Replacement);});
        app.MapGet("/api/dashboard",(LibraryWebService service)=>service.Dashboard());
        app.MapGet("/api/live",(LibraryWebService service)=>{
            var status=live.Status();
            var matches=status.Key is {} k?service.List(k.VehicleId,k.TrackId,k.LayoutId).OrderByDescending(s=>s.Favorite).ThenByDescending(s=>s.UpdatedAt).ToArray():[];
            return Results.Ok(new {Status=status,MatchingSetups=matches});
        });
        app.MapGet("/api/lmu/catalog/{kind}",(string kind,CatalogImportService service,CancellationToken ct)=>service.Preview(kind,ct));
        app.MapPost("/api/lmu/catalog/import",(CatalogSelection r,CatalogImportService service,CancellationToken ct)=>service.Confirm(new(r.PreviewId,r.SelectedIds),ct));
        app.MapPost("/api/lmu/catalog/review",(CatalogReviewRequest r,CatalogImportService service,CancellationToken ct)=>service.Review(r,ct));
        app.MapPost("/api/lmu/catalog/confirm",(CatalogConfirmRequest r,CatalogImportService service,CancellationToken ct)=>service.Confirm(r,ct));
        app.MapPost("/api/live/setups",async(LiveCreateRequest r,CancellationToken ct)=>{
            var captureService=new SetupLibraryService(db,live.Reader(r.ContextToken),timeProvider);
            return await captureService.CaptureNew(r.Name,r.Category,r.PreferredUse,ct);
        });
        app.MapPost("/api/live/setups/{id:guid}/versions",async(Guid id,LiveVersionRequest r,CancellationToken ct)=>{
            var captureService=new SetupLibraryService(db,live.Reader(r.ContextToken),timeProvider);
            return await captureService.CaptureVersion(id,r.ExpectedVersion,ct);
        });
        app.MapGet("/api/setups",(HttpRequest r,LibraryWebService service)=>service.List(Empty(r,"vehicle"),Empty(r,"track"),Empty(r,"layout"),Empty(r,"category"),Empty(r,"q"),r.Query["favorites"]=="true",Empty(r,"trash")??"exclude"));
        app.MapPost("/api/setups",(CreateSetupRequest r,ISetupLibrary l)=>Results.Ok(l.CreateManual(r.Key,r.Draft)));
        app.MapGet("/api/setups/{id:guid}",(Guid id,ISetupLibrary l)=>l.Get(id,true));
        app.MapPut("/api/setups/{id:guid}",(Guid id,EditSetupRequest r,ISetupLibrary l)=>l.Edit(id,r.ExpectedVersion,r.Draft));
        app.MapPost("/api/setups/{id:guid}/versions",(Guid id,EditSetupRequest r,ISetupLibrary l)=>l.CreateVersion(id,r.ExpectedVersion,r.Draft));
        app.MapPost("/api/setups/{id:guid}/favorite",(Guid id,FavoriteRequest r,ISetupLibrary l)=>l.SetFavorite(id,r.ExpectedVersion,r.Favorite));
        app.MapPost("/api/setups/{id:guid}/delete",(Guid id,VersionRequest r,ISetupLibrary l)=>{l.Delete(id,r.ExpectedVersion);return Results.NoContent();});
        app.MapPost("/api/setups/{id:guid}/restore",(Guid id,VersionRequest r,ISetupLibrary l)=>l.Restore(id,r.ExpectedVersion));
        app.MapPost("/api/setups/{id:guid}/copy",(Guid id,CopyRequest r,ISetupLibrary l)=>l.CopyVersion(id,r.VersionId,r.Name,r.TargetKey));
        app.MapGet("/api/setups/{id:guid}/compare",(Guid id,Guid left,Guid right,LibraryWebService service)=>service.Compare(id,left,right));
        string backupDir=Path.Combine(Path.GetDirectoryName(options.DatabasePath)!,"backups");
        app.MapPost("/api/backups",()=>{
            var id=Guid.NewGuid();string path=Path.Combine(backupDir,$"export-{id:N}.sqlite");library.Backup(path);
            return Results.Ok(new {Id=id,FileName=Path.GetFileName(path),DownloadUrl=$"/api/backups/{id}",RestoreReleased=true});
        });
        app.MapPost("/api/restores/preview",(HttpRequest r,LibraryRestoreService service,CancellationToken ct)=>service.Stage(r.Body,ct));
        app.MapPost("/api/restores/confirm",(RestoreConfirmRequest r,LibraryRestoreService service)=>service.Confirm(r.PreviewId,r.Confirmation));
        app.MapDelete("/api/restores/{id}",(string id,LibraryRestoreService service)=>{service.Discard(id);return Results.NoContent();});
        app.MapGet("/api/backups/{id:guid}",(Guid id)=>{
            string path=Path.Combine(backupDir,$"export-{id:N}.sqlite");
            return File.Exists(path)?Results.File(path,"application/vnd.sqlite3",$"lmu-library-{id:N}.sqlite"):Results.NotFound();
        });
        return app;
    }
    private static string? Empty(HttpRequest r,string key)=>string.IsNullOrWhiteSpace(r.Query[key])?null:r.Query[key].ToString();
}
