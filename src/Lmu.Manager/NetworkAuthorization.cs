using Lmu.Localization;
using System.Net;
using Lmu.Web;
namespace Lmu.Manager;
public static class NetworkAuthorization
{
 public static PrivateInterface? Select(ManagerSettings settings,IEnumerable<PrivateInterface> choices){
  var approval=settings.Network;if(!settings.LanEnabled||approval is not{Trusted:true,HttpRiskAccepted:true,FirewallUnderstood:true}||approval.Port!=settings.Port||string.IsNullOrEmpty(approval.Fingerprint))return null;
  var matches=choices.Where(n=>n.Eligible&&n.Fingerprint==approval.Fingerprint).ToArray();return matches.Length==1?matches[0]:null;
 }
 public static object FirewallPlan(FirewallHelpRequest request,IEnumerable<PrivateInterface> choices,string executable){
  SettingsStore.Validate(new(Port:request.Port));if(!request.Consent)throw new ArgumentException(UiText.C("manager.theManualFirewallInstructionsMustBeExplicitlyRequested"));var matches=choices.Where(n=>n.Eligible&&n.Fingerprint==request.Fingerprint).ToArray();if(matches.Length!=1)throw new ArgumentException(UiText.C("manager.networkIsNoLongerUniquelyAvailableSelectAgain"));var n=matches[0];
  if(Path.GetFileName(executable)!="LMU Setup Manager.exe")throw new ArgumentException(UiText.C("manager.firewallInstructionsAreAvailableOnlyForTheRegular"));
  static string Quote(string text)=>"'"+text.Replace("'","''")+"'";
  // InterfaceAlias accepts wildcard syntax: reject ambiguous aliases rather than broadening a rule.
  if(n.Name.IndexOfAny(['*','?','[',']'])>=0||n.Name.Any(char.IsControl))throw new ArgumentException(UiText.C("manager.adapterNameIsUnsuitableForANarrowlyScoped"));
  var profile=n.Category switch{0=>"Public",1=>"Private",_=>throw new ArgumentException(UiText.C("manager.unknownFirewallProfile"))};
  var command="New-NetFirewallRule -DisplayName "+Quote("LMU Setup Manager – "+n.Name)+" -Direction Inbound -Action Allow -Enabled True -Profile "+profile+" -Program "+Quote(Path.GetFullPath(executable))+" -Protocol TCP -LocalPort "+request.Port+" -InterfaceAlias "+Quote(n.Name)+" -LocalAddress "+Quote(n.Address.ToString())+" -RemoteAddress "+Quote(n.Cidr)+" -EdgeTraversalPolicy Block";
  return new{Command=command,Profile=profile,Address=n.Address.ToString(),Subnet=n.Cidr,Port=request.Port,Notice=UiText.C("manager.manualInstructionsOnlyNoRuleHasBeenCreated")};
 }
}
