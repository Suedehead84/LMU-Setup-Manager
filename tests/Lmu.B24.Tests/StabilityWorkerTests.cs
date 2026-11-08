using System.Text.Json;
using Lmu.Diagnostics;
using Lmu.LiveCapture;
using Lmu.Web;

static class StabilityWorkerTests
{
 public static async Task Run(Action<bool,string> check)
 {
  var retry=new RetryDelay(TimeSpan.FromMilliseconds(500),TimeSpan.FromSeconds(8));
  check(new[]{retry.Complete(false).TotalSeconds,retry.Complete(false).TotalSeconds,retry.Complete(false).TotalSeconds,retry.Complete(false).TotalSeconds,retry.Complete(false).TotalSeconds}.SequenceEqual(new[]{1d,2d,4d,8d,8d}),"navigation retries increase to bounded eight-second backoff");
  check(retry.Complete(true).TotalMilliseconds==500&&retry.Complete(false).TotalSeconds==1,"successful response resets backoff without a retry storm");
  var catalog=new RetryDelay(TimeSpan.FromSeconds(3),TimeSpan.FromSeconds(24));for(int i=0;i<1000;i++)catalog.Complete(false);check(catalog.Complete(false).TotalSeconds==24,"prolonged catalog outage stays capped without overflow");
  using(var probe=new RestProbe("http://127.0.0.1:6397",new TimeoutHandler())){var watch=System.Diagnostics.Stopwatch.StartNew();var rows=await probe.PollCatalogs(CancellationToken.None);check(rows.Length==2&&rows.All(r=>r.Status is null&&r.Error is not null),"real RestProbe converts both two-second HTTP timeouts to unavailable captures");check(watch.Elapsed.TotalSeconds<4,"parallel catalog timeouts remain bounded without serial timeout multiplication");}
  var feed=new Feed();var transport=new Slow();var health=new LiveHealth();var engine=new LiveStateEngine();using var worker=new LmuLiveWorker(feed,transport,engine,health);await worker.StartAsync(CancellationToken.None);
  try{await Task.Delay(12000);check(feed.Reads>=80,"slow REST responses do not block ten-Hz native worker");check(transport.MaxCatalog==1&&transport.MaxNavigation==1,"repeated failures never overlap catalog batches or navigation requests");check(transport.CatalogCalls<=3&&transport.NavigationCalls<=5,"twelve-second outage uses bounded retries including slow response time");check(!engine.Status().Fresh&&!engine.Status().CanCapture&&engine.Status().Values.TC is null,"native interruptions never publish old fresh values or allow capture");
   string json=JsonSerializer.Serialize(health.Snapshot());check(!json.Contains("SENSITIVE")&&json.Contains("timeout-or-transport-error"),"health export records transport channel without body URL or error text");check(json.Contains("Windows")&&json.Contains("CpuMilliseconds")&&json.Contains("MaxPollGapMilliseconds"),"health export includes timestamped manager resource and poll windows");Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/stability-worker-health.json",json);
   Volatile.Write(ref transport.Healthy,true);bool recovered=false;var until=DateTimeOffset.UtcNow.AddSeconds(12);while(DateTimeOffset.UtcNow<until){var recoverySnapshot=JsonSerializer.SerializeToElement(health.Snapshot());recovered=recoverySnapshot.GetProperty("Events").EnumerateArray().Any(e=>e.GetProperty("Channel").GetString()=="navigation"&&e.GetProperty("State").GetString()=="ok");if(recovered)break;await Task.Delay(100);}check(recovered,"REST channel recovers through bounded retry without restarting worker");check(!engine.Status().Fresh&&!engine.Status().CanCapture&&engine.Status().Key is null,"HTTP recovery alone cannot invent native freshness or identity");
  }finally{await worker.StopAsync(CancellationToken.None);}
  var bounded=new LiveHealth();for(int i=0;i<1000;i++){bounded.Native(feed.Read(),0,100);bounded.Rest("navigation",[],0,1);bounded.Rest("navigation",[new(DateTimeOffset.UtcNow,"SENSITIVE URL",200,"SENSITIVE BODY",null)],0,1);}
  var snapshot=JsonSerializer.SerializeToElement(bounded.Snapshot());check(snapshot.GetProperty("Events").GetArrayLength()==128,"diagnostic transitions remain bounded during reconnect storms");check(snapshot.GetProperty("Windows").GetArrayLength()<=120,"resource history remains bounded to 120 windows");
 }
 private sealed class Feed:INativeFrameSource{public int Reads;public NativeFrame Read(){Interlocked.Increment(ref Reads);var at=DateTimeOffset.UtcNow;return new(true,"SENSITIVE process",new(at,"LIVE",Decoder.Empty(),"SENSITIVE native failure"));}}
 private sealed class Slow:ILmuCatalogTransport{
  public int CatalogCalls,NavigationCalls,MaxCatalog,MaxNavigation;public bool Healthy;private int catalogs,navigation;
  public async Task<RestCapture[]> Read(CancellationToken token){CatalogCalls++;MaxCatalog=Math.Max(MaxCatalog,Interlocked.Increment(ref catalogs));try{await Task.Delay(1500,token);return [new(DateTimeOffset.UtcNow,"SENSITIVE URL",null,"SENSITIVE BODY","SENSITIVE error")];}finally{Interlocked.Decrement(ref catalogs);}}
  public async Task<RestCapture?> ReadNavigation(CancellationToken token){NavigationCalls++;MaxNavigation=Math.Max(MaxNavigation,Interlocked.Increment(ref navigation));try{await Task.Delay(1200,token);return Volatile.Read(ref Healthy)?new(DateTimeOffset.UtcNow,"SENSITIVE URL",200,"{}",null):new(DateTimeOffset.UtcNow,"SENSITIVE URL",null,null,"SENSITIVE error");}finally{Interlocked.Decrement(ref navigation);}}
 }
 private sealed class TimeoutHandler:HttpMessageHandler{protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){await Task.Delay(10000,token);return new(System.Net.HttpStatusCode.OK);}}
}
