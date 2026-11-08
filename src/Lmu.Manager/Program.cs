using Lmu.Localization;
using Lmu.SetupLibrary;
namespace Lmu.Manager;
internal static class Program
{
 [STAThread]private static void Main(string[] args)
 {
  ApplicationConfiguration.Initialize();
  string Option(string key,string fallback){int i=Array.IndexOf(args,key);return i<0?fallback:i+1<args.Length?args[i+1]:throw new ArgumentException(UiText.C("manager.missingValue")+key);}
  try{
   foreach(var arg in args.Where(a=>a.StartsWith("--",StringComparison.Ordinal)))if(arg is not ("--db" or "--settings-dir" or "--no-browser" or "--command" or "--port"))throw new ArgumentException(UiText.C("manager.unknownOption")+arg);
   var directory=Option("--settings-dir",SettingsStore.DefaultDirectory);using var instance=new SingleInstance(directory);
   if(!instance.IsOwner){instance.Send(Option("--command","open")).GetAwaiter().GetResult();return;}
   if(args.Contains("--command"))return;
   var controller=new ManagerController(new(directory),Option("--db",LibraryDatabase.DefaultPath),new WindowsPrivateNetworks(),new WindowsAutoStart(Environment.ProcessPath!,Option("--db",LibraryDatabase.DefaultPath),directory),args.Contains("--port")?int.Parse(Option("--port","5087")):null);
   using var tray=new TrayContext(controller,instance,args.Contains("--no-browser"));Application.Run(tray);
  }catch(Exception){MessageBox.Show(UiText.T("manager.lmuSetupManagerCouldNotStartCheckSettings"),"LMU Setup Manager",MessageBoxButtons.OK,MessageBoxIcon.Error);}
 }
}
