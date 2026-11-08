using Lmu.Localization;
using System.Text;
using System.Text.Json;
namespace Lmu.Diagnostics;
public sealed record Datum(object? Value, string Source, string? Unit = null, string? Note = null);
public sealed record Sample(DateTimeOffset ObservedAt, string Mode, Dictionary<string, Datum> Fields, string? Error = null);
public interface ITelemetrySource { Sample Read(DateTimeOffset now); }
public static class Decoder
{
    public static int I(byte[] b, int o) => BitConverter.ToInt32(b, o);
    public static double D(byte[] b, int o) => BitConverter.ToDouble(b, o);
    public static string Text(byte[] b, int o, int n) => Encoding.UTF8.GetString(b, o, n).Split('\0')[0];
    public static bool? Flag(byte b) => b switch { 0 => false, 1 => true, _ => null };
    public static string Session(int n) => n switch { 0 => "Testday", >=1 and <=4 => "Training", >=5 and <=8 => "Qualifying", 9 => "Warmup", >=10 and <=13 => "Rennen", _ => "unbekannt" };
    public static double? Bias(double rear) => double.IsFinite(rear) && rear >= 0 && rear <= 1 ? Math.Round((1-rear)*100, 4) : null;
    public static Dictionary<string, Datum> Empty() => new[] { "CarName","CarId","RawTrackName","TrackName","LayoutId","RawVehicleModel","PlayerVehicleFile","SessionCode","SessionElapsed","SessionStart","VehicleModelId","VehicleModelName","TrackId","TrackLayoutId","TrackLayoutName","IdentitySource","IdentityConfidence","Session","FixedSetup","TC","TCCut","TCSlip","ABS","BrakeBias","CurrentLap","BestLap","LapInvalidated","TrackLimitsSteps","Location","GameVersion","TelemetryElapsed","LapNumber","LapStartET","CompletedLaps","ScoringLapStartET","LastLapTime","CountLapFlag","InPits","InGarage","RawPitState","PlayerControl","DriverName","DriverSteamId","InRealtime","GamePhase" }.ToDictionary(k=>k, _=>new Datum(null,"none",Note:UiText.C("diagnostics.unavailable")));
    public static Dictionary<string, Datum> Decode(byte[] s, byte[]? t, byte[]? v, int abiVersion = 14200)
    {
        var f=Empty();
        void Set(string key, object? value, string source, string? unit=null, string? note=null) => f[key]=new(value, source, unit,note);
        Set("GameVersion",abiVersion,"LMU_Data.gameVersion");
        Set("InRealtime",Flag(s[Layout.S_mInRealtime]),"LMU_Data.scoring");
        Set("GamePhase",s[Layout.S_mGamePhase],"LMU_Data.scoring");
        Set("RawTrackName", Text(s,Layout.S_mTrackName,64),"LMU_Data.scoring");
        Set("SessionCode", I(s,Layout.S_mSession),"LMU_Data.scoring");
        var elapsed=D(s,Layout.S_mCurrentET);
        Set("SessionElapsed", double.IsFinite(elapsed) ? elapsed : null,"LMU_Data.scoring","s");
        var start=BitConverter.ToSingle(s,Layout.S_mStartET);
        Set("SessionStart", float.IsFinite(start) ? start : null,"LMU_Data.scoring","s");
        Set("Session",Session(I(s,Layout.S_mSession)),"LMU_Data.scoring");
        Set("FixedSetup",Flag(s[Layout.S_mIsFixedSetup]),"LMU_Data.mIsFixedSetup");
        Set("TrackLimitsStepsPerPenalty",s[Layout.S_mTrackLimitsStepsPerPenalty],"LMU_Data.scoring");
        Set("TrackLimitsStepsPerPoint",s[Layout.S_mTrackLimitsStepsPerPoint],"LMU_Data.scoring");
        if(t is null) return f;
        Set("CarName",Text(t,Layout.T_mVehicleName,64),"LMU_Data.telemetry",note:UiText.C("diagnostics.vehicleEntryTeamNeverAModelKey"));
        Set("RawVehicleModel",Text(t,Layout.T_mVehicleModel,30),"LMU_Data.mVehicleModel");
        var physics=D(t,Layout.T_mElapsedTime);
        var lapStart=D(t,Layout.T_mLapStartET);
        Set("TelemetryElapsed",double.IsFinite(physics) ? physics : null,"LMU_Data.telemetry","s");
        Set("LapStartET",double.IsFinite(lapStart) ? lapStart : null,"LMU_Data.telemetry","s");
        Set("LapNumber",I(t,Layout.T_mLapNumber),"LMU_Data.telemetry",note:UiText.C("diagnostics.nativeNumber"));
        Set("PlayerSlotId",I(t,Layout.T_mID),"LMU_Data",note:UiText.C("diagnostics.sessionIdOnlyNotAStableVehicleId"));
        foreach(var (key,offset,maxOffset) in new[] { ("TC",Layout.T_mTC,Layout.T_mTCMax),("TCCut",Layout.T_mTCCut,Layout.T_mTCCutMax),("TCSlip",Layout.T_mTCSlip,Layout.T_mTCSlipMax),("ABS",Layout.T_mABS,Layout.T_mABSMax) })
        {
            int value=t[offset], max=t[maxOffset];
            Set(key+"Max",max,"LMU_Data.telemetry","Stufe",UiText.C("diagnostics.nativeLimit0RemainsUndefinedUnsupported"));
            Set(key,max>0 && value<=max ? value : null,"LMU_Data.telemetry","Stufe",max==0 ? UiText.C("diagnostics.max0UnsupportedOrUndefined") : UiText.C("diagnostics.maximum",(max)));
        }
        Set("BrakeBias",Bias(D(t,Layout.T_mRearBrakeBias)),"LMU_Data.mRearBrakeBias",UiText.C("diagnostics.frontPercent"));
        Set("LapInvalidated",Flag(t[Layout.T_mLapInvalidated]),"LMU_Data.telemetry");
        Set("RawLapInvalidated",t[Layout.T_mLapInvalidated],"LMU_Data.telemetry",note:UiText.C("diagnostics.rawByte"));
        Set("TrackLimitsSteps",t[Layout.T_mTrackLimitsSteps],"LMU_Data.telemetry","Schritte",UiText.C("diagnostics.notEquatedWithPenalties"));
        var lap=D(t,Layout.T_mElapsedTime)-D(t,Layout.T_mLapStartET);
        Set("CurrentLap",double.IsFinite(lap)&&lap>=0&&lapStart>=0 ? lap:null,"LMU_Data.telemetry","s");
        if(v is not null)
        {
            if(abiVersion==14200) Set("PlayerVehicleFile",Text(v,Layout.V_mVehFilename,32),"LMU_Data.mVehFilename",note:UiText.C("diagnostics.technicalBindingToRestOnlyNotAModel"));
            Set("CompletedLaps",(int)BitConverter.ToInt16(v,Layout.V_mTotalLaps),"LMU_Data.scoring");
            var scoredStart=D(v,Layout.V_mLapStartET);
            Set("ScoringLapStartET",double.IsFinite(scoredStart) ? scoredStart : null,"LMU_Data.scoring","s");
            var last=D(v,Layout.V_mLastLapTime);
            Set("RawLastLapTime",double.IsFinite(last) ? last:null,"LMU_Data.scoring","s",UiText.C("diagnostics.unfilteredValue"));
            foreach(var (key,offset) in new[] {("RawLastSector1",Layout.V_mLastSector1),("RawLastSector2",Layout.V_mLastSector2),
                ("RawCurrentSector1",Layout.V_mCurSector1),("RawCurrentSector2",Layout.V_mCurSector2)}) {
                var raw=D(v,offset);Set(key,double.IsFinite(raw) ? raw:null,"LMU_Data.scoring","s",UiText.C("diagnostics.sentinelsRetained"));
            }
            Set("OutstandingPenalties",(int)BitConverter.ToInt16(v,Layout.V_mNumPenalties),"LMU_Data.scoring");
            Set("LastLapTime",double.IsFinite(last)&&last>0 ? last : null,"LMU_Data.scoring","s");
            Set("CountLapFlag",v[Layout.V_mCountLapFlag]<=2 ? (int)v[Layout.V_mCountLapFlag] : null,"LMU_Data.scoring",note:UiText.C("diagnostics.0CountNeitherLapNorTime1Count"));
            Set("InPits",Flag(v[Layout.V_mInPits]),"LMU_Data.scoring");
            Set("InGarage",Flag(v[Layout.V_mInGarageStall]),"LMU_Data.scoring");
            Set("RawPitState",(int)v[Layout.V_mPitState],"LMU_Data.scoring",note:UiText.C("diagnostics.rawValueSdkComment04ConflictsWith"));
            Set("PlayerControl",(int)unchecked((sbyte)v[Layout.V_mControl]),"LMU_Data.scoring");
            Set("DriverName",Text(v,Layout.V_mDriverName,32),"LMU_Data.scoring");
            if(abiVersion==14200) Set("DriverSteamId",BitConverter.ToUInt64(v,Layout.V_mSteamID).ToString(System.Globalization.CultureInfo.InvariantCulture),"LMU_Data.mSteamID");
            double best=D(v,Layout.V_mBestLapTime);
            Set("BestLap",double.IsFinite(best)&&best>0?best:null,"LMU_Data.scoring","s",UiText.C("diagnostics.nativeTimingCandidate"));
            Set("Location",v[Layout.V_mInGarageStall]==1 ? "Garage" : v[Layout.V_mInPits]==1 ? "Boxengasse" : UiText.C("diagnostics.track"),"LMU_Data.scoring");
        }
        return f;
    }
    // Only the exact known garage-summary property is read; never infer fixed from setup locks.
    public static bool? GarageFixed(JsonElement root) => root.ValueKind==JsonValueKind.Object && root.TryGetProperty("fixedSetupRace",out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : null;
}
public sealed record Change(DateTimeOffset ObservedAt,string Field, Datum? Before,Datum After,string Kind);
public sealed class ChangeTracker
{
    private Dictionary<string,Datum>? previous;
    public IEnumerable<Change> Update(Sample sample)
    {
        var changes = new List<Change>();
        foreach(var (key,value) in sample.Fields)
        {
            Datum? old=null; previous?.TryGetValue(key,out old);
            if(previous is null || old != value)
                changes.Add(new(sample.ObservedAt,key,old,value,old?.Value is null || value.Value is null ? "availability-or-baseline" : "observed-change"));
        }
        previous=new(sample.Fields);
        return changes;
    }
}







