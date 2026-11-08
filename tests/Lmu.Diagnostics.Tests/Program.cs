using System.Text.Json;
using Lmu.Diagnostics;
int count=0;
void Check(bool ok,string name) { if(!ok)throw new Exception("FAIL: "+name); Console.WriteLine("PASS "+name);count++; }
void Put(byte[] b,int offset,double value)=>BitConverter.GetBytes(value).CopyTo(b,offset);
Check(Layout.S_Size==548 && Layout.V_Size==584 && Layout.T_Size==1888,"ABI struct sizes");
Check(Layout.TelemStart+104*Layout.T_Size==Layout.MappingSize,"mapping boundaries");
Check(Decoder.Flag(0)==false && Decoder.Flag(1)==true && Decoder.Flag(2)==null,"tri-state flags");
Check(Decoder.Bias(.478)==52.2 && Decoder.Bias(double.NaN)==null && Decoder.Bias(1.1)==null,"rear fraction to front percent and invalid values");
Check(Decoder.Session(1)=="Training" && Decoder.Session(8)=="Qualifying" && Decoder.Session(10)=="Rennen" && Decoder.Session(99)=="unbekannt","session interpretation");
foreach(var (json,expected) in new (string,bool?)[]{("{}",null),("{\"fixedSetupRace\":false}",false),("{\"fixedSetupRace\":true}",true),("{\"fixedSetupRace\":0}",null),("{\"locked\":true}",null)})
{
    using var doc=JsonDocument.Parse(json);
    Check(Decoder.GarageFixed(doc.RootElement)==expected,"strict garage fixed "+json);
}
var s=new byte[548];var t=new byte[1888];var v=new byte[584];
s[Layout.S_mIsFixedSetup]=1;t[Layout.T_mTC]=3;t[Layout.T_mTCMax]=10;
Put(t,Layout.T_mRearBrakeBias,.478);Put(t,Layout.T_mElapsedTime,110);Put(t,Layout.T_mLapStartET,100);Put(v,Layout.V_mBestLapTime,-1);
var f=Decoder.Decode(s,t,v);
Check((int?)f["TC"].Value==3 && f["ABS"].Value is null,"unsupported electronics stays unavailable");
Check((double?)f["CurrentLap"].Value==10 && f["BestLap"].Value is null,"lap seconds and invalid best sentinel");
Check((bool?)f["FixedSetup"].Value==true && f["CarId"].Value is null,"explicit fixed and no invented stable IDs");
var tracker=new ChangeTracker();
var now=DateTimeOffset.UtcNow;
Check(tracker.Update(new(now,"TEST",f)).Any(),"initial baseline");
Check(!tracker.Update(new(now,"TEST",f)).Any(),"unchanged values");
t[Layout.T_mTC]=4;var changed=tracker.Update(new(now.AddMilliseconds(100),"TEST",Decoder.Decode(s,t,v))).ToArray();
Check(changed.Length==1 && changed[0].Field=="TC" && changed[0].Kind=="observed-change","single live setting change");
var lost=tracker.Update(new(now.AddSeconds(1),"TEST",Decoder.Empty())).ToArray();
Check(lost.Any(c=>c.Field=="FixedSetup" && c.After.Value is null),"disconnect clears fixed");
Check(tracker.Update(new(now.AddSeconds(2),"TEST",f)).All(c=>c.Kind=="availability-or-baseline"),"reconnect is baseline");
AbiEvidenceTests.Run(Check);
Check(SupportedAbi.IsSupported(14000) && SupportedAbi.IsSupported(14200), "explicit supported ABI list");
Check(new[]{0,-1,13999,14001,14100,14201,15000}.All(v=>!SupportedAbi.IsSupported(v)), "unknown ABI versions remain rejected");
if(OperatingSystem.IsWindows())
{
    var mappingName="LMU_PhaseA_SYNTHETIC_"+Guid.NewGuid().ToString("N");
    using var mapping=System.IO.MemoryMappedFiles.MemoryMappedFile.CreateNew(mappingName,324824);
    using var view=mapping.CreateViewAccessor();
    view.Write(64,14000);view.Write(Layout.Scoring+Layout.S_mNumVehicles,1);
    view.Write(Layout.Scoring+Layout.S_mIsFixedSetup,(byte)1);
    var reader=new SharedMemorySource(mappingName);
    Check((bool?)reader.Read(now).Fields["FixedSetup"].Value==true,"read-only named mapping adapter (SYNTHETIC)");
    Check(reader.Read(now.AddSeconds(3)).Fields["FixedSetup"].Value is null,"stale mapping fails closed");
    // Independent official offsets, deliberately not derived from Layout constants.
    view.Write(64,14200);
    view.Write(1632+68,20.0);
    view.Write(128464,(byte)2);view.Write(128465,(byte)1);view.Write(128466,(byte)1);
    view.Write(128468+1888+0,42);
    view.Write(128468+1888+12,110.0);view.Write(128468+1888+24,100.0);
    view.Write(128468+1888+664,.478);
    view.Write(128468+1888+745,(byte)1);
    view.Write(128468+1888+750,(byte)3);view.Write(128468+1888+751,(byte)10);
    view.Write(128468+1888+752,(byte)4);view.Write(128468+1888+753,(byte)10);
    view.Write(128468+1888+754,(byte)5);view.Write(128468+1888+755,(byte)10);
    view.Write(128468+1888+756,(byte)6);view.Write(128468+1888+757,(byte)10);
    view.Write(2192,42);view.Write(2192+196,(byte)1);
    view.Write(2192+144,95.5);
    var abi14200=reader.Read(now.AddSeconds(4));
    Check(abi14200.Error is null && (bool?)abi14200.Fields["FixedSetup"].Value==true,"14200 fixed flag through real mapping adapter (SYNTHETIC)");
    Check((int?)abi14200.Fields["TC"].Value==3 && (int?)abi14200.Fields["TCCut"].Value==5 &&
          (int?)abi14200.Fields["TCSlip"].Value==4 && (int?)abi14200.Fields["ABS"].Value==6,"14200 selected player slot electronics (SYNTHETIC)");
    Check((double?)abi14200.Fields["BrakeBias"].Value==52.2 && (double?)abi14200.Fields["CurrentLap"].Value==10 &&
          (double?)abi14200.Fields["BestLap"].Value==95.5 && (bool?)abi14200.Fields["LapInvalidated"].Value==true,"14200 bias, lap timing and scoring ID join (SYNTHETIC)");
    view.Write(1632+348,(byte)0);
    Check((bool?)reader.Read(now.AddSeconds(4.1)).Fields["FixedSetup"].Value==false,"14200 explicit open setup (SYNTHETIC)");
    view.Write(1632+348,(byte)2);
    Check(reader.Read(now.AddSeconds(4.2)).Fields["FixedSetup"].Value is null,"14200 malformed fixed flag remains unknown");
    view.Write(64,15000);
    Check(reader.Read(now).Fields["FixedSetup"].Value is null,"unknown build fails closed");
}
Check(Decoder.Decode(s,t,v,14000)["PlayerVehicleFile"].Value is null, "new identity field is gated to officially verified ABI 14200");
var invalidSession=new byte[548];
Put(invalidSession,Layout.S_mCurrentET,double.NaN);
BitConverter.GetBytes(float.NaN).CopyTo(invalidSession,Layout.S_mStartET);
var invalidContext=Decoder.Decode(invalidSession,t,v);
Check(invalidContext["SessionElapsed"].Value is null && invalidContext["SessionStart"].Value is null,"invalid native identity clocks are unavailable and JSON-safe");
await IdentityTests.Run(Check);
LapTests.RunTests(Check);
SyntheticReplayTests.Run(Check);
await DiagnosticLogTests.Run(Check);
Console.WriteLine($"All {count} assertions passed. Synthetic adapter and catalog fixtures; live evidence is documented separately.");
return 0;







