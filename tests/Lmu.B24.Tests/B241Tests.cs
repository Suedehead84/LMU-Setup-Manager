using System.Text.Json;
using System.IO.MemoryMappedFiles;
using Lmu.Diagnostics;
using Lmu.LiveCapture;
using Lmu.SetupLibrary;

static class B241Tests
{
 public static void Run(Action<bool,string> check,string vehicles,string tracks)
 {
  using var real=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/transitions.synthetic.json")));
  JsonElement Real(string name)=>real.RootElement.GetProperty("snapshots").EnumerateArray().Single(x=>x.GetProperty("label").GetString()==name);
  var esc=Real("ESC-frozen-start");var late=Real("ESC-frozen-late");var end=Real("ended-with-old-scoring");
  check(esc.GetProperty("et").GetDouble()==late.GetProperty("et").GetDouble()&&late.GetProperty("seconds").GetDouble()-esc.GetProperty("seconds").GetDouble()>60,"synthetic ESC freezes scoring over 60s while local player and realtime flags remain");
  check(end.GetProperty("count").GetInt32()==3&&end.GetProperty("events").EnumerateArray().Any(x=>x.GetInt32()==5),"synthetic unload retains old populated scoring; player presence cannot prove active weekend");
  var clock=new TestClock(DateTimeOffset.UtcNow);var engine=new LiveStateEngine(clock);var car=IdentityResolver.ReadVehicleCatalog(vehicles).Single(v=>v.Stem==esc.GetProperty("players")[0].GetProperty("file").GetString());
  double elapsed=135;int control=1;string track="SYNTHETIC Circuit Alpha",process="SYNTHETIC-b241";string file=car.Stem;
  Sample Scoring(bool frozen=false){var f=Decoder.Empty();foreach(var(k,v)in new(string,object?)[]{("GameRunning",true),("GameVersion",14200),("OptionsLocation",0),("PlayerControl",control),("PlayerSlotId",0),("PlayerVehicleFile",file),("RawTrackName",track),("SessionElapsed",elapsed),("SessionStart",54000f),("SessionCode",5),("Session","Qualifying"),("FixedSetup",false),("GamePhase",5)})f[k]=new(v,"SYNTHETIC transition; synthetic state reference");if(frozen)f["SessionSourceStatus"]=new("Frozen","SYNTHETIC publication stopped");return new(clock.Now,"LIVE",f,frozen?"Keine aktuelle native Scoring-Publikation":null);}
  void Observe(bool frozen=false){clock.Now=clock.Now.AddMilliseconds(100);engine.Observe(new(true,process,new(clock.Now,"LIVE",Decoder.Empty(),"SYNTHETIC unavailable telemetry"),Scoring(frozen)));}
  RestCapture[] Catalogs()=>[new(clock.Now,"http://127.0.0.1:6397/rest/sessions/getAllVehicles",200,vehicles,null,clock.Now),new(clock.Now,"http://127.0.0.1:6397/rest/sessions/getTracksAll",200,tracks,null,clock.Now)];
  void Bind(){for(int i=0;i<2;i++){elapsed+=.1;Observe();var t=engine.BeginRequest();engine.Complete(t,Catalogs());}}
  string NavBody(string nav="NAV_REALTIME",string game="GSTATE_DYN",string session="QUALIFY1",bool loading=false)=>JsonSerializer.Serialize(new{loadingStatus=new{loading},state=new{appBuild=14200,gamePhase="GREEN",gameSession=session,gameState=game,internalStateCode="OP_RAN_REALTIME_PASS",navigationState=nav}});
  void Nav(string? body=null){var t=engine.BeginNavigation();engine.CompleteNavigation(t,new(clock.Now,"http://127.0.0.1:6397/navigation/state",200,body??NavBody(),null,clock.Now));}
  Bind();var initial=engine.Status();var instance=initial.SessionIdentity!.InstanceId;check(initial.Key is not null&&instance is not null,"confirmed overview has separate runtime weekend instance");
  Observe();check(engine.Status().Key==initial.Key&&engine.Status().SessionIdentity!.InstanceId==instance,"overview to garage preserves same confirmed instance");
  control=0;elapsed+=1;Observe();Nav();check(engine.Status().Key==initial.Key,"garage to vehicle keeps native identity");
  bool stable=true;for(int i=0;i<750;i++){Observe(true);if(i%5==0)Nav();if(i%30==0){var t=engine.BeginRequest();engine.Complete(t,Catalogs());}if(i%5==0){var s=engine.Status();stable&=s.Key==initial.Key&&s.SessionIdentity!.InstanceId==instance&&!s.Fresh&&!s.CanCapture&&s.Values.TC is null;}}
  check(stable,"75s synthetic ESC scenario retains only proved identity with fresh context-bound REST; no live values/capture");
  elapsed+=.2;Observe();check(engine.Status().Key==initial.Key&&engine.Status().SessionIdentity!.InstanceId==instance,"ESC to vehicle resumes same instance without new matching");
  control=1;elapsed+=.2;Observe();check(engine.Status().Key==initial.Key,"ESC to garage preserves target identity");
  var oldNavigation=engine.BeginNavigation();elapsed+=.1;Observe();var changed=Scoring();changed.Fields["SessionSourceStatus"]=new("Ended","SYNTHETIC unload synthetic event");engine.Observe(new(true,process,new(clock.Now,"LIVE",Decoder.Empty(),"unloaded"),changed with{Error="Native Session-Ende/Unload-Publikation"}));
  engine.CompleteNavigation(oldNavigation,new(clock.Now,"http://127.0.0.1:6397/navigation/state",200,NavBody(),null,clock.Now));
  check(engine.Status().Key is null&&engine.Status().SessionIdentity is {Ended:true,InstanceId:null},"native unload clears identity and instance despite old successful navigation response");
  elapsed=12.2;control=1;Bind();var second=engine.Status();check(second.Key==initial.Key&&second.SessionIdentity!.InstanceId!=instance,"identical restarted weekend gets a new instance after two catalog confirmations");instance=second.SessionIdentity!.InstanceId;
  Nav(NavBody("NAV_MAIN_MENU","GSTATE_POST"));check(engine.Status().Key is null&&engine.Status().SessionIdentity!.Ended,"observed REST post-session main menu independently ends frozen binding");
  Observe();check(engine.Status().Key is null,"same retained native clock cannot undo proven REST end");
  elapsed=1;Bind();check(engine.Status().Key==initial.Key,"new advancing native session revalidates after explicit REST end");instance=engine.Status().SessionIdentity!.InstanceId;
  control=0;Observe();Nav();clock.Now=clock.Now.AddSeconds(3);Observe(true);check(engine.Status().Key is null,"stale REST navigation never extends frozen scoring");
  Bind();var beforeGap=engine.Status().SessionIdentity!.InstanceId;clock.Now=clock.Now.AddSeconds(3);check(engine.Status().Key is null,"no native read cannot be replaced by REST alone");Bind();check(engine.Status().SessionIdentity!.InstanceId==beforeGap,"short source outage and revalidation do not invent another weekend");
  for(int cycle=0;cycle<3;cycle++){
   Bind();var sameWeekend=engine.Status().SessionIdentity!.InstanceId;var delayed=engine.BeginRequest();clock.Now=clock.Now.AddMilliseconds(100);var missing=Scoring() with{Error="SYNTHETIC short scoring read interruption"};engine.Observe(new(true,process,new(clock.Now,"LIVE",Decoder.Empty(),"SYNTHETIC telemetry gap"),missing));
   check(engine.Status().Key is null&&!engine.Status().Fresh&&!engine.Status().CanCapture,"100ms native gap hides matching and capture conservatively "+cycle);
   elapsed+=.1;Observe();engine.Complete(delayed,Catalogs());check(engine.Status().Key is null,"pre-gap delayed REST cannot revalidate post-gap context "+cycle);
   var fresh=engine.BeginRequest();engine.Complete(fresh,Catalogs());check(engine.Status().Key is null,"one new post-gap batch is insufficient "+cycle);
   elapsed+=.1;Observe();fresh=engine.BeginRequest();engine.Complete(fresh,Catalogs());check(engine.Status().Key==initial.Key&&engine.Status().SessionIdentity!.InstanceId==sameWeekend,"two current batches restore same weekend without inventing a session "+cycle);
  }
  control=0;Observe();Nav(NavBody(session:"RACE1"));for(int i=0;i<25;i++)Observe(true);check(engine.Status().Key is null,"contradictory REST session code cannot retain frozen scoring");
  Bind();Observe();Nav(NavBody(loading:true));for(int i=0;i<25;i++)Observe(true);check(engine.Status().Key is null,"REST loading response cannot retain ESC identity");
  Bind();Observe();Nav("{}");for(int i=0;i<25;i++)Observe(true);check(engine.Status().Key is null,"unknown REST schema cannot retain frozen scoring");
  var unproved=new LiveStateEngine(clock);unproved.Observe(new(true,process,new(clock.Now,"LIVE",Decoder.Empty(),"paused"),Scoring(true)));var nt=unproved.BeginNavigation();unproved.CompleteNavigation(nt,new(clock.Now,"http://127.0.0.1:6397/navigation/state",200,NavBody(),null,clock.Now));for(int i=0;i<2;i++){var t=unproved.BeginRequest();unproved.Complete(t,Catalogs());}check(unproved.Status().Key is null,"starting manager with frozen scoring never invents a new confirmed identity from REST");
  Bind();track="SYNTHETIC Circuit Alpha Alternate";elapsed+=1;Observe();check(engine.Status().Key is null,"another layout cannot inherit paused identity");Bind();check(engine.Status().Key?.LayoutId=="SYNTHETIC_ALPHA_ALT","new layout requires exact two-batch confirmation");
  file="MISSING";Observe();Bind();check(engine.Status().Key is null,"another unknown vehicle cannot inherit setup identity");
  if(OperatingSystem.IsWindows()){
   string name="LMU_B241_SYNTHETIC_"+Guid.NewGuid().ToString("N");using var map=MemoryMappedFile.CreateNew(name,324824);using var view=map.CreateViewAccessor();view.Write(64,14200);view.Write(Layout.Scoring+Layout.S_mNumVehicles,18);view.Write(0,5);var source=new SharedMemorySource(name);var s=source.ReadSession(clock.Now);check(s.Error is not null&&s.Fields["SessionSourceStatus"].Value as string=="Ended","raw unload wins even when retained scoring has no valid player copy");view.Write(0,4);s=source.ReadSession(clock.Now);check(s.Fields["SessionSourceStatus"].Value as string=="Loading","raw load event cannot expose previous weekend scoring");view.Write(0,7);s=source.ReadSession(clock.Now);check(s.Fields["SessionSourceStatus"].Value as string=="SessionEnded","end-session clears binding without conflating it with full track unload");
  }
 }
 private sealed class TestClock(DateTimeOffset now):TimeProvider{public DateTimeOffset Now=now;public override DateTimeOffset GetUtcNow()=>Now;}
}
