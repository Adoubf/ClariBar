param([switch]$SmokeTest,[switch]$Development,[switch]$Elevated,[switch]$Diagnostics,[ValidateSet("overview","taskbar","sensors","colors","general","about")][string]$Page="overview")
$ErrorActionPreference='Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
$artifact = "$env:CLARIBAR_ROOT\artifacts\ClariBar\0.7.0"
if(!$SmokeTest -and !$Development){
 $uninstall='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{93FDC1C2-EB8E-48A4-9CF0-F59AEB645816}_is1'
 $installed=Get-ItemProperty -LiteralPath $uninstall -ErrorAction SilentlyContinue
 if($installed.InstallLocation -and (Test-Path (Join-Path $installed.InstallLocation 'ClariBar.exe'))){$artifact=$installed.InstallLocation}
}
$exe = "$artifact\ClariBar.exe"
if (!(Test-Path $exe)) { throw 'Run scripts\build.ps1 to build the application first.' }
if ($SmokeTest) { Start-Process $exe -WorkingDirectory $artifact -ArgumentList '--smoke-test' }
else {
 $arguments=@("--page=$Page")
 if($Elevated){$arguments+='--elevated'}
 if($Diagnostics){$arguments+='--diagnostics'}
 Start-Process $exe -WorkingDirectory $artifact -ArgumentList $arguments
}
