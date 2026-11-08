using Lmu.Diagnostics;
using System.Text;
using System.Text.Json;
internal static class DiagnosticLogTests
{
    private sealed class BlockedWriter : TextWriter
    {
        public override Encoding Encoding=>Encoding.UTF8;
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Lines {get;}=[];
        public override async Task WriteLineAsync(string? value) {
            Entered.TrySetResult();await Release.Task;Lines.Add(value!);
        }
    }
    public static async Task Run(Action<bool,string> check)
    {
        var output=new BlockedWriter();
        var log=new DiagnosticLog(output,2);
        try {
            log.Write("first",new {n=1});
            await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            log.Write("second",new {n=2});log.Write("third",new {n=3});
            check(output.Lines.Count==0,"blocked disk does not block sampler enqueue");
            bool stopped=false;
            try{log.Write("fourth",new {n=4});}catch(IOException){stopped=true;}
            check(stopped,"full logging queue explicitly stops instead of silently dropping samples");
        } finally {output.Release.TrySetResult();log.Dispose();}
        check(output.Lines.Count==3,"normal logger disposal drains every accepted record");
        var text=new StringWriter();
        using(var writer=new DiagnosticLog(text)) {
            var capture=new RestCapture(DateTimeOffset.UtcNow,"http://local/example",200,"{\"raw\":true}",null,DateTimeOffset.UtcNow);
            writer.Rest(capture);writer.Rest(capture);
        }
        var rows=text.ToString().Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries).Select(s=>JsonDocument.Parse(s)).ToArray();
        check(rows.Length==3 && rows[0].RootElement.GetProperty("type").GetString()=="rest-body",
            "repeated REST body stored once without discarding either response");
        var body=rows[0].RootElement.GetProperty("data");
        check(body.GetProperty("Body").GetString()=="{\"raw\":true}" && rows.Skip(1).All(r=>r.RootElement.GetProperty("data").GetProperty("BodyId").GetString()==body.GetProperty("BodyId").GetString()),
            "deduplicated responses reference the exact preserved raw body");
        foreach(var row in rows)row.Dispose();
    }
}
