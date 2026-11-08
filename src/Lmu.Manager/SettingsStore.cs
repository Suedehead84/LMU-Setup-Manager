using Lmu.Localization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
namespace Lmu.Manager;

public static class Dpapi {public static byte[] Protect(byte[] data)=>Lmu.Web.WindowsSecrets.Protect(data);public static byte[] Unprotect(byte[] data)=>Lmu.Web.WindowsSecrets.Unprotect(data);}
public sealed class SettingsStore(string directory)
{
 public string DirectoryPath {get;}=Path.GetFullPath(directory);
 public static string DefaultDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LmuFixedSetupManager","manager");
 public Lmu.Web.ManagerSettings Load(){var p=Path.Combine(DirectoryPath,"settings.json");if(!File.Exists(p))return new();var s=JsonSerializer.Deserialize<Lmu.Web.ManagerSettings>(File.ReadAllText(p))??throw new InvalidDataException(UiText.C("manager.emptySettings"));Validate(s);return s;}
 public static void Validate(Lmu.Web.ManagerSettings s){if(s.Port is <1024 or >65535)throw new ArgumentException(UiText.C("manager.portMustBeBetween1024And65535"));if(s.TrayLanguage is not null&&!UiText.Catalog.Languages.Any(l=>string.Equals(l.Code,s.TrayLanguage,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException(UiText.C("manager.invalidLanguage"));}
 public void Save(Lmu.Web.ManagerSettings settings){Validate(settings);Atomic(Path.Combine(DirectoryPath,"settings.json"),JsonSerializer.SerializeToUtf8Bytes(settings));}
 public static void Atomic(string path,byte[] bytes){Directory.CreateDirectory(Path.GetDirectoryName(path)!);var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(tmp,bytes);File.Move(tmp,path,true);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
}
public sealed class DeviceGrants
{
 private sealed record Grant(Guid Id,string Name,string Hash,DateTimeOffset CreatedAt,DateTimeOffset ExpiresAt);
 private sealed record Invitation(Guid Id,string Name,DateTimeOffset ExpiresAt,bool Used=false);
 public sealed record CreatedInvitation(Guid Id,string Token,DateTimeOffset ExpiresAt);
 private readonly object gate=new();private readonly string path;private readonly TimeProvider clock;private List<Grant> grants=[];private readonly Dictionary<string,Invitation> invitations=[];
 public DeviceGrants(string directory,TimeProvider? timeProvider=null){clock=timeProvider??TimeProvider.System;path=Path.Combine(directory,"devices.protected");if(File.Exists(path))grants=JsonSerializer.Deserialize<List<Grant>>(Dpapi.Unprotect(File.ReadAllBytes(path)))??throw new InvalidDataException(UiText.C("manager.invalidDevicePermissions"));}
 private static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
 public string Invite(string name)=>CreateInvitation(name).Token;
 public CreatedInvitation CreateInvitation(string name){if(string.IsNullOrWhiteSpace(name)||name.Length>80||name.Any(char.IsControl))throw new ArgumentException(UiText.C("manager.deviceNameMustContain180ReadableCharacters"));lock(gate){var now=clock.GetUtcNow();foreach(var key in invitations.Where(p=>p.Value.ExpiresAt<=now).Select(p=>p.Key).ToArray())invitations.Remove(key);var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));var invite=new Invitation(Guid.NewGuid(),name.Trim(),now.AddMinutes(10));invitations[Hash(token)]=invite;return new(invite.Id,token,invite.ExpiresAt);}}
 public Lmu.Web.InvitationView Status(Guid id){lock(gate){var invite=invitations.Values.FirstOrDefault(i=>i.Id==id);return invite is null?new(id,clock.GetUtcNow(),"unavailable"):new(id,invite.ExpiresAt,invite.ExpiresAt<=clock.GetUtcNow()?"expired":invite.Used?"used":"active");}}
 public string? Redeem(string token){lock(gate){var hash=Hash(token);if(!invitations.TryGetValue(hash,out var invite)||invite.Used||invite.ExpiresAt<=clock.GetUtcNow())return null;invitations[hash]=invite with{Used=true};var access=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var now=clock.GetUtcNow();var grant=new Grant(Guid.NewGuid(),invite.Name,Hash(access),now,now.AddDays(30));grants.Add(grant);try{Save();return access;}catch{grants.Remove(grant);invitations.Remove(hash);throw;}}}
 public bool Authenticate(string token){if(token.Length!=64)return false;lock(gate){var hash=Hash(token);return grants.Any(g=>g.ExpiresAt>clock.GetUtcNow()&&CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(g.Hash),Encoding.ASCII.GetBytes(hash)));}}
 public Lmu.Web.DeviceView[] List(){lock(gate)return grants.Where(g=>g.ExpiresAt>clock.GetUtcNow()).Select(g=>new Lmu.Web.DeviceView(g.Id,g.Name,g.CreatedAt,g.ExpiresAt)).ToArray();}
 public void Revoke(Guid id){lock(gate){grants.RemoveAll(g=>g.Id==id);Save();}}
 public void ClearInvitations(){lock(gate)invitations.Clear();}
 public void RevokeAll(){lock(gate){grants.Clear();invitations.Clear();Save();}}
 private void Save()=>SettingsStore.Atomic(path,Dpapi.Protect(JsonSerializer.SerializeToUtf8Bytes(grants)));
}
