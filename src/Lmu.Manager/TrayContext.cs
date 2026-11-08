using Lmu.Localization;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
namespace Lmu.Manager;

internal sealed class TrayContext:ApplicationContext
{
 private string Language=>controller.TrayLanguage;
 private readonly ManagerController controller;private readonly NotifyIcon icon;private readonly Icon image;private readonly Control dispatcher=new();private readonly System.Windows.Forms.Timer timer=new(){Interval=2000};private readonly ToolStripMenuItem lan=new();private bool quitting;
 public TrayContext(ManagerController controller,SingleInstance instance,bool suppressBrowser)
 {
  this.controller=controller;dispatcher.CreateControl();using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("manager.ico")!;image=new Icon(stream);icon=new NotifyIcon{Icon=image,Text="LMU Setup Manager",Visible=true};
  var menu=new ContextMenuStrip();menu.Items.Add(UiText.T("manager.openWebInterface",Language),null,(_,_)=>Open());lan.Text=UiText.T("manager.enableHomeNetworkAccess",Language);lan.Click+=(_,_)=>{try{var s=controller.View().Settings;if(!s.LanEnabled&&s.Network is null){Open("settings");Error(UiText.T("manager.selectANetworkAndConfirmTheSetupWizard",Language));return;}controller.RequestHomeNetwork(new(!s.LanEnabled));}catch(Exception e){Error(e.Message);}};menu.Items.Add(lan);menu.Items.Add(UiText.T("manager.openSettings",Language),null,(_,_)=>Open("settings"));menu.Items.Add(UiText.T("manager.showServerStatus",Language),null,(_,_)=>Status());menu.Items.Add(new ToolStripSeparator());menu.Items.Add(UiText.T("manager.quitLmuSetupManager",Language),null,async(_,_)=>await Quit());icon.ContextMenuStrip=menu;icon.DoubleClick+=(_,_)=>Open();
  controller.Changed+=Update;timer.Tick+=async(_,_)=>{try{await controller.CheckNetworkAsync();}catch{Error(UiText.T("manager.networkCheckFailedPleaseCheckServerStatus",Language));}};timer.Start();SystemEvents.SessionEnding+=SessionEnding;
  instance.Listen(command=>{var done=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);dispatcher.BeginInvoke(()=>{switch(command){case "open":Open();done.SetResult("OK");break;case "status":done.SetResult(JsonSerializer.Serialize(controller.View()));break;case "exit":done.SetResult("OK");_ = Quit();break;default:done.SetResult("INVALID");break;}});return done.Task;});
  _=Start(suppressBrowser);
 }
 private async Task Start(bool suppress){try{await controller.StartAsync();Update();if(!suppress&&controller.View().Settings.OpenBrowser)Open();}catch(Exception e){Error(e.Message);Update();}}
 private void Open(string page="dashboard"){try{if(!controller.View().Running){Status();return;}Process.Start(new ProcessStartInfo(controller.LocalUrl(page)){UseShellExecute=true});}catch{Error(UiText.T("manager.couldNotOpenTheDefaultBrowserCheckThe",Language));}}
 private void Status(){var s=controller.View();MessageBox.Show(UiText.Localize(s.Message,Language)+UiText.T("manager.version",Language)+s.Version+UiText.T("manager.port",Language)+s.Settings.Port+UiText.T("manager.database",Language)+s.DatabasePath+"\nBackups: "+s.BackupPath+UiText.T("manager.homeNetwork",Language)+(s.LanActive?UiText.T("manager.active",Language):UiText.T("manager.off",Language)),UiText.T("manager.lmuSetupManagerServerStatus",Language),MessageBoxButtons.OK,s.Running?MessageBoxIcon.Information:MessageBoxIcon.Warning);}
 private void Update(){if(dispatcher.IsDisposed)return;if(dispatcher.InvokeRequired){dispatcher.BeginInvoke(Update);return;}var s=controller.View();var items=icon.ContextMenuStrip!.Items;items[0].Text=UiText.T("manager.openWebInterface",Language);items[2].Text=UiText.T("manager.openSettings",Language);items[3].Text=UiText.T("manager.showServerStatus",Language);items[5].Text=UiText.T("manager.quitLmuSetupManager",Language);lan.Text=s.Settings.LanEnabled?UiText.T("manager.disableHomeNetworkAccess",Language):UiText.T("manager.enableHomeNetworkAccess",Language);lan.Checked=s.Settings.LanEnabled;lan.Enabled=!s.Changing&&!quitting;icon.Text=TrayText.Tooltip(s.Running,s.LanActive,Language);}
 private void Error(string message){icon.ShowBalloonTip(8000,"LMU Setup Manager",UiText.Localize(message,Language),ToolTipIcon.Warning);}
 private async Task Quit(){if(quitting)return;quitting=true;timer.Stop();try{await controller.DisposeAsync();}catch{Error(UiText.T("manager.shutdownReportedAnErrorPleaseCheckStatusAnd",Language));}finally{ExitThread();}}
 private void SessionEnding(object? sender,SessionEndingEventArgs e){if(dispatcher.InvokeRequired){dispatcher.Invoke(()=>SessionEnding(sender,e));return;}if(quitting)return;quitting=true;timer.Stop();try{controller.DisposeAsync().AsTask().GetAwaiter().GetResult();}finally{ExitThread();}}
 protected override void Dispose(bool disposing){if(disposing){controller.Changed-=Update;SystemEvents.SessionEnding-=SessionEnding;timer.Dispose();icon.Visible=false;icon.Dispose();image.Dispose();dispatcher.Dispose();}base.Dispose(disposing);}
}
