$ErrorActionPreference='Stop'
[xml]$metadata=Get-Content (Join-Path (Split-Path $PSScriptRoot) 'Version.props') -Raw
$feature=[int]$metadata.Project.PropertyGroup.ClariBarFeatureVersion
$refinement=[int]$metadata.Project.PropertyGroup.ClariBarRefinementVersion
[pscustomobject]@{
 DisplayVersion=if($refinement -eq 0){"0.$feature"}else{"0.$feature$refinement"}
 PackageVersion="0.$feature.$refinement"
 FileVersion="0.$feature.$refinement.0"
}
