param([string]$InnoCompiler)
$ErrorActionPreference='Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
$sourceRoot=Split-Path $PSScriptRoot
if(!$InnoCompiler){$InnoCompiler="$env:CLARIBAR_ROOT\programs\InnoSetup\ISCC.exe"}
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$testRoot="$env:CLARIBAR_ROOT\temp\upgrade-check-$stamp"
# Compile the real installer code with a distinct identity and a tiny fixture
# payload. Never reuse the installed application's registry key or shortcuts.
$testId='{'+[Guid]::NewGuid().ToString().ToUpperInvariant()+'}'
$liveKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{93FDC1C2-EB8E-48A4-9CF0-F59AEB645816}_is1'
$testKey="HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${testId}_is1"
$groupName="ClariBar Upgrade Check $stamp"
$group=Join-Path ([Environment]::GetFolderPath('Programs')) $groupName
$liveRegistration=Get-ItemProperty $liveKey -ErrorAction SilentlyContinue
$liveBefore=$liveRegistration | ConvertTo-Json -Compress
$liveProcesses=@(Get-Process ClariBar -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
$events=[Collections.Generic.List[object]]::new()
$app="$testRoot\old-app"
$data="$testRoot\运行数据"
[void](New-Item -ItemType Directory -Force -Path $testRoot,"$testRoot\old-payload\broker","$testRoot\new-payload","$data\data")
function Assert($condition,[string]$message){if(!$condition){throw $message}}
function Snapshot([string]$root){
 $result=@{}
 Get-ChildItem $root -Recurse -File | ForEach-Object {$result[$_.FullName]=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}
 return $result
}
function Assert-Retained($snapshot){
 foreach($path in $snapshot.Keys){
  Assert (Test-Path -LiteralPath $path) "User data was removed: $path"
  Assert ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $snapshot[$path]) "User data was changed: $path"
 }
}
function Add-Event([string]$name){$events.Add([ordered]@{name=$name;passed=$true})}
function Run-Setup([string]$setup,[string]$name,[string[]]$extra=@(),[switch]$ExpectFailure){
 $log="$testRoot\$name.log"
 $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',"/LOG=`"$log`"")+$extra
 $process=Start-Process -FilePath $setup -ArgumentList $arguments -PassThru
 Assert ($process.WaitForExit(120000)) "Installer timed out: $name"
 if($ExpectFailure){Assert ($process.ExitCode -ne 0) "Expected setup to reject $name"}
 else{Assert ($process.ExitCode -eq 0) "Installer failed: $name ($($process.ExitCode)); see $log"}
 return $log
}
function Get-Uninstaller{
 $entry=Get-ItemProperty -LiteralPath $testKey
 return $entry.UninstallString.Trim('"')
}
function Remove-Fixture{
 if(Test-Path -LiteralPath $testKey){
  $process=Start-Process -FilePath (Get-Uninstaller) -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$testRoot\uninstall-$([Guid]::NewGuid().ToString('N')).log`"") -PassThru
  Assert ($process.WaitForExit(120000)) 'Fixture uninstaller timed out.'
  Assert ($process.ExitCode -eq 0) "Fixture uninstaller failed: $($process.ExitCode)"
  Assert (!(Test-Path -LiteralPath $testKey)) 'Fixture registration remains.'
 }
}
function Assert-Installation([string]$directory,[string]$dataRoot,[string]$marker){
 $entry=Get-ItemProperty -LiteralPath $testKey
 Assert ($entry.InstallLocation.TrimEnd('\') -eq $directory) 'Incorrect program directory.'
 Assert ($entry.'Inno Setup CodeFile: DataRoot' -eq $dataRoot) 'Incorrect retained data root.'
 Assert (([IO.File]::ReadAllText("$directory\runtime-path.txt")).Trim() -eq $dataRoot) 'Runtime data pointer was not retained.'
 Assert (([IO.File]::ReadAllText("$directory\ClariBar.exe")) -eq $marker) 'Incorrect application payload.'
 Assert ($entry.'Inno Setup: Selected Tasks' -notmatch 'desktopicon') 'Upgrade lost the previous shortcut choice.'
 $links=@(Get-ChildItem $group -Filter '*.lnk' | Where-Object Name -notmatch 'Uninstall|卸载')
 Assert ($links.Count -eq 1) "Expected one application shortcut in the isolated group, found $($links.Count)."
 $shell=New-Object -ComObject WScript.Shell
 Assert ($shell.CreateShortcut($links[0].FullName).TargetPath -eq "$directory\ClariBar.exe") 'Shortcut was not updated to the new directory.'
}
$liveLinks=@{}
if($liveRegistration){
 $liveGroup=Join-Path ([Environment]::GetFolderPath('Programs')) $liveRegistration.'Inno Setup: Icon Group'
 if(Test-Path $liveGroup){$liveLinks=Snapshot $liveGroup}
}
try{
 foreach($payload in @('old-payload','new-payload')){
  Copy-Item "$env:CLARIBAR_ROOT\build\claribar-native\ClariBar.Maintenance.dll" "$testRoot\$payload" -Force
 }
 [IO.File]::WriteAllText("$testRoot\old-payload\ClariBar.exe",'old-program')
 [IO.File]::WriteAllText("$testRoot\old-payload\legacy-only.dll",'obsolete-program-file')
 [IO.File]::WriteAllText("$testRoot\old-payload\broker\old-runtime.dll",'obsolete-duplicate-runtime')
 [IO.File]::WriteAllText("$testRoot\new-payload\ClariBar.exe",'new-program')
 foreach($kind in @('old','new')){
  $version=if($kind -eq 'old'){'0.82'}else{'0.83'}
  $fileVersion=if($kind -eq 'old'){'0.8.2.0'}else{'0.8.3.0'}
  $compiler=Start-Process $InnoCompiler -ArgumentList @('/Qp',"/DAppVersion=$version","/DAppFileVersion=$fileVersion","/DClariBarAppId=$testId","/DClariBarGroupName=`"$groupName`"","/DPayloadDir=`"$testRoot\$kind-payload`"","/DReleaseDir=`"$testRoot\$kind-setup`"","/DSourceRoot=`"$sourceRoot`"","`"$sourceRoot\installer\ClariBar.iss`"") -PassThru -Wait -RedirectStandardOutput "$testRoot\compile-$kind.log" -RedirectStandardError "$testRoot\compile-$kind-errors.log"
  Assert ($compiler.ExitCode -eq 0) "Fixture compilation failed: $kind; see $testRoot\compile-$kind-errors.log"
 }
 $old="$testRoot\old-setup\ClariBar-Setup-0.82-x64.exe"
 $new="$testRoot\new-setup\ClariBar-Setup-0.83-x64.exe"
 [void](Run-Setup $old 'install-old' @("/DIR=`"$app`"","/DATAROOT=`"$data`"","/GROUP=`"$groupName`"",'/MERGETASKS=!desktopicon'))
 foreach($file in @('data\settings.json','data\ocr-credential.bin','data\shortcuts.json','data\history.json','data\pins\image.png','models\en-zh\model.bin')){
  $path=Join-Path $data $file
  [void](New-Item -ItemType Directory -Force -Path (Split-Path $path))
  [IO.File]::WriteAllText($path,"Retain $file with Unicode: 用户配置")
 }
 [IO.File]::WriteAllText("$app\broker\user-notes.txt",'User-created data inside a legacy program subdirectory.')
 $snapshot=Snapshot $data
 $snapshot["$app\broker\user-notes.txt"]=(Get-FileHash "$app\broker\user-notes.txt").Hash
 Assert-Installation $app $data 'old-program'
 Add-Event 'isolated-first-install-with-unicode-data-path'

 $log=Run-Setup $new 'upgrade-without-data-or-directory-arguments'
 Assert ((Get-Content $log -Raw) -match 'Previous ClariBar uninstall completed') 'Upgrade did not execute the old uninstaller.'
 Assert-Installation $app $data 'new-program'
 Assert (!(Test-Path "$app\legacy-only.dll")) 'Obsolete program file survived the upgrade.'
 Assert (!(Test-Path "$app\broker\old-runtime.dll")) 'Obsolete runtime survived the upgrade.'
 Assert-Retained $snapshot
 Add-Event 'automatic-uninstall-before-upgrade-and-preserve-user-data'

 $log=Run-Setup $new 'same-version-reinstall' @('/LANG=english')
 Assert ((Get-Content $log -Raw) -match 'Previous ClariBar uninstall completed') 'Same-version reinstall skipped uninstall.'
 Assert-Installation $app $data 'new-program'
 Assert-Retained $snapshot
 Add-Event 'same-version-reinstall-retains-data-and-shortcut-preferences'

 $nextApp="$testRoot\new-app"
 [void](Run-Setup $new 'change-program-directory' @("/DIR=`"$nextApp`""))
 Assert-Installation $nextApp $data 'new-program'
 Assert (!(Test-Path "$app\ClariBar.exe")) 'Old program directory still contains an executable.'
 Assert-Retained $snapshot
 $app=$nextApp
 Add-Event 'change-program-directory-reuses-original-data-directory'

 # Simulate an older release without the custom registry metadata.
 Remove-ItemProperty -LiteralPath $testKey -Name 'Inno Setup CodeFile: DataRoot'
 [IO.File]::WriteAllText("$app\runtime-path.txt",$data,[Text.UTF8Encoding]::new($true))
 [void](Run-Setup $new 'legacy-data-pointer-fallback')
 Assert-Installation $app $data 'new-program'
 Assert-Retained $snapshot
 Add-Event 'legacy-utf8-runtime-pointer-recovers-data-root'

 $programHash=(Get-FileHash "$app\ClariBar.exe").Hash
 [void](Run-Setup $new 'reject-data-directory-change' @("/DATAROOT=`"$testRoot\wrong-data`"") -ExpectFailure)
 Assert ((Get-FileHash "$app\ClariBar.exe").Hash -eq $programHash) 'Rejected data override changed the program.'
 Assert-Retained $snapshot
 Add-Event 'conflicting-data-override-stops-before-uninstall'

 $uninstaller=Get-Uninstaller
 Move-Item $uninstaller "$uninstaller.saved"
 try{
  [void](Run-Setup $new 'missing-uninstaller' -ExpectFailure)
  Assert ((Get-FileHash "$app\ClariBar.exe").Hash -eq $programHash) 'Missing-uninstaller rejection changed the program.'
  Assert-Retained $snapshot
 }finally{Move-Item "$uninstaller.saved" $uninstaller}
 Add-Event 'incomplete-old-installation-stops-before-overwriting-files'

 # An executable with a nonzero exit code models a failed old uninstaller.
 [IO.File]::WriteAllText("$testRoot\failed-uninstall.cpp",'#include <windows.h>'+[Environment]::NewLine+'int WINAPI wWinMain(HINSTANCE,HINSTANCE,PWSTR,int){return 23;}')
 & cl.exe /nologo /O1 /MT "/Fo$testRoot\failed-uninstall.obj" "$testRoot\failed-uninstall.cpp" /link /SUBSYSTEM:WINDOWS "/OUT:$testRoot\failed-uninstall.exe" *> "$testRoot\compile-failure-stub.log"
 Assert ($LASTEXITCODE -eq 0) 'Failed-uninstaller fixture compilation failed.'
 Copy-Item $uninstaller "$uninstaller.saved"
 try{
  Copy-Item "$testRoot\failed-uninstall.exe" $uninstaller -Force
  $rejectedApp="$testRoot\must-not-be-installed"
  $log=Run-Setup $new 'failed-uninstaller' @("/DIR=`"$rejectedApp`"") -ExpectFailure
  Assert ((Get-Content $log -Raw) -match '23') 'Uninstaller failure was not reported.'
  Assert (!(Test-Path "$rejectedApp\ClariBar.exe")) 'New files were installed after uninstall failed.'
  Assert ((Get-FileHash "$app\ClariBar.exe").Hash -eq $programHash) 'Uninstaller failure changed the old program.'
  Assert-Retained $snapshot
 }finally{Copy-Item "$uninstaller.saved" $uninstaller -Force;Remove-Item "$uninstaller.saved"}
 Add-Event 'failed-uninstaller-aborts-without-installing-new-files'
 [void](Run-Setup $new 'retry-after-uninstall-failure')
 Assert-Installation $app $data 'new-program'
 Assert-Retained $snapshot
 Add-Event 'retry-after-uninstall-failure'

 Remove-Fixture
 Assert-Retained $snapshot
 Assert (!(Test-Path "$app\ClariBar.exe")) 'Uninstall left the program executable.'
 Assert (!(Test-Path $group)) 'Uninstall left the isolated shortcut group.'
 Add-Event 'final-uninstall-retains-personal-data'

 # Keep data under the old program directory, then move only the program.
 $embeddedApp="$testRoot\embedded-app"
 $embeddedData="$embeddedApp\个人数据"
 [void](Run-Setup $old 'install-embedded-data' @("/DIR=`"$embeddedApp`"","/DATAROOT=`"$embeddedData`"","/GROUP=`"$groupName`"",'/MERGETASKS=!desktopicon'))
 [IO.File]::WriteAllText("$embeddedData\data\settings.json",'Keep data even when it is inside the old application directory.')
 $embeddedSnapshot=Snapshot $embeddedData
 $embeddedNext="$testRoot\moved-embedded-app"
 [void](Run-Setup $new 'move-program-with-embedded-data' @("/DIR=`"$embeddedNext`""))
 Assert-Installation $embeddedNext $embeddedData 'new-program'
 Assert-Retained $embeddedSnapshot
 Assert (!(Test-Path "$embeddedApp\ClariBar.exe")) 'Moving the program left its old executable.'
 Add-Event 'data-inside-old-program-directory-survives-uninstall-and-move'
 Remove-Fixture
 Assert-Retained $embeddedSnapshot

 $liveAfter=Get-ItemProperty $liveKey -ErrorAction SilentlyContinue | ConvertTo-Json -Compress
 Assert ($liveBefore -eq $liveAfter) 'Lifecycle checks changed the live application registration.'
 Assert-Retained $liveLinks
 foreach($id in $liveProcesses){Assert (Get-Process -Id $id -ErrorAction SilentlyContinue) 'Lifecycle checks closed the live application.'}
 Add-Event 'live-installation-registration-shortcuts-and-processes-untouched'
 $summary=[ordered]@{capturedAt=[DateTimeOffset]::Now.ToString('o');passed=$true;testAppId=$testId;testRoot=$testRoot;events=$events}
 $summary | ConvertTo-Json -Depth 6 | Set-Content "$env:CLARIBAR_ROOT\logs\claribar-installer-upgrade-validation.json" -Encoding UTF8
 $summary | ConvertTo-Json -Depth 6
}finally{
 if(Test-Path -LiteralPath $testKey){Write-Warning "Test installation remains at $testRoot; its identity is $testId. Inspect logs before removing it."}
}
