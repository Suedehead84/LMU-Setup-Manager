using System.Diagnostics;
using Lmu.Diagnostics;
using Lmu.LiveCapture;
namespace Lmu.Web;

public sealed record HealthEvent(DateTimeOffset At,string Channel,string State,double? OutageMilliseconds);
public sealed record HealthWindow(DateTimeOffset At,double WallMilliseconds,double CpuMilliseconds,long WorkingSetBytes,int ProcessThreads,long PendingWorkItems,int AvailableWorkers,long AllocatedBytes,int NativeReads,double MaxNativeReadMilliseconds,double MaxPollGapMilliseconds,int RestRequests,int RestFailures,double MaxRestMilliseconds);

// Bounded, in-memory and owner-only. No bodies, exception text, identifiers or credentials.
// These are manager measurements, not game frametimes or wheelbase measurements.
public sealed class LiveHealth
{
 private readonly object gate=new();private readonly Queue<HealthEvent> events=new();private readonly Queue<HealthWindow> windows=new();
 private readonly Dictionary<string,(string State,DateTimeOffset Since)> channels=new();
 private DateTimeOffset start=DateTimeOffset.UtcNow;private TimeSpan cpu=ReadCpu();private long allocated=GC.GetTotalAllocatedBytes();
 private int reads,requests,failures;private double nativeMax,gapMax,restMax;
 private object? latestSources;
 private static TimeSpan ReadCpu(){using var p=Process.GetCurrentProcess();return p.TotalProcessorTime;}
 private void Transition(DateTimeOffset at,string channel,string value)
 {
  if(channels.TryGetValue(channel,out var old)&&old.State==value)return;
  bool wasUnavailable=channels.TryGetValue(channel,out old)&&old.State!="ok";
  double? duration=value=="ok"&&wasUnavailable?(at-old.Since).TotalMilliseconds:null;
  channels[channel]=(value,wasUnavailable&&value!="ok"?old.Since:at);events.Enqueue(new(at,channel,value,duration));while(events.Count>128)events.Dequeue();
 }
 public void Native(NativeFrame frame,double milliseconds,double gapMilliseconds)
 {
  lock(gate){reads++;nativeMax=Math.Max(nativeMax,milliseconds);gapMax=Math.Max(gapMax,gapMilliseconds);
   var now=DateTimeOffset.UtcNow;
   latestSources=new{At=now,TelemetryObservedAt=frame.Sample.ObservedAt,TelemetrySampleAgeMilliseconds=(now-frame.Sample.ObservedAt).TotalMilliseconds,ScoringObservedAt=frame.SessionSample?.ObservedAt,ScoringSampleAgeMilliseconds=frame.SessionSample is {} scoring?(double?)(now-scoring.ObservedAt).TotalMilliseconds:null};
   Transition(now,"process",frame.Running?"ok":"absent");
   Transition(now,"telemetry",!frame.Running?"process-absent":frame.Sample.Error is not null?"unavailable-or-unvalidated":(now-frame.Sample.ObservedAt).TotalSeconds>2?"stale":"ok");
   Transition(now,"scoring",!frame.Running?"process-absent":frame.SessionSample is null?"unavailable":frame.SessionSample.Error is null?"ok":frame.SessionSample.Fields.GetValueOrDefault("SessionSourceStatus")?.Value as string=="Frozen"?"frozen":"unavailable-or-unvalidated");
   if((now-start).TotalSeconds<5)return;
   using var p=Process.GetCurrentProcess();var next=p.TotalProcessorTime;long bytes=GC.GetTotalAllocatedBytes();ThreadPool.GetAvailableThreads(out int available,out _);
   windows.Enqueue(new(now,(now-start).TotalMilliseconds,(next-cpu).TotalMilliseconds,p.WorkingSet64,p.Threads.Count,ThreadPool.PendingWorkItemCount,available,bytes-allocated,reads,nativeMax,gapMax,requests,failures,restMax));while(windows.Count>120)windows.Dequeue();
   start=now;cpu=next;allocated=bytes;reads=requests=failures=0;nativeMax=gapMax=restMax=0;
  }
 }
 public void Rest(string channel,RestCapture[] captures,double milliseconds,int expected)
 {
  bool ok=captures.Length==expected&&captures.All(c=>c.Status is >=200 and <300&&c.Error is null);
  lock(gate){requests+=expected;if(!ok)failures++;restMax=Math.Max(restMax,milliseconds);Transition(DateTimeOffset.UtcNow,channel,ok?"ok":captures.Any(c=>c.Status is >=400)?"http-error":"timeout-or-transport-error");}
 }
 public object Snapshot(){lock(gate)return new{Schema=1,CapturedAt=DateTimeOffset.UtcNow,LatestSources=latestSources,Events=events.ToArray(),Windows=windows.ToArray(),Current=new{NativeReads=reads,RestRequests=requests,RestFailures=failures,MaxNativeReadMilliseconds=nativeMax,MaxPollGapMilliseconds=gapMax,MaxRestMilliseconds=restMax}};}
}

public sealed class RetryDelay(TimeSpan normal,TimeSpan maximum)
{
 private int failures;
 public TimeSpan Complete(bool success){failures=success?0:Math.Min(failures+1,10);return TimeSpan.FromMilliseconds(Math.Min(maximum.TotalMilliseconds,normal.TotalMilliseconds*Math.Pow(2,failures)));}
}
