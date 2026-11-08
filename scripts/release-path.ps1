function Get-LmuReleaseTarget([string]$ProjectRoot,[string]$Version){
 if($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?$'){throw 'Invalid VERSION'}
 $parent=[IO.Path]::GetFullPath((Join-Path $ProjectRoot 'artifacts/releases'))
 $target=[IO.Path]::GetFullPath((Join-Path $parent $Version))
 if(!$target.StartsWith($parent+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe release target'}
 if(Test-Path -LiteralPath $target){throw 'Release already exists; never overwritten. Choose a new version or review the existing release.'}
 return $target
}
