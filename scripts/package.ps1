param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
$root=Split-Path $PSScriptRoot
$version='0.6.1'
$stage="$env:CLARIBAR_ROOT\build\package\$version\payload"
$release="$env:CLARIBAR_ROOT\artifacts\ClariBar\releases"
$inner="$env:CLARIBAR_ROOT\build\package\$version\installer"
& "$PSScriptRoot\build-branding.ps1"
if(!$SkipBuild){& "$PSScriptRoot\build.ps1" -Check -UiCheck}
# All publish/runtime downloads stay in the dedicated E-drive development shell.
[void](New-Item -ItemType Directory -Force -Path $stage,"$stage\broker","$stage\adapter",$release,$inner)
& dotnet publish "$root\src\ClariBar.App\ClariBar.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:WindowsAppSDKSelfContained=true -o $stage --nologo
if($LASTEXITCODE -ne 0){throw 'Self-contained UI publish failed.'}
foreach($resource in @('App.xbf','MainWindow.xbf','ClariBar.pri')){if(!(Test-Path (Join-Path $stage $resource))){throw "Published application XAML resource missing: $resource"}}
& dotnet publish "$root\src\ClariBar.SensorBroker\ClariBar.SensorBroker.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o "$stage\broker" --nologo
if($LASTEXITCODE -ne 0){throw 'Self-contained broker publish failed.'}
Copy-Item "$env:CLARIBAR_ROOT\build\claribar-native\ClariBar.Native.dll","$env:CLARIBAR_ROOT\build\claribar-native\ClariBar.Maintenance.dll" $stage -Force
Copy-Item "$env:CLARIBAR_ROOT\build\claribar-native\ExplorerTap\ClariBar.ExplorerTap.dll" "$stage\adapter" -Force
Copy-Item "$root\README.md","$root\THIRD-PARTY-NOTICES.md","$root\LICENSE" $stage -Force
[void](New-Item -ItemType Directory -Force -Path "$stage\licenses")
Copy-Item "$root\docs\licenses\*" "$stage\licenses" -Recurse -Force
Copy-Item "$env:CLARIBAR_ROOT\programs\InnoSetup\license.txt" "$stage\licenses\Inno-Setup-license.txt" -Force
foreach($notice in @('LICENSE.txt','ThirdPartyNotices.txt')){
 $file=Join-Path $env:DOTNET_ROOT $notice
 if(Test-Path $file){Copy-Item $file "$stage\licenses\dotnet-$notice" -Force}
}
[void](New-Item -ItemType Directory -Force -Path "$stage\prerequisites")
Copy-Item "$env:CLARIBAR_ROOT\downloads\packaging\PawnIO_setup.exe" "$stage\prerequisites" -Force
Get-ChildItem $stage -Filter *.pdb -Recurse | Remove-Item -Force
[IO.File]::WriteAllText("$stage\runtime-path.txt",$env:CLARIBAR_ROOT,[Text.UTF8Encoding]::new($false))
$payload=@(Get-ChildItem $stage -Recurse -File | Where-Object Name -ne 'payload-manifest.json' | ForEach-Object {
 [ordered]@{path=$_.FullName.Substring($stage.Length+1);bytes=$_.Length;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}
})
@{version=$version;architecture='win-x64';selfContained=$true;files=$payload} | ConvertTo-Json -Depth 5 | Set-Content "$stage\payload-manifest.json" -Encoding UTF8
$compiler="$env:CLARIBAR_ROOT\programs\InnoSetup\ISCC.exe"
if(!(Test-Path $compiler)){throw 'Inno Setup compiler is missing under the dedicated tools directory.'}
& $compiler /Qp "/DPayloadDir=$stage" "/DReleaseDir=$inner" "/DSourceRoot=$root" "$root\installer\ClariBar.iss"
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$bootstrap="$env:CLARIBAR_ROOT\build\installer-bootstrap"
& cmake -S "$root\installer" -B $bootstrap -G Ninja -DCMAKE_BUILD_TYPE=Release "-DINNER_SETUP=$inner/ClariBar-Setup-$version-x64.exe"
if($LASTEXITCODE -ne 0){throw 'Installer bootstrap configure failed.'}
& cmake --build $bootstrap
if($LASTEXITCODE -ne 0){throw 'Installer bootstrap build failed.'}
$setup="$release\ClariBar-Setup-$version-x64.exe"
Copy-Item "$bootstrap\ClariBar.Setup.exe" $setup -Force
$hash=(Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$setup.sha256","$hash  $([IO.Path]::GetFileName($setup))`n",[Text.UTF8Encoding]::new($false))
[ordered]@{version=$version;installer=$setup;bytes=(Get-Item $setup).Length;sha256=$hash;payload=$stage;files=$payload.Count;selfContained=$true} | ConvertTo-Json | Set-Content "$env:CLARIBAR_ROOT\logs\claribar-package.json" -Encoding UTF8
Write-Output "Installer ready: $setup"
