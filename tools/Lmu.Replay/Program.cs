using System.Text.Json;
using Lmu.Replay;
if(args.Length>=3 && args[0]=="--audit")
{
    var paths=args.Skip(2).SelectMany(p=>Directory.Exists(p)?Directory.EnumerateFiles(p,"*.jsonl",SearchOption.AllDirectories):[p]).Distinct().Order();
    var audits=paths.Select(p=>{var a=ScoringAudit.Read(p);Console.WriteLine($"{p}: {a.LiveSamples} live samples, {a.FlagChanges.Count} flag states, {a.RecordedCompletedLaps.Count} recorded completions");return a;}).ToArray();
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    File.WriteAllText(args[1],JsonSerializer.Serialize(audits,new JsonSerializerOptions{WriteIndented=true}));
    return 0;
}
if(args.Length!=2)
{
    Console.Error.WriteLine("Offline replay: Lmu.Replay <replay-input.json.gz> <report.json> | --audit <report.json> <JSONL files/directories...>");
    return 2;
}
var input=RecordedLapRun.Load(args[0]);
var results=new[]{
    RecordedLapReplay.Evaluate(input,strictNegativeStart:true,recordedIdentities:true),
    RecordedLapReplay.Evaluate(input,strictNegativeStart:true),
    RecordedLapReplay.Evaluate(input)
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(args[1],JsonSerializer.Serialize(new {
    Classification="OFFLINE REPLAY of recorded live inputs; not a new LMU drive",
    input.SourceSha256,input.OriginalCompletedLaps,Results=results
},new JsonSerializerOptions {WriteIndented=true}));
foreach(var result in results)
{
    Console.WriteLine($"{result.Mode}: {result.Samples} samples, {result.IdentityTransitions.Count} identity transitions, unbound tickets={result.TicketsWithoutRecordedStart}");
    foreach(var lap in result.CompletedLaps)
        Console.WriteLine($"Lap {lap.LapNumber}: {lap.LapTimeSeconds} s, Full={lap.FullyObserved}, {lap.Validity}, Constant={lap.ConstantSetup}, Eligible={lap.EligibleForSetupHistory}, {string.Join("; ",lap.Limitations)}");
}
return 0;
