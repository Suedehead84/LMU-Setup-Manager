param([string]$Executable="artifacts/releases/1.0.0/win-x64/tools/library/Lmu.Library.Tool.exe")
$ErrorActionPreference="Stop"
Set-Location (Split-Path $PSScriptRoot)
$exe=(Resolve-Path -LiteralPath $Executable).Path
$testDir=Join-Path $PWD ("artifacts/b1-smoke-"+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $testDir|Out-Null
$db=Join-Path $testDir 'test.sqlite'
$request=Join-Path $testDir 'request.json'
$checks=0
$calls=0
function Invoke-Tool([string]$RequestPath){
 # Process redirection bypasses the PS 5.1 stderr -> NativeCommandError conversion.
 # Keep ErrorActionPreference=Stop; inspect this process's exit code and both streams.
 if($db.Contains('"') -or $RequestPath.Contains('"')){throw 'Invalid quote in Windows file path'}
 $info=New-Object System.Diagnostics.ProcessStartInfo
 $info.FileName=$exe
 $info.Arguments='--db "'+$db+'" --request "'+$RequestPath+'"'
 $info.UseShellExecute=$false
 $info.CreateNoWindow=$true
 $info.RedirectStandardOutput=$true
 $info.RedirectStandardError=$true
 $info.StandardOutputEncoding=New-Object System.Text.UTF8Encoding($false)
 $info.StandardErrorEncoding=New-Object System.Text.UTF8Encoding($false)
 $process=New-Object System.Diagnostics.Process
 $process.StartInfo=$info
 try {
  if(!$process.Start()){throw 'Tool process did not start'}
  # Drain concurrently so neither pipe can block the child while the other is read.
  $stdoutTask=$process.StandardOutput.ReadToEndAsync()
  $stderrTask=$process.StandardError.ReadToEndAsync()
  if(!$process.WaitForExit(30000)){$process.Kill();$process.WaitForExit();throw 'Tool request timed out after 30 seconds'}
  $result=[pscustomobject]@{ExitCode=$process.ExitCode;StdOut=$stdoutTask.GetAwaiter().GetResult();StdErr=$stderrTask.GetAwaiter().GetResult()}
  $script:calls++
  $prefix=Join-Path $testDir ('call-{0:D3}' -f $script:calls)
  $encoding=New-Object System.Text.UTF8Encoding($false)
  [IO.File]::WriteAllText($prefix+'.stdout.json',$result.StdOut,$encoding)
  [IO.File]::WriteAllText($prefix+'.stderr.json',$result.StdErr,$encoding)
  [IO.File]::WriteAllText($prefix+'.result.json',($result|ConvertTo-Json),$encoding)
  return $result
 }finally{$process.Dispose()}
}
function Read-Success($result){
 if($result.ExitCode -ne 0){throw "Unexpected tool exit code $($result.ExitCode): $($result.StdErr)"}
 if(![string]::IsNullOrWhiteSpace($result.StdErr)){throw "Unexpected tool stderr despite exit code 0: $($result.StdErr)"}
 if([string]::IsNullOrWhiteSpace($result.StdOut)){throw 'Tool returned no success JSON'}
 return ($result.StdOut|ConvertFrom-Json -ErrorAction Stop)
}
function Invoke-Request($value){
 ConvertTo-Json -InputObject $value -Depth 16|Set-Content $request -Encoding UTF8
 return (Read-Success (Invoke-Tool $request))
}
function Check($value,$message){if(!$value){throw $message};$script:checks++;Write-Output "PASS $message"}
$initial=Read-Success (Invoke-Tool ((Resolve-Path docs/examples/b1-manual.json).Path))
$setup=$initial[3].Result
Check ($setup.Origin -eq 'Manual' -and $setup.Current.Content.Mode -eq 'FixedLab') 'release creates manual Fixed Lab without LMU'
$again=Invoke-Request @{Action='setup.get';Data=@{Id=$setup.Id}}
Check ($again.Result.Current.Id -eq $setup.Current.Id) 'release data survives process restart'
$fav=Invoke-Request @{Action='setup.favorite';Data=@{Id=$setup.Id;ExpectedVersion=$setup.Current.Id;Value=$true}}
Check ($fav.Result.Current.Number -eq 2 -and $fav.Result.Current.Content.Favorite) 'release versions favorites'
$copy=Invoke-Request @{Action='setup.saveAs';Data=@{Id=$setup.Id;ExpectedVersion=$fav.Result.Current.Id;Name='Safe Race'}}
Check ($copy.Result.Id -ne $setup.Id -and $copy.Result.Origin -eq 'Copied') 'release save-as keeps source'
$null=Invoke-Request @{Action='setup.delete';Data=@{Id=$setup.Id;ExpectedVersion=$fav.Result.Current.Id}}
$trash=Invoke-Request @{Action='setup.get';Data=@{Id=$setup.Id;IncludeDeleted=$true}}
Check ($null -ne $trash.Result.DeletedAt -and $trash.Result.Versions.Count -eq 2) 'release soft delete retains history'
$restored=Invoke-Request @{Action='setup.restore';Data=@{Id=$setup.Id;ExpectedVersion=$fav.Result.Current.Id}}
Check ($null -eq $restored.Result.DeletedAt) 'release restores deleted setup'
$null=Invoke-Request @{Action='backup';Data=@{Path=(Join-Path $testDir 'backup.sqlite')}}
Check (Test-Path (Join-Path $testDir 'backup.sqlite')) 'release writes real SQLite backup'
$invalid=@{Action='setup.create';Data=@{Key=$setup.Key;Draft=@{Name='Missing TC';Values=@{TCCut=7;TCSlip=7;ABS=6;BrakeBiasFrontPercent=51.25}}}}
ConvertTo-Json -InputObject $invalid -Depth 12|Set-Content $request -Encoding UTF8
$errorFile=Join-Path $testDir 'expected-error.json'
$rejected=Invoke-Tool $request
[IO.File]::WriteAllText($errorFile,$rejected.StdErr,(New-Object System.Text.UTF8Encoding($false)))
Check ($rejected.ExitCode -eq 2) 'release rejects missing required electronics instead of defaulting to zero'
Check ([string]::IsNullOrWhiteSpace($rejected.StdOut)) 'rejected request has no success output on stdout'
$errorJson=$rejected.StdErr|ConvertFrom-Json -ErrorAction Stop
Check ($errorJson.Type -eq 'JsonException' -and ![string]::IsNullOrWhiteSpace($errorJson.Error)) 'stderr contains structured JSON validation error'
Check ($errorJson.Error -match '\bTC\b') 'validation error identifies the missing TC value'
$final=Invoke-Request @{Action='setup.search';Data=@{}}
Check ($final.Result.Count -eq 2) 'rejected input leaves library unchanged'
[ordered]@{Classification='Real local SQLite release smoke; manual inputs, no LMU live test';Assertions=$checks;TestDirectory=$testDir;PowerShellVersion=$PSVersionTable.PSVersion.ToString();ProcessCalls=$calls}|
 ConvertTo-Json|Set-Content (Join-Path $testDir 'result.json')
"All $checks release assertions passed. Isolated test files: $testDir"
