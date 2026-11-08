param([string]$Executable="artifacts/releases/1.0.0/win-x64/Lmu.Web.exe")
$ErrorActionPreference="Stop"
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName System.Net.Http
$exe=(Resolve-Path -LiteralPath $Executable).Path
$dir=Join-Path $PWD ('artifacts/web-release-smoke-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $dir|Out-Null
$listener=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,0)
$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
$info=New-Object Diagnostics.ProcessStartInfo
$info.FileName=$exe;$info.Arguments='--db "'+(Join-Path $dir 'library.sqlite')+'" --port '+$port
$accessFile=Join-Path $dir 'access.protected'
$info.Arguments+=' --access-file "'+$accessFile+'"'
$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
$process=New-Object Diagnostics.Process;$process.StartInfo=$info
$client=$null;$checks=0;$stdoutTail=$null;$stderr=$null;$started=$false
function Check($ok,$label){if(!$ok){throw $label};$script:checks++;Write-Output "PASS $label"}
try {
 if(!$process.Start()){throw 'Web release failed to start'}
 $started=$true
 $stderr=$process.StandardError.ReadToEndAsync()
 $token=$null;$timer=[Diagnostics.Stopwatch]::StartNew()
 while(!$token){
  $lineTask=$process.StandardOutput.ReadLineAsync()
  $remaining=[Math]::Max(1,30000-[int]$timer.ElapsedMilliseconds)
  if(!$lineTask.Wait($remaining)){throw 'Web startup timeout'}
  $line=$lineTask.GetAwaiter().GetResult()
  if($null -eq $line){throw 'Web release exited before becoming ready'}
  if($line -match '#access='){throw 'Access secret leaked to stdout'}
  if($line -like 'Browser:*'){
   Add-Type -AssemblyName System.Security
   $protected=[IO.File]::ReadAllBytes($accessFile)
   $token=[Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect($protected,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))
  }
 }
 $stdoutTail=$process.StandardOutput.ReadToEndAsync()
 $handler=New-Object Net.Http.HttpClientHandler;$handler.UseProxy=$false
 $client=New-Object Net.Http.HttpClient($handler);$client.BaseAddress=[Uri]('http://127.0.0.1:'+$port);$client.Timeout=[TimeSpan]::FromSeconds(15)
 function Request([string]$Method,[string]$Path,$Body=$null){
  $message=New-Object Net.Http.HttpRequestMessage((New-Object Net.Http.HttpMethod($Method)),$Path)
  if($null -ne $Body){$json=ConvertTo-Json -InputObject $Body -Depth 15 -Compress;$message.Content=New-Object Net.Http.StringContent($json,[Text.Encoding]::UTF8,'application/json')}
  try{$response=$client.SendAsync($message).GetAwaiter().GetResult();try{$text=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult();if(!$response.IsSuccessStatusCode){throw "$Method $Path : $($response.StatusCode) $text"};if($text){return ($text|ConvertFrom-Json)}}finally{$response.Dispose()}}finally{$message.Dispose()}
 }
 $html=$client.GetStringAsync('/').GetAwaiter().GetResult()
 Check ($html.Contains('/app.js') -and $html.Contains('lang="de"')) 'published web shell and asset references available'
 $languages=Request GET '/api/languages'
 Check (@($languages.languages | Where-Object {$_.code -in @('de','en') -and $_.complete}).Count -eq 2) 'published language registry exposes complete German and English before pairing'
 foreach($code in @('de','en')){$resource=Request GET ('/api/languages/'+$code);Check ($resource.meta.code -eq $code -and $resource.messages.'web.readyForTheSession') ('published validated '+$code+' resource loads without external service')}
 $client.DefaultRequestHeaders.Add('X-LMU-Language','en')
 $localized=$client.GetAsync('/api/setups').GetAwaiter().GetResult();try{$errorText=$localized.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json;Check ([int]$localized.StatusCode -eq 401 -and $errorText.error -eq 'Connect to the library using the invitation link') 'published English authorization error preserves denial'}finally{$localized.Dispose()}
 $client.DefaultRequestHeaders.Remove('X-LMU-Language')|Out-Null
 $anonymous=$client.GetAsync('/api/setups').GetAwaiter().GetResult();try{Check ([int]$anonymous.StatusCode -eq 401) 'published API requires access grant'}finally{$anonymous.Dispose()}
 $anonymous=$client.GetAsync('/api/live').GetAwaiter().GetResult();try{Check ([int]$anonymous.StatusCode -eq 401) 'published B2.2 live API requires access grant'}finally{$anonymous.Dispose()}
 $upload=New-Object Net.Http.ByteArrayContent -ArgumentList (,[byte[]](1,2,3))
 $anonymous=$client.PostAsync('/api/restores/preview',$upload).GetAwaiter().GetResult();try{Check ([int]$anonymous.StatusCode -eq 401) 'published restore upload requires access grant'}finally{$anonymous.Dispose();$upload.Dispose()}
 $client.DefaultRequestHeaders.Add('Authorization','Bearer '+$token)
 $session=Request GET '/api/session';$client.DefaultRequestHeaders.Add('X-LMU-CSRF',$session.csrf)
 $live=Request GET '/api/live'
 Check ($null -ne $live.status -and $null -ne $live.status.values -and $null -ne $live.status.canCapture) 'published B2.2 live status has explicit availability and capture state'
 if(!$live.status.fresh){Check ($null -eq $live.status.values.tc -and $null -eq $live.status.values.brakeBiasFrontPercent -and @($live.matchingSetups).Count -eq 0) 'published inactive telemetry exposes no stale readings or matching'}
 $js=$client.GetStringAsync('/app.js').GetAwaiter().GetResult()
 Check ($js.Contains('/lmu/catalog/') -and $js.Contains('/live/setups') -and $js.Contains('LiveCaptured')) 'published B2.2 import and explicit capture UI assets available'
 function Rejected([string]$Path,$Body,[int]$Expected,[string]$Label){
  $message=New-Object Net.Http.HttpRequestMessage((New-Object Net.Http.HttpMethod('POST')),$Path)
  $message.Content=New-Object Net.Http.StringContent((ConvertTo-Json -InputObject $Body -Depth 8 -Compress),[Text.Encoding]::UTF8,'application/json')
  try{$response=$client.SendAsync($message).GetAwaiter().GetResult();try{Check ([int]$response.StatusCode -eq $Expected) $Label}finally{$response.Dispose()}}finally{$message.Dispose()}
 }
 Rejected '/api/live/setups' @{contextToken='obsolete';name='Must not save';category='Open'} 409 'published live save rejects obsolete browser context'
 Rejected '/api/lmu/catalog/import' @{previewId='obsolete';selectedIds=@('fabricated')} 409 'published import rejects unissued preview'
 $client.DefaultRequestHeaders.Remove('X-LMU-CSRF')|Out-Null
 Rejected '/api/live/setups' @{contextToken='obsolete';name='Must not save';category='Open'} 403 'published live save requires CSRF despite valid access grant'
 Rejected '/api/restores/confirm' @{previewId='obsolete';confirmation='BIBLIOTHEK ERSETZEN'} 403 'published restore requires CSRF despite valid access grant'
 $client.DefaultRequestHeaders.Add('X-LMU-CSRF',$session.csrf)
 $null=Request POST '/api/catalog/vehicles' @{id='SMOKE_CAR';name='Synthetic release car'}
 $null=Request POST '/api/catalog/tracks' @{id='SMOKE_TRACK';name='Synthetic release track'}
 $null=Request POST '/api/catalog/layouts' @{trackId='SMOKE_TRACK';id='A';name='Layout A'}
 $key=@{vehicleId='SMOKE_CAR';trackId='SMOKE_TRACK';layoutId='A'}
 $draft=@{name='Release manual';category='Open';preferredUse='Safe Race';notes='Synthetic local release test';values=@{tc=7;tcCut=7;tcSlip=7;abs=6;brakeBiasFrontPercent=51.25}}
 $setup=Request POST '/api/setups' @{key=$key;draft=$draft}
 Check ($setup.current.content.category -eq 'Open' -and $null -eq $setup.current.capture) 'published manual Open classification does not fabricate live session'
 Check ($setup.current.content.values.brakeBiasFrontPercent -eq 51.25) 'published web preserves front brake bias percentage'
 $version=Request POST "/api/setups/$($setup.id)/versions" @{expectedVersion=$setup.current.id;draft=$draft}
 Check ($version.current.number -eq 2) 'published explicit version action appends immutable version'
 $favorite=Request POST "/api/setups/$($setup.id)/favorite" @{expectedVersion=$version.current.id;favorite=$true}
 Check ($favorite.current.content.favorite) 'published favorite action persists'
 $copy=Request POST "/api/setups/$($setup.id)/copy" @{versionId=$setup.current.id;name='Old base copy'}
 Check ($copy.id -ne $setup.id -and $copy.copiedFromVersionId -eq $setup.current.id) 'published old version copy preserves lineage'
 $null=Request POST "/api/setups/$($setup.id)/delete" @{expectedVersion=$favorite.current.id}
 $trash=@(Request GET '/api/setups?trash=only');Check ($trash.Count -eq 1) 'published trash exposes deleted setup'
 $restored=Request POST "/api/setups/$($setup.id)/restore" @{expectedVersion=$favorite.current.id}
 Check ($null -eq $restored.deletedAt -and $restored.versions.Count -eq 3) 'published restore retains every version'
 $found=@(Request GET '/api/setups?q=Synthetic%20local');Check ($found.Count -eq 2) 'published free text search includes notes'
 $backup=Request POST '/api/backups' @{}
 $bytes=$client.GetByteArrayAsync($backup.downloadUrl).GetAwaiter().GetResult()
 [IO.File]::WriteAllBytes((Join-Path $dir 'download.sqlite'),$bytes)
 Check ([Text.Encoding]::ASCII.GetString($bytes,0,15) -eq 'SQLite format 3') 'published backup download is a real SQLite file'
 Check ($backup.restoreReleased) 'database restore uses separate validated preview and explicit confirmation'
 $draft.name='Transient after backup';$transient=Request POST '/api/setups' @{key=$key;draft=$draft}
 $upload=New-Object Net.Http.ByteArrayContent -ArgumentList (,$bytes)
 $response=$client.PostAsync('/api/restores/preview',$upload).GetAwaiter().GetResult()
 try{if(!$response.IsSuccessStatusCode){throw 'Published backup preview failed'};$preview=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json;Check ($preview.setups -eq 2 -and $preview.versions -eq 4) 'published restore preview reads backed-up setup and version counts'}finally{$response.Dispose();$upload.Dispose()}
 Rejected '/api/restores/confirm' @{previewId=$preview.id;confirmation='yes'} 400 'published restore rejects missing explicit replacement phrase'
 $restoredBackup=Request POST '/api/restores/confirm' @{previewId=$preview.id;confirmation='BIBLIOTHEK ERSETZEN'}
 Check (Test-Path -LiteralPath (Join-Path (Join-Path $dir 'backups') $restoredBackup.safetyBackup)) 'published restore creates automatic pre-restore SQLite backup'
 $after=@(Request GET '/api/setups');Check ($after.Count -eq 2 -and !($after|Where-Object {$_.id -eq $transient.id})) 'published restore replaces whole library and refreshes existing services'
 $upload=New-Object Net.Http.ByteArrayContent -ArgumentList (,[byte[]](1,2,3))
 $response=$client.PostAsync('/api/restores/preview',$upload).GetAwaiter().GetResult();try{Check ([int]$response.StatusCode -eq 400) 'published restore rejects corrupt file without overwriting library'}finally{$response.Dispose();$upload.Dispose()}
 [ordered]@{Classification='Published local web release; real HTTP and isolated SQLite, no LMU drive';Assertions=$checks;PowerShellVersion=$PSVersionTable.PSVersion.ToString();Directory=$dir}|ConvertTo-Json|Set-Content (Join-Path $dir 'result.json')
 "All $checks web release assertions passed. Results: $dir"
}finally {
 if($client){$client.Dispose()}
 if($started -and !$process.HasExited){$process.Kill();$process.WaitForExit()}
 if($stderr){[IO.File]::WriteAllText((Join-Path $dir 'server-stderr.log'),$stderr.GetAwaiter().GetResult())}
 if($stdoutTail){[IO.File]::WriteAllText((Join-Path $dir 'server-stdout.log'),$stdoutTail.GetAwaiter().GetResult())}
 $process.Dispose()
}
