using Lmu.Localization;
using System.IO.Compression;
using System.Text.Json;
using Lmu.Diagnostics;
namespace Lmu.Replay;

public sealed record RecordedSample(DateTimeOffset At,string Mode,string? Error,Dictionary<string,JsonElement> Fields,int Line,string? ProcessInstance=null);
public sealed record RecordedCapture(int Line,int SampleIndex,DateTimeOffset At,DateTimeOffset RequestedAt,
    string Url,int? Status,string? Error,string? Body,string? Catalog);
public sealed record RecordedLapRun(string SourceSha256,List<RecordedSample> Samples,List<RecordedCapture> Rest,
    Dictionary<string,string> Catalogs,List<LapRecord> OriginalCompletedLaps)
{
    public static RecordedLapRun Load(string path)
    {
        using var file=File.OpenRead(path);
        using var zip=new GZipStream(file,CompressionMode.Decompress);
        return JsonSerializer.Deserialize<RecordedLapRun>(zip,new JsonSerializerOptions {PropertyNameCaseInsensitive=true})
            ??throw new InvalidDataException(UiText.C("diagnostics.replayFileIsEmpty"));
    }
}
public sealed record IdentityReplayObservation(DateTimeOffset At,SessionIdentity Identity);
public sealed record ReplayEvaluation(string Mode,int Samples,List<IdentityReplayObservation> IdentityTransitions,
    List<LapEvent> Events,int TicketsWithoutRecordedStart)
{
    public IEnumerable<LapRecord> CompletedLaps=>Events.Where(e=>e.Type=="lap-complete").Select(e=>e.Lap!);
}
public static class RecordedLapReplay
{
    private static readonly HashSet<string> FloatingFields=[
        "TelemetryElapsed","LapStartET","ScoringLapStartET","SessionElapsed","SessionStart",
        "LastLapTime","BestLap","BrakeBias","CurrentLap","RawLastLapTime",
        "RawLastSector1","RawLastSector2","RawCurrentSector1","RawCurrentSector2"];
    public static Sample Materialize(RecordedSample row)
    {
        var fields=new Dictionary<string,Datum>();
        foreach(var (key,value) in row.Fields)
        {
            object? parsed=value.ValueKind switch {
                JsonValueKind.Null=>null,JsonValueKind.True=>true,JsonValueKind.False=>false,
                JsonValueKind.String=>value.GetString(),
                JsonValueKind.Number when FloatingFields.Contains(key)=>value.GetDouble(),
                JsonValueKind.Number when value.TryGetInt32(out var n)=>n,
                JsonValueKind.Number=>value.GetDouble(),_=>null };
            fields[key]=new(parsed,"recorded source (replay)");
        }
        return new(row.At,row.Mode,fields,row.Error);
    }
    private sealed record Batch(int Index,DateTimeOffset Start,DateTimeOffset Finished,RestCapture[] Captures);
    public static ReplayEvaluation Evaluate(RecordedLapRun input,bool strictNegativeStart=false,bool recordedIdentities=false)
    {
        // Recorded transport order is the original six-path Poll order. Never send an HTTP request.
        var batches=input.Rest.Chunk(6).Select(rows=>
        {
            if(rows.Length!=6 || rows.Select(r=>r.SampleIndex).Distinct().Count()!=1)
                throw new InvalidDataException(UiText.C("diagnostics.noCompleteRecordedSixResponseRestBatches"));
            return new Batch(rows[0].SampleIndex,rows.Min(r=>r.RequestedAt),rows.Max(r=>r.At),
                rows.Select(r=>new RestCapture(r.At,r.Url,r.Status,
                    r.Catalog is { } catalog ? input.Catalogs[catalog]:r.Body,r.Error,r.RequestedAt)).ToArray());
        }).ToArray();
        var coordinator=new IdentityCoordinator();
        var analyzer=new LapAnalyzer();
        var events=new List<LapEvent>();
        var identities=new List<IdentityReplayObservation>();
        var tickets=new Dictionary<int,IdentityTicket>();
        var processed=new HashSet<int>();
        SessionIdentity? last=null;int unbound=0;
        for(int i=0;i<input.Samples.Count;i++)
        {
            var sample=Materialize(input.Samples[i]);
            for(int b=0;b<batches.Length;b++)
                if(!tickets.ContainsKey(b) && batches[b].Start<sample.ObservedAt && i>0)
                    tickets[b]=coordinator.BeginRequest(batches[b].Start);
            string process=sample.Fields.GetValueOrDefault("GameRunning")?.Value is false ? "" :
                input.Samples[i].ProcessInstance??"recorded-process (original PID unavailable)";
            coordinator.UpdateLive(sample,process);
            var evaluationAt=sample.ObservedAt;
            for(int b=0;b<batches.Length;b++)
            {
                var batch=batches[b];
                if(batch.Index!=i || !processed.Add(b))continue;
                if(!tickets.TryGetValue(b,out var ticket))
                { ticket=coordinator.BeginRequest(batch.Start);unbound++; }
                if(batch.Finished>evaluationAt)evaluationAt=batch.Finished;
                coordinator.Complete(ticket,batch.Captures,evaluationAt);
            }
            var identity=coordinator.Current(evaluationAt);
            if(recordedIdentities)
            {
                string? Get(string key)=>sample.Fields.GetValueOrDefault(key)?.Value as string;
                identity=new(Get("VehicleModelId"),Get("VehicleModelName"),Get("TrackId"),Get("TrackName"),
                    Get("TrackLayoutId"),Get("TrackLayoutName"),Get("IdentitySource")??"none",
                    Get("IdentityConfidence")??"Unknown",Get("IdentityReason"));
            }
            identity.AddTo(sample);
            if(identity!=last){identities.Add(new(sample.ObservedAt,identity));last=identity;}
            // Explicitly isolate the independent original -1 sentinel rejection for causal comparison.
            if(strictNegativeStart && sample.Fields.GetValueOrDefault("LapStartET")?.Value is double start && start<0)
                events.AddRange(analyzer.Interrupt(sample.ObservedAt,UiText.C("diagnostics.original1StartTimeCheckReplayComparison")));
            else events.AddRange(analyzer.Update(sample,identity,process));
        }
        if(input.Samples.Count>0)events.AddRange(analyzer.Stop(input.Samples[^1].At.AddTicks(1)));
        string mode=recordedIdentities?"RecordedIdentityWithOriginalSentinelGate":
            strictNegativeStart?"CorrectedIdentityOnly":"CorrectedIdentityAndSentinelAnchor";
        return new(mode,input.Samples.Count,identities,events,unbound);
    }
}
