using System.Diagnostics;
using Lmu.LiveCapture;
using Lmu.Diagnostics;
namespace Lmu.Web;

public sealed class LmuLiveWorker(INativeFrameSource source,ILmuCatalogTransport transport,LiveStateEngine state,LiveHealth? diagnostics=null):BackgroundService
{
    public LiveHealth Health {get;}=diagnostics??new();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task<RestCapture[]>? pending=null;IdentityTicket? ticket=null;var next=DateTimeOffset.MinValue;
        Task<RestCapture?>? navigation=null;NavigationTicket? navigationTicket=null;var nextNavigation=DateTimeOffset.MinValue;
        var catalogRetry=new RetryDelay(TimeSpan.FromSeconds(3),TimeSpan.FromSeconds(24));
        var navRetry=new RetryDelay(TimeSpan.FromMilliseconds(500),TimeSpan.FromSeconds(8));
        long last=Stopwatch.GetTimestamp(),catalogStart=0,navStart=0;
        static bool Ok(RestCapture[] captures,int count)=>captures.Length==count&&captures.All(c=>c.Status is >=200 and <300&&c.Error is null);
        async Task<RestCapture[]> Catalog(){try{return await transport.Read(stoppingToken);}catch(Exception e)when(e is HttpRequestException or IOException or TimeoutException){return [];}}
        async Task<RestCapture?> Navigation(){try{return await transport.ReadNavigation(stoppingToken);}catch(Exception e)when(e is HttpRequestException or IOException or TimeoutException){return null;}}
        try {
            while(!stoppingToken.IsCancellationRequested) {
                long begin=Stopwatch.GetTimestamp();var frame=source.Read();double readMs=Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
                state.Observe(frame);Health.Native(frame,readMs,Stopwatch.GetElapsedTime(last,begin).TotalMilliseconds);last=begin;
                var now=DateTimeOffset.UtcNow;
                if(navigation?.IsCompleted==true){var capture=await navigation;RestCapture[] captures=capture is null?[]:[capture];state.CompleteNavigation(navigationTicket!,capture);Health.Rest("navigation",captures,Stopwatch.GetElapsedTime(navStart).TotalMilliseconds,1);nextNavigation=now+navRetry.Complete(Ok(captures,1));navigation=null;}
                if(pending?.IsCompleted==true){var captures=await pending;state.Complete(ticket!,captures);Health.Rest("catalog",captures,Stopwatch.GetElapsedTime(catalogStart).TotalMilliseconds,2);next=now+catalogRetry.Complete(Ok(captures,2));pending=null;}
                if(frame.Running&&navigation is null&&now>=nextNavigation){navigationTicket=state.BeginNavigation();navStart=Stopwatch.GetTimestamp();navigation=Navigation();}
                if(frame.Running&&pending is null&&now>=next){ticket=state.BeginRequest();catalogStart=Stopwatch.GetTimestamp();pending=Catalog();}
                await Task.Delay(100,stoppingToken);
            }
        }catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){}
        finally {try{if(pending is not null)await pending;if(navigation is not null)await navigation;}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){}}
    }
}
