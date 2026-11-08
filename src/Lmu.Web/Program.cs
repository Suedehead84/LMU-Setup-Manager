using Lmu.Localization;
using Lmu.Web;
using Lmu.SetupLibrary;
using System.Diagnostics;
using System.Net;
using System.Reflection;

var version=typeof(WebHostFactory).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

if(args.Contains("--help")) {
    Console.WriteLine("LMU Web "+version+" (B2.6): [--db <library.sqlite>] [--port 5087] [--open] [--access-file <DPAPI-file>]\nDefault database: "+LibraryDatabase.DefaultPath);return;
}
if(args.Contains("--lan")){Console.Error.WriteLine(UiText.Localize(UiText.C("api.homeNetworkAccessIsManagedExclusivelyThroughLmu")));Environment.ExitCode=2;return;}
string Option(string key,string fallback){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
for(int i=0;i<args.Length;i++) {
    if(args[i] is "--lan" or "--open")continue;
    if(args[i] is not ("--db" or "--port" or "--access-file")||++i>=args.Length)throw new ArgumentException(UiText.C("api.unknownOrIncompleteOption"));
}
if(!int.TryParse(Option("--port","5087"),out int port)||port is <1 or >65535)throw new ArgumentException(UiText.C("api.invalidPort"));
var options=new ServerOptions(Path.GetFullPath(Option("--db",LibraryDatabase.DefaultPath)),port,args.Contains("--lan"));
await using var app=WebHostFactory.Create(options);
await app.StartAsync();
var local=$"http://127.0.0.1:{port}/#access={options.AccessKey}";
Console.WriteLine("LMU Setup Manager "+version+" (B2.6) | "+options.DatabasePath);
var accessFile=Option("--access-file","");if(accessFile.Length>0)File.WriteAllBytes(Path.GetFullPath(accessFile),WindowsSecrets.Protect(System.Text.Encoding.UTF8.GetBytes(options.AccessKey)));
Console.WriteLine($"Browser: http://127.0.0.1:{port}/");
if(options.Lan)foreach(var ip in Dns.GetHostAddresses(Dns.GetHostName()).Where(ip=>ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&!IPAddress.IsLoopback(ip)))
    Console.WriteLine(UiText.Localize(UiText.C("api.homeNetworkHttp",(ip),(port))));
Console.WriteLine(UiText.Localize(UiText.C("api.signInUsingOpenTheAccessCodeIs")));
if(args.Contains("--open"))Process.Start(new ProcessStartInfo(local){UseShellExecute=true});
await app.WaitForShutdownAsync();
