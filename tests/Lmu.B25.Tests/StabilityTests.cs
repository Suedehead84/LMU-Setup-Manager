using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Lmu.Manager;
using Lmu.Web;

static class StabilityTests
{
 public static async Task Run(Action<bool,string> check)
 {
  var root=Path.GetFullPath(Path.Combine("artifacts","stability-tests-"+Guid.NewGuid().ToString("N")));var store=new SettingsStore(Path.Combine(root,"settings"));
  var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
  var networks=new Networks();var net=networks.Read()[0];store.Save(new(false,false,port,true,new(net.Fingerprint,port,true,true,true)));
  ManagerController? manager=null;string cookie="";byte[]? grantBytes=null;
  HttpClient Client(){var c=new HttpClient(new HttpClientHandler{UseProxy=false,UseCookies=false}){BaseAddress=new("http://127.0.0.1:"+port)};if(cookie.Length>0)c.DefaultRequestHeaders.Add("Cookie",cookie);return c;}
  async Task Apply(ManagerSettings next){manager!.RequestSettings(next);var until=DateTimeOffset.UtcNow.AddSeconds(15);while(manager.View().Changing&&DateTimeOffset.UtcNow<until)await Task.Delay(30);if(manager.View().Changing)throw new Exception("Settings timeout");}
  try {
   for(int restart=0;restart<4;restart++){
    manager=new(store,Path.Combine(root,"library.sqlite"),networks,new Auto());await manager.StartAsync();
    if(restart==0){var invitation=JsonSerializer.SerializeToElement(manager.Invite("SYNTHETIC restart cookie"));string key=invitation.GetProperty("Urls")[0].GetString()!.Split("access=")[1];using var client=Client();var response=await client.PostAsJsonAsync("/api/session",new{key});check(response.IsSuccessStatusCode,"stability pairing creates a real HTTP device grant");cookie=response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];grantBytes=File.ReadAllBytes(Path.Combine(store.DirectoryPath,"devices.protected"));}
    using(var client=Client()){var session=await client.GetFromJsonAsync<JsonElement>("/api/session");check(session.GetProperty("authenticated").GetBoolean()&&(await client.GetAsync("/api/setups")).IsSuccessStatusCode,"persistent HTTP cookie survives manager restart "+restart);check((await client.GetAsync("/api/manager/health")).StatusCode==HttpStatusCode.Forbidden,"device cannot read owner-only health diagnostics "+restart);}
    check(manager.View().Devices.Length==1&&File.ReadAllBytes(Path.Combine(store.DirectoryPath,"devices.protected")).SequenceEqual(grantBytes!),"restart leaves protected grant bytes unchanged "+restart);
    int before=networks.Reads;var blocked=manager.CheckRemote(IPAddress.Parse("192.0.2.1"),cookie.Split('=')[1],"");check(!blocked.Allowed&&!blocked.Authenticated&&networks.Reads==before+1,"remote authorization validates network once and rejects unrelated subnet "+restart);
    before=networks.Reads;check(!manager.AuthenticateDevice("")&&networks.Reads==before,"empty cookie does not perform network discovery "+restart);
    if(restart==0){using var owner=Client();owner.DefaultRequestHeaders.Remove("Cookie");owner.DefaultRequestHeaders.Authorization=new("Bearer",new Uri(manager.LocalUrl()).Fragment.Split("access=")[1].Split('&')[0]);var health=await owner.GetFromJsonAsync<JsonElement>("/api/manager/health");check(health.GetProperty("schema").GetInt32()==1,"local owner can export bounded health diagnostics");using var anonymous=Client();anonymous.DefaultRequestHeaders.Remove("Cookie");check((await anonymous.GetAsync("/api/manager/health")).StatusCode==HttpStatusCode.Unauthorized,"anonymous health export is denied");}
    await manager.DisposeAsync();manager=null;
   }
   networks.Available=false;manager=new(store,Path.Combine(root,"library.sqlite"),networks,new Auto());await manager.StartAsync();
   check(!manager.View().LanActive&&manager.View().Devices.Length==1,"unavailable startup network blocks access without deleting grant");
   using(var client=Client())check((await client.GetAsync("/api/setups")).StatusCode==HttpStatusCode.Unauthorized,"dormant grant cannot bypass inactive network approval");
   check(File.ReadAllBytes(Path.Combine(store.DirectoryPath,"devices.protected")).SequenceEqual(grantBytes!),"startup discovery failure performs no protected-grant rewrite");
   check(store.Load().LanEnabled&&store.Load().Network?.Fingerprint==net.Fingerprint,"unknown startup network preserves approval intent while access is suspended");
   networks.Available=true;await manager.CheckNetworkAsync();
   using(var client=Client())check((await client.GetAsync("/api/setups")).IsSuccessStatusCode,"return of exact approved network restores existing device cookie without pairing");
   networks.UnknownProfile=true;await manager.CheckNetworkAsync();check(!manager.View().LanActive&&store.Load().LanEnabled&&manager.View().Devices.Length==1,"temporary NLA profile uncertainty suspends access without destroying approval");
   networks.UnknownProfile=false;await manager.CheckNetworkAsync();check(manager.View().LanActive,"exact profile recovery resumes LAN without reapproval");
   networks.Available=false;await manager.CheckNetworkAsync();check(!manager.View().LanActive&&manager.View().Devices.Length==1,"runtime network loss retains dormant device but closes LAN");
   await Apply(manager.View().Settings with{LanEnabled=false});check(manager.View().Devices.Length==1,"explicit disable preserves dormant paired devices");manager.Revoke(manager.View().Devices.Single().Id);check(manager.View().Devices.Length==0,"device revocation remains separate and explicit");
  }finally{if(manager is not null)await manager.DisposeAsync();}
  var clock=new MonotonicClock();var cache=new DiscoverySnapshot<int>(TimeSpan.FromSeconds(2),clock);int scans=0;
  var tasks=Enumerable.Range(0,40).Select(_=>Task.Run(()=>cache.Read("same",()=>Interlocked.Increment(ref scans))));await Task.WhenAll(tasks);check(scans==1,"40 concurrent discovery readers share one hardware scan");
  clock.Stamp=2000;check(cache.Read("same",()=>++scans)==2,"hardware cache expires at strict two-second boundary");check(cache.Read("changed",()=>++scans)==3,"topology change bypasses hardware cache immediately");
  bool failed=false;try{cache.Read("failure",()=>throw new IOException("SYNTHETIC"));}catch(IOException){failed=true;}check(failed&&cache.Read("changed",()=>++scans)==4,"failed discovery cannot reuse previously populated hardware cache");
 }
 private sealed class Networks:IPrivateNetworks{public bool Available=true,UnknownProfile;public int Reads;public PrivateInterface[] Read(){Interlocked.Increment(ref Reads);return Available?[new("SYNTHETIC",IPAddress.Parse("127.0.0.2"),8,"SYNTHETIC Ethernet",UnknownProfile?"":"11111111-1111-1111-1111-111111111111",UnknownProfile?-1:1,"127.0.0.1","SYNTHETIC MAC","Ethernet",!UnknownProfile)]:[];}}
 private sealed class Auto:IAutoStart{public bool Enabled=>false;public void Set(bool enabled){}}
 private sealed class MonotonicClock:TimeProvider{public long Stamp;public override long TimestampFrequency=>1000;public override long GetTimestamp()=>Stamp;}
}
