param([switch]$Check, [switch]$UiCheck, [switch]$ElevatedCheck, [switch]$TaskbarCheck)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
$sourceRoot = Split-Path $PSScriptRoot
$cache = $env:CLARIBAR_ROOT
$versionInfo=& "$PSScriptRoot\Get-ClariBarVersion.ps1"
$artifact = "$cache\artifacts\ClariBar\$($versionInfo.DisplayVersion)"
Set-Location $sourceRoot
if (Get-Process -Name ClariBar -ErrorAction SilentlyContinue) {
    throw 'Quit ClariBar from the system tray before rebuilding.'
}
Start-Transcript -Path "$cache\logs\claribar-build.log" -Append
try {
    & cmake -S "$sourceRoot\src\ClariBar.Native" -B "$cache\build\claribar-native" -G Ninja -DCMAKE_BUILD_TYPE=Release
    if ($LASTEXITCODE -ne 0) { throw 'Native configure failed' }
    & cmake --build "$cache\build\claribar-native"
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
    [void](New-Item -ItemType Directory -Force -Path $artifact,"$artifact\broker")
    foreach ($project in @('ClariBar.SensorBroker','ClariBar.App')) {
        $destination=if($project -eq 'ClariBar.App'){$artifact}else{"$artifact\broker"}
        & dotnet publish "$sourceRoot\src\$project\$project.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $destination --nologo
        if ($LASTEXITCODE -ne 0) { throw "Managed publish failed: $project" }
    }
    Copy-Item "$cache\build\claribar-native\ClariBar.Native.dll" $artifact -Force
    $tap="$cache\build\claribar-native\ExplorerTap\ClariBar.ExplorerTap.dll"
    $tapHash=(Get-FileHash -LiteralPath $tap -Algorithm SHA256).Hash.Substring(0,16)
    $adapter="$cache\artifacts\ClariBar\adapters\$tapHash"
    [void](New-Item -ItemType Directory -Force -Path $adapter)
    if(!(Test-Path "$adapter\ClariBar.ExplorerTap.dll")){Copy-Item $tap "$adapter\ClariBar.ExplorerTap.dll"}
    "$adapter\ClariBar.ExplorerTap.dll" | Set-Content "$artifact\adapter-path.txt" -Encoding UTF8
    Copy-Item "$sourceRoot\assets\branding\ClariBar.ico" $artifact -Force
    Copy-Item "$sourceRoot\THIRD-PARTY-NOTICES.md" $artifact -Force
    [void](New-Item -ItemType Directory -Force -Path "$artifact\licenses")
    Copy-Item "$sourceRoot\licenses\*" "$artifact\licenses" -Recurse -Force
    Copy-Item "$sourceRoot\README.md" $artifact -Force
    if ($Check) {
        $project="$sourceRoot\tests\ClariBar.Checks\ClariBar.Checks.csproj"
        & dotnet build $project -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Checks build failed' }
        & dotnet "$cache\build\bin\ClariBar.Checks\Release\net10.0-windows\ClariBar.Checks.dll" "$artifact\broker\ClariBar.SensorBroker.dll"
        if ($LASTEXITCODE -ne 0) { throw 'Checks failed' }
    }
    if ($UiCheck -or $ElevatedCheck -or $TaskbarCheck) {
        $testArgs = if ($ElevatedCheck) { '--smoke-test --elevated' } else { '--smoke-test' }
        if($TaskbarCheck){$testArgs+=' --taskbar-check'}
        Remove-Item "$cache\logs\claribar-ui-smoke.json" -ErrorAction SilentlyContinue
        $process = Start-Process "$artifact\ClariBar.exe" -WorkingDirectory $artifact -ArgumentList $testArgs -PassThru
        if (!$process.WaitForExit(120000)) { Stop-Process -Id $process.Id -Force; throw 'UI smoke check timed out' }
        if ($process.ExitCode -ne 0) { throw "UI exited with $($process.ExitCode)" }
        $report = Get-Content "$cache\logs\claribar-ui-smoke.json" -Raw -Encoding UTF8 | ConvertFrom-Json
        if (!$report.passed) { throw "UI smoke check failed: $($report.error)" }
        $report | ConvertTo-Json
        Copy-Item "$cache\logs\claribar-ui-smoke.json" "$cache\logs\claribar-ui-smoke-$(if($ElevatedCheck){'admin'}else{'user'}).json" -Force
    }
    Write-Output "Build ready: $artifact\ClariBar.exe"
} finally { Stop-Transcript }
