param([switch]$Publish,[switch]$Clean,[string]$ReviewDirectory="")
$ErrorActionPreference="Stop"
$root=Split-Path $PSScriptRoot
Set-Location $root
. (Join-Path $PSScriptRoot 'release-path.ps1')
$version=(Get-Content VERSION -Raw).Trim()
[xml]$versionProps=Get-Content Directory.Build.props -Raw
if($versionProps.Project.PropertyGroup.Version -ne $version){throw 'VERSION and Directory.Build.props differ'}
& node scripts/check-docs.cjs
if($LASTEXITCODE){exit $LASTEXITCODE}
& node scripts/check-translations.cjs
if($LASTEXITCODE){exit $LASTEXITCODE}
if($Publish){
 if($ReviewDirectory){$release=[IO.Path]::GetFullPath((Join-Path $root $ReviewDirectory));$reviewRoot=[IO.Path]::GetFullPath((Join-Path $root 'artifacts/opensource-review'))+[IO.Path]::DirectorySeparatorChar;if(!$release.StartsWith($reviewRoot,[StringComparison]::OrdinalIgnoreCase)-or(Test-Path -LiteralPath $release)){throw 'Review directory must be new and below artifacts/opensource-review'}}else{$release=Get-LmuReleaseTarget $root $version}
}
$buildCommit=$null;$buildDirty=$null
if(Test-Path -LiteralPath (Join-Path $root '.git')){$buildCommit=& git rev-parse HEAD;if($LASTEXITCODE){throw 'Cannot identify source commit'};$buildDirty=[bool](git status --porcelain)}
$sourceIdentityText=& node scripts/prepare-opensource-review.cjs identity
if($LASTEXITCODE){throw 'MIT source identity failed'}
$sourceIdentity=$sourceIdentityText|ConvertFrom-Json
$dotnet=Join-Path $root ".tools/dotnet/dotnet.exe"
if(!(Test-Path $dotnet)){$dotnet="dotnet"}
$env:DOTNET_CLI_HOME=Join-Path $root ".tools/cli"
$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"
$env:APPDATA=Join-Path $root ".tools/appdata"
$env:NUGET_PACKAGES=Join-Path $root ".tools/nuget-packages"
if($Clean){
 & $dotnet restore Lmu.FixedSetupManager.slnx --configfile NuGet.Config -r win-x64 --force-evaluate -m:1
 if($LASTEXITCODE){exit $LASTEXITCODE}
 & $dotnet clean Lmu.FixedSetupManager.slnx -c Release -m:1
 if($LASTEXITCODE){exit $LASTEXITCODE}
 & $dotnet clean Lmu.FixedSetupManager.slnx -c Release -r win-x64 -m:1
 if($LASTEXITCODE){exit $LASTEXITCODE}
}
& $dotnet restore Lmu.FixedSetupManager.slnx --configfile NuGet.Config --force-evaluate -m:1
if($LASTEXITCODE){exit $LASTEXITCODE}
& $dotnet build Lmu.FixedSetupManager.slnx -c Release --no-restore --no-incremental -m:1
if($LASTEXITCODE){exit $LASTEXITCODE}
$suiteRecords=New-Object 'System.Collections.Generic.List[object]'
function Invoke-CheckedSuite([string]$Name,[string]$Command,[string[]]$SuiteArguments){
 $watch=[Diagnostics.Stopwatch]::StartNew();$lines=New-Object 'System.Collections.Generic.List[string]'
 & $Command @SuiteArguments | ForEach-Object {$line=$_.ToString();$lines.Add($line);Write-Output $line}
 if($LASTEXITCODE){throw ('Suite failed: '+$Name)}
 $summary=$lines | Where-Object {$_ -match '^All ([0-9]+) '} | Select-Object -Last 1
 if(!$summary -or $summary -notmatch '^All ([0-9]+) '){throw ('No assertion summary: '+$Name)}
 $suiteRecords.Add([pscustomobject][ordered]@{suite=$Name;assertions=[int]$Matches[1];status='passed';elapsedMilliseconds=[math]::Round($watch.Elapsed.TotalMilliseconds)})
}
Invoke-CheckedSuite 'Diagnostics' $dotnet @('run','--project','tests/Lmu.Diagnostics.Tests','-c','Release','--no-build')
Invoke-CheckedSuite 'SetupLibrary' $dotnet @('run','--project','tests/Lmu.SetupLibrary.Tests','-c','Release','--no-build')
Invoke-CheckedSuite 'Web' $dotnet @('run','--project','tests/Lmu.Web.Tests','-c','Release','--no-build')
Invoke-CheckedSuite 'LiveCapture' $dotnet @('run','--project','tests/Lmu.LiveCapture.Tests','-c','Release','--no-build')
Invoke-CheckedSuite 'B23' $dotnet @('run','--project','tests/Lmu.B23.Tests','-c','Release','--no-build')
Invoke-CheckedSuite 'B24' $dotnet @('run','--project','tests/Lmu.B24.Tests','-c','Release','--no-build')
Invoke-CheckedSuite 'B25' $dotnet @('run','--project','tests/Lmu.B25.Tests','-c','Release','--no-build')
Invoke-CheckedSuite 'ui-home10' 'node' @('tests/ui-home10.cjs')
Invoke-CheckedSuite 'ui-rc2' 'node' @('tests/ui-rc2.cjs')
Invoke-CheckedSuite 'opensource-release' 'node' @('tests/opensource-release.cjs')
Invoke-CheckedSuite 'rc-release' 'node' @('tests/rc-release.cjs')
Invoke-CheckedSuite 'ui-b26' 'node' @('tests/ui-b26.cjs')
Invoke-CheckedSuite 'publication-checks' 'node' @('tests/publication-checks.cjs')
Invoke-CheckedSuite 'ui-b252' 'node' @('tests/ui-b252.cjs')
Invoke-CheckedSuite 'ui-b251' 'node' @('tests/ui-b251.cjs')
Invoke-CheckedSuite 'ui-b25' 'node' @('tests/ui-b25.cjs')
Invoke-CheckedSuite 'ui-b24' 'node' @('tests/ui-b24.cjs')
Invoke-CheckedSuite 'ui-regression' 'node' @('tests/ui-regression.cjs')
& node scripts/privacy-audit.cjs
if($LASTEXITCODE){exit $LASTEXITCODE}
if($Publish){
 $stage=Join-Path $root ('artifacts/.publish-staging/'+[Guid]::NewGuid().ToString('N'))
 $win=Join-Path $stage 'win-x64'
 New-Item -ItemType Directory $win -Force|Out-Null
 $projects=@(@('src/Lmu.Web',$win),@('src/Lmu.Manager',$win),@('src/Lmu.Diagnostics',(Join-Path $win 'tools/diagnostics')),@('tools/Lmu.Library.Tool',(Join-Path $win 'tools/library')))
 foreach($project in $projects){
  & $dotnet publish $project[0] -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=None -p:DebugSymbols=false -o $project[1]
  if($LASTEXITCODE){throw 'Publish failed; only private staging directory retained, existing releases untouched.'}
 }
 Copy-Item LICENSE $win/
 Copy-Item THIRD_PARTY_NOTICES.md $win/
 Copy-Item docs/licenses $win/ -Recurse -Force
 Copy-Item docs/lmu-pitwall-LICENSE.txt $win/
 Copy-Item TRANSLATING.md $win/
 Copy-Item docs/QR_DEPENDENCIES.json $win/
 Copy-Item RELEASE_NOTES.md $win/
 Copy-Item VERSION $win/
 $endIdentityText=& node scripts/prepare-opensource-review.cjs identity
 if($LASTEXITCODE){throw 'Final MIT source identity failed'}
 if(($endIdentityText|ConvertFrom-Json).sourceDigest -ne $sourceIdentity.sourceDigest){throw 'Source/build inputs changed during qualification; no release published'}
 $sourceIdentityText|Set-Content -LiteralPath (Join-Path $win 'SOURCE-IDENTITY.json') -Encoding UTF8
 $total=($suiteRecords | Measure-Object -Property assertions -Sum).Sum
 [ordered]@{version=$version;status='passed';assertionsTotal=[int]$total;suites=$suiteRecords.ToArray();privacyAudit='passed';cleanBuild=[bool]$Clean}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $win 'TEST_RESULTS.json') -Encoding UTF8
 Copy-Item -LiteralPath (Join-Path $win 'TEST_RESULTS.json') -Destination $stage

 if($version -match '-rc\.' -or $version -eq '1.0.0'){
  $publicDocs=Join-Path $stage 'public-docs'
  & node scripts/publication-files.cjs project $publicDocs
  if($LASTEXITCODE){throw 'RC user-documentation projection failed'}
  Get-ChildItem -LiteralPath $publicDocs -Force|ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $win -Recurse -Force}
  Copy-Item -LiteralPath (Join-Path $publicDocs 'README.md') -Destination (Join-Path $win 'README-Web.md')
  $stable=$version -eq '1.0.0'
  $marker=if($stable){'PUBLICATION-HOLD.txt'}else{'UNPUBLISHED.txt'}
  $notice=if($stable){'Stable MIT release review '+$version+'. Official publication awaits owner approval and remaining rights review. This marker adds no restriction to MIT permissions. See LICENSE and third-party notices.'}else{'UNPUBLISHED LOCAL RELEASE CANDIDATE '+$version+'. NOT FINAL 1.0.0. No public distribution approved.'}
  [IO.File]::WriteAllText((Join-Path $win $marker),$notice,[Text.UTF8Encoding]::new($false))
  [IO.File]::WriteAllText((Join-Path $stage $marker),$notice,[Text.UTF8Encoding]::new($false))
  [ordered]@{status=$(if($stable){'stable-mit-review'}else{'unpublished-release-candidate'});version=$version;sourceCommit=$buildCommit;dirty=$buildDirty;cleanBuild=[bool]$Clean;sdk=(& $dotnet --version);node=(& node --version);createdUtc=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $win 'BUILD-IDENTITY.json') -Encoding UTF8
 }else{
  Copy-Item docs/PHASE_B2_6.md (Join-Path $win 'README-Web.md')
  Copy-Item docs/PHASE_B2_5_1.md $win/
  Copy-Item docs/STABILITY_PREVIEW_9.md $win/
  Copy-Item docs/HOME_NETWORK_PREVIEW_10.md $win/
  Copy-Item docs/examples/b1-manual.json (Join-Path $win 'tools/library/')
  Copy-Item docs/PHASE_B1.md (Join-Path $win 'tools/library/README-B1.md')
 }
 if(Get-ChildItem -LiteralPath $win -Recurse -File | Where-Object {$_.Name -match '\.(sqlite|db|jsonl|log|pdb|protected)$'}){throw 'Unexpected private/debug file in release'}
 $zip=Join-Path $stage ('LMU-FixedSetupManager-'+$version+'-win-x64.zip')
 Compress-Archive -Path (Join-Path $win '*') -DestinationPath $zip -CompressionLevel Optimal
 Copy-Item RELEASE_NOTES.md $stage/
 $manifest=Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)+'  '+$_.FullName.Substring($stage.Length+1).Replace('\','/')}
 $manifest | Set-Content -Encoding UTF8 (Join-Path $stage 'SHA256SUMS.txt')
 New-Item -ItemType Directory (Split-Path $release) -Force|Out-Null
 $stageParent=[IO.Path]::GetFullPath((Join-Path $root 'artifacts/.publish-staging'))+[IO.Path]::DirectorySeparatorChar
 if(!([IO.Path]::GetFullPath($stage).StartsWith($stageParent,[StringComparison]::OrdinalIgnoreCase))){throw 'Unsafe staging target'}
 [IO.Directory]::Move($stage,$release) # Fails atomically if another build already created the target; never merges/overwrites.
 Write-Output ('Release: '+$release)
}
