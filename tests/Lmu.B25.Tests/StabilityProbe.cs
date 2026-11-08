using System.Diagnostics;
using System.Text.Json;
using Lmu.Manager;

static class StabilityProbe
{
 public static void Run()
 {
  var store=new SettingsStore(SettingsStore.DefaultDirectory);var settings=store.Load();var provider=new WindowsPrivateNetworks();var current=provider.Read();
  int? grants=null;bool readable=true;try{grants=new DeviceGrants(store.DirectoryPath).List().Length;}catch{readable=false;}
  Console.WriteLine(JsonSerializer.Serialize(new{Scope="default-settings-read-only",SavedLanEnabled=settings.LanEnabled,HasApproval=settings.Network is not null,ApprovalMatchesCurrent=NetworkAuthorization.Select(settings,current) is not null,Candidates=current.Length,Eligible=current.Count(n=>n.Eligible),DiscoveryFailed=provider.LastError is not null,ProtectedGrantsReadable=readable,ProtectedGrantCount=grants}));
  // Warm-up occurs above. Cold-provider scans reproduce the previous uncached WMI path.
  foreach(bool cached in new[]{false,true}){
   var shared=new WindowsPrivateNetworks();var times=new List<double>();int failures=0,changes=0;string? fingerprint=null;using var process=Process.GetCurrentProcess();var cpu=process.TotalProcessorTime;var wall=Stopwatch.StartNew();
   for(int i=0;i<10;i++){var p=cached?shared:new WindowsPrivateNetworks();var watch=Stopwatch.StartNew();var result=p.Read();times.Add(watch.Elapsed.TotalMilliseconds);if(p.LastError is not null)failures++;var next=string.Join("|",result.Select(n=>n.Fingerprint));if(fingerprint is not null&&next!=fingerprint)changes++;fingerprint=next;}
   times.Sort();Console.WriteLine(JsonSerializer.Serialize(new{Mode=cached?"bounded-hardware-cache":"uncached-hardware-baseline",Samples=times.Count,WallMilliseconds=wall.Elapsed.TotalMilliseconds,CpuMilliseconds=(process.TotalProcessorTime-cpu).TotalMilliseconds,MedianMilliseconds=times[times.Count/2],MaxMilliseconds=times.Max(),DiscoveryFailures=failures,ConsecutiveFingerprintChanges=changes}));
  }
 }
}
