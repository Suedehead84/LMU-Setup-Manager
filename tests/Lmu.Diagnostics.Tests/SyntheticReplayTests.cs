using System.IO.Compression;
using System.Text.Json;
using Lmu.Diagnostics;
using Lmu.Replay;

internal static class SyntheticReplayTests
{
    // Independently authored two-second laps. No captured JSON or timing is reused.
    public static void Run(Action<bool,string> check)
    {
        RecordedLapRun Input(bool suppressed)
        {
            var rows=new List<RecordedSample>();
            for(int i=1;i<=48;i++)
            {
                int lap=i/20+1,completed=i/20;
                var values=new Dictionary<string,object?> {
                    ["GameVersion"]=14200,["InRealtime"]=true,["GamePhase"]=5,
                    ["TelemetryElapsed"]=i/10d,["LapStartET"]=(lap-1)*2d,["LapNumber"]=lap,
                    ["SessionCode"]=5,["SessionStart"]=0d,["PlayerSlotId"]=0,["PlayerControl"]=0,
                    ["RawVehicleModel"]="Synthetic car",["RawTrackName"]="Synthetic circuit",
                    ["PlayerVehicleFile"]="SYNTHETIC_ENTRY",["FixedSetup"]=true,
                    ["DriverName"]="Synthetic driver",["InGarage"]=false,["InPits"]=false,
                    ["TC"]=0,["TCCut"]=2,["TCSlip"]=3,["ABS"]=4,["BrakeBias"]=52.5,
                    ["LapInvalidated"]=false,["TrackLimitsSteps"]=0,
                    ["SessionElapsed"]=i/10d,["ScoringLapStartET"]=completed*2d,
                    ["CompletedLaps"]=completed,["LastLapTime"]=completed>0?2d:null,
                    ["CountLapFlag"]=suppressed&&i==27?1:2,["RawPitState"]=0,
                    ["VehicleModelId"]="SYNTHETIC_CAR",["VehicleModelName"]="Synthetic car",
                    ["TrackId"]="SYNTHETIC_TRACK",["TrackName"]="Synthetic circuit",
                    ["TrackLayoutId"]="SYNTHETIC_GP",["TrackLayoutName"]="Synthetic GP",
                    ["IdentityConfidence"]="Corroborated",["IdentitySource"]="SYNTHETIC test binding"
                };
                rows.Add(new(DateTimeOffset.UnixEpoch.AddMilliseconds(i*100),"LIVE",null,
                    values.ToDictionary(k=>k.Key,k=>JsonSerializer.SerializeToElement(k.Value)),i,"SYNTHETIC process"));
            }
            return new("SYNTHETIC-NOT-A-RECORDING",rows,[],[],[]);
        }
        var file=Path.Combine(Path.GetTempPath(),"lmu-synthetic-"+Guid.NewGuid()+".gz");
        try {
            using(var output=File.Create(file))using(var gzip=new GZipStream(output,CompressionMode.Compress))
                JsonSerializer.Serialize(gzip,Input(false));
            var loaded=RecordedLapRun.Load(file);
            check(loaded.Samples.Count==48&&loaded.SourceSha256=="SYNTHETIC-NOT-A-RECORDING","synthetic offline replay roundtrip preserves explicit provenance");
            var materialized=RecordedLapReplay.Materialize(loaded.Samples[0]);
            check(materialized.Fields["TC"].Value is int n&&n==0&&materialized.Fields["BrakeBias"].Value is double b&&b==52.5,"offline replay preserves zero electronics and floating bias types");
            var clean=RecordedLapReplay.Evaluate(loaded,recordedIdentities:true).CompletedLaps.Single(l=>l.LapNumber==2);
            check(clean.FullyObserved&&clean.TimingConfirmed&&clean.EligibleForSetupHistory,"synthetic complete replay traverses native boundary and scoring gates");
            var blocked=RecordedLapReplay.Evaluate(Input(true),recordedIdentities:true).CompletedLaps.Single(l=>l.LapNumber==2);
            check(blocked.TimeCountingSuppressionObserved&&!blocked.EligibleForSetupHistory&&blocked.Validity!="Valid","synthetic replay retains transient time suppression after flag returns to two");
            var unknown=RecordedLapReplay.Evaluate(loaded).CompletedLaps;
            check(unknown.All(l=>!l.EligibleForSetupHistory),"replay without identity proof never invents history eligibility");
        } finally {File.Delete(file);}
    }
}
