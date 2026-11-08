using System.Text;
using Decoder = Lmu.Diagnostics.Decoder;
using Lmu.Diagnostics;

internal static class LapTests
{
    private static readonly DateTimeOffset At=DateTimeOffset.Parse("2026-11-04T12:00:00Z");
    private static readonly SetupSnapshot Setup=new(3,4,5,6,52.2);
    private static readonly SetupIdentityKey Identity=new("SYNTHETIC_ASTER_GT3","SYNTHETIC_ALPHA","SYNTHETIC_ALPHA_GP");
    // Explicitly SYNTHETIC 2-second laps, sampled every 100 ms, scoring every 200 ms.
    private static LapFrame Frame(int tick,int scoringDelay=0)
    {
        int lap=tick/20+1;
        double elapsed=tick/10.0;
        int scoreTick=Math.Max(0,(tick-scoringDelay)/2*2);
        int completed=scoreTick/20;
        var score=new LapScoring(scoreTick/10.0,completed*2,completed,completed>0?2:null,2,false,false,0,0);
        return new(At.AddMilliseconds(tick*100),elapsed,lap,(lap-1)*2,"session-a-fixed",
            Identity,true,"steam:SYNTHETIC-driver",Setup,false,0,score);
    }
    private static List<LapEvent> Run(Func<int,LapFrame>? transform=null,int end=48,int start=1)
    {
        var engine=new LapAnalyzer();var events=new List<LapEvent>();
        for(int i=start;i<=end;i++) events.AddRange(engine.Observe(transform?.Invoke(i)??Frame(i)));
        return events;
    }
    private static LapRecord Completed(List<LapEvent> events,int number=2) =>
        events.Where(e=>e.Type=="lap-complete" && e.Lap?.LapNumber==number).Single().Lap!;
    public static void RunTests(Action<bool,string> check)
    {
        var association=new LapAnalyzer();
        association.Observe(Frame(25));association.Observe(Frame(26) with {Setup=Setup with {TCCut=5}});
        association.Observe(Frame(27) with {Context="different-native-session"});
        association.Observe(Frame(28));
        var returned=association.Stop(At.AddSeconds(3)).Single(e=>e.Lap is not null).Lap!;
        check(returned.KnownSetupChangesInNativeLap.Count==0 && !returned.SetupChanged,
            "native context switch and return cannot resurrect changes from an older matching lap marker");
        var rewound=new LapAnalyzer();rewound.Observe(Frame(25));
        rewound.Observe(Frame(26) with {Setup=Setup with {TCCut=5}});rewound.Observe(Frame(25));
        check(rewound.Stop(At.AddSeconds(3)).Single(e=>e.Lap is not null).Lap!.KnownSetupChangesInNativeLap.Count==0,
            "native clock rewind clears matching-marker change association");
        var normalEvents=Run();
        var normal=Completed(normalEvents);
        check(normal.FullyObserved && normal.Validity=="Valid" && normal.ConstantSetup &&
            normal.EligibleForSetupHistory && normal.LapTimeSeconds==2,"complete observed valid fixed lap has constant snapshot and confirmed native time");
        check(normal.ValidityEvidence=="DerivedFromObservedFlagsAndCountedTiming" && normal.SessionId is null &&
            !string.IsNullOrEmpty(normal.ObservationSessionId),"validity is explicitly derived and observation period is not an invented LMU session ID");
        check(normal.LapTimeSource=="LMU_Data.VehicleScoringInfoV01.mLastLapTime" &&
            normal.EndBoundaryObservedAt<normal.FinalizedAt &&
            normal.LastObservedAt<normal.EndBoundaryObservedAt,"scoring confirmation time is separated from in-lap observation and boundary");
        var initial=Completed(normalEvents,1);
        check(!initial.FullyObserved && initial.Validity=="Unknown" && !initial.ConstantSetup &&
            !initial.EligibleForSetupHistory,"program starts mid-lap: first completed lap stays partial");
        var invalidEvents=Run(i=>Frame(i) with {Invalidated=i==30});
        var invalid=Completed(invalidEvents);
        check(invalid.Validity=="Invalid" && !invalid.EligibleForSetupHistory &&
            invalid.InvalidationCause is null && invalidEvents.Count(e=>e.Type=="lap-invalidated")==1,
            "native invalidation latches despite reset; no fabricated cause");
        var unknown=Completed(Run(i=>Frame(i) with {Invalidated=i==30?null:false}));
        check(unknown.FullyObserved && unknown.Validity=="Unknown" && !unknown.EligibleForSetupHistory,
            "unknown current-lap validity cannot become valid");
        var steps=Completed(Run(i=>Frame(i) with {TrackLimitsSteps=i>=28?9:0}));
        check(steps.Validity=="Valid" && steps.MaximumObservedTrackLimitsSteps==9,
            "track-limits steps alone do not invalidate a lap");
        var nonCounting=Completed(Run(i=>Frame(i) with {Scoring=Frame(i).Scoring! with {CountLapFlag=i is >=28 and <=32 ? 1:2}}));
        check(nonCounting.Validity=="Unknown" && !nonCounting.EligibleForSetupHistory,
            "count-lap-only flag is not a proved valid timed lap");
        foreach(var (name,altered) in new[]{
            ("TC",Setup with {TC=4}),("TCCut",Setup with {TCCut=5}),("TCSlip",Setup with {TCSlip=6}),
            ("ABS",Setup with {ABS=7}),("BrakeBias",Setup with {BrakeBias=52.3})})
        {
            var events=Run(i=>Frame(i) with {Setup=i is >=28 and <34 ? altered:Setup});
            var lap=Completed(events);
            check(lap.SetupChanged && !lap.ConstantSetup && !lap.EligibleForSetupHistory &&
                lap.SetupChanges.Count==2 && lap.SetupSnapshot==lap.LastObservedSetup &&
                lap.SetupChanges.All(c=>c.Field==name && c.Kind=="ValueChange"),
                "in-lap "+name+" change and return are both retained");
            check(events.Count(e=>e.Type=="setup-change-during-lap")==2,
                "timestamped setup-change events for "+name);
        }
        var multiple=Completed(Run(i=>Frame(i) with {
            Setup=i is >=26 and <28 ? Setup with {TC=4,ABS=7} :
                i is >=30 and <32 ? Setup with {BrakeBias=53.0} : Setup }));
        check(multiple.SetupChanges.Count==6 && multiple.SetupChanged && multiple.SetupSnapshot==multiple.LastObservedSetup &&
            !multiple.ConstantSetup,"multiple changes returning to initial snapshot cannot masquerade as constant");
        var missing=Completed(Run(i=>Frame(i) with {Setup=i==30 ? Setup with {ABS=null}:Setup}));
        check(!missing.AllRelevantElectronicsAvailable && !missing.ConstantSetup && !missing.EligibleForSetupHistory &&
            missing.SetupChanges.All(c=>c.Kind=="AvailabilityChange"),"missing electronics revokes coverage without inventing a driver action");
        var alwaysMissing=Completed(Run(i=>Frame(i) with {Setup=Setup with {TCSlip=null}}));
        check(!alwaysMissing.ConstantSetup && !alwaysMissing.AllRelevantElectronicsAvailable,
            "unproven irrelevant aid is not silently ignored");
        var boundaryEvents=Run(i=>Frame(i) with {Setup=i>=40 ? Setup with {TC=4}:Setup},end:68);
        var beforeBoundary=Completed(boundaryEvents);
        var afterBoundary=Completed(boundaryEvents,3);
        check(beforeBoundary.BoundarySetupUncertain && !beforeBoundary.ConstantSetup &&
            !beforeBoundary.SetupChanged && !beforeBoundary.EligibleForSetupHistory,
            "boundary-spanning setup delta is not falsely attributed inside old lap");
        check(afterBoundary.BoundarySetupUncertain && !afterBoundary.ConstantSetup &&
            boundaryEvents.Count(e=>e.Type=="setup-change-at-boundary")==1,
            "new lap also loses constant-setup proof at ambiguous boundary");
        check(beforeBoundary.BoundarySetupObservations.Count==1 && afterBoundary.BoundarySetupObservations.Count==1 &&
            beforeBoundary.BoundarySetupObservations[0]==afterBoundary.BoundarySetupObservations[0],
            "ambiguous boundary setup interval is preserved in both records with timestamp");
        var unknownPit=Completed(Run(i=>Frame(i) with {Scoring=Frame(i).Scoring! with {InPits=i==30?null:false}}));
        check(!unknownPit.PitCoverageComplete && !unknownPit.FullyObserved &&
            unknownPit.AllRelevantElectronicsAvailable && !unknownPit.EligibleForSetupHistory,
            "missing pit data cannot masquerade as full observation or missing electronics");
        var uncertainInvalid=Completed(Run(i=>Frame(i) with {Invalidated=i==40}));
        check(uncertainInvalid.Validity=="Unknown","invalid flag appearing exactly at boundary cannot certify old lap valid");

        foreach(var fixedStatus in new bool?[]{false,null})
        {
            var lap=Completed(Run(i=>Frame(i) with {FixedSetup=fixedStatus,Context="constant-"+fixedStatus}));
            check(lap.FullyObserved && lap.ConstantSetup && !lap.EligibleForSetupHistory,
                "only explicitly fixed laps can be history candidates: "+(fixedStatus?.ToString()??"Unknown"));
        }
        var noIdentity=Completed(Run(i=>Frame(i) with {Identity=null,Context="no-identity"}));
        check(noIdentity.VehicleModelId is null && !noIdentity.EligibleForSetupHistory,
            "unknown identity never receives a setup-library assignment");
        var noDriver=Completed(Run(i=>Frame(i) with {DriverToken=null,Context="unknown-driver"}));
        check(!noDriver.EligibleForSetupHistory,"unknown driver cannot be trusted training data");

        foreach(var (name,newContext) in new[]{
            ("Fixed to Open","open-session"),("vehicle","other-vehicle"),("layout","other-layout"),
            ("driver","other-driver"),("process","other-process"),("session restart","other-session")})
        {
            var events=Run(i=>Frame(i) with {Context=i>=30 ? newContext:"session-a-fixed",FixedSetup=name=="Fixed to Open" && i>=30?false:true});
            check(events.Any(e=>e.Type=="lap-aborted" && e.Lap?.LapNumber==2) &&
                !events.Where(e=>e.Lap?.LapNumber==2).Any(e=>e.Lap!.EligibleForSetupHistory),
                "active lap aborted for context change: "+name);
        }
        foreach(var (label,alter) in new (string,Func<LapFrame,LapFrame>)[]{
            ("Fixed flag",f=>f with {FixedSetup=false}),
            ("vehicle ID",f=>f with {Identity=Identity with {VehicleModelId="SYNTHETIC-other-model"}}),
            ("layout ID",f=>f with {Identity=Identity with {TrackLayoutId="SYNTHETIC-other-layout"}}),
            ("driver token",f=>f with {DriverToken="SYNTHETIC-other-driver"})})
        {
            var isolated=Run(i=>i>=30?alter(Frame(i)):Frame(i));
            check(isolated.Any(e=>e.Type=="lap-aborted" && e.Lap?.LapNumber==2) &&
                !isolated.Any(e=>e.Lap?.LapNumber==2 && e.Lap.EligibleForSetupHistory),
                "analyzer itself rejects "+label+" change even with unchanged external context fingerprint");
        }
        var restarted=new LapAnalyzer();var restartEvents=new List<LapEvent>();
        for(int i=1;i<=70;i++) restartEvents.AddRange(restarted.Observe(i<30 ? Frame(i) :
            Frame(i-29) with {ObservedAt=At.AddMilliseconds(i*100)}));
        check(restartEvents.Any(e=>e.Type=="lap-aborted") &&
            restartEvents.Where(e=>e.Type=="lap-start").Select(e=>e.LapId).Distinct().Count()>=3,
            "native clock/lap reset starts a new observation period");

        var interrupted=new LapAnalyzer();var gapEvents=new List<LapEvent>();
        for(int i=1;i<=48;i++)
        {
            if(i==30) gapEvents.AddRange(interrupted.Interrupt(At.AddSeconds(3),"SYNTHETIC lost mapping"));
            else gapEvents.AddRange(interrupted.Observe(Frame(i)));
        }
        check(gapEvents.Count(e=>e.Type=="telemetry-interruption")==1 &&
            !Completed(gapEvents).FullyObserved && !Completed(gapEvents).EligibleForSetupHistory,
            "mapping loss and reconnection never bridge a complete lap");
        var wallGap=Run(i=>Frame(i) with {ObservedAt=Frame(i).ObservedAt.AddSeconds(i>=30?1:0)});
        check(wallGap.Any(e=>e.Type=="telemetry-interruption") && !Completed(wallGap).FullyObserved,
            "large sampling gap invalidates continuity even when physics otherwise advances");
        var frozen=new LapAnalyzer();var freezeEvents=new List<LapEvent>();
        for(int i=1;i<=48;i++)
        {
            var frame=i is >=29 and <=36 ? Frame(28) with {ObservedAt=At.AddMilliseconds(i*100),Scoring=Frame(i).Scoring} : Frame(i);
            freezeEvents.AddRange(frozen.Observe(frame));
        }
        check(freezeEvents.Count(e=>e.Type=="telemetry-interruption")>=1 &&
            !Completed(freezeEvents).FullyObserved,"frozen physics is detected despite advancing scoring");
        var outlap=Completed(Run(i=>Frame(i) with {Scoring=Frame(i).Scoring! with {
            InPits=i<28,PitState=i is >=26 and <28?4:0}}));
        check(outlap.LapKind=="Outlap" && !outlap.EligibleForSetupHistory,
            "pit exit marks outlap and excludes setup training");
        var inlap=Completed(Run(i=>Frame(i) with {Scoring=Frame(i).Scoring! with {
            InPits=i>=30,PitState=i>=30?2:0}}));
        check(inlap.LapKind=="Inlap" && !inlap.EligibleForSetupHistory,
            "pit entry marks inlap and excludes setup training");
        var garageEngine=new LapAnalyzer();var garageEvents=new List<LapEvent>();
        for(int i=1;i<30;i++) garageEvents.AddRange(garageEngine.Observe(Frame(i)));
        garageEvents.AddRange(garageEngine.Observe(Frame(30) with {Setup=Setup with {TC=9},
            Scoring=Frame(30).Scoring! with {InGarage=true,InPits=true}}));
        check(garageEvents.Any(e=>e.Type=="lap-aborted") &&
            garageEvents.All(e=>e.Type!="setup-change-during-lap"),
            "garage setup change cannot be attached to old active lap");
        var preLap=Completed(Run(i=>Frame(i) with {Setup=i<10?Setup with {TC=9}:Setup}));
        check(preLap.SetupChanges.Count==0 && preLap.ConstantSetup,
            "setup changes before observed start are not carried into next lap");

        var opaquePitState=Completed(Run(i=>Frame(i) with {Scoring=Frame(i).Scoring! with {PitState=5}}));
        check(opaquePitState.LapKind=="Normal","unknown live PitState enum does not fabricate a pit transition");
        var staleWindow=Completed(Run(i=>Frame(i) with {Scoring=i is >=26 and <=37 ? Frame(24).Scoring:Frame(i).Scoring}));
        check(!staleWindow.FullyObserved && !staleWindow.EligibleForSetupHistory,
            "frozen scoring cannot certify pit/count coverage despite advancing physics");
        var earlyScore=Completed(Run(i=>Frame(i) with {Scoring=i==39 ? Frame(40).Scoring:Frame(i).Scoring}));
        check(earlyScore.TimingConfirmed && earlyScore.EligibleForSetupHistory,
            "scoring may reach next boundary before telemetry without assigning next-lap settings backwards");
        var delayed=Completed(Run(i=>Frame(i,4),end:52));
        check(delayed.FullyObserved && delayed.LapTimeSeconds==2 && delayed.TimingConfirmed,
            "delayed scoring joined to matching boundary by ET and completed count");
        var stale=Completed(Run(i=>Frame(i) with {Scoring=i==32 ? Frame(10).Scoring:Frame(i).Scoring}));
        check(stale.FullyObserved && stale.LapTimeSeconds==2,
            "older scoring update is ignored without resetting monotonic telemetry");
        var duplicateEngine=new LapAnalyzer();var duplicates=new List<LapEvent>();
        for(int i=1;i<=43;i++) duplicates.AddRange(duplicateEngine.Observe(Frame(i)));
        for(int i=1;i<=3;i++) duplicates.AddRange(duplicateEngine.Observe(Frame(43) with {ObservedAt=At.AddSeconds(4.3+i*.1)}));
        check(duplicates.Count(e=>e.Type=="lap-complete" && e.Lap?.LapNumber==2)==1,
            "duplicate telemetry/scoring does not complete a lap twice");
        var noTiming=Completed(Run(i=>Frame(i) with {Scoring=Frame(i).Scoring! with {LastLapTime=null}},end:65));
        check(noTiming.LapTimeSeconds is null && !noTiming.TimingConfirmed && !noTiming.FullyObserved &&
            noTiming.DerivedStartToStartSeconds==2,"missing native last time is not replaced by a derived duration");
        var mismatch=Completed(Run(i=>Frame(i) with {Scoring=Frame(i).Scoring! with {LastLapTime=i>=40?9:2}}));
        check(mismatch.LapTimeSeconds is null && mismatch.ReportedLastLapTimeSeconds==9 &&
            !mismatch.EligibleForSetupHistory,"inconsistent official time stays untrusted rather than assigned");
        var negativeCounter=Completed(Run(i=>Frame(i) with {Scoring=Frame(i).Scoring! with {CompletedLaps=-1}},end:65));
        check(!negativeCounter.TimingConfirmed && !negativeCounter.FullyObserved && !negativeCounter.EligibleForSetupHistory,
            "negative native completed-lap sentinel never certifies a lap");
        var jump=Run(i=>Frame(i) with {Scoring=Frame(i).Scoring! with {CompletedLaps=i>=40?100:Frame(i).Scoring!.CompletedLaps}},end:65);
        check(Completed(jump).LapTimeSeconds is null && !Completed(jump).EligibleForSetupHistory,
            "unrelated scoring counter cannot supply another lap's last time");
        var stop=new LapAnalyzer();var stopEvents=new List<LapEvent>();
        for(int i=1;i<=30;i++) stopEvents.AddRange(stop.Observe(Frame(i)));
        stopEvents.AddRange(stop.Stop(At.AddSeconds(3.1)));
        check(stopEvents.All(e=>e.Type!="telemetry-interruption"),"normal program stop does not invent a game telemetry failure");
        check(stopEvents.Any(e=>e.Type=="lap-aborted") &&
            !stopEvents.Any(e=>e.Lap?.LapNumber==2 && e.Lap.EligibleForSetupHistory),
            "stopping diagnosis emits incomplete aborted observation");
        check(!(Setup with {ABS=-1}).AllRelevantValuesAvailable,"invalid electronics range is not available");
        LapFrame Anchored(int i)
        {
            var f=Frame(i);int completed=i/2*2/20;
            return f with {LapNumber=i/20,LapStartET=i<20?-1:i/20*2,
                Scoring=f.Scoring! with {LapStartET=completed==0?-1:completed*2,
                    CompletedLaps=completed,CountLapFlag=completed==0?1:2,LastLapTime=completed<2?null:2}};
        }
        var anchoredEvents=Run(Anchored);
        var anchored=Completed(anchoredEvents,1);
        check(anchored.FullyObserved && anchored.EligibleForSetupHistory &&
            anchoredEvents.All(e=>e.Lap?.LapNumber!=0),
            "observed -1 outlap anchor permits first real boundary without inventing an outlap record");
        var noAnchor=Completed(Run(Anchored,start:25),1);
        check(!noAnchor.FullyObserved && !noAnchor.EligibleForSetupHistory,
            "first-boundary fix never upgrades program start inside timed lap");
        var brokenAnchor=new LapAnalyzer();var anchorGap=new List<LapEvent>();
        for(int i=1;i<=48;i++)
            if(i==19)anchorGap.AddRange(brokenAnchor.Interrupt(Anchored(i).ObservedAt,"SYNTHETIC gap before first line"));
            else anchorGap.AddRange(brokenAnchor.Observe(Anchored(i)));
        check(!Completed(anchorGap,1).FullyObserved,"first-boundary anchor cannot bridge a telemetry interruption");
        var changedFirst=Completed(Run(i=>Anchored(i) with {Setup=i>=20?Setup with {TC=4}:Setup}),1);
        check(changedFirst.FullyObserved && changedFirst.BoundarySetupUncertain && !changedFirst.ConstantSetup &&
            !changedFirst.EligibleForSetupHistory,"first real line preserves setup ambiguity across sentinel anchor");
        var invalidAnchor=Run(i=>Anchored(i) with {LapStartET=i<20?-2:Anchored(i).LapStartET});
        check(!Completed(invalidAnchor,1).FullyObserved,"only observed -1/Lap0 sentinel is allowed, not arbitrary negative starts");
        ParserTests(check);
        Console.WriteLine("Lap state-machine fixtures are entirely SYNTHETIC. They do not demonstrate real LMU lap timing or validity semantics.");
    }
    private static void ParserTests(Action<bool,string> check)
    {
        var s=new byte[548];var t=new byte[1888];var v=new byte[584];
        void D(byte[] b,int o,double n)=>BitConverter.GetBytes(n).CopyTo(b,o);
        void I(byte[] b,int o,int n)=>BitConverter.GetBytes(n).CopyTo(b,o);
        void Text(byte[] b,int o,string value)=>Encoding.UTF8.GetBytes(value).CopyTo(b,o);
        I(s,64,10);D(s,68,10);s[108]=5;s[115]=1;s[348]=1;
        D(t,12,10);I(t,20,4);D(t,24,8);D(t,664,.478);Text(t,796,"SYNTHETIC Aster GT3");
        Text(s,0,"SYNTHETIC Circuit Alpha");Text(v,4,"SYNTHETIC driver");
        Text(v,544,"32_26_WRT_65186446");
        BitConverter.GetBytes((short)3).CopyTo(v,100);D(v,168,2);D(v,256,8);v[506]=2;v[457]=5;
        BitConverter.GetBytes(42UL).CopyTo(v,536);
        foreach(var o in new[]{750,752,754,756}){t[o]=3;t[o+1]=10;}
        var sample=new Sample(At,"LIVE",Decoder.Decode(s,t,v));
        var rawTelemetry=t.ToArray();var rawScoring=v.ToArray();var rawSession=s.ToArray();
        rawTelemetry[745]=2;rawTelemetry[767]=1;rawSession[351]=8;rawSession[352]=4;
        D(rawScoring,168,-1);D(rawScoring,176,-1);D(rawScoring,184,60.25);
        BitConverter.GetBytes((short)2).CopyTo(rawScoring,194);
        var raw=Decoder.Decode(rawSession,rawTelemetry,rawScoring);
        check(raw["LapInvalidated"].Value is null && (byte?)raw["RawLapInvalidated"].Value==2 &&
            raw["LastLapTime"].Value is null && (double?)raw["RawLastLapTime"].Value == -1,
            "raw invalidation byte and lap-time sentinel survive normalization without inventing validity");
        check((byte?)raw["TrackLimitsStepsPerPenalty"].Value==8 && (byte?)raw["TrackLimitsStepsPerPoint"].Value==4 &&
            (double?)raw["RawCurrentSector1"].Value == -1 && (double?)raw["RawCurrentSector2"].Value==60.25 &&
            (int?)raw["OutstandingPenalties"].Value==2,"verified native limits scaling/sectors/penalty fields retained separately");
        var identity=new SessionIdentity(Identity.VehicleModelId,"SYNTHETIC Aster GT3",Identity.TrackId,"Monza",
            Identity.TrackLayoutId,"Monza","SYNTHETIC test binding","Corroborated",null);
        check((int?)sample.Fields["RawPitState"].Value==5,"out-of-comment native PitState is retained losslessly");
        var frame=LapFrame.From(sample,identity,"process-a",out _);
        check(frame is not null && frame.LapNumber==4 && frame.LapStartET==8 && frame.Scoring?.CompletedLaps==3 &&
            frame.Scoring.LastLapTime==2 && frame.DriverToken=="steam:42",
            "official independent byte offsets decode lap/count/driver fields into analyzer");
        var waiting=t.ToArray();
        I(waiting,20,0);D(waiting,24,-1);
        var outlapSample=new Sample(At,"LIVE",Decoder.Decode(s,waiting,v));
        check(outlapSample.Fields["CurrentLap"].Value is null &&
            LapFrame.From(outlapSample,identity,"process-a",out _)?.IsPreTimingAnchor==true,
            "native -1 sentinel is not displayed as elapsed+1 round time");
        sample.Fields["DriverSteamId"]=new("43","SYNTHETIC");
        var changedDriver=LapFrame.From(sample,identity,"process-a",out _);
        check(frame?.Context!=changedDriver?.Context,"native driver change affects lap context");
        sample.Fields["PlayerControl"]=new(3,"SYNTHETIC replay");
        check(LapFrame.From(sample,identity,"process-a",out _) is null,"replay is not a local driving observation");
        sample.Fields["PlayerControl"]=new(0,"SYNTHETIC");
        sample.Fields["InRealtime"]=new(false,"SYNTHETIC monitor");
        check(LapFrame.From(sample,identity,"process-a",out _) is null,"monitor interrupts lap observation");
        sample.Fields["InRealtime"]=new(true,"SYNTHETIC");
        sample.Fields["GamePhase"]=new(9,"SYNTHETIC pause");
        check(LapFrame.From(sample,identity,"process-a",out _) is null,"native pause phase interrupts observation");
        sample.Fields["GamePhase"]=new(5,"SYNTHETIC");
        sample.Fields["GameVersion"]=new(14000,"SYNTHETIC");
        check(LapFrame.From(sample,identity,"process-a",out _) is null,"unverified old ABI lap semantics remain gated");
    }
}






