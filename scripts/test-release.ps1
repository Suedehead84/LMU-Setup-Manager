param([string]$ReleaseDirectory="")
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$version=(Get-Content VERSION -Raw).Trim()
if(!$ReleaseDirectory){$ReleaseDirectory=Join-Path $PWD ('artifacts/releases/'+$version)}
$release=(Resolve-Path -LiteralPath $ReleaseDirectory).Path
$checks=0
function Check($ok,$name){if(!$ok){throw $name};$script:checks++;Write-Output "PASS $name"}
Check ((Get-Content (Join-Path $release 'win-x64/VERSION') -Raw).Trim() -eq $version) 'release metadata matches source VERSION'
Check ((Get-Item -LiteralPath (Join-Path $release 'win-x64/Lmu.Web.exe')).VersionInfo.ProductVersion -like ($version+'*')) 'published EXE product version matches release version'
. (Join-Path $PSScriptRoot 'release-path.ps1')
$refused=$false;try{$null=Get-LmuReleaseTarget $PWD $version}catch{$refused=$true}
Check $refused 'publishing refuses an existing release version without overwriting'
$refused=$false;try{$null=Get-LmuReleaseTarget $PWD '../outside'}catch{$refused=$true}
Check $refused 'release path rejects traversal in version string'
$entries=Get-Content (Join-Path $release 'SHA256SUMS.txt')
foreach($line in $entries){
 if($line -notmatch '^([A-F0-9]{64})  (.+)$'){throw 'Invalid manifest entry'}
 $expected=$Matches[1];$path=[IO.Path]::GetFullPath((Join-Path $release $Matches[2]))
 if(!$path.StartsWith($release+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe manifest path'}
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected){throw 'Release manifest mismatch'}
}
Check ($entries.Count -gt 10) 'every published component and ZIP matches SHA256 manifest'
$zip=Join-Path $release ('LMU-FixedSetupManager-'+$version+'-win-x64.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
 $names=@($archive.Entries | ForEach-Object {$_.FullName.Replace('\','/')})
 Check (!($names | Where-Object {$_ -match '(^|/)\.\.(/|$)|^[A-Za-z]:|^/|\.(sqlite|db|jsonl|log|pdb)$|(^|/)(backups|\.research)/|^diagnostics/'})) 'ZIP contains no private databases logs debug files or traversal paths'
 foreach($required in @('LMU Setup Manager.exe','wwwroot/settings.js','Lmu.Web.exe','e_sqlite3.dll','wwwroot/app.js','wwwroot/ui-helpers.js','wwwroot/app.css','tools/library/Lmu.Library.Tool.exe','tools/library/e_sqlite3.dll','tools/diagnostics/Lmu.Diagnostics.exe','licenses/Microsoft.Data.Sqlite-MIT.txt','lmu-pitwall-LICENSE.txt','RELEASE_NOTES.md')){if($names -notcontains $required){throw ('Missing ZIP dependency: '+$required)}}
 Check ($true) 'ZIP retains complete runtime native dependencies assets tools and licenses'
}finally{$archive.Dispose()}
$target=Join-Path $PWD ('artifacts/release-package-test-'+[Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $zip -DestinationPath $target
Check ((Get-Content (Join-Path $target 'VERSION') -Raw).Trim() -eq $version) 'ZIP extracts as self-contained versioned application'
$encoderPath=Join-Path $target 'wwwroot/vendor/qrcode-2.0.4.js'
Check ((Test-Path -LiteralPath $encoderPath) -and (Get-FileHash -LiteralPath $encoderPath).Hash -eq '79EC86F82856005B1C887905CFCCFCFBEC3821CA61C7FD5A952FAA5F778F791C') 'ZIP includes exact reviewed local QR encoder'
$qrFiles=@('wwwroot/invitation.js','licenses/qrcode-generator-MIT.txt','licenses/jsQR-Apache-2.0.txt','QR_DEPENDENCIES.json')
$networkGuide=(Test-Path -LiteralPath (Join-Path $target 'PHASE_B2_5_1.md')) -or (Test-Path -LiteralPath (Join-Path $target 'docs/SECURITY.md'))
Check ((@($qrFiles | Where-Object {!(Test-Path -LiteralPath (Join-Path $target $_))}).Count -eq 0) -and $networkGuide) 'ZIP includes QR lifecycle code provenance full notices and network security guidance'
Check (!(Get-ChildItem -LiteralPath $target -Recurse -File | Where-Object {$_.Name -eq 'jsQR-1.4.0.js'}) -and (Get-Content (Join-Path $target 'wwwroot/index.html') -Raw).Contains('/invitation.js')) 'ZIP wires local invitation UI without shipping test decoder'
$localeFiles=@('wwwroot/i18n.js','wwwroot/language-ui.js','wwwroot/locale-policy.json','wwwroot/locales/de.json','wwwroot/locales/en.json','TRANSLATING.md','README-Web.md')
Check (@($localeFiles | Where-Object {!(Test-Path -LiteralPath (Join-Path $target $_))}).Count -eq 0) 'ZIP includes shared language resources controls policy and translation guide'
foreach($code in @('de','en')){Check ((Get-FileHash -LiteralPath (Join-Path $target ('wwwroot/locales/'+$code+'.json'))).Hash -eq (Get-FileHash -LiteralPath ('src/Lmu.Web/wwwroot/locales/'+$code+'.json')).Hash) ('ZIP retains exact reviewed '+$code+' translation resource')}
Check (@(Get-ChildItem -LiteralPath (Join-Path $target 'wwwroot/locales') -Filter '*.json').Count -eq 2) 'ZIP ships only complete German and English without partial test language'
$homeUi=Join-Path $target 'wwwroot/settings.js'
$homeGuide=(Test-Path -LiteralPath (Join-Path $target 'HOME_NETWORK_PREVIEW_10.md')) -or (Test-Path -LiteralPath (Join-Path $target 'docs/INSTALLATION.md'))
Check ($homeGuide -and (Get-Content -LiteralPath $homeUi -Raw).Contains('role="switch"') -and !(Get-Content -LiteralPath $homeUi -Raw).Contains('id="http-risk"') -and (Get-FileHash -LiteralPath $homeUi).Hash -eq (Get-FileHash -LiteralPath 'src/Lmu.Web/wwwroot/settings.js').Hash) 'ZIP ships reviewed single-switch home UI and current guidance'
if($version -match '-rc\.'){
 Check ((Get-Content -LiteralPath (Join-Path $target 'UNPUBLISHED.txt') -Raw).Contains($version) -and (Get-Content -LiteralPath (Join-Path $target 'UNPUBLISHED.txt') -Raw).Contains('UNPUBLISHED')) 'RC ZIP is explicitly unpublished and version-consistent'
 Check ((Get-Content -LiteralPath (Join-Path $target 'wwwroot/index.html') -Raw).Contains('shell.unpublishedCandidate') -and (Get-Content -LiteralPath (Join-Path $target 'wwwroot/index.html') -Raw).Contains($version)) 'RC UI visibly identifies candidate and translated distribution warning'
 Check (!(Get-ChildItem -LiteralPath $target -Recurse -File | Where-Object {$_.Name -match 'EULA_DRAFT|\.cs$|\.csproj$|\.slnx$|\.internal\.md$|PHASE_B2_'})) 'RC package excludes private source legal drafts and obsolete network instructions'
 Check ((@(@('docs/INSTALLATION.md','docs/INSTALLATION.de.md','docs/RC_MANUAL_ACCEPTANCE.md','docs/RC_CANDIDATE.md') | Where-Object {!(Test-Path -LiteralPath (Join-Path $target $_))})).Count -eq 0) 'RC ZIP includes current bilingual installation acceptance and candidate notes'
}
if($version -eq '1.0.0'){
 Check ((Get-Content -LiteralPath (Join-Path $target 'PUBLICATION-HOLD.txt') -Raw).Contains('1.0.0') -and !(Test-Path -LiteralPath (Join-Path $target 'UNPUBLISHED.txt'))) 'stable version is separate from legal/publication approval'
 Check (!(Get-Content -LiteralPath (Join-Path $target 'wwwroot/index.html') -Raw).Contains('release-candidate') -and !(Get-Content -LiteralPath (Join-Path $target 'wwwroot/index.html') -Raw).Contains('shell.unpublishedCandidate') -and (Get-Content -LiteralPath (Join-Path $target 'wwwroot/index.html') -Raw).Contains('<small>1.0.0</small>')) 'stable UI removes RC banner and keeps subtle 1.0.0 version'
 Check (!(Get-ChildItem -LiteralPath $target -Recurse -File | Where-Object {$_.Name -match 'EULA_DRAFT|\.cs$|\.csproj$|\.slnx$|\.internal\.md$|PHASE_B2_|^RC_'})) 'stable package excludes private source legal drafts and RC-only guidance'
 Check ((@(@('docs/INSTALLATION.md','docs/INSTALLATION.de.md','docs/ACCEPTANCE.md','docs/RELEASE_STATUS.md','LICENSE') | Where-Object {!(Test-Path -LiteralPath (Join-Path $target $_))})).Count -eq 0) 'stable ZIP includes current bilingual installation acceptance and release status'
}

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-web.ps1 -Executable (Join-Path $target 'Lmu.Web.exe')
if($LASTEXITCODE){throw 'Extracted ZIP web release failed'}
Check ($true) 'extracted ZIP web EXE passes complete release checks'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-b1.ps1 -Executable (Join-Path $target 'tools/library/Lmu.Library.Tool.exe')
if($LASTEXITCODE){throw 'Extracted ZIP library release failed'}
Check ($true) 'extracted ZIP CLI passes complete release checks'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-manager.ps1 -Executable (Join-Path $target 'LMU Setup Manager.exe')
if($LASTEXITCODE){throw 'Extracted ZIP manager release failed'}
Check ($true) 'extracted ZIP tray EXE passes complete manager checks'
Write-Output "All $checks package assertions passed. Isolated extraction: $target"
