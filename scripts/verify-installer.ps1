param([string]$Installer='E:\haoyue\cache\claribar\artifacts\ClariBar\releases\ClariBar-Setup-0.6.1-x64.exe',[string]$ExistingTestRoot)
$ErrorActionPreference='Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
$testRoot=if($ExistingTestRoot){$ExistingTestRoot}else{"$env:CLARIBAR_ROOT\temp\installer-check-$(Get-Date -Format yyyyMMdd-HHmmss)"}
$app="$testRoot\app"
$data="$testRoot\运行数据"
[void](New-Item -ItemType Directory -Force -Path $testRoot,"$data\data")
$sentinel="$data\data\keep-on-uninstall.txt"
[IO.File]::WriteAllText($sentinel,'ClariBar installer must preserve runtime data.')
$events=@()
function Assert($condition,[string]$message){if(!$condition){throw $message}}
function Run-Installer([string]$log){
 $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',"/DIR=`"$app`"","/DATAROOT=`"$data`"",'/MERGETASKS=!desktopicon',"/LOG=`"$log`"")
 $process=Start-Process $Installer -ArgumentList $arguments -PassThru
 Assert ($process.WaitForExit(180000)) 'Installer timed out.'
 Assert ($process.ExitCode -eq 0) "Installer exited with $($process.ExitCode)"
}
function Start-IsolatedApp([string]$arguments){
 $info=[Diagnostics.ProcessStartInfo]::new("$app\ClariBar.exe",$arguments)
 $info.WorkingDirectory=$app;$info.UseShellExecute=$false
 $info.EnvironmentVariables['PATH']="$env:SystemRoot\System32;$env:SystemRoot"
 $info.EnvironmentVariables['DOTNET_ROOT']="$testRoot\missing-dotnet"
 $info.EnvironmentVariables['DOTNET_ROOT_X64']="$testRoot\missing-dotnet"
 $info.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP']='0'
 $info.EnvironmentVariables.Remove('CLARIBAR_ROOT')
 return [Diagnostics.Process]::Start($info)
}
if(!$ExistingTestRoot){Run-Installer "$testRoot\install.log"}
Assert (Test-Path "$app\ClariBar.exe") 'Installed application missing.'
Assert (([IO.File]::ReadAllText("$app\runtime-path.txt")).Trim() -eq $data) 'Selected Unicode data directory was not preserved.'
foreach($directory in @($app,"$app\broker")){
 foreach($file in @('hostfxr.dll','coreclr.dll')){Assert (Test-Path "$directory\$file") "Self-contained runtime is missing: $directory\$file"}
}
$manifest=Get-Content "$app\payload-manifest.json" -Raw -Encoding UTF8 | ConvertFrom-Json
foreach($entry in $manifest.files){
 if($entry.path -eq 'runtime-path.txt'){continue}
 $file=Join-Path $app $entry.path
 Assert (Test-Path $file) "Missing installed file: $($entry.path)"
 Assert ((Get-FileHash $file -Algorithm SHA256).Hash -eq $entry.sha256) "Installed file hash mismatch: $($entry.path)"
}
$group=Join-Path ([Environment]::GetFolderPath('Programs')) '澄栏 ClariBar'
$link=Join-Path $group '澄栏 ClariBar.lnk'
Assert (Test-Path $link) 'Start menu shortcut missing.'
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut($link)
Assert ($shortcut.TargetPath -eq "$app\ClariBar.exe") 'Start menu shortcut points to a development launcher.'
$events+=@{name='install-and-payload-integrity';passed=$true;files=$manifest.files.Count;shortcutTarget=$shortcut.TargetPath}
$process=Start-IsolatedApp '--smoke-test --taskbar-check'
Assert ($process.WaitForExit(120000)) 'Installed application smoke check timed out.'
Assert ($process.ExitCode -eq 0) "Installed application exit code: $($process.ExitCode)"
$report=Get-Content "$data\logs\claribar-ui-smoke.json" -Raw -Encoding UTF8 | ConvertFrom-Json
Assert $report.passed "Installed application smoke failed: $($report.error)"
Assert ($report.sensorCount -gt 0 -and $report.taskbar.panels -gt 0) 'Installed broker or taskbar adapter did not work.'
$events+=@{name='self-contained-start-without-development-runtime';passed=$true;sensors=$report.sensorCount;taskbars=$report.taskbar.panels;dataRoot=$data}
$process=Start-IsolatedApp '--page=sensors'
Start-Sleep -Seconds 5
Assert (!$process.HasExited) 'Installed application did not stay running before upgrade.'
Run-Installer "$testRoot\upgrade.log"
Assert ($process.WaitForExit(15000)) 'Upgrade did not gracefully close the running application.'
Assert (([IO.File]::ReadAllText($sentinel)) -eq 'ClariBar installer must preserve runtime data.') 'Upgrade changed runtime data.'
$events+=@{name='upgrade-running-application-and-preserve-data';passed=$true}
$process=Start-IsolatedApp '--page=sensors'
Start-Sleep -Seconds 5
$uninstall=Start-Process "$app\unins000.exe" -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$testRoot\uninstall.log`"") -PassThru
Assert ($uninstall.WaitForExit(120000)) 'Uninstall timed out.'
Assert ($uninstall.ExitCode -eq 0) "Uninstall exited with $($uninstall.ExitCode)"
Assert ($process.WaitForExit(15000)) 'Uninstall did not gracefully close the running application.'
Assert (!(Test-Path "$app\ClariBar.exe")) 'Uninstall left the application executable behind.'
$leftovers=@($manifest.files | Where-Object {Test-Path (Join-Path $app $_.path)})
Assert ($leftovers.Count -eq 0) "Uninstall left $($leftovers.Count) application payload files behind."
Assert (Test-Path $sentinel) 'Uninstall removed user data.'
Assert (!(Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{93FDC1C2-EB8E-48A4-9CF0-F59AEB645816}_is1')) 'Uninstall registration remains.'
$group=Join-Path ([Environment]::GetFolderPath('Programs')) '澄栏 ClariBar'
Assert (!(Test-Path "$group\澄栏 ClariBar.lnk")) 'Uninstall left its test Start menu shortcut.'
$events+=@{name='uninstall-running-application-and-preserve-data';passed=$true;remainingPayloadFiles=$leftovers.Count}
$summary=[ordered]@{capturedAt=[DateTimeOffset]::Now.ToString('o');passed=$true;installer=$Installer;sha256=(Get-FileHash $Installer -Algorithm SHA256).Hash;testRoot=$testRoot;events=$events}
$summary | ConvertTo-Json -Depth 6 | Set-Content "$env:CLARIBAR_ROOT\logs\claribar-installer-validation.json" -Encoding UTF8
$summary | ConvertTo-Json -Depth 6
