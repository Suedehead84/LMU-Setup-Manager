param([string]$Executable='artifacts/releases/1.0.0/win-x64/LMU Setup Manager.exe')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$exe=(Resolve-Path -LiteralPath $Executable).Path;$version=(Get-Content VERSION -Raw).Trim();$checks=0
function Check($ok,$name){if(!$ok){throw $name};$script:checks++;Write-Output "PASS $name"}
Check ([IO.Path]::GetFileName($exe) -ceq 'LMU Setup Manager.exe') 'visible executable has exact required filename'
$v=(Get-Item -LiteralPath $exe).VersionInfo
Check ($v.ProductName -eq 'LMU Setup Manager' -and $v.FileDescription -eq 'LMU Setup Manager') 'Windows product name and description correct'
Check ($v.ProductVersion -like ($version+'*') -and $v.FileVersion -eq ([xml](Get-Content Directory.Build.props -Raw)).Project.PropertyGroup.FileVersion) 'Windows product/file versions match current release metadata'
Check ([string]::IsNullOrWhiteSpace($v.CompanyName)) 'no invented manufacturer in Windows resources'
$b=[IO.File]::ReadAllBytes($exe);$pe=[BitConverter]::ToInt32($b,60);Check ([BitConverter]::ToUInt16($b,$pe+92) -eq 2) 'Windows GUI subsystem does not create console window'
Add-Type -AssemblyName System.Drawing
$icon=[Drawing.Icon]::ExtractAssociatedIcon($exe);try{$bitmap=$icon.ToBitmap();try{$green=$false;for($x=0;$x -lt $bitmap.Width;$x++){for($y=0;$y -lt $bitmap.Height;$y++){$p=$bitmap.GetPixel($x,$y);if($p.G -gt 150 -and $p.R -lt 130 -and $p.B -gt 100){$green=$true}}};Check $green 'own green technical icon embedded in EXE'}finally{$bitmap.Dispose()}}finally{$icon.Dispose()}
$root=Join-Path $PWD ('artifacts/manager-smoke-'+[Guid]::NewGuid().ToString('N'));$config=Join-Path $root 'config';New-Item -ItemType Directory $config -Force|Out-Null;$db=Join-Path $root 'library.sqlite'
$l=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,0);$l.Start();$port=$l.LocalEndpoint.Port;$l.Stop()
@{OpenBrowser=$false;AutoStart=$false;Port=$port;LanEnabled=$false}|ConvertTo-Json|Set-Content (Join-Path $config 'settings.json') -Encoding UTF8
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;$sha=[Security.Cryptography.SHA256]::Create();try{$hash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($config).ToUpperInvariant())))).Replace('-','').Substring(0,16)}finally{$sha.Dispose()};$pipeName='LmuSetupManager-'+$sid+'-'+$hash
function Command([string]$text){$p=New-Object IO.Pipes.NamedPipeClientStream('.', $pipeName,[IO.Pipes.PipeDirection]::InOut);try{$p.Connect(2000);$w=New-Object IO.StreamWriter($p,(New-Object Text.UTF8Encoding($false)),1024,$true);$w.AutoFlush=$true;$r=New-Object IO.StreamReader($p);$w.WriteLine($text);$t=$r.ReadLineAsync();if(!$t.Wait(5000)){throw 'Manager IPC response timeout'};return $t.GetAwaiter().GetResult()}finally{$p.Dispose()}}
function Launch([string]$extra=''){$i=New-Object Diagnostics.ProcessStartInfo;$i.FileName=$exe;$i.Arguments='--db "'+$db+'" --settings-dir "'+$config+'" --no-browser '+$extra;$i.UseShellExecute=$false;$i.CreateNoWindow=$true;$i.RedirectStandardOutput=$true;$i.RedirectStandardError=$true;$p=New-Object Diagnostics.Process;$p.StartInfo=$i;if(!$p.Start()){throw 'Manager did not start'};return $p}
function Ready { $until=[DateTime]::UtcNow.AddSeconds(20);do{try{$s=Command 'status'|ConvertFrom-Json;if($s.Running){return $s}}catch{};Start-Sleep -Milliseconds 150}while([DateTime]::UtcNow -lt $until);throw 'Tray/server startup timeout'}
$process=$null
try{
 $process=Launch;$status=Ready
 Check ($status.Running -and !$status.LanActive) 'real tray process starts managed loopback server'
 Check ($status.DatabasePath -eq [IO.Path]::GetFullPath($db)) 'tray uses exact explicit existing database path'
 Check (!$status.Settings.OpenBrowser -and !$status.Settings.AutoStart) 'saved startup preferences loaded'
 $second=Launch '--command status';try{Check ($second.WaitForExit(10000) -and $second.ExitCode -eq 0) 'second process contacts single owner and exits';Check ((Command 'status'|ConvertFrom-Json).Running) 'original owner remains available without duplicate server'}finally{if(!$second.HasExited){$second.Kill()};$second.Dispose()}
 $null=Command 'exit';Check ($process.WaitForExit(15000)) 'owner exits through graceful tray shutdown path';Check ([string]::IsNullOrWhiteSpace($process.StandardOutput.ReadToEnd()) -and [string]::IsNullOrWhiteSpace($process.StandardError.ReadToEnd())) 'tray writes no console output or access secrets'
 $process.Dispose();$process=$null;$l=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,$port);try{$l.Start();Check $true 'shutdown releases listening port'}finally{$l.Stop()}
 Check (Test-Path -LiteralPath $db) 'library file retained after shutdown'
 $process=Launch;$status=Ready;Check ($status.Settings.Port -eq $port -and !$status.LanActive) 'restart uses saved port and safe network settings';$null=Command 'exit';Check ($process.WaitForExit(15000)) 'restarted owner shuts down cleanly'
 Write-Output "All $checks manager release assertions passed. Isolated files: $root"
}finally{if($process){if(!$process.HasExited){try{$null=Command 'exit';$null=$process.WaitForExit(5000)}catch{};if(!$process.HasExited){$process.Kill();$process.WaitForExit()}};$process.Dispose()}}
