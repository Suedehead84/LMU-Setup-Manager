using Lmu.Localization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Lmu.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
namespace Lmu.Manager;

public sealed class ManagerController: IManagerBridge,IAsyncDisposable
{
 private readonly SettingsStore store;private readonly DeviceGrants devices;private readonly IPrivateNetworks networks;private readonly IAutoStart autoStart;private readonly SemaphoreSlim change=new(1,1);private readonly object stateLock=new();private WebApplication? host;private ManagerSettings settings;private PrivateInterface[] bindings=[];private bool running,lanActive,changing,stopping;private string message=UiText.C("manager.serverStarting");
 private bool persistenceBlocked,bindFailed;private DateTimeOffset nextBindAttempt;private int bindFailures;
 private DateTimeOffset? lastRemoteContact;private readonly string executablePath;
 private readonly string ownerKey=Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),csrf=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
 public string DatabasePath{get;}public event Action? Changed;public static string Version=>typeof(ManagerController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
 public string TrayLanguage{get{lock(stateLock)return settings.TrayLanguage??UiText.SystemLanguage;}}
 public ManagerController(SettingsStore store,string databasePath,IPrivateNetworks networks,IAutoStart autoStart,int? initialPort=null,string? executablePath=null){this.executablePath=executablePath??Environment.ProcessPath??"";this.store=store;this.networks=networks;this.autoStart=autoStart;DatabasePath=Path.GetFullPath(databasePath);settings=store.Load();if(initialPort is {} port){settings=settings with{Port=port};SettingsStore.Validate(settings);}devices=new(store.DirectoryPath);}
 public string LocalUrl(string route="dashboard"){lock(stateLock)return $"http://127.0.0.1:{settings.Port}/#access={ownerKey}&page={route}";}
 public ManagerView View(){var choices=networks.Read();lock(stateLock){bool active=Active(choices);return new(true,Version,settings with{AutoStart=autoStart.Enabled},running,active,changing,lanActive&&!active?UiText.C("manager.networkPropertiesChangedOrAreUncertainAccessBlocked"):message,DatabasePath,Path.Combine(Path.GetDirectoryName(DatabasePath)!,"backups"),active?bindings.Select(n=>$"http://{n.Address}:{settings.Port}/").ToArray():[],devices.List(),new(choices.Select(n=>n.Choice()).ToArray(),UiText.C("manager.httpNoTransportEncryption"),UiText.C("manager.theAppDoesNotChangeTheFirewallBinding"),active?lastRemoteContact:null,NetworkState(choices)));}}
 public async Task StartAsync(){await change.WaitAsync().ConfigureAwait(false);try{await ApplyCore(settings,true).ConfigureAwait(false);}finally{change.Release();}}
 public void RequestSettings(ManagerSettings next){SettingsStore.Validate(next);lock(stateLock){if(stopping||changing)throw new ArgumentException(UiText.C("manager.aChangeIsAlreadyInProgress"));changing=true;}Changed?.Invoke();_ = Task.Run(async()=>{await change.WaitAsync().ConfigureAwait(false);try{if(!stopping)await ApplyCore(next,false).ConfigureAwait(false);}catch{lock(stateLock)if(!persistenceBlocked)message=UiText.C("manager.couldNotApplySettingsCheckAgainThroughThe");}finally{lock(stateLock)changing=false;change.Release();Changed?.Invoke();}});}
 public void RequestPreferences(ManagerPreferences preferences){ManagerSettings next;lock(stateLock)next=settings with{OpenBrowser=preferences.OpenBrowser,AutoStart=preferences.AutoStart,Port=preferences.Port,TrayLanguage=preferences.TrayLanguage};RequestSettings(next);}
 public void RequestHomeNetwork(HomeNetworkRequest request)
 {
  ManagerSettings next;lock(stateLock)next=settings;
  if(request.RemoveApproval){if(!request.Confirmed||request.Enabled||request.Fingerprint is not null)throw new ArgumentException(UiText.C("manager.homeRemovalNeedsConfirmation"));next=next with{LanEnabled=false,Network=null};}
  else if(request.Fingerprint is {} fingerprint){
   if(!request.Enabled||!request.Confirmed)throw new ArgumentException(UiText.C("manager.homeConsentRequired"));
   var matches=networks.Read().Where(n=>n.Eligible&&n.Fingerprint==fingerprint).ToArray();if(matches.Length!=1)throw new ArgumentException(UiText.C("manager.networkIsNoLongerUniquelyAvailableSelectAgain"));
   // One informed consent covers trust, cleartext HTTP and manual firewall responsibility.
   next=next with{LanEnabled=true,Network=new(fingerprint,next.Port,true,true,true)};
  }else {if(request.Enabled&&next.Network is not{Trusted:true,HttpRiskAccepted:true,FirewallUnderstood:true})throw new ArgumentException(UiText.C("manager.homeConsentRequired"));next=next with{LanEnabled=request.Enabled};}
  RequestSettings(next);
 }
 private async Task ApplyCore(ManagerSettings requested,bool startup,bool explicitChange=true)
 {
  var previous=settings;var current=await Task.Run(networks.Read).ConfigureAwait(false);var selected=NetworkAuthorization.Select(requested,current);PrivateInterface[] candidate=selected is null?[]:[selected];
  // A failed proof changes runtime access, never the saved approval or requested switch.
  try{if(startup&&requested.AutoStart||!startup&&explicitChange&&requested.AutoStart!=previous.AutoStart)autoStart.Set(requested.AutoStart);if(startup||explicitChange||requested!=previous)store.Save(requested);}
  catch{lock(stateLock){persistenceBlocked=true;lanActive=false;bindings=[];lastRemoteContact=null;message=UiText.C("manager.homeSaveFailed");}await StopHost().ConfigureAwait(false);await StartHost(previous,[]).ConfigureAwait(false);throw;}
  lock(stateLock)persistenceBlocked=false;
  bool restart=host is null||!running||requested.Port!=previous.Port||(candidate.Length>0)!=lanActive||!candidate.ToHashSet().SetEquals(bindings);
  if(explicitChange&&!startup&&(requested.LanEnabled!=previous.LanEnabled||requested.Network!=previous.Network))devices.ClearInvitations();
  if(restart){devices.ClearInvitations();lock(stateLock){lanActive=false;bindings=[];lastRemoteContact=null;}await StopHost().ConfigureAwait(false);
   try{await StartHost(requested,candidate).ConfigureAwait(false);lock(stateLock){bindFailed=false;bindFailures=0;}}
   catch{await StopHost().ConfigureAwait(false);if(requested.Port!=previous.Port){requested=previous;store.Save(requested);}try{await StartHost(requested,[]).ConfigureAwait(false);}catch{lock(stateLock){running=false;message=UiText.C("manager.serverFailedToStartThePortIsOccupied");}if(startup)throw new IOException(UiText.C("manager.couldNotStartTheServerCheckPortUsage"));return;}
    lock(stateLock){bindFailed=true;bindFailures=Math.Min(bindFailures+1,4);nextBindAttempt=DateTimeOffset.UtcNow.AddSeconds(Math.Min(30,5*Math.Pow(2,bindFailures-1)));}}
  }
  lock(stateLock){settings=requested;message=UiText.C(lanActive?"manager.serverRunningOnTheTrustedHomeNetworkCheck":"manager.serverRunningLocallyOnly");}Changed?.Invoke();
 }
 private string NetworkState(PrivateInterface[] current)=>persistenceBlocked?"save-failed":!settings.LanEnabled?"off":settings.Network is not{Trusted:true,HttpRiskAccepted:true,FirewallUnderstood:true}||settings.Network.Port!=settings.Port?"needs-approval":changing||!running?"checking":DiscoveryUnavailable(current)?"unavailable":NetworkAuthorization.Select(settings,current) is null?"different-network":Active(current)?"active":bindFailed?"listener-unavailable":"checking";
 private async Task StartHost(ManagerSettings next,PrivateInterface[] addresses){var options=new ServerOptions(DatabasePath,next.Port,WebRoot:Path.Combine(AppContext.BaseDirectory,"wwwroot")){AccessKey=ownerKey,Csrf=csrf,ListenAddresses=[IPAddress.Loopback,..addresses.Select(n=>n.Address).Distinct()]};var app=WebHostFactory.Create(options,manager:this);host=app;try{await app.StartAsync().ConfigureAwait(false);lock(stateLock){settings=next;running=true;lanActive=addresses.Length>0;bindings=addresses;}}catch{await app.DisposeAsync().ConfigureAwait(false);host=null;throw;}app.Lifetime.ApplicationStopped.Register(()=>{lock(stateLock){running=false;lanActive=false;bindings=[];if(ReferenceEquals(host,app)&&!stopping)message=UiText.C("manager.webServerStoppedUnexpectedlyQuitTheManagerThrough");}Changed?.Invoke();});}
 private async Task StopHost(){var app=host;host=null;if(app is null)return;using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(10));try{await app.StopAsync(stop.Token).ConfigureAwait(false);}finally{await app.DisposeAsync().ConfigureAwait(false);SqliteConnection.ClearAllPools();lock(stateLock)running=false;}}
 private static bool DiscoveryUnavailable(PrivateInterface[] current)=>current.Length==0||current.All(n=>string.IsNullOrEmpty(n.NetworkId)||n.Category<0);
 private bool Active(PrivateInterface[] current)=>running&&lanActive&&!stopping&&bindings.Length==1&&NetworkAuthorization.Select(settings,current)?.Fingerprint==bindings[0].Fingerprint;
 public bool AuthenticateDevice(string token){if(token.Length!=64)return false;var current=networks.Read();lock(stateLock)return Active(current)&&devices.Authenticate(token);}
 public string? RedeemInvitation(string invitation){var current=networks.Read();lock(stateLock)return Active(current)?devices.Redeem(invitation):null;}
 public object Invite(string name){var current=networks.Read();lock(stateLock){if(!Active(current)||changing)throw new ArgumentException(UiText.C("manager.confirmedHomeNetworkAccessIsNotActive"));var invite=devices.CreateInvitation(name);return new{Name=name,invite.Id,invite.ExpiresAt,ExpiresInMinutes=10,Urls=bindings.Select(n=>$"http://{n.Address}:{settings.Port}/#access={invite.Token}").ToArray()};}}
 public InvitationView InvitationStatus(Guid id){var current=networks.Read();lock(stateLock)return Active(current)?devices.Status(id):new(id,DateTimeOffset.UtcNow,"unavailable");}
 public void Revoke(Guid id){devices.Revoke(id);Changed?.Invoke();}
 public bool AllowRemote(IPAddress ip){var current=networks.Read();lock(stateLock)return Active(current)&&WindowsPrivateNetworks.SameSubnet(ip,bindings[0]);}
 public RemoteAuthorization CheckRemote(IPAddress ip,string cookie,string bearer){var current=networks.Read();lock(stateLock){bool allowed=Active(current)&&WindowsPrivateNetworks.SameSubnet(ip,bindings[0]);return new(allowed,allowed&&(devices.Authenticate(cookie)||devices.Authenticate(bearer)));}}
 public void RecordRemoteContact(){lock(stateLock)lastRemoteContact=DateTimeOffset.UtcNow;}
 public object FirewallHelp(FirewallHelpRequest request)=>NetworkAuthorization.FirewallPlan(request,networks.Read(),executablePath);
 public async Task CheckNetworkAsync(){lock(stateLock){if(stopping||persistenceBlocked||!lanActive&&!settings.LanEnabled||bindFailed&&DateTimeOffset.UtcNow<nextBindAttempt)return;}if(!change.Wait(0))return;try{var current=await Task.Run(networks.Read).ConfigureAwait(false);bool active,valid,approved;lock(stateLock){if(stopping)return;active=lanActive;valid=Active(current);approved=NetworkAuthorization.Select(settings,current) is not null;}if(active&&!valid||!active&&approved)await ApplyCore(settings,false,false).ConfigureAwait(false);}finally{change.Release();}}
 public async ValueTask DisposeAsync(){lock(stateLock){stopping=true;lanActive=false;}await change.WaitAsync().ConfigureAwait(false);try{await StopHost().ConfigureAwait(false);}finally{change.Release();}}
}
