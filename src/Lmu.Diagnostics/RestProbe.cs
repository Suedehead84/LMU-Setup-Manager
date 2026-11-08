using Lmu.Localization;
using System.Net;
using System.Text.Json;
namespace Lmu.Diagnostics;
public sealed record RestCapture(DateTimeOffset ObservedAt,string Url,int? Status,string? Body,string? Error,DateTimeOffset RequestedAt = default);
public sealed class RestProbe : IDisposable
{
    public static readonly string[] Paths = ["/rest/garage/summary","/rest/garage/getPlayerGarageData","/rest/garage/UIScreen/CarSetupOverview","/swagger-schema.json","/rest/sessions/getAllVehicles","/rest/sessions/getTracksAll"];
    private readonly HttpClient client;
    private readonly Uri baseUri;
    public RestProbe(string address, HttpMessageHandler? handler = null)
    {
        baseUri=new Uri(address);
        if(baseUri.Scheme!="http" || !IPAddress.TryParse(baseUri.Host.Trim('[',']'),out var ip) || !IPAddress.IsLoopback(ip) || baseUri.AbsolutePath!="/")
            throw new ArgumentException(UiText.C("diagnostics.restAddressMustBeHttp12700"));
        client=new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect=false, UseProxy=false })
            { Timeout=TimeSpan.FromMilliseconds(2000), MaxResponseContentBufferSize=2*1024*1024 };
    }
    public Task<RestCapture[]> Poll(CancellationToken token)=>ReadPaths(Paths,token);
    public Task<RestCapture[]> PollCatalogs(CancellationToken token)=>ReadPaths(["/rest/sessions/getAllVehicles","/rest/sessions/getTracksAll"],token);
    public async Task<RestCapture?> PollNavigation(CancellationToken token)=>(await ReadPaths(["/navigation/state"],token)).Single();
    private async Task<RestCapture[]> ReadPaths(IEnumerable<string> paths,CancellationToken token) => await Task.WhenAll(paths.Select(async path=>
    {
        var url=new Uri(baseUri,path);
        var requestedAt=DateTimeOffset.UtcNow;
        try
        {
            using var response=await client.GetAsync(url,token);
            var body=await response.Content.ReadAsStringAsync(token);
            return new RestCapture(DateTimeOffset.UtcNow,url.ToString(),(int)response.StatusCode,body,null,requestedAt);
        }
        catch(Exception e) when(e is HttpRequestException or TaskCanceledException)
        { return new RestCapture(DateTimeOffset.UtcNow,url.ToString(),null,null,e.Message,requestedAt); }
    }));
    public void Dispose()=>client.Dispose();
}



