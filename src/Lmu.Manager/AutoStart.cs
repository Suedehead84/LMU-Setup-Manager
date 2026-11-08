using Lmu.Localization;
using Microsoft.Win32;
namespace Lmu.Manager;
public interface IAutoStart{bool Enabled{get;}void Set(bool enabled);}
public sealed class WindowsAutoStart(string executable,string databasePath,string settingsDirectory):IAutoStart
{
 private const string Key="Software\\Microsoft\\Windows\\CurrentVersion\\Run",Name="LMU Setup Manager";
 public bool Enabled{get{using var k=Registry.CurrentUser.OpenSubKey(Key);return k?.GetValue(Name) as string==Command;}}
 private string Command=>BuildCommand(executable,databasePath,settingsDirectory);
 public static string BuildCommand(string executable,string databasePath,string settingsDirectory){string Quote(string p){var full=Path.GetFullPath(p);if(full.Contains('"'))throw new ArgumentException(UiText.C("manager.invalidAutostartPath"));return "\""+full+"\"";}return Quote(executable)+" --db "+Quote(databasePath)+" --settings-dir "+Quote(settingsDirectory);}
 public void Set(bool enabled){using var k=Registry.CurrentUser.CreateSubKey(Key,true);if(enabled)k.SetValue(Name,Command,RegistryValueKind.String);else k.DeleteValue(Name,false);}
}
