using Lmu.Localization;
namespace Lmu.Diagnostics;

/// <summary>
/// Pure observation state machine. Native telemetry boundaries and independently paced scoring
/// are joined by start ET and completed-lap count. No persistent setup history is stored here.
/// </summary>
public sealed partial class LapAnalyzer
{
    public const double MaximumTelemetryGapSeconds=.5;
    public const double ScoringWaitSeconds=2;
    public const double MaximumScoringAgeSeconds=.75;
    public const double TimingToleranceSeconds=.25;
    private const double Epsilon=.000001;
    private readonly string runId=Guid.NewGuid().ToString("N");
    private long period,sequence;
    private string? context;
    private LapFrame? previous;
    private DateTimeOffset lastPhysicsProgress;
    private double? stalledAt;
    private bool interruptionReported;
    private ObservedLap? active;
    private readonly List<PendingLap> pending=[];
    private double lastScoringET=double.NegativeInfinity;
    private LapScoring? cachedScoring;

    private sealed class ObservedLap(LapFrame first,bool beginningObserved,string id,string session,Episode episode)
    {
        public Episode Episode {get;}=episode;
        public LapFrame First {get;}=first;
        public string Id {get;}=id;
        public string Session {get;}=session;
        public bool BeginningObserved {get;}=beginningObserved;
        public LapFrame Last {get;set;}=first;
        public SetupSnapshot LastSetup {get;set;}=first.Setup;
        public bool SetupCoverage {get;set;}=first.Setup.AllRelevantValuesAvailable;
        public bool BoundaryUncertain {get;set;}
        public bool InvalidSeen {get;set;}
        public bool ValidityCoverage {get;set;}=true;
        public bool CountingCoverage {get;set;}=true;
        public bool CountSeen {get;set;}
        public int? LastCountFlag {get;set;}
        public bool TimeCountingSuppressionObserved {get;set;}
        public List<LapCountLapFlagObservation> CountFlagObservations {get;}=[];
        public bool ScoringCoverage {get;set;}=true;
        public bool PitCoverage {get;set;}=true;
        public int? OldCompletedLaps {get;set;}
        public int? MaximumTrackLimits {get;set;}
        public bool SawExit {get;set;}
        public bool SawEntry {get;set;}
        public bool SawPits {get;set;}
        public bool? LastPit {get;set;}
        public List<LapSetupChange> Changes {get;}=[];
        public List<string> Notes {get;}=[];
        public List<LapBoundarySetupObservation> BoundaryObservations {get;}=[];
    }
    private sealed class PendingLap(ObservedLap lap,double end,DateTimeOffset observedAt)
    {
        public ObservedLap Lap {get;}=lap;
        public double End {get;}=end;
        public DateTimeOffset EndObservedAt {get;}=observedAt;
        public bool BoundaryValidityUncertain {get;set;}
        public double? CandidateTime {get;set;}
        public double CandidateScoringET {get;set;}
        public double? ReportedTime {get;set;}
        public bool ScoringMatched {get;set;}
        public bool MatchedTimeMissing {get;set;}
    }

    public IReadOnlyList<LapEvent> Update(Sample sample,SessionIdentity identity,string processInstance)
    {
        var frame=LapFrame.From(sample,identity,processInstance,out var reason);
        return frame is null ? Interrupt(sample.ObservedAt,reason) : Observe(frame);
    }
    public IReadOnlyList<LapEvent> Interrupt(DateTimeOffset now,string reason)
    {
        var events=new List<LapEvent>();
        if(!interruptionReported)
            events.Add(new("telemetry-interruption",now,active?.Id,new {Reason=reason}));
        interruptionReported=true;
        AbortActive(now,reason,events);
        FlushPending(now,UiText.C("diagnostics.observationInterrupted")+reason,events);
        previous=null;context=null;stalledAt=null;lastScoringET=double.NegativeInfinity;cachedScoring=null;
        return events;
    }
    public IReadOnlyList<LapEvent> Stop(DateTimeOffset now)
    {
        var events=new List<LapEvent>();
        AbortActive(now,UiText.C("diagnostics.observationEnded"),events);
        FlushPending(now,UiText.C("diagnostics.observationEndedBeforeFinalScoringConfirmation"),events);
        previous=null;context=null;stalledAt=null;cachedScoring=null;lastScoringET=double.NegativeInfinity;
        interruptionReported=true;
        return events;
    }

    public IReadOnlyList<LapEvent> Observe(LapFrame frame)
    {
        var events=new List<LapEvent>();
        if(!double.IsFinite(frame.Elapsed) || !double.IsFinite(frame.LapStartET) ||
            (frame.LapStartET<0 && !frame.IsPreTimingAnchor) || frame.Elapsed<frame.LapStartET || frame.LapNumber<0)
            return Interrupt(frame.ObservedAt,UiText.C("diagnostics.invalidNativeLapMarkers"));
        if(frame.Scoring is { } guard && (guard.InGarage==true || guard.PlayerControl is not 0))
            return Interrupt(frame.ObservedAt,UiText.C("diagnostics.garageOrNoLocalPlayerControl"));
        if(previous is not null)
        {
            double wallGap=(frame.ObservedAt-previous.ObservedAt).TotalSeconds;
            double physicsGap=frame.Elapsed-previous.Elapsed;
            bool gap=wallGap<0 || wallGap>MaximumTelemetryGapSeconds || physicsGap>MaximumTelemetryGapSeconds;
            if(gap)
            {
                var interrupted=Interrupt(frame.ObservedAt,UiText.C("diagnostics.telemetryGapLongerThan05SOr"));
                foreach(var item in interrupted)events.Add(item.Type=="telemetry-interruption" ? item with {Details=new {
                    Reason=UiText.C("diagnostics.telemetryGapLongerThan05SOr"),WallGapSeconds=wallGap,
                    PhysicsGapSeconds=physicsGap,PreviousObservedAt=frame.ObservedAt.AddSeconds(-wallGap),
                    MaximumTelemetryGapSeconds }} : item);
            }
            else if(frame.Elapsed==previous.Elapsed &&
                (frame.ObservedAt-lastPhysicsProgress).TotalSeconds>MaximumTelemetryGapSeconds)
            {
                events.AddRange(Interrupt(frame.ObservedAt,UiText.C("diagnostics.physicsFrozen")));
                context=frame.Context;stalledAt=frame.Elapsed;
                return events;
            }
        }
        bool changed=(context is not null && context!=frame.Context) ||
            (previous is not null && (previous.FixedSetup!=frame.FixedSetup ||
                previous.Identity!=frame.Identity || previous.DriverToken!=frame.DriverToken));
        bool rewind=previous is not null && (frame.Elapsed<previous.Elapsed ||
            frame.LapNumber<previous.LapNumber || (frame.IsPreTimingAnchor && !previous.IsPreTimingAnchor));
        if(changed || rewind)
        {
            AbortActive(frame.ObservedAt,changed ? UiText.C("diagnostics.sessionDriverVehicleLayoutOrFixedContextChanged") : UiText.C("diagnostics.sessionClockOrLapNumberReset"),events);
            FlushPending(frame.ObservedAt,UiText.C("diagnostics.contextSessionChangedBeforeScoringWasConfirmed"),events);
            previous=null;context=null;stalledAt=null;lastScoringET=double.NegativeInfinity;cachedScoring=null;
        }
        if(stalledAt is { } frozen && context==frame.Context && frame.Elapsed==frozen)
            return events;
        if(stalledAt is not null) context=null;
        stalledAt=null;
        if(context is null)
        {
            context=frame.Context;period++;lastPhysicsProgress=frame.ObservedAt;
        }
        interruptionReported=false;
        if(previous is null || frame.Elapsed>previous.Elapsed) lastPhysicsProgress=frame.ObservedAt;
        if(frame.Scoring is { } incoming && double.IsFinite(incoming.UpdatedET) &&
            double.IsFinite(incoming.LapStartET) && incoming.UpdatedET>=lastScoringET &&
            incoming.UpdatedET<=frame.Elapsed+.5)
        { cachedScoring=incoming;lastScoringET=incoming.UpdatedET; }
        bool scoringUsable=cachedScoring is { } accepted &&
            frame.Elapsed-accepted.UpdatedET<=MaximumScoringAgeSeconds &&
            accepted.UpdatedET<=frame.Elapsed+.5;
        frame=frame with {Scoring=scoringUsable ? cachedScoring:null};
        if(frame.IsPreTimingAnchor)
        {
            // Observed in the real GT3 trace: timed laps have not begun, not a corrupt timestamp.
            // Keep only a continuity anchor. Never invent an outlap time or completed lap.
            if(previous?.IsPreTimingAnchor!=true)
                events.Add(new("lap-awaiting-first-boundary",frame.ObservedAt,Details:new {
                    frame.LapNumber,frame.LapStartET,Reason=UiText.C("diagnostics.nativeLap0WithStartTime1Prepare") }));
            previous=frame;
            return events;
        }
        if(active is null)
        {
            bool firstBoundary=previous?.IsPreTimingAnchor==true && frame.LapNumber==1 &&
                frame.LapStartET>=previous.Elapsed-Epsilon && frame.LapStartET<=frame.Elapsed+Epsilon;
            active=Start(frame,firstBoundary,events);
            if(firstBoundary && (previous!.Setup!=frame.Setup ||
                !previous.Setup.AllRelevantValuesAvailable || !frame.Setup.AllRelevantValuesAvailable))
            {
                active.BoundaryUncertain=true;
                var boundary=new LapBoundarySetupObservation(frame.ObservedAt,previous.Setup,frame.Setup,
                    UiText.C("diagnostics.setupSamplingIntervalOverlapsTheFirstDetectedLap"));
                active.BoundaryObservations.Add(boundary);
                events.Add(new("setup-change-at-boundary",frame.ObservedAt,active.Id,boundary));
            }
        }        else if(frame.LapNumber==active.First.LapNumber && Near(frame.LapStartET,active.First.LapStartET))
        {
            ObserveWithin(active,frame,scoringUsable,events);
        }
        else
        {
            bool sequential=previous is not null && frame.LapNumber==active.First.LapNumber+1 &&
                frame.LapStartET>active.First.LapStartET &&
                frame.LapStartET>=previous.Elapsed-Epsilon && frame.LapStartET<=frame.Elapsed+Epsilon;
            if(sequential)
            {
                var closing=new PendingLap(active,frame.LapStartET,frame.ObservedAt) {
                    BoundaryValidityUncertain=frame.Invalidated!=false && !active.InvalidSeen };
                if(!scoringUsable) active.ScoringCoverage=false;
                if(frame.Scoring?.InPits is null) active.PitCoverage=false;
                if(frame.Scoring?.InPits==true) active.SawPits=true;
                pending.Add(closing);
                bool setupUncertain=active.LastSetup!=frame.Setup ||
                    !active.LastSetup.AllRelevantValuesAvailable || !frame.Setup.AllRelevantValuesAvailable;
                LapBoundarySetupObservation? boundaryObservation=null;
                if(setupUncertain)
                {
                    active.BoundaryUncertain=true;
                    boundaryObservation=new(frame.ObservedAt,active.LastSetup,frame.Setup,UiText.C("diagnostics.samplingIntervalOverlapsStartFinish"));
                    active.BoundaryObservations.Add(boundaryObservation);
                    events.Add(new("setup-change-at-boundary",frame.ObservedAt,active.Id,new {
                        ClosingLapId=active.Id,NextLapNumber=frame.LapNumber,
                        Before=active.LastSetup,After=frame.Setup,
                        Reason=UiText.C("diagnostics.changeIntervalOverlapsStartFinishNotAttributedTo") }));
                }
                active=Start(frame,true,events);
                active.BoundaryUncertain=setupUncertain;
                if(boundaryObservation is not null) active.BoundaryObservations.Add(boundaryObservation);
            }
            else
            {
                AbortActive(frame.ObservedAt,UiText.C("diagnostics.lapNumberAndStartTimeDoNotMatch"),events);
                FlushPending(frame.ObservedAt,UiText.C("diagnostics.nonSequentialLapMarkers"),events);
                active=Start(frame,false,events);
            }
        }
        // Start() has not yet interpreted this frame; repeated interpretation is idempotent.
        if(active is not null) ObserveFlagsAndScoring(active,frame,scoringUsable,events);
        ConfirmPending(frame,scoringUsable,events);
        previous=frame;
        return events;
    }
    private ObservedLap Start(LapFrame frame,bool beginningObserved,List<LapEvent> events)
    {
        string id=$"{runId}:{++sequence}";
        var episode=GetEpisode(frame,id,events);
        var lap=new ObservedLap(frame,beginningObserved,id,$"{runId}:period-{period}",episode);
        events.Add(new("lap-start",frame.ObservedAt,lap.Id,new {
            frame.LapNumber,frame.LapStartET,BeginningObserved=beginningObserved,frame.Identity,frame.FixedSetup }));
        return lap;
    }
    private void ObserveWithin(ObservedLap lap,LapFrame frame,bool scoringUsable,List<LapEvent> events)
    {
        foreach(var (field,before,after) in lap.LastSetup.Differences(frame.Setup))
        {
            string kind=before is null || after is null ? "AvailabilityChange":"ValueChange";
            var change=new LapSetupChange(frame.ObservedAt,frame.Elapsed,field,before,after,kind,lap.Last.ObservedAt,lap.Last.Elapsed,true);
            lap.Changes.Add(change);lap.Episode.Changes.Add(change);
            events.Add(new(kind=="ValueChange" ? "setup-change-during-lap":"setup-availability-during-lap",
                frame.ObservedAt,lap.Id,change));
        }
        lap.LastSetup=frame.Setup;
        lap.SetupCoverage &= frame.Setup.AllRelevantValuesAvailable;
        lap.Last=frame;lap.Episode.Last=frame;lastEpisodeFrame=frame;
    }
    private static void ObserveFlagsAndScoring(ObservedLap lap,LapFrame frame,bool scoringUsable,List<LapEvent> events)
    {
        if(frame.Invalidated==true && !lap.InvalidSeen)
        {
            lap.InvalidSeen=true;
            events.Add(new("lap-invalidated",frame.ObservedAt,lap.Id,new {
                Source="LMU_Data.mLapInvalidated",Cause=(string?)null }));
        }
        if(frame.Invalidated is null) lap.ValidityCoverage=false;
        if(frame.TrackLimitsSteps is { } steps)
            lap.MaximumTrackLimits=Math.Max(lap.MaximumTrackLimits??steps,steps);
        lap.ScoringCoverage &= scoringUsable;
        if(!scoringUsable || frame.Scoring is not { } scoring || !Near(scoring.LapStartET,lap.First.LapStartET)) return;
        if(scoring.CompletedLaps<0) {lap.ScoringCoverage=false;return;}
        lap.OldCompletedLaps=scoring.CompletedLaps;
        ObserveCountFlag(lap,frame,scoring,events);
        if(scoring.InPits is { } inPits)
        {
            if(lap.LastPit==false && inPits) lap.SawEntry=true;
            if(lap.LastPit==true && !inPits) lap.SawExit=true;
            lap.SawPits |= inPits;
            lap.LastPit=inPits;
        }
        else {lap.PitCoverage=false;lap.Notes.Add(UiText.C("diagnostics.pitStateUnknown"));}
        // PitState numeric semantics disagree with the installed header comment in the live build.
        // Classify only from observed InPits transitions, never a guessed enum value.
    }
    private static void ObserveCountFlag(ObservedLap lap,LapFrame frame,LapScoring scoring,List<LapEvent> events)
    {
        lap.CountSeen=true;
        // SDK documents current counting policy, not a permanent UI invalidity latch.
        // Once suppression is observed, later 2 cannot restore a proof of continuous counting.
        lap.CountingCoverage &= scoring.CountLapFlag==2;
        lap.TimeCountingSuppressionObserved |= scoring.CountLapFlag is 0 or 1;
        if(lap.CountFlagObservations.Count==0 || lap.LastCountFlag!=scoring.CountLapFlag)
        {
            lap.CountFlagObservations.Add(new(frame.ObservedAt,frame.Elapsed,scoring.UpdatedET,scoring.LapStartET,scoring.CountLapFlag));
            events.Add(new("lap-countability-change",frame.ObservedAt,lap.Id,new {
                Before=lap.LastCountFlag,After=scoring.CountLapFlag,Source="LMU_Data.mCountLapFlag",
                scoring.UpdatedET,scoring.LapStartET,ProvesUiInvalidation=false }));
        }
        lap.LastCountFlag=scoring.CountLapFlag;
    }
    private void ConfirmPending(LapFrame frame,bool scoringUsable,List<LapEvent> events)
    {
        foreach(var p in pending.ToArray())
        {
            var score=scoringUsable ? frame.Scoring:null;
            if(score is not null && Near(score.LapStartET,p.Lap.First.LapStartET))
            {
                // Delayed scoring for the closing lap can still revoke counting confidence.
                ObserveCountFlag(p.Lap,frame,score,events);
            }
            bool matches=score is not null && Near(score.LapStartET,p.End) &&
                p.Lap.OldCompletedLaps is { } total && score.CompletedLaps==total+1;
            if(matches){p.ScoringMatched=true;if(score!.LastLapTime is null)p.MatchedTimeMissing=true;}
            if(matches && score!.LastLapTime is { } time && double.IsFinite(time) && time>0)
            {
                p.ReportedTime=time;
                if(p.CandidateTime==time && score.UpdatedET>p.CandidateScoringET)
                {
                    bool consistent=Math.Abs(time-(p.End-p.Lap.First.LapStartET))<=TimingToleranceSeconds;
                    Finish(p,frame.ObservedAt,consistent,time,consistent ? null:UiText.C("diagnostics.scoringTimeConflict"),events);
                    continue;
                }
                p.CandidateTime=time;p.CandidateScoringET=score.UpdatedET;
            }
            if(score is not null && score.LapStartET>p.End+Epsilon)
            {
                Finish(p,frame.ObservedAt,false,null,UiText.C("diagnostics.scoringSkippedTheTargetLap"),events);continue;
            }
            if((frame.ObservedAt-p.EndObservedAt).TotalSeconds>ScoringWaitSeconds)
                Finish(p,frame.ObservedAt,false,null,UiText.C("diagnostics.noMatchingScoringConfirmedTwiceWithin2S"),events);
        }
    }
    private void Finish(PendingLap p,DateTimeOffset at,bool timingConfirmed,double? reported,string? note,List<LapEvent> events)
    {
        if(!pending.Remove(p)) return;
        if(note is not null) p.Lap.Notes.Add(note);
        var record=Record(p.Lap,"Completed",p.End,at,timingConfirmed,reported??p.ReportedTime,p.BoundaryValidityUncertain,p.EndObservedAt,p.ScoringMatched,p.MatchedTimeMissing);
        events.Add(new("lap-complete",at,p.Lap.Id,Lap:record));
    }
    private void FlushPending(DateTimeOffset at,string reason,List<LapEvent> events)
    {
        foreach(var p in pending.ToArray()) Finish(p,at,false,null,reason,events);
    }
    private void AbortActive(DateTimeOffset at,string reason,List<LapEvent> events)
    {
        if(active is not { } lap) return;
        lap.Notes.Add(reason);lap.Episode.HadInterruption=true;
        events.Add(new("lap-aborted",at,lap.Id,new {Reason=reason},Record(lap,"Aborted",null,at,false,null,false)));
        active=null;
    }
    private static LapRecord Record(ObservedLap lap,string status,double? end,DateTimeOffset at,
        bool timingConfirmed,double? reported,bool boundaryValidityUncertain,DateTimeOffset? boundaryObservedAt=null,bool scoringMatched=false,bool timeMissing=false)
    {
        bool telemetryFully=status=="Completed" && lap.BeginningObserved && end is not null && !lap.Episode.HadInterruption;
        bool fully=telemetryFully && timingConfirmed && lap.ScoringCoverage && lap.PitCoverage;
        bool segmentChanged=lap.Changes.Any(c=>c.Kind=="ValueChange");
        bool changed=lap.Episode.Changes.Any(c=>c.Before is not null && c.After is not null && !Equals(c.Before,c.After));
        bool constant=fully && lap.SetupCoverage && !changed && !lap.BoundaryUncertain;
        string validity=lap.InvalidSeen ? "Invalid" : fully && lap.ValidityCoverage && lap.CountSeen &&
            lap.CountingCoverage && !boundaryValidityUncertain ? "Valid":"Unknown";
        string kind=lap.SawEntry && lap.SawExit ? "InOutlap" : lap.SawEntry ? "Inlap" :
            lap.SawExit ? "Outlap" : lap.SawPits ? "PitLapUnknown" : lap.BeginningObserved ? "Normal":"Unknown";
        var notes=lap.Notes.Distinct().ToList();
        if(lap.Episode.HadInterruption) notes.Add(UiText.C("diagnostics.matchingNativeLapMarkersInInterruptedObservationSegments"));
        if(lap.CountSeen && !lap.CountingCoverage) notes.Add(UiText.C("diagnostics.scoringTemporarilyDisallowedLapTimeCountingNotDirect"));
        if(scoringMatched && timeMissing) notes.Add(UiText.C("diagnostics.scoringBoundaryAndCountersMatchButNoNative"));
        if(!lap.BeginningObserved) notes.Add(UiText.C("diagnostics.observationBeganDuringTheLapNotAtIts"));
        if(lap.BoundaryUncertain) notes.Add(UiText.C("diagnostics.setupCannotBeUnambiguouslyAttributedAtStartFinish"));
        if(boundaryValidityUncertain) notes.Add(UiText.C("diagnostics.invalidationFlagAtTheBoundaryMayStillRefer"));
        if(!lap.PitCoverage) notes.Add(UiText.C("diagnostics.pitStateWasNotContinuouslyAvailable"));
        if(!lap.ScoringCoverage) notes.Add(UiText.C("diagnostics.scoringWasMissingOrMoreThan075"));
        if(!lap.SetupCoverage) notes.Add(UiText.C("diagnostics.notAllFiveRelevantElectronicsValuesWereContinuously"));
        if(lap.First.Identity is null) notes.Add(UiText.C("diagnostics.noReliablyResolvedVehicleLayoutIdentity"));
        if(lap.First.DriverToken is null) notes.Add(UiText.C("diagnostics.driverCannotBeIdentified"));
        return new() {
            LapId=lap.Id,ObservationSessionId=lap.Session,SessionId=null,
            VehicleModelId=lap.First.Identity?.VehicleModelId,TrackId=lap.First.Identity?.TrackId,
            TrackLayoutId=lap.First.Identity?.TrackLayoutId,FixedSetup=lap.First.FixedSetup,
            LapNumber=lap.First.LapNumber,Status=status,LapKind=kind,
            FirstObservedAt=lap.First.ObservedAt,LastObservedAt=lap.Last.ObservedAt,FinalizedAt=at,EndBoundaryObservedAt=boundaryObservedAt,LapStartET=lap.First.LapStartET,LapEndET=end,
            LapTimeSeconds=timingConfirmed ? reported:null,ReportedLastLapTimeSeconds=reported,
            LapTimeSource=timingConfirmed ? "LMU_Data.VehicleScoringInfoV01.mLastLapTime":null,
            DerivedStartToStartSeconds=end-lap.First.LapStartET,TimingConfirmed=timingConfirmed,
            Validity=validity,ValidityEvidence=lap.InvalidSeen ? "ObservedNativeInvalidatedFlag" :
                validity=="Valid" ? "DerivedFromObservedFlagsAndCountedTiming":"InsufficientEvidence",
            InvalidationCause=null,FullyObserved=fully,AllRelevantElectronicsAvailable=lap.SetupCoverage,
            NativeEndBoundaryObserved=status=="Completed" && end is not null,TelemetryFullyObserved=telemetryFully,
            TimeCountingSuppressionObserved=lap.TimeCountingSuppressionObserved,
            TimeCountingContinuous=telemetryFully && lap.ScoringCoverage && lap.CountSeen && lap.CountingCoverage,
            CountLapFlagObservations=Array.AsReadOnly(lap.CountFlagObservations.ToArray()),
            ScoringCoverageComplete=lap.ScoringCoverage,PitCoverageComplete=lap.PitCoverage,
            SetupChanged=changed,SegmentSetupChanged=segmentChanged,NativeLapObservationId=lap.Episode.Id,
            NativeLapHadObservationInterruption=lap.Episode.HadInterruption,
            ObservedSegmentIds=Array.AsReadOnly(lap.Episode.Segments.ToArray()),
            KnownSetupChangesInNativeLap=Array.AsReadOnly(lap.Episode.Changes.ToArray()),
            LastObservedCountLapFlag=lap.LastCountFlag,ScoringBoundaryMatched=scoringMatched,MatchedScoringTimeMissing=timeMissing,
            BoundarySetupUncertain=lap.BoundaryUncertain,ConstantSetup=constant,
            EligibleForSetupHistory=fully && validity=="Valid" && constant && lap.First.FixedSetup==true &&
                lap.First.Identity is not null && lap.First.DriverToken is not null && kind=="Normal",
            SetupSnapshot=lap.First.Setup,LastObservedSetup=lap.LastSetup,
            SetupChanges=Array.AsReadOnly(lap.Changes.ToArray()),
            BoundarySetupObservations=Array.AsReadOnly(lap.BoundaryObservations.ToArray()),MaximumObservedTrackLimitsSteps=lap.MaximumTrackLimits,
            Limitations=Array.AsReadOnly(notes.ToArray()) };
    }
    private static bool Near(double a,double b)=>Math.Abs(a-b)<=Epsilon;
}










