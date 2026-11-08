using System.Text;
using Lmu.Manager;
using Lmu.Web;
using Lmu.SetupLibrary;
using System.Net;

static class B251UiFixture
{
 public static async Task Run(string directory="artifacts/b251-ui",int port=5116,bool single=false){
  var root=Path.GetFullPath(directory);Directory.CreateDirectory(root);var store=new SettingsStore(Path.Combine(root,"config"));store.Save(new(false,false,port));var database=Path.Combine(root,"library.sqlite");var library=new SetupLibraryService(new LibraryDatabase(database));if(library.Vehicles().Count==0){library.RegisterVehicle(new("SYNTHETIC","SYNTHETIC vehicle"));library.RegisterTrack(new("SYNTHETIC","SYNTHETIC track"));library.RegisterLayout(new("SYNTHETIC","GP","SYNTHETIC GP"));library.CreateManual(new("SYNTHETIC","SYNTHETIC","GP"),new("SYNTHETIC UI setup",new(5,6,4,9,49)));}
  await using var manager=new ManagerController(store,database,new Networks(single),new AutoStart(),executablePath:Path.Combine(AppContext.BaseDirectory,"LMU Setup Manager.exe"));await manager.StartAsync();File.WriteAllBytes(Path.Combine(root,"access.protected"),Dpapi.Protect(Encoding.UTF8.GetBytes(manager.LocalUrl("settings"))));Console.WriteLine($"SYNTHETIC network UI fixture ready on loopback port {port}. Credential export encrypted; no real LAN, firewall or registry changes.");using var stop=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};try{await Task.Delay(Timeout.Infinite,stop.Token);}catch(OperationCanceledException){}
 }
 private sealed class Networks(bool single):IPrivateNetworks{public PrivateInterface[] Read()=>new PrivateInterface[]{new("SYNTHETIC-A",IPAddress.Parse("127.0.0.2"),8,"SYNTHETIC Ethernet A","11111111-1111-1111-1111-111111111111",0,"127.0.0.1","SYNTHETIC A","Ethernet",true),new("SYNTHETIC-B",IPAddress.Parse("127.0.0.3"),8,"SYNTHETIC Ethernet B","22222222-2222-2222-2222-222222222222",1,"127.0.0.1","SYNTHETIC B","Ethernet",true)}.Take(single?1:2).ToArray();}
 private sealed class AutoStart:IAutoStart{public bool Enabled{get;private set;}public void Set(bool enabled)=>Enabled=enabled;}
}
