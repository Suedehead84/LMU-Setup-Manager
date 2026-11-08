using Lmu.Localization;
using System.Diagnostics;
using Lmu.Diagnostics;
using Lmu.SetupLibrary;
namespace Lmu.LiveCapture;

public sealed class LmuSourceException(string message):Exception(message);
public sealed record NativeFrame(bool Running,string ProcessInstance,Sample Sample,Sample? SessionSample=null);
public interface INativeFrameSource {NativeFrame Read();}
public interface ILmuCatalogTransport {Task<RestCapture[]> Read(CancellationToken token);Task<RestCapture?> ReadNavigation(CancellationToken token)=>Task.FromResult<RestCapture?>(null);}
public sealed class LmuCatalogTransport : ILmuCatalogTransport,IDisposable
{
    private readonly RestProbe probe=new("http://127.0.0.1:6397");
    public Task<RestCapture[]> Read(CancellationToken token)=>probe.PollCatalogs(token);
    public Task<RestCapture?> ReadNavigation(CancellationToken token)=>probe.PollNavigation(token);
    public void Dispose()=>probe.Dispose();
}
public sealed class NativeFrameSource:INativeFrameSource
{
    private ITelemetrySource? source;
    public NativeFrame Read()
    {
        var now=DateTimeOffset.UtcNow;
        NativeFrame Missing(string message)=>new(false,"",new(now,"LIVE",Decoder.Empty(),message));
        if(!OperatingSystem.IsWindows())return Missing(UiText.C("live.lmuLiveDataRequiresWindows"));
        var processes=Process.GetProcessesByName("Le Mans Ultimate");
        try {
            if(processes.Length!=1)return Missing(UiText.C("live.lmuIsNotRunningOrTheProcessIs"));
            var p=processes[0];var instance=$"{p.Id}:{p.StartTime.ToUniversalTime().Ticks}";
            source??=new SharedMemorySource();var sample=source.Read(now) with {ObservedAt=DateTimeOffset.UtcNow};
            sample.Fields["GameRunning"]=new(true,"Windows.Process");return new(true,instance,sample,((SharedMemorySource)source).ReadSession(DateTimeOffset.UtcNow));
        }catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception){return Missing(UiText.C("live.lmuProcessCannotBeReadOrHasChanged"));}
        finally {foreach(var p in processes)p.Dispose();}
    }
}
public sealed record LiveElectronics(int? TC,int? TCCut,int? TCSlip,int? ABS,double? BrakeBiasFrontPercent);
public sealed record LiveStatus(bool? GameRunning,bool SharedMemoryConnected,bool Fresh,DateTimeOffset? ObservedAt,
    string? Vehicle,string? Track,string? Layout,string? Session,bool? SessionFixedSetup,SetupKey? Key,
    LiveElectronics Values,bool CanCapture,string? ContextToken,string Message,SessionEvidence? SessionIdentity=null);

// One state engine for web status and capture; all byte decoding remains in Phase A.
public sealed class LiveStateEngine(TimeProvider? timeProvider=null)
{
    private readonly TimeProvider clock=timeProvider??TimeProvider.System;
    private readonly object gate=new();private readonly IdentityCoordinator identity=new();
    private readonly SessionIdentityState session=new();private readonly Dictionary<long,IdentityTicket> sessionTickets=[];
    private NativeFrame? current,previous;private string? fingerprint;private string token=Guid.NewGuid().ToString("N");
    public IdentityTicket BeginRequest(){lock(gate){var t=identity.BeginRequest(clock.GetUtcNow());sessionTickets[t.RequestId]=session.Begin(clock.GetUtcNow());return t;}}
    public NavigationTicket BeginNavigation(){lock(gate)return session.BeginNavigation(clock.GetUtcNow());}
    public void CompleteNavigation(NavigationTicket ticket,RestCapture? capture){lock(gate)session.CompleteNavigation(ticket,capture,clock.GetUtcNow());}
    public void Complete(IdentityTicket ticket,RestCapture[] captures){lock(gate){identity.Complete(ticket,captures,clock.GetUtcNow());if(sessionTickets.Remove(ticket.RequestId,out var paired))session.Complete(paired,captures,clock.GetUtcNow());}}
    public void Observe(NativeFrame frame)
    {
        lock(gate) {
            if(!frame.Running)frame=frame with {ProcessInstance="",Sample=new(frame.Sample.ObservedAt,"LIVE",Decoder.Empty(),UiText.C("live.lmuIsNotRunning"))};
            string? next=LiveIdentityContext.From(frame.Sample,frame.ProcessInstance)?.Fingerprint;
            if(next!=fingerprint || current is not null &&
                ((frame.Sample.ObservedAt-current.Sample.ObservedAt).TotalSeconds is <0 or >2 || Number(frame.Sample,"TelemetryElapsed")<Number(current.Sample,"TelemetryElapsed")))
                token=Guid.NewGuid().ToString("N");
            fingerprint=next;previous=current?.ProcessInstance==frame.ProcessInstance?current:null;current=frame;
            identity.UpdateLive(frame.Sample,frame.ProcessInstance);
            session.Observe(frame.Running?frame.SessionSample:null,frame.ProcessInstance);
        }
    }
    public LiveStatus Status(){lock(gate)return StatusLocked();}
    private LiveStatus StatusLocked()
    {
        var now=clock.GetUtcNow();var s=current?.Sample;
        bool recent=s is not null&&(now-s.ObservedAt).TotalSeconds is >=0 and <=2;
        bool connected=recent&&current!.Running&&s!.Mode=="LIVE"&&s.Error is null;
        bool progressing=previous is not null&&s is not null&&Number(s,"TelemetryElapsed") is {} et&&Number(previous.Sample,"TelemetryElapsed") is {} old&&et>old&&et-old<=.5;
        bool fresh=connected&&progressing&&Value(s!,"InRealtime") is true&&Int(s!,"PlayerControl")==0&&Value(s!,"InGarage") is false&&Int(s!,"GamePhase") is not (null or 0 or 7 or 8 or 9);
        if(fresh&&current?.SessionSample is {} scoring&&(scoring.Error is not null||new[]{"PlayerVehicleFile","PlayerSlotId","SessionCode","SessionStart","FixedSetup","RawTrackName"}.Any(k=>Convert.ToString(Value(scoring,k),System.Globalization.CultureInfo.InvariantCulture)!=Convert.ToString(Value(s!,k),System.Globalization.CultureInfo.InvariantCulture))))fresh=false;
        var telemetryBinding=identity.Current(now);
        var binding=current?.SessionSample is not null?session.Current(now):telemetryBinding;
        SetupKey? key=(current?.SessionSample is not null||fresh)&&binding.SetupKey is {} k?new(k.VehicleModelId,k.TrackId,k.TrackLayoutId):null;
        // A contradictory fresh telemetry binding is positive evidence against session matching.
        if(fresh&&telemetryBinding.SetupKey is {} tk&&key is not null&&key!=new SetupKey(tk.VehicleModelId,tk.TrackId,tk.TrackLayoutId))key=null;
        if(fresh&&key is not null&&(Value(s!,"RawVehicleModel") as string!=binding.VehicleModelName||Value(s!,"RawTrackName") as string!=binding.TrackLayoutName))key=null;
        var values=fresh?new LiveElectronics(Int(s!,"TC"),Int(s!,"TCCut"),Int(s!,"TCSlip"),Int(s!,"ABS"),Number(s!,"BrakeBias")):new(null,null,null,null,null);
        string? captureIssue=null;
        var observed=fresh&&key is not null&&telemetryBinding.SetupKey is not null?ObservationLocked(telemetryBinding,out captureIssue):null;
        if(fresh&&key is not null&&telemetryBinding.SetupKey is null)captureIssue=UiText.C("live.telemetryIdentityNotYetConfirmed");
        string message=!recent?UiText.C("live.liveDataIsMissingOrStale"):!current!.Running?UiText.C("live.lmuIsNotRunning"):!connected?UiText.C("live.sharedMemoryDisconnectedOrNotValidated"):!fresh?UiText.C("live.noFreshActivePlayerDataPauseGarageOr"):key is null?binding.Reason??UiText.C("live.identityNotYetConfirmedUnambiguously"):observed is null?captureIssue??UiText.C("live.identityConfirmedNotAllFiveElectronicsValuesAre"):UiText.C("live.freshLmuValuesAndUnambiguousMapping");
        if(key is not null&&!fresh)message=UiText.C("live.sessionIdentityConfirmedLibraryValuesAvailableLiveElectronics");
        return new(recent?current!.Running:null,connected||current?.Running==true&&current.SessionSample is {Error:null} ss&&(now-ss.ObservedAt).TotalSeconds is >=0 and <=2,fresh,s?.ObservedAt,
            key is not null?binding.VehicleModelName:fresh?Value(s!,"RawVehicleModel") as string:null,
            key is not null?binding.TrackName:null,key is not null?binding.TrackLayoutName:fresh?Value(s!,"RawTrackName") as string:null,
            key is not null||fresh?Value(key is not null?current!.SessionSample??s!:s!,"Session") as string:null,key is not null||fresh?Value(key is not null?current!.SessionSample??s!:s!,"FixedSetup") as bool?:null,key,values,observed is not null,observed is null?null:token,message,current?.SessionSample is not null?session.Evidence(now):null);
    }
    private ObservedSetup? ObservationLocked(SessionIdentity binding)=>ObservationLocked(binding,out _);
    private ObservedSetup? ObservationLocked(SessionIdentity binding,out string? issue)
    {
        issue=null;
        if(previous is null||current is null)return null;
        try{var snapshot=LiveSetupReader.FromSamples(previous.Sample,current.Sample,binding,current.ProcessInstance,clock.GetUtcNow());SetupLibraryService.ValidateObservation(snapshot,clock.GetUtcNow());return snapshot;}
        catch(LibraryValidationException e){issue=e.Message;return null;}
    }
    public ObservedSetup ReadForContext(string expectedToken)
    {
        lock(gate) {
            var status=StatusLocked();
            if(!status.CanCapture||status.ContextToken!=expectedToken)throw new LibraryConflictException(UiText.C("live.liveContextChangedOrValuesAreNoLonger"));
            return ObservationLocked(identity.Current(clock.GetUtcNow()))??throw new LibraryValidationException(UiText.C("live.liveValuesIncomplete"));
        }
    }
    public ILiveSetupReader Reader(string expectedToken)=>new ContextReader(this,expectedToken);
    private sealed class ContextReader(LiveStateEngine engine,string expected):ILiveSetupReader
    {public Task<ObservedSetup> ReadCurrent(CancellationToken token=default){token.ThrowIfCancellationRequested();return Task.FromResult(engine.ReadForContext(expected));}}
    private static object? Value(Sample s,string k)=>s.Fields.GetValueOrDefault(k)?.Value;
    private static int? Int(Sample s,string k)=>Value(s,k) switch{int i=>i,byte b=>b,short n=>n,_=>null};
    private static double? Number(Sample s,string k)=>Value(s,k) switch{double d when double.IsFinite(d)=>d,float f when float.IsFinite(f)=>f,_=>null};
}
