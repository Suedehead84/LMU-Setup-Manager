using Lmu.Localization;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Lmu.Diagnostics;

if(args.Contains("--help"))
{
    Console.WriteLine("LMU Phase A: [--demo] [--seconds 30] [--json diagnostics/run.jsonl] [--rest http://127.0.0.1:6397] [--verbose]");
    return 0;
}
string Option(string key,string fallback) { int i=Array.IndexOf(args,key); return i>=0 && i+1<args.Length ? args[i+1]:fallback; }
try
{
    var allowed=new HashSet<string>{"--demo","--seconds","--json","--rest","--verbose"};
    for(int i=0;i<args.Length;i++) { if(!allowed.Contains(args[i])) throw new ArgumentException(UiText.C("diagnostics.unknownOption")+args[i]); if(args[i] is not ("--demo" or "--verbose") && ++i>=args.Length) throw new ArgumentException(UiText.C("diagnostics.optionValueMissing")); }
    bool demo=args.Contains("--demo");
    bool verbose=args.Contains("--verbose");
    if(!double.TryParse(Option("--seconds","0"),CultureInfo.InvariantCulture,out double seconds) || !double.IsFinite(seconds) || seconds<0)
        throw new ArgumentException(UiText.C("diagnostics.invalidSeconds"));
    var file=Path.GetFullPath(Option("--json","diagnostics/run-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".jsonl"));
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    using var writer=new DiagnosticLog(new StreamWriter(file,false));
    void Log(string type,object data)=>writer.Write(type,data);
    using var cancel=new CancellationTokenSource();
    Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
    if(seconds>0)cancel.CancelAfter(TimeSpan.FromSeconds(seconds));
    ITelemetrySource source=demo ? new DemoSource() : OperatingSystem.IsWindows() ? new SharedMemorySource() : throw new PlatformNotSupportedException(UiText.C("diagnostics.liveModeRequiresWindows"));
    using var rest=new RestProbe(Option("--rest","http://127.0.0.1:6397"));
    Log("run",new { schemaVersion=5,mode=demo?"DEMO":"LIVE",startedAt=DateTimeOffset.UtcNow, pollMs=100,restPollMs=3000,
        sampleFormat="Values; annotations in sample-schema on change",restFormat="SHA256 BodyId references rest-body; full bodies preserved",
        writer="bounded asynchronous lossless queue; overflow stops run; buffered output flushed on normal exit",verbose });
    Console.WriteLine($"{(demo?UiText.T("diagnostics.demoSynthetic"):UiText.T("diagnostics.liveReadOnly"))} | JSONL: {file}");
    var tracker=new ChangeTracker();
    var identities=new IdentityCoordinator();
    var laps=new LapAnalyzer();
    void PublishLaps(IEnumerable<LapEvent> events)
    {
        foreach(var item in events)
        {
            Log(item.Type,item);
            if(item.Lap is { } lap)
            {
                var time=lap.LapTimeSeconds is { } seconds
                    ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss\.fff",CultureInfo.InvariantCulture) : UiText.T("diagnostics.unknownValue");
                Console.WriteLine($"{item.ObservedAt:O} {item.Type} | Lap={lap.LapNumber} Time={time} Valid={lap.Validity} FullyObserved={lap.FullyObserved} FixedSetup={lap.FixedSetup?.ToString()??"Unknown"} SetupChanged={lap.SetupChanged} ConstantSetup={lap.ConstantSetup} EligibleForSetupHistory={lap.EligibleForSetupHistory} Kind={lap.LapKind}");
            }
            else if(verbose) Console.WriteLine($"{item.ObservedAt:O} {item.Type} | {item.LapId} {JsonSerializer.Serialize(item.Details)}");
        }
    }
    IdentityTicket? pendingTicket=null;
    SessionIdentity? lastIdentity=null;
    Task<RestCapture[]>? pending=null;
    var nextRest=DateTimeOffset.MinValue;
    var nextDisplay=DateTimeOffset.MinValue;
    bool? wasRunning=null;
    string? lastSchema=null;
    while(!cancel.IsCancellationRequested)
    {
        var loop=Stopwatch.StartNew();
        var captureStartedAt=DateTimeOffset.UtcNow;
        bool running=false;
        string processInstance="";
        if(!demo)
        {
            foreach(var process in Process.GetProcessesByName("Le Mans Ultimate"))
            {
                using(process)
                {
                    try { if(process.ProcessName.Equals("Le Mans Ultimate",StringComparison.OrdinalIgnoreCase)) {running=true;processInstance=process.Id.ToString(CultureInfo.InvariantCulture);} }
                    catch(InvalidOperationException) { }
                }
            }
        }
        double processMs=loop.Elapsed.TotalMilliseconds;
        var readStartedAt=DateTimeOffset.UtcNow;
        var sample=source.Read(readStartedAt);
        var now=DateTimeOffset.UtcNow;
        sample=sample with {ObservedAt=now};
        double readMs=loop.Elapsed.TotalMilliseconds-processMs;
        sample.Fields["GameRunning"]=new(demo?null:running,demo?"demo":"Windows.Process");
        if(!demo && !running) sample=new(now,"LIVE",Decoder.Empty(),UiText.C("diagnostics.lmuProcessNotFound"));
        sample.Fields["GameRunning"]=new(demo?null:running,demo?"demo":"Windows.Process");
        identities.UpdateLive(sample,processInstance);
        if(!demo && pending is null && now>=nextRest)
        { pendingTicket=identities.BeginRequest(now);pending=rest.Poll(cancel.Token);nextRest=now.AddSeconds(3); }
        if(pending?.IsCompleted==true)
        {
            var captures=await pending;
            if(pendingTicket is not null) identities.Complete(pendingTicket,captures,DateTimeOffset.UtcNow);
            foreach(var capture in captures)
            {
                writer.Rest(capture);
                if(capture.Url.EndsWith("/rest/garage/summary")) Log("garage-ui-context",GarageUiContext.Read(capture,sample));
                if(capture.Status==200 && capture.Url.EndsWith("/rest/garage/summary") && capture.Body is not null)
                {
                    try
                    {
                        using var json=JsonDocument.Parse(capture.Body);
                        var fixedCandidate=Decoder.GarageFixed(json.RootElement);
                        Log("garageFixedCandidate",new { capture.ObservedAt,value=fixedCandidate,source=capture.Url,note=UiText.C("diagnostics.garageUiContextNotEvidenceOfActiveVehicle") });
                        if(verbose) Console.WriteLine(UiText.C("diagnostics.garageUiFixedsetupraceNotLiveStatus",(fixedCandidate?.ToString() ?? UiText.T("diagnostics.unknownValue"))));
                    } catch(JsonException) { }
                }
            }
            pending=null;
        }
        var identity=identities.Current(DateTimeOffset.UtcNow);
        sample.Fields["CatalogRefreshWarning"]=new(identities.CatalogRefreshWarning,"REST content catalogs");
        identity.AddTo(sample);
        double identityFinishedMs=loop.Elapsed.TotalMilliseconds;
        if(!demo) PublishLaps(laps.Update(sample,identity,processInstance));
        double analyzerFinishedMs=loop.Elapsed.TotalMilliseconds;
        if(identity!=lastIdentity) { Log("identity",new { sample.ObservedAt,identity });lastIdentity=identity; }
        var schema=sample.Fields.ToDictionary(p=>p.Key,p=>new {p.Value.Source,p.Value.Unit,p.Value.Note});
        var schemaJson=JsonSerializer.Serialize(schema);
        if(schemaJson!=lastSchema){Log("sample-schema",schema);lastSchema=schemaJson;}
        Log("sample",new {sample.ObservedAt,sample.Mode,sample.Error,ProcessInstance=processInstance,
            Values=sample.Fields.ToDictionary(p=>p.Key,p=>p.Value.Value)});
        foreach(var change in tracker.Update(sample))
        {
            Log("change",change);
            if(change.Field is "TC" or "TCCut" or "TCSlip" or "ABS" or "BrakeBias" or "FixedSetup")
                Console.WriteLine($"{now:O} {change.Field}: {change.Before?.Value ?? UiText.T("diagnostics.unavailable")} -> {change.After.Value ?? UiText.T("diagnostics.unavailable")} ({change.Kind})");
        }
        if(now>=nextDisplay || running!=wasRunning)
        {
            Console.WriteLine($"{now:HH:mm:ss} LMU={(demo?"DEMO":running?UiText.T("diagnostics.running"):UiText.T("diagnostics.notRunning"))} | {UiText.Localize(sample.Error ?? UiText.C("diagnostics.sharedMemoryConnected"))}");
            if(verbose) foreach(var (name,datum) in sample.Fields) Console.WriteLine($"  {name,-20} {datum.Value ?? UiText.T("diagnostics.unavailable")} {datum.Unit} [{datum.Source}]");
            nextDisplay=now.AddSeconds(2);wasRunning=running;
        }
        Log("sampler-performance",new {sample.ObservedAt,captureStartedAt,readStartedAt,CaptureFinishedAt=now,
            ProcessMs=processMs,ReadMs=readMs,IdentityAndRestMs=identityFinishedMs-processMs-readMs,
            AnalyzerAndEventOutputMs=analyzerFinishedMs-identityFinishedMs,
            LoggingAndConsoleMs=loop.Elapsed.TotalMilliseconds-analyzerFinishedMs,
            LoopWorkMs=loop.Elapsed.TotalMilliseconds,WrittenLines=writer.WrittenLines});
        try {await Task.Delay(demo?500:100,cancel.Token);} catch(OperationCanceledException){break;}
    }
    if(!demo) PublishLaps(laps.Stop(DateTimeOffset.UtcNow));
    if(pending is not null) foreach(var capture in await pending)writer.Rest(capture);
    Log("end",new {at=DateTimeOffset.UtcNow});
    return 0;
}
catch(Exception e) when(e is ArgumentException or IOException or PlatformNotSupportedException)
{ Console.Error.WriteLine(UiText.Localize(e.Message));return 2; }





