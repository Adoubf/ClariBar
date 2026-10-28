$ErrorActionPreference='Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
$root=Split-Path $PSScriptRoot
& cmake -S "$root\src\ClariBar.Native" -B "$env:CLARIBAR_ROOT\build\claribar-native" -G Ninja -DCMAKE_BUILD_TYPE=Release
if($LASTEXITCODE -ne 0){throw 'Native configure failed'}
& cmake --build "$env:CLARIBAR_ROOT\build\claribar-native"
if($LASTEXITCODE -ne 0){throw 'Native build failed'}
