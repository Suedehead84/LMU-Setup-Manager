using Lmu.Localization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
namespace Lmu.Diagnostics;

// Lossless, bounded queue. A stalled disk stops the run explicitly instead of dropping samples.
public sealed class DiagnosticLog : IDisposable
{
    private readonly Channel<string> queue;
    private readonly Task worker;
    private readonly HashSet<string> bodies=[];
    private long written;
    public long WrittenLines=>Interlocked.Read(ref written);
    public DiagnosticLog(TextWriter output,int capacity=4096)
    {
        queue=Channel.CreateBounded<string>(new BoundedChannelOptions(capacity) {
            SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait });
        worker=Task.Run(async ()=>{
            try {
                await foreach(var line in queue.Reader.ReadAllAsync()) {
                    await output.WriteLineAsync(line);Interlocked.Increment(ref written);
                }
                await output.FlushAsync();
            } finally {output.Dispose();}
        });
    }
    public void Write(string type,object data)
    {
        if(worker.IsFaulted)throw new IOException(UiText.C("diagnostics.couldNotWriteTheDiagnosticLog"),worker.Exception);
        var line=JsonSerializer.Serialize(new {type,data});
        if(!queue.Writer.TryWrite(line))throw new IOException(UiText.C("diagnostics.diagnosticLogOverloadedRunStoppedWithoutSilentlyDropping"));
    }
    public void Rest(RestCapture capture)
    {
        string? bodyId=null;
        if(capture.Body is { } body) {
            bodyId=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
            if(bodies.Add(bodyId))Write("rest-body",new {BodyId=bodyId,Body=body});
        }
        Write("rest",new {capture.ObservedAt,capture.RequestedAt,capture.Url,capture.Status,capture.Error,BodyId=bodyId});
    }
    public void Dispose()
    {
        queue.Writer.TryComplete();
        try {worker.GetAwaiter().GetResult();}
        catch(Exception e){throw new IOException(UiText.C("diagnostics.diagnosticLogWasNotWrittenCompletely"),e);}
    }
}
