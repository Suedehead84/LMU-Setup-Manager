namespace Lmu.Manager;

// Only expensive hardware classification is cached, never network approval/profile/address.
// Single-flight: concurrent browsers cannot start concurrent WMI hardware queries.
public sealed class DiscoverySnapshot<T>(TimeSpan lifetime,TimeProvider? timeProvider=null)
{
 private readonly object gate=new();private readonly TimeProvider clock=timeProvider??TimeProvider.System;
 private T? value;private bool populated;private string? topology;private long observed;
 public T Read(string key,Func<T> discover)
 {
  lock(gate){if(populated&&key==topology&&clock.GetElapsedTime(observed)<lifetime)return value!;
   populated=false;var next=discover();value=next;topology=key;observed=clock.GetTimestamp();populated=true;return next;}
 }
}
