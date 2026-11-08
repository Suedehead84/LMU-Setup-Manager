using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Lmu.Manager;
using Lmu.Web;

static class B252Tests
{
 public static async Task Run(Action<bool,string> check){
  var root=Path.GetFullPath(Path.Combine("artifacts","b252-tests-"+Guid.NewGuid().ToString("N")));
  var clock=new Clock(DateTimeOffset.UtcNow);var grants=new DeviceGrants(Path.Combine(root,"timed"),clock);
  var invitation=grants.CreateInvitation("SYNTHETIC QR device");
  check(invitation.Token.Length==48&&invitation.ExpiresAt==clock.Now.AddMinutes(10),"QR invitations retain original random token length and exact ten-minute lifetime");
  check(grants.Status(invitation.Id).Status=="active","non-secret invitation ID reports active state");
  check(!JsonSerializer.Serialize(grants.Status(invitation.Id)).Contains(invitation.Token),"status response contains no invitation secret");
  clock.Now=clock.Now.AddMinutes(10).AddTicks(-1);check(grants.Status(invitation.Id).Status=="active","invitation remains active immediately before deadline");
  clock.Now=clock.Now.AddTicks(1);check(grants.Status(invitation.Id).Status=="expired"&&grants.Redeem(invitation.Token) is null,"deadline rejects redemption exactly at ten minutes");
  invitation=grants.CreateInvitation("SYNTHETIC one use");var access=grants.Redeem(invitation.Token)!;
  check(grants.Status(invitation.Id).Status=="used"&&grants.Authenticate(access),"successful redemption immediately reports used and creates existing device grant");
  check(grants.Redeem(invitation.Token) is null,"used invitation never issues a second grant");
  grants.Revoke(grants.List().Single().Id);check(!grants.Authenticate(access)&&grants.Status(invitation.Id).Status=="used","device revocation does not reactivate its invitation");
  check(new DeviceGrants(Path.Combine(root,"timed"),clock).Status(invitation.Id).Status=="unavailable","invitations and status are not persisted on restart");
  check(grants.Status(Guid.NewGuid()).Status=="unavailable","unknown status ID grants no authority");
  var failedInvite=grants.CreateInvitation("SYNTHETIC persistence failure");var protectedPath=Path.Combine(root,"timed","devices.protected");File.SetAttributes(protectedPath,FileAttributes.ReadOnly);bool saveFailed=false;try{grants.Redeem(failedInvite.Token);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){saveFailed=true;}finally{File.SetAttributes(protectedPath,FileAttributes.Normal);}
  check(saveFailed&&grants.Status(failedInvite.Id).Status=="unavailable"&&grants.List().Length==0,"failed grant persistence cannot report a successfully authorized device or revive its invitation");
  var pending=grants.CreateInvitation("SYNTHETIC LAN off");grants.RevokeAll();check(grants.Status(pending.Id).Status=="unavailable"&&grants.Redeem(pending.Token) is null,"LAN revocation clears pending invitations and QR status");
  var l=new TcpListener(IPAddress.Loopback,0);l.Start();var port=((IPEndPoint)l.LocalEndpoint).Port;l.Stop();
  var networks=new Networks();var store=new SettingsStore(Path.Combine(root,"http"));var n=networks.Read().Single();store.Save(new(false,false,port,true,new(n.Fingerprint,port,true,true,true)));
  await using var manager=new ManagerController(store,Path.Combine(root,"library.sqlite"),networks,new AutoStart());await manager.StartAsync();
  using var owner=new HttpClient(new HttpClientHandler{UseProxy=false,UseCookies=false}){BaseAddress=new("http://127.0.0.1:"+port)};
  var ownerToken=new Uri(manager.LocalUrl()).Fragment.Split("access=")[1].Split('&')[0];owner.DefaultRequestHeaders.Authorization=new("Bearer",ownerToken);
  var session=await owner.GetFromJsonAsync<JsonElement>("/api/session");owner.DefaultRequestHeaders.Add("X-LMU-CSRF",session.GetProperty("csrf").GetString());
  var info=await (await owner.PostAsJsonAsync("/api/manager/invitations",new{name="SYNTHETIC HTTP QR"})).Content.ReadFromJsonAsync<JsonElement>();
  var id=info.GetProperty("id").GetGuid();var url=info.GetProperty("urls")[0].GetString()!;var token=new Uri(url).Fragment[8..];
  check(info.GetProperty("expiresInMinutes").GetInt32()==10&&info.GetProperty("expiresAt").GetDateTimeOffset()>DateTimeOffset.UtcNow.AddMinutes(9),"protected invitation API supplies absolute expiry and original URL");
  var response=await owner.GetAsync("/api/manager/invitations/"+id);var status=await response.Content.ReadFromJsonAsync<JsonElement>();
  check(status.GetProperty("status").GetString()=="active"&&response.Headers.CacheControl?.NoStore==true,"owner status is active and no-store");
  using var guest=new HttpClient(new HttpClientHandler{UseProxy=false,UseCookies=false}){BaseAddress=owner.BaseAddress};
  check((await guest.GetAsync("/api/manager/invitations/"+id)).StatusCode==HttpStatusCode.Unauthorized,"unrecognized device cannot inspect invitation status");
  var output=Console.Out;var errors=Console.Error;using var captured=new StringWriter();JsonElement paired;
  try{Console.SetOut(captured);Console.SetError(captured);var result=await guest.PostAsJsonAsync("/api/session",new{key=token});paired=await result.Content.ReadFromJsonAsync<JsonElement>();await owner.GetAsync("/api/manager/invitations/"+id);await guest.PostAsJsonAsync("/api/session",new{key=token});}
  finally{Console.SetOut(output);Console.SetError(errors);}
  check(!captured.ToString().Contains(token)&&!captured.ToString().Contains(url),"invitation status and redemption do not write secret to application output");
  check(paired.GetProperty("authenticated").GetBoolean()&&manager.View().Devices.Length==1,"QR link redeems through existing process without another PC confirmation");
  check((await owner.GetFromJsonAsync<JsonElement>("/api/manager/invitations/"+id)).GetProperty("status").GetString()=="used","owner observes redemption by ID without transmitting token again");
  var reused=await guest.PostAsJsonAsync("/api/session",new{key=token});check(reused.StatusCode==HttpStatusCode.Unauthorized&&(await reused.Content.ReadAsStringAsync()).Contains("bereits eingelöster"),"reused link returns understandable generic rejection without secret disclosure");
  var deviceInvite=manager.Invite("SYNTHETIC status role");var element=JsonSerializer.SerializeToElement(deviceInvite);var key=new Uri(element.GetProperty("Urls")[0].GetString()!).Fragment[8..];var credential=manager.RedeemInvitation(key)!;
  guest.DefaultRequestHeaders.Authorization=new("Bearer",credential);
  check((await guest.GetAsync("/api/manager/invitations/"+id)).StatusCode==HttpStatusCode.Forbidden,"authorized library device still cannot read owner invitation status");
  check((await guest.GetAsync("/api/setups")).IsSuccessStatusCode,"QR device retains normal protected library access");
  manager.Revoke(manager.View().Devices.Single(d=>d.Name=="SYNTHETIC status role").Id);
  check((await guest.GetAsync("/api/setups")).StatusCode==HttpStatusCode.Unauthorized,"QR-derived device access is individually revocable");
 }
 private sealed class Networks:IPrivateNetworks{public PrivateInterface[] Read()=>[new("SYNTHETIC-QR",IPAddress.Parse("127.0.0.2"),8,"SYNTHETIC QR Ethernet","33333333-3333-3333-3333-333333333333",0,"127.0.0.1","SYNTHETIC","Ethernet",true)];}
 private sealed class AutoStart:IAutoStart{public bool Enabled=>false;public void Set(bool enabled){}}
}
