param([switch]$SkipBuild,[string]$InnoCompiler,[string]$PawnIOInstaller)
$ErrorActionPreference='Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
$root=Split-Path $PSScriptRoot
$versionInfo=& "$PSScriptRoot\Get-ClariBarVersion.ps1"
$version=$versionInfo.DisplayVersion
$stage="$env:CLARIBAR_ROOT\build\package\$version\payload"
$release="$env:CLARIBAR_ROOT\artifacts\ClariBar\releases"
$inner="$env:CLARIBAR_ROOT\build\package\$version\installer"
$brokerPublish="$env:CLARIBAR_ROOT\build\package\$version\broker-publish"
if(!$InnoCompiler){
 $candidates=@("$env:CLARIBAR_ROOT\programs\InnoSetup\ISCC.exe",(Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Inno Setup 7\ISCC.exe'),(Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'))
 $InnoCompiler=$candidates | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
 if(!$InnoCompiler){$InnoCompiler='ISCC.exe'}
 if(!(Test-Path $InnoCompiler)){
  $command=Get-Command ISCC.exe -ErrorAction SilentlyContinue
  if($command){$InnoCompiler=$command.Source}
 }
}
$compilerCommand=Get-Command $InnoCompiler -ErrorAction SilentlyContinue
if($compilerCommand){$InnoCompiler=$compilerCommand.Source}
if(!(Test-Path -LiteralPath $InnoCompiler)){throw 'Install Inno Setup 7.1 or provide -InnoCompiler with its ISCC.exe path.'}
if(!$PawnIOInstaller){$PawnIOInstaller="$env:CLARIBAR_ROOT\downloads\packaging\PawnIO_setup.exe"}
if(!(Test-Path -LiteralPath $PawnIOInstaller)){throw 'Download the official PawnIO installer and provide its path with -PawnIOInstaller.'}
$signature=Get-AuthenticodeSignature -LiteralPath $PawnIOInstaller
if($signature.Status -ne 'Valid'){throw 'The PawnIO installer must have a valid digital signature.'}
& "$PSScriptRoot\build-branding.ps1"
if(!$SkipBuild){& "$PSScriptRoot\build.ps1" -Check}
# Rebuild owned staging directories so an older publish cannot leave stale DLLs.
foreach($directory in @($stage,$brokerPublish)){if(Test-Path $directory){Remove-Item $directory -Recurse -Force}}
[void](New-Item -ItemType Directory -Force -Path $stage,$brokerPublish,"$stage\adapter",$release,$inner)
& dotnet publish "$root\src\ClariBar.App\ClariBar.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:WindowsAppSDKSelfContained=true -o $stage --nologo
if($LASTEXITCODE -ne 0){throw 'Self-contained UI publish failed.'}
foreach($resource in @('App.xbf','MainWindow.xbf','ClariBar.pri')){if(!(Test-Path (Join-Path $stage $resource))){throw "Published application XAML resource missing: $resource"}}
& dotnet publish "$root\src\ClariBar.SensorBroker\ClariBar.SensorBroker.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $brokerPublish --nologo
if($LASTEXITCODE -ne 0){throw 'Self-contained broker publish failed.'}
# Both executables use the same app-local runtime. Reject version conflicts
# rather than silently replacing a dependency used by the other process.
$sharedFiles=0;$sharedBytes=0L
foreach($file in Get-ChildItem $brokerPublish -Recurse -File){
 if($file.Extension -eq '.pdb'){continue}
 $relative=$file.FullName.Substring($brokerPublish.Length+1);$target=Join-Path $stage $relative
 if(Test-Path $target){
  if((Get-FileHash $file.FullName).Hash -ne (Get-FileHash $target).Hash){throw "UI and broker publish conflicting dependencies: $relative"}
  $sharedFiles++;$sharedBytes+=$file.Length
 }else{[void](New-Item -ItemType Directory -Force -Path (Split-Path $target));Copy-Item $file.FullName $target}
}
foreach($file in @('ClariBar.SensorBroker.exe','ClariBar.SensorBroker.dll','ClariBar.SensorBroker.deps.json','ClariBar.SensorBroker.runtimeconfig.json','hostfxr.dll','coreclr.dll','ClariBar.SystemMetrics.dll')){
 if(!(Test-Path (Join-Path $stage $file))){throw "Shared application payload is missing: $file"}
}
Copy-Item "$env:CLARIBAR_ROOT\build\claribar-native\ClariBar.Native.dll","$env:CLARIBAR_ROOT\build\claribar-native\ClariBar.Maintenance.dll" $stage -Force
Copy-Item "$env:CLARIBAR_ROOT\build\claribar-native\ExplorerTap\ClariBar.ExplorerTap.dll" "$stage\adapter" -Force
Copy-Item "$root\README.md","$root\README.zh-CN.md","$root\THIRD-PARTY-NOTICES.md","$root\LICENSE" $stage -Force
if(Test-Path "$root\assets\screenshots"){
 [void](New-Item -ItemType Directory -Force -Path "$stage\assets")
 Copy-Item "$root\assets\screenshots" "$stage\assets" -Recurse -Force
}
[void](New-Item -ItemType Directory -Force -Path "$stage\licenses")
Copy-Item "$root\licenses\*" "$stage\licenses" -Recurse -Force
Copy-Item (Join-Path (Split-Path $InnoCompiler) 'license.txt') "$stage\licenses\Inno-Setup-license.txt" -Force
$dotnetCommand=Get-Command dotnet.exe -ErrorAction Stop
foreach($notice in @('LICENSE.txt','ThirdPartyNotices.txt')){
 $file=Join-Path (Split-Path $dotnetCommand.Source) $notice
 if(Test-Path $file){Copy-Item $file "$stage\licenses\dotnet-$notice" -Force}
}
[void](New-Item -ItemType Directory -Force -Path "$stage\prerequisites")
Copy-Item $PawnIOInstaller "$stage\prerequisites\PawnIO_setup.exe" -Force
Get-ChildItem $stage -Filter *.pdb -Recurse | Remove-Item -Force
# The installer writes the selected runtime directory after installation.
Remove-Item "$stage\runtime-path.txt" -ErrorAction SilentlyContinue
$payload=@(Get-ChildItem $stage -Recurse -File | Where-Object Name -ne 'payload-manifest.json' | ForEach-Object {
 [ordered]@{path=$_.FullName.Substring($stage.Length+1);bytes=$_.Length;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}
})
$payloadBytes=0L;foreach($entry in $payload){$payloadBytes+=$entry.bytes}
@{version=$version;architecture='win-x64';selfContained=$true;files=$payload} | ConvertTo-Json -Depth 5 | Set-Content "$stage\payload-manifest.json" -Encoding UTF8
& $InnoCompiler /Qp "/DAppVersion=$version" "/DAppFileVersion=$($versionInfo.FileVersion)" "/DPayloadDir=$stage" "/DReleaseDir=$inner" "/DSourceRoot=$root" "$root\installer\ClariBar.iss"
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$bootstrap="$env:CLARIBAR_ROOT\build\installer-bootstrap"
& cmake -S "$root\installer" -B $bootstrap -G Ninja -DCMAKE_BUILD_TYPE=Release "-DINNER_SETUP=$inner/ClariBar-Setup-$version-x64.exe"
if($LASTEXITCODE -ne 0){throw 'Installer bootstrap configure failed.'}
& cmake --build $bootstrap
if($LASTEXITCODE -ne 0){throw 'Installer bootstrap build failed.'}
$setup="$release\ClariBar-Setup-$version-x64.exe"
Copy-Item "$bootstrap\ClariBar.Setup.exe" $setup -Force
$hash=(Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$setup.sha256",$hash+'  '+[IO.Path]::GetFileName($setup)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
[ordered]@{version=$version;installer=$setup;bytes=(Get-Item $setup).Length;sha256=$hash;payload=$stage;files=$payload.Count;payloadBytes=$payloadBytes;selfContained=$true;sharedRuntime=$true;sharedFiles=$sharedFiles;deduplicatedBytes=$sharedBytes} | ConvertTo-Json | Set-Content "$env:CLARIBAR_ROOT\logs\claribar-package.json" -Encoding UTF8
Write-Output "Installer ready: $setup"
