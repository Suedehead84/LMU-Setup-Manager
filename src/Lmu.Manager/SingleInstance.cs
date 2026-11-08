using Lmu.Localization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
namespace Lmu.Manager;
public sealed class SingleInstance:IDisposable
{
 private readonly Mutex mutex;private bool owned;private readonly CancellationTokenSource stop=new();private Task? server;
 public string PipeName{get;}
 public SingleInstance(string directory){var user=WindowsIdentity.GetCurrent().User?.Value??throw new InvalidOperationException(UiText.C("manager.cannotIdentifyTheWindowsUser"));var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToUpperInvariant())))[..16];PipeName="LmuSetupManager-"+user+"-"+hash;mutex=new(false,"Local\\"+PipeName);try{owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}}
 public bool IsOwner=>owned;
 public async Task<string?> Send(string command){using var client=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));await client.ConnectAsync(timeout.Token);using var writer=new StreamWriter(client,new UTF8Encoding(false),1024,true){AutoFlush=true};using var reader=new StreamReader(client,Encoding.UTF8,false,1024,true);await writer.WriteLineAsync(command);return await reader.ReadLineAsync(timeout.Token);}
 public void Listen(Func<string,Task<string>> command){server=Task.Run(async()=>{while(!stop.IsCancellationRequested){try{using var pipe=new NamedPipeServerStream(PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);await pipe.WaitForConnectionAsync(stop.Token);using var reader=new StreamReader(pipe,Encoding.UTF8,false,1024,true);using var writer=new StreamWriter(pipe,new UTF8Encoding(false),1024,true){AutoFlush=true};var line=await reader.ReadLineAsync(stop.Token);await writer.WriteLineAsync(await command(line??""));}catch(OperationCanceledException)when(stop.IsCancellationRequested){break;}catch(IOException){/* A departing second launch must not terminate the owner. */}}});}
 public void Dispose(){stop.Cancel();if(owned){mutex.ReleaseMutex();owned=false;}mutex.Dispose();stop.Dispose();}
}
