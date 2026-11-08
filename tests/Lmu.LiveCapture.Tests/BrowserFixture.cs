using Lmu.Diagnostics;
using Lmu.LiveCapture;
using Lmu.Web;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using System.Net;
using System.Text;

// Test-only opt-in host. Never part of the published app. All native values are SYNTHETIC.
static class BrowserFixture
{
    public static async Task Run(bool localization=false)
    {
        var vehicles=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/vehicles.json"));
        var tracks=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/tracks.json"));
        var entry=IdentityResolver.ReadVehicleCatalog(vehicles).First(v=>v.Model=="SYNTHETIC Aster GT3");
        var engine=new LiveStateEngine();var transport=new FixtureTransport(vehicles,tracks);
        var directory=localization?"artifacts/b26-views":"artifacts/b241-ui";Directory.CreateDirectory(directory);var port=localization?5119:5111;var address=localization?"127.0.0.3":"127.0.0.1";
        var options=new ServerOptions(Path.GetFullPath(Path.Combine(directory,"synthetic.sqlite")),port,WebRoot:Path.GetFullPath("src/Lmu.Web/wwwroot")){ListenAddresses=[IPAddress.Parse(address)]};
        await using var app=WebHostFactory.Create(options,engine,transport,startLiveWorker:false);
        var control=new FixtureControl();
        app.MapPost("/api/fixture/state",(FixtureControl next)=>{Volatile.Write(ref control,next);return next;}); // Test project only, normal API access + CSRF protections apply.
        using var stop=new CancellationTokenSource();
        double elapsed=10;int ticks=0,weekend=1;
        var source=Task.Run(async()=>{
            try{while(!stop.IsCancellationRequested){
                var c=Volatile.Read(ref control);var now=DateTimeOffset.UtcNow;if(c.Weekend!=weekend){elapsed=10;weekend=c.Weekend;}if(!c.Frozen)elapsed+=.1;var fields=new Dictionary<string,Datum>();
                foreach(var (key,value) in new (string,object?)[]{("GameRunning",true),("GameVersion",14200),("InRealtime",true),("PlayerControl",0),("InGarage",false),("FixedSetup",false),("GamePhase",5),
                    ("RawVehicleModel",entry.Model),("RawTrackName",c.TrackName),("PlayerVehicleFile",entry.Stem),("PlayerSlotId",1),("SessionCode",5),("Session","SYNTHETIC Qualifying"),("SessionStart",0f),
                    ("TelemetryElapsed",elapsed),("SessionElapsed",elapsed),("TC",c.TC),("TCCut",c.TCCut),("TCSlip",7),("ABS",6),("BrakeBias",51.25),("TCMax",10),("TCCutMax",10),("TCSlipMax",10),("ABSMax",12)})fields[key]=new(value,"SYNTHETIC browser fixture");
                var scoring=new Dictionary<string,Datum>(fields);scoring["PlayerControl"]=new(c.Monitor?1:0,"SYNTHETIC");scoring["OptionsLocation"]=new(c.Monitor?0:3,"SYNTHETIC");
                fields["InRealtime"]=new(!c.Monitor&&!c.Ended,"SYNTHETIC");fields["InGarage"]=new(c.Monitor,"SYNTHETIC");
                if(c.Frozen||c.Ended)scoring["SessionSourceStatus"]=new(c.Ended?"Ended":"Frozen","SYNTHETIC");
                engine.Observe(new(c.Connected,"SYNTHETIC-browser-fixture",new(now,"LIVE",fields),new(now,"LIVE",scoring,c.Ended?"SYNTHETIC unload":c.Frozen?"SYNTHETIC frozen scoring":null)));
                if(ticks%5==0){var nt=engine.BeginNavigation();var navAt=DateTimeOffset.UtcNow;engine.CompleteNavigation(nt,new(navAt,"http://127.0.0.1:6397/navigation/state",200,System.Text.Json.JsonSerializer.Serialize(new{loadingStatus=new{loading=false},state=new{appBuild=14200,gamePhase="GREEN",gameSession="QUALIFY1",gameState=c.Ended?"GSTATE_SETUP":"GSTATE_DYN",internalStateCode="OP_RAN_REALTIME_PASS",navigationState=c.Ended?"NAV_MAIN_MENU":"NAV_REALTIME"}}),null,navAt));}
                if(ticks++%10==0){var ticket=engine.BeginRequest();engine.Complete(ticket,await transport.Read(stop.Token));}
                await Task.Delay(100,stop.Token);
            }}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        });
        try {
            await app.StartAsync();
            Console.WriteLine("SYNTHETIC UI FIXTURE ONLY. No LMU process or Shared Memory is read. Isolated database: "+options.DatabasePath);
            File.WriteAllBytes(Path.Combine(directory,"access.protected"),WindowsSecrets.Protect(Encoding.UTF8.GetBytes($"http://{address}:{port}/#access={options.AccessKey}")));
            Console.WriteLine($"SYNTHETIC browser fixture on loopback port {port}. Owner access exported with DPAPI only.");
            await app.WaitForShutdownAsync();
        }finally{stop.Cancel();await source;}
    }
    public sealed record FixtureControl(bool Monitor=false,bool Connected=true,string TrackName="SYNTHETIC Circuit Alpha",int TC=7,int TCCut=7,bool Frozen=false,bool Ended=false,int Weekend=1);
    private sealed class FixtureTransport(string vehicles,string tracks):ILmuCatalogTransport
    {
        public Task<RestCapture[]> Read(CancellationToken token){var now=DateTimeOffset.UtcNow;return Task.FromResult<RestCapture[]>([
            new(now,"http://127.0.0.1:6397/rest/sessions/getAllVehicles",200,vehicles,null,now),
            new(now,"http://127.0.0.1:6397/rest/sessions/getTracksAll",200,tracks,null,now)]);}
    }
}
