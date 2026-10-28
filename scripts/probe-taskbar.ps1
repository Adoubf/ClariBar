param([switch]$Apply,[switch]$Hold,[switch]$Watchdog,[switch]$Extended,[switch]$Adaptive,[switch]$Centering)
$ErrorActionPreference='Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
$root=Split-Path $PSScriptRoot
$native="$env:CLARIBAR_ROOT\build\claribar-native"
$tap="$native\ExplorerTap\ClariBar.ExplorerTap.dll"
$hash=(Get-FileHash -LiteralPath $tap -Algorithm SHA256).Hash.Substring(0,16)
$immutable="$env:CLARIBAR_ROOT\artifacts\ClariBar\adapters\$hash"
[void](New-Item -ItemType Directory -Force -Path $immutable)
if(!(Test-Path "$immutable\ClariBar.ExplorerTap.dll")){Copy-Item $tap "$immutable\ClariBar.ExplorerTap.dll"}
& dotnet build "$root\tests\ClariBar.TaskbarProbe\ClariBar.TaskbarProbe.csproj" -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'Probe build failed'}
$probeArgs=@($native,"$immutable\ClariBar.ExplorerTap.dll")
if($Apply){$probeArgs+='--apply'}
if($Hold){$probeArgs+='--hold'}
if($Watchdog){$probeArgs+='--watchdog'}
if($Extended){$probeArgs+='--extended'}
if($Adaptive){$probeArgs+='--adaptive'}
if($Centering){$probeArgs+='--centering'}
& dotnet "$env:CLARIBAR_ROOT\build\bin\ClariBar.TaskbarProbe\Release\net10.0-windows\ClariBar.TaskbarProbe.dll" @probeArgs
if($LASTEXITCODE -ne 0){throw "Taskbar probe returned $LASTEXITCODE"}
