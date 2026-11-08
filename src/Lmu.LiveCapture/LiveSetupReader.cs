using Lmu.Localization;
using System.Diagnostics;
using Lmu.Diagnostics;
using Lmu.SetupLibrary;
namespace Lmu.LiveCapture;

public sealed class LiveSetupReader(string restAddress="http://127.0.0.1:6397"):ILiveSetupReader
{
    public async Task<ObservedSetup> ReadCurrent(CancellationToken cancellationToken=default)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException(UiText.C("live.lmuLiveCaptureRequiresWindows"));
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(12));
        using var rest=new RestProbe(restAddress);var source=new SharedMemorySource();var coordinator=new IdentityCoordinator();
        Task<RestCapture[]>? pending=null;IdentityTicket? ticket=null;var next=DateTimeOffset.MinValue;
        Sample? previous=null;string? previousProcess=null;
        try {
            while(true) {
                deadline.Token.ThrowIfCancellationRequested();
                var processes=Process.GetProcessesByName("Le Mans Ultimate");string process;
                try {
                    if(processes.Length!=1)throw new LibraryValidationException(UiText.C("live.noUniquelyIdentifiedRunningLmuProcess"));
                    process=$"{processes[0].Id}:{processes[0].StartTime.ToUniversalTime().Ticks}";
                }finally{foreach(var p in processes)p.Dispose();}
                var sample=source.Read(DateTimeOffset.UtcNow);var now=DateTimeOffset.UtcNow;sample=sample with {ObservedAt=now};
                sample.Fields["GameRunning"]=new(true,"Windows.Process");coordinator.UpdateLive(sample,process);
                if(pending is null&&now>=next){ticket=coordinator.BeginRequest(now);pending=rest.Poll(deadline.Token);next=now.AddSeconds(3);}
                if(pending?.IsCompleted==true){coordinator.Complete(ticket!,await pending,DateTimeOffset.UtcNow);pending=null;}
                var identity=coordinator.Current(DateTimeOffset.UtcNow);
                if(previous is not null&&previousProcess==process&&identity.SetupKey is not null) {
                    try{return FromSamples(previous,sample,identity,process,DateTimeOffset.UtcNow);}
                    catch(LibraryValidationException){/* Wait for a complete, coherent current observation until deadline. */}
                }
                previous=sample;previousProcess=process;
                await Task.Delay(100,deadline.Token);
            }
        }catch(OperationCanceledException)when(!cancellationToken.IsCancellationRequested){throw new LibraryValidationException(UiText.C("live.noFreshUnambiguousCompleteLmuObservationWithin12"));}
        finally {
            deadline.Cancel();if(pending is not null)await pending; // RestProbe captures transport cancellation as data.
        }
    }
    public static ObservedSetup FromSamples(Sample before,Sample current,SessionIdentity identity,string processInstance,DateTimeOffset now)
    {
        object? V(Sample s,string key)=>s.Fields.GetValueOrDefault(key)?.Value;
        int? I(Sample s,string key)=>V(s,key) switch {byte b=>b,int i=>i,_=>null};
        double? N(Sample s,string key)=>V(s,key) switch {double d when double.IsFinite(d)=>d,float f when float.IsFinite(f)=>f,_=>null};
        if(identity.SetupKey is not {} key || before.Mode!="LIVE" || current.Mode!="LIVE" || before.Error is not null || current.Error is not null ||
            string.IsNullOrWhiteSpace(processInstance) || I(current,"GameVersion")!=14200 || I(before,"GameVersion")!=14200 ||
            V(current,"GameRunning") is not true || V(before,"GameRunning") is not true || V(current,"InRealtime") is not true ||
            V(before,"InRealtime") is not true || V(current,"PlayerControl") is not 0 || V(before,"PlayerControl") is not 0 ||
            V(current,"InGarage") is not false || V(before,"InGarage") is not false || V(current,"FixedSetup") is not bool ||
            I(current,"GamePhase") is null or 0 or 7 or 8 or 9 || I(before,"GamePhase") is null or 0 or 7 or 8 or 9 ||
            V(current,"RawVehicleModel") as string!=identity.VehicleModelName || V(current,"RawTrackName") as string!=identity.TrackLayoutName ||
            (now-current.ObservedAt).TotalSeconds is <0 or >2 || (current.ObservedAt-before.ObservedAt).TotalSeconds is <=0 or >.5 ||
            N(current,"TelemetryElapsed") is not {} elapsed || N(before,"TelemetryElapsed") is not {} oldElapsed || elapsed<=oldElapsed || elapsed-oldElapsed>.5 ||
            LiveIdentityContext.From(before,processInstance)?.Fingerprint is not {} fingerprint ||
            LiveIdentityContext.From(current,processInstance)?.Fingerprint!=fingerprint)
            throw new LibraryValidationException(UiText.C("live.liveIdentitySourceIsNotUnambiguousFreshAnd"));
        int Aid(string name)=>I(current,name)??throw new LibraryValidationException(UiText.C("live.isMissing",name switch{"TCCut"=>"TC Cut","TCSlip"=>"TC Slip",_=>name}));
        var values=new Electronics(Aid("TC"),Aid("TCCut"),Aid("TCSlip"),Aid("ABS"),N(current,"BrakeBias")??throw new LibraryValidationException(UiText.C("live.brakeBiasIsMissing")));
        var limits=new AidLimits(Aid("TCMax"),Aid("TCCutMax"),Aid("TCSlipMax"),Aid("ABSMax"),"LMU_Data ABI 14200 native maxima; unique active model binding");
        return new(new(key.VehicleModelId,identity.VehicleModelName??throw new LibraryValidationException(UiText.C("live.modelNameIsMissing")),Limits:limits),
            new(key.TrackId,identity.TrackName??throw new LibraryValidationException(UiText.C("live.trackNameIsMissing"))),
            new(key.TrackId,key.TrackLayoutId,identity.TrackLayoutName??throw new LibraryValidationException(UiText.C("live.layoutNameIsMissing"))),values,limits,
            new(current.ObservedAt,V(current,"FixedSetup") as bool?,"LMU_Data ABI 14200 + corroborated content catalogs; no driver/profile data"));
    }
}
