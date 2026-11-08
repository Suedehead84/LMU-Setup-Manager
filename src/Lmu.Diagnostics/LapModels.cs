using Lmu.Localization;
using System.Globalization;
using System.Text.Json;
namespace Lmu.Diagnostics;

public sealed record SetupSnapshot(int? TC,int? TCCut,int? TCSlip,int? ABS,double? BrakeBias)
{
    // Max=0 in native telemetry does not prove that an aid is irrelevant.
    public bool AllRelevantValuesAvailable => TC is >=0 and <=255 && TCCut is >=0 and <=255 && TCSlip is >=0 and <=255 &&
        ABS is >=0 and <=255 && BrakeBias is { } bias && double.IsFinite(bias) && bias is >=0 and <=100;
    public IEnumerable<(string Field,object? Before,object? After)> Differences(SetupSnapshot next)
    {
        foreach(var d in new (string,object?,object?)[] {
            ("TC",TC,next.TC),("TCCut",TCCut,next.TCCut),("TCSlip",TCSlip,next.TCSlip),
            ("ABS",ABS,next.ABS),("BrakeBias",BrakeBias,next.BrakeBias)})
            if(!Equals(d.Item2,d.Item3)) yield return d;
    }
}
public sealed record LapSetupChange(DateTimeOffset ObservedAt,double TelemetryElapsed,string Field,
    object? Before,object? After,string Kind,DateTimeOffset? PreviousObservedAt=null,double? PreviousTelemetryElapsed=null,bool ContinuityProven=true);
public sealed record LapBoundarySetupObservation(DateTimeOffset ObservedAt,SetupSnapshot Before,SetupSnapshot After,string Reason);
public sealed record LapCountLapFlagObservation(DateTimeOffset ObservedAt,double TelemetryElapsed,double ScoringElapsed,
    double ScoringLapStartET,int? Flag);
public sealed record LapScoring(double UpdatedET,double LapStartET,int CompletedLaps,double? LastLapTime,int? CountLapFlag,
    bool? InPits,bool? InGarage,int? PitState,int? PlayerControl);

public sealed record LapFrame(DateTimeOffset ObservedAt,double Elapsed,int LapNumber,double LapStartET,
    string Context,SetupIdentityKey? Identity,bool? FixedSetup,string? DriverToken,SetupSnapshot Setup,
    bool? Invalidated,int? TrackLimitsSteps,LapScoring? Scoring,string? NativeContext=null)
{
    public bool IsPreTimingAnchor => LapNumber==0 && LapStartET == -1;
    public static LapFrame? From(Sample sample,SessionIdentity identity,string processInstance,out string reason)
    {
        object? Value(string key)=>sample.Fields.GetValueOrDefault(key)?.Value;
        string? Text(string key)=>Value(key) is string v && !string.IsNullOrWhiteSpace(v) ? v:null;
        int? Int(string key)=>Value(key) switch {int v=>v,byte v=>v,short v=>v,sbyte v=>v,_=>null};
        double? Number(string key)=>Value(key) switch {
            double d when double.IsFinite(d)=>d,float f when float.IsFinite(f)=>f,_=>null };
        bool? Bool(string key)=>Value(key) is bool b ? b:null;
        reason=sample.Error??UiText.C("diagnostics.incompleteLapTelemetry");
        if(sample.Mode!="LIVE") {reason=UiText.C("diagnostics.notARealLiveSource");return null;}
        if(Int("GameVersion")!=14200) {reason=UiText.C("diagnostics.lapAnalysisRequiresTheOfficiallyVerifiedAbi14200");return null;}
        if(sample.Error is not null || string.IsNullOrEmpty(processInstance)) return null;
        if(Bool("InRealtime")!=true || Int("GamePhase") is null or 0 or 7 or 8 or 9)
        {reason=UiText.C("diagnostics.notInRealTimeOrPaused");return null;}
        if(Number("TelemetryElapsed") is not { } elapsed || Number("LapStartET") is not { } start ||
            elapsed<0 || start>elapsed+.001 || Int("LapNumber") is not { } lap || lap<0 ||
            (start<0 && !(lap==0 && start == -1)) ||
            Int("SessionCode") is not { } session || Number("SessionStart") is not { } sessionStart ||
            Int("PlayerSlotId") is not { } slot || Text("RawVehicleModel") is not { } model ||
            Text("RawTrackName") is not { } track || Text("PlayerVehicleFile") is not { } file) return null;
        var control=Int("PlayerControl");
        if(control!=0) {reason=UiText.C("diagnostics.noLocallyControlledPlayerVehicleAiReplayRemote");return null;}
        if(Bool("InGarage")==true) {reason=UiText.C("diagnostics.playerInGarage");return null;}
        string? driver=Text("DriverSteamId") is { } steam && steam!="0" ? "steam:"+steam :
            Text("DriverName") is { } driverName ? "profile:"+driverName:null;
        var fixedSetup=Bool("FixedSetup");
        // This is an observed context, never an invented LMU session ID.
        string nativeContext=JsonSerializer.Serialize(new object?[]{processInstance,session,sessionStart,slot,model,track,file,driver,fixedSetup});
        string context=JsonSerializer.Serialize(new object?[]{nativeContext,identity.SetupKey});
        LapScoring? scoring=null;
        if(Number("SessionElapsed") is { } scoringET && Number("ScoringLapStartET") is { } scoringStart &&
            Int("CompletedLaps") is { } total)
            scoring=new(scoringET,scoringStart,total,Number("LastLapTime"),Int("CountLapFlag"),
                Bool("InPits"),Bool("InGarage"),Int("RawPitState"),control);
        return new(sample.ObservedAt,elapsed,lap,start,context,identity.SetupKey,fixedSetup,driver,
            new(Int("TC"),Int("TCCut"),Int("TCSlip"),Int("ABS"),Number("BrakeBias")),
            Bool("LapInvalidated"),Int("TrackLimitsSteps"),scoring,nativeContext);
    }
}
public sealed record LapRecord
{
    public required string LapId {get;init;}
    public required string ObservationSessionId {get;init;}
    public string? SessionId {get;init;} // No reliably scoped official session ID has been found.
    public string? VehicleModelId {get;init;}
    public string? TrackId {get;init;}
    public string? TrackLayoutId {get;init;}
    public bool? FixedSetup {get;init;}
    public int LapNumber {get;init;}
    public required string Status {get;init;}
    public required string LapKind {get;init;}
    public required DateTimeOffset FirstObservedAt {get;init;}
    public required DateTimeOffset LastObservedAt {get;init;}
    public DateTimeOffset FinalizedAt {get;init;}
    public DateTimeOffset? EndBoundaryObservedAt {get;init;}
    public double LapStartET {get;init;}
    public double? LapEndET {get;init;}
    public double? LapTimeSeconds {get;init;}
    public string? LapTimeSource {get;init;}
    public double? ReportedLastLapTimeSeconds {get;init;}
    public double? DerivedStartToStartSeconds {get;init;}
    public bool TimingConfirmed {get;init;}
    public required string Validity {get;init;}
    public required string ValidityEvidence {get;init;}
    public string? InvalidationCause {get;init;}
    public bool FullyObserved {get;init;}
    // Separate telemetry continuity from the historical combined FullyObserved/timing gate.
    public bool NativeEndBoundaryObserved {get;init;}
    public bool TelemetryFullyObserved {get;init;}
    public bool TimeCountingSuppressionObserved {get;init;}
    public bool TimeCountingContinuous {get;init;}
    public IReadOnlyList<LapCountLapFlagObservation> CountLapFlagObservations {get;init;}=[];
    public bool AllRelevantElectronicsAvailable {get;init;}
    public bool ScoringCoverageComplete {get;init;}
    public bool PitCoverageComplete {get;init;}
    public bool SetupChanged {get;init;}
    public bool SegmentSetupChanged {get;init;}
    public string? NativeLapObservationId {get;init;}
    public bool NativeLapHadObservationInterruption {get;init;}
    public IReadOnlyList<string> ObservedSegmentIds {get;init;}=[];
    public IReadOnlyList<LapSetupChange> KnownSetupChangesInNativeLap {get;init;}=[];
    public int? LastObservedCountLapFlag {get;init;}
    public bool ScoringBoundaryMatched {get;init;}
    public bool MatchedScoringTimeMissing {get;init;}
    public bool BoundarySetupUncertain {get;init;}
    public bool ConstantSetup {get;init;}
    public bool EligibleForSetupHistory {get;init;}
    public required SetupSnapshot SetupSnapshot {get;init;}
    public required SetupSnapshot LastObservedSetup {get;init;}
    public required IReadOnlyList<LapSetupChange> SetupChanges {get;init;}
    public required IReadOnlyList<LapBoundarySetupObservation> BoundarySetupObservations {get;init;}
    public int? MaximumObservedTrackLimitsSteps {get;init;}
    public required IReadOnlyList<string> Limitations {get;init;}
}
public sealed record LapEvent(string Type,DateTimeOffset ObservedAt,string? LapId=null,object? Details=null,LapRecord? Lap=null);







