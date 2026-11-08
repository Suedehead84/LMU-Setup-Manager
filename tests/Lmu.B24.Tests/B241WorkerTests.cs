using System.Net;
using System.Text;
using System.Text.Json;
using Lmu.Diagnostics;
using Lmu.LiveCapture;
using Lmu.Web;
using Decoder=Lmu.Diagnostics.Decoder;

static class B241WorkerTests
{
 public static async Task Run(Action<bool,string> check,string vehicles,string tracks)
 {
  var car=IdentityResolver.ReadVehicleCatalog(vehicles).First(v=>v.Model=="SYNTHETIC Aster GT3");var feed=new Feed(car.Stem);using var transport=new Transport(vehicles,tracks);var engine=new LiveStateEngine();using var worker=new LmuLiveWorker(feed,transport,engine);await worker.StartAsync(CancellationToken.None);
  try {
   async Task Wait(Func<bool> predicate){var until=DateTimeOffset.UtcNow.AddSeconds(9);while(!predicate()&&DateTimeOffset.UtcNow<until)await Task.Delay(100);}
   await Wait(()=>engine.Status().Key is not null);var first=engine.Status();check(first.Key is not null,"production worker confirms native identity with two independently timestamped catalog batches");
   var instance=first.SessionIdentity!.InstanceId;Volatile.Write(ref feed.Frozen,true);await Task.Delay(2700);var paused=engine.Status();check(paused.Key==first.Key&&paused.SessionIdentity!.InstanceId==instance&&!paused.Fresh&&!paused.CanCapture,"production worker independently refreshes navigation while native clock is frozen");
   check(transport.NavigationRequests>=5&&transport.OnlyGet,"production REST adapter polls navigation independently and remains GET-only");
   Volatile.Write(ref feed.Ended,true);await Wait(()=>engine.Status().SessionIdentity?.Ended==true);check(engine.Status().Key is null&&engine.Status().SessionIdentity!.InstanceId is null,"production worker promptly observes unload without cached target");
   Volatile.Write(ref feed.Frozen,false);Volatile.Write(ref feed.Ended,false);await Wait(()=>engine.Status().Key is not null);check(engine.Status().Key==first.Key&&engine.Status().SessionIdentity!.InstanceId!=instance,"production worker rebinds identical new weekend under a distinct instance");
  }finally{await worker.StopAsync(CancellationToken.None);}
 }
 private sealed class Feed(string file):INativeFrameSource
 {
  public bool Frozen,Ended;private double elapsed=100;
  public NativeFrame Read(){var now=DateTimeOffset.UtcNow;if(!Volatile.Read(ref Frozen))elapsed+=.1;var f=Decoder.Empty();foreach(var(k,v)in new(string,object?)[]{("GameRunning",true),("GameVersion",14200),("OptionsLocation",0),("PlayerControl",0),("PlayerSlotId",0),("PlayerVehicleFile",file),("RawTrackName","SYNTHETIC Circuit Alpha"),("SessionElapsed",elapsed),("SessionStart",54000f),("SessionCode",5),("Session","Qualifying"),("FixedSetup",false),("GamePhase",5)})f[k]=new(v,"SYNTHETIC production-worker regression");bool end=Volatile.Read(ref Ended),frozen=Volatile.Read(ref Frozen);if(end||frozen)f["SessionSourceStatus"]=new(end?"Ended":"Frozen","SYNTHETIC");return new(true,"SYNTHETIC-worker",new(now,"LIVE",Decoder.Empty(),"no electronics"),new(now,"LIVE",f,end?"unloaded":frozen?"frozen":null));}
 }
 private sealed class Transport:ILmuCatalogTransport,IDisposable
 {
  private readonly RestProbe probe;private readonly Handler handler;
  public Transport(string vehicles,string tracks){handler=new(vehicles,tracks);probe=new("http://127.0.0.1:6397",handler);}
  public int NavigationRequests=>Volatile.Read(ref handler.NavigationRequests);public bool OnlyGet=>handler.OnlyGet;
  public Task<RestCapture[]> Read(CancellationToken token)=>probe.PollCatalogs(token);
  public Task<RestCapture?> ReadNavigation(CancellationToken token)=>probe.PollNavigation(token);
  public void Dispose()=>probe.Dispose();
 }
 private sealed class Handler(string vehicles,string tracks):HttpMessageHandler
 {
  public int NavigationRequests;public bool OnlyGet=true;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){OnlyGet&=request.Method==HttpMethod.Get;var body=request.RequestUri!.AbsolutePath switch{"/rest/sessions/getAllVehicles"=>vehicles,"/rest/sessions/getTracksAll"=>tracks,"/navigation/state"=>Navigation(),_=>throw new Exception("Unexpected REST path")};return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")});}
  private string Navigation(){Interlocked.Increment(ref NavigationRequests);return JsonSerializer.Serialize(new{loadingStatus=new{loading=false},state=new{appBuild=14200,gamePhase="GREEN",gameSession="QUALIFY1",gameState="GSTATE_DYN",internalStateCode="OP_RAN_REALTIME_PASS",navigationState="NAV_REALTIME"}});}
 }
}
