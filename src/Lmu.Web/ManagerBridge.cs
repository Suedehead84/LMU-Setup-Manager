using System.Net;
namespace Lmu.Web;

public sealed record NetworkApproval(string Fingerprint,int Port,bool Trusted,bool HttpRiskAccepted,bool FirewallUnderstood);
public sealed record FirewallHelpRequest(string Fingerprint,int Port,bool Consent);
public sealed record NetworkChoice(string Fingerprint,string AdapterName,string Address,string Cidr,string Gateway,string Profile,bool Eligible,string Reason);
public sealed record NetworkSetupView(NetworkChoice[] Choices,string Transport,string FirewallStatus,DateTimeOffset? LastRemoteContact,string State="off");
public sealed record HomeNetworkRequest(bool Enabled,string? Fingerprint=null,bool Confirmed=false,bool RemoveApproval=false);
public sealed record ManagerPreferences(bool OpenBrowser,bool AutoStart,int Port,string? TrayLanguage=null);
public sealed record ManagerSettings(bool OpenBrowser=true,bool AutoStart=false,int Port=5087,bool LanEnabled=false,NetworkApproval? Network=null,string? TrayLanguage=null);
public sealed record DeviceView(Guid Id,string Name,DateTimeOffset CreatedAt,DateTimeOffset ExpiresAt);
public sealed record ManagerView(bool Managed,string Version,ManagerSettings Settings,bool Running,bool LanActive,bool Changing,string Message,string DatabasePath,string BackupPath,string[] Addresses,DeviceView[] Devices,NetworkSetupView? NetworkSetup=null);
public sealed record DeviceInvitation(string Name);
public sealed record InvitationView(Guid Id,DateTimeOffset ExpiresAt,string Status);
public sealed record RemoteAuthorization(bool Allowed,bool Authenticated);
public static class ManagedSecurity {public static bool OwnerAllowed(bool managed,bool loopback)=>!managed||loopback;}
public interface IManagerBridge
{
    ManagerView View();
    void RequestSettings(ManagerSettings settings);
    void RequestHomeNetwork(HomeNetworkRequest request);
    void RequestPreferences(ManagerPreferences preferences);
    object Invite(string name);
    InvitationView InvitationStatus(Guid id);
    void Revoke(Guid id);
    bool AuthenticateDevice(string token);
    string? RedeemInvitation(string invitation);
    bool AllowRemote(IPAddress address);
    RemoteAuthorization CheckRemote(IPAddress address,string cookie,string bearer)=>new(AllowRemote(address),AuthenticateDevice(cookie)||AuthenticateDevice(bearer));
    object FirewallHelp(FirewallHelpRequest request);
    void RecordRemoteContact();
}
