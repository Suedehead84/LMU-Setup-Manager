using Lmu.Localization;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Lmu.Web;
namespace Lmu.Manager;
public sealed record NetworkProfile(Guid AdapterId,bool Connected,int Category,string NetworkId="");
public sealed record PrivateInterface(string Id,IPAddress Address,int PrefixLength,string Name="",string NetworkId="",int Category=-1,string Gateway="",string Hardware="",string Kind="",bool Eligible=false,string Reason="",string EnvironmentKey="")
{
 public string Fingerprint=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{Id,Address=Address.ToString(),PrefixLength,Name,NetworkId,Category,Gateway,Hardware,Kind,Eligible,Reason,EnvironmentKey})));
 public string Cidr{get{var b=Address.GetAddressBytes();for(int i=0;i<4;i++)b[i]&=(byte)(255<<Math.Clamp(8-(PrefixLength-i*8),0,8));return new IPAddress(b)+"/"+PrefixLength;}}
 public NetworkChoice Choice()=>new(Fingerprint,Name,Address.ToString(),Cidr,Gateway,Category switch{0=>UiText.C("manager.public"),1=>UiText.C("manager.private"),2=>UiText.C("manager.domain"),_=>UiText.C("manager.unknown")},Eligible,Reason);
}
public interface IPrivateNetworks{PrivateInterface[] Read();}
public sealed class WindowsPrivateNetworks:IPrivateNetworks
{
 public string? LastError{get;private set;}
 private sealed record HardwareAdapter(bool Physical,bool Virtual,string Description);
 private readonly DiscoverySnapshot<Dictionary<Guid,HardwareAdapter>> hardwareSnapshot=new(TimeSpan.FromSeconds(2));
 public PrivateInterface[] Read()
 {
  object? manager=null;LastError=null;try{
   manager=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"),true)!)!;
   var profiles=new List<NetworkProfile>();
   var connections=((dynamic)manager).GetNetworkConnections();
   foreach(dynamic connection in connections){object? network=null;try{
    network=connection.GetNetwork();
    Marshal.ThrowExceptionForHR(((INetworkConnectionIdentity)(object)connection).GetAdapterId(out var adapterId));
    bool connected=(bool)connection.IsConnected;
    int category=(int)((dynamic)network).GetCategory();
    Marshal.ThrowExceptionForHR(((INetworkIdentity)network).GetNetworkId(out var networkId));
    profiles.Add(new(adapterId,connected,category,networkId.ToString("D")));
   }finally{Release(network);Release(connection);}}
   var all=NetworkInterface.GetAllNetworkInterfaces();var up=all.Where(n=>n.OperationalStatus==OperationalStatus.Up).ToArray();
   var vpn=up.Where(n=>IsVpn(n.NetworkInterfaceType,n.Name+" "+n.Description)).Select(n=>n.Id).Order(StringComparer.Ordinal).ToArray();
   var environmentKey=string.Join("|",up.Select(n=>n.Id+":"+n.NetworkInterfaceType+":"+string.Join(",",n.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork).Select(a=>a.Address.ToString()).Order(StringComparer.Ordinal))).Order(StringComparer.Ordinal))+"|VPN:"+string.Join(",",vpn);var result=new List<PrivateInterface>();
   var hardware=hardwareSnapshot.Read(environmentKey,ReadHardware);
   foreach(var nic in up){if(!Guid.TryParse(nic.Id,out var id))continue;var connected=profiles.Where(p=>p.AdapterId==id&&p.Connected).Distinct().ToArray();var profile=connected.Length==1?connected[0]:null;hardware.TryGetValue(id,out var h);
    var properties=nic.GetIPProperties();var gateway=string.Join(",",properties.GatewayAddresses.Select(a=>a.Address).Where(a=>a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork).Select(a=>a.ToString()).Order(StringComparer.Ordinal));
    foreach(var a in properties.UnicastAddresses.Where(a=>IsPrivate(a.Address))){
     string reason=CandidateReason(h?.Physical==true,h?.Virtual!=false,nic.NetworkInterfaceType,nic.Name+" "+nic.Description,profile,a.Address,a.PrefixLength,vpn.Length>0);
     if(nic.Name.IndexOfAny(['*','?','[',']'])>=0||nic.Name.Any(char.IsControl))reason=UiText.C("manager.adapterNameIsUnsuitableForUnambiguousFirewallScoping");
     result.Add(new(nic.Id,a.Address,a.PrefixLength,nic.Name,profile?.NetworkId??"",profile?.Category??-1,gateway,nic.GetPhysicalAddress().ToString(),nic.NetworkInterfaceType.ToString(),reason.Length==0,reason,environmentKey));
    }
   }
   return result.OrderBy(n=>n.Id,StringComparer.Ordinal).ThenBy(n=>n.Address.ToString(),StringComparer.Ordinal).ToArray();
  }catch(Exception e){LastError=e.ToString();return [];}finally{Release(manager);}
 }
 private static Dictionary<Guid,HardwareAdapter> ReadHardware(){object? locator=null,service=null,rows=null;try{locator=Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator",true)!)!;service=((dynamic)locator).ConnectServer(".","root\\StandardCimv2");rows=((dynamic)service).ExecQuery("SELECT InterfaceGuid,HardwareInterface,Virtual,InterfaceDescription FROM MSFT_NetAdapter");var result=new Dictionary<Guid,HardwareAdapter>();foreach(dynamic row in (dynamic)rows){try{object? Value(string key)=>row.Properties_.Item(key).Value;var text=Convert.ToString(Value("InterfaceGuid"));if(Guid.TryParse(text,out var id))result[id]=new(Value("HardwareInterface") is true,Value("Virtual") is not false,Convert.ToString(Value("InterfaceDescription"))??"");}finally{Release(row);}}return result;}finally{Release(rows);Release(service);Release(locator);}}
 private static void Release(object? value){if(value is not null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
 public static bool IsVpn(NetworkInterfaceType type,string text)=>type is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp||new[]{"vpn","wireguard","wintun","tailscale","zerotier","hamachi","tap-","tap "}.Any(w=>text.Contains(w,StringComparison.OrdinalIgnoreCase));
 public static string CandidateReason(bool physical,bool virtualAdapter,NetworkInterfaceType type,string description,NetworkProfile? profile,IPAddress address,int prefix,bool vpnActive){
  if(vpnActive)return UiText.C("manager.activeVpnTunnelConnectionLanRemainsOffAs");
  if(!physical||virtualAdapter||type is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)||IsVpn(type,description)||new[]{"virtual","hyper-v","vmware","wi-fi direct","hotspot","loopback","bridge"}.Any(w=>description.Contains(w,StringComparison.OrdinalIgnoreCase)))return UiText.C("manager.notAVerifiedPhysicalEthernetWiFiInterface");
  if(profile is null||!profile.Connected||profile.Category is not (0 or 1)||!Guid.TryParse(profile.NetworkId,out var network)||network==Guid.Empty)return UiText.C("manager.currentWindowsNetworkIdentityNotConfirmedUnambiguously");
  var minimum=address.GetAddressBytes()[0] switch{10=>8,172=>12,192=>16,_=>32};if(!IsPrivate(address)||prefix<minimum||prefix>30)return UiText.C("manager.noSuitableLocalIpv4AddressRange");
  return "";
 }
 // Retained helper for the B2.5 classification regressions; trust is now a separate decision.
 public static HashSet<Guid> QualifiedAdapters(IEnumerable<NetworkProfile> profiles)=>profiles.Where(p=>p.Connected).GroupBy(p=>p.AdapterId).Where(g=>g.All(p=>p.Category==1)).Select(g=>g.Key).ToHashSet();
 public static bool IsPrivate(IPAddress ip){if(ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)return false;var b=ip.GetAddressBytes();return b[0]==10||b[0]==172&&b[1] is >=16 and <=31||b[0]==192&&b[1]==168;}
 public static bool SameSubnet(IPAddress a,PrivateInterface network){if(!IsPrivate(a)||network.PrefixLength is <1 or >32)return false;var b=a.GetAddressBytes();var n=network.Address.GetAddressBytes();for(int i=0;i<4;i++){int bits=Math.Clamp(network.PrefixLength-i*8,0,8),mask=(255<<(8-bits))&255;if((b[i]&mask)!=(n[i]&mask))return false;}return true;}
}
