param(
 [Parameter(Mandatory=$true)][string]$ApplicationDirectory,
 [Parameter(Mandatory=$true)][string]$SettingsDirectory,
 [Parameter(Mandatory=$true)][string]$SelectedImage,
 [Parameter(Mandatory=$true)][string]$ValidationDirectory,
 [ValidateSet('en-US','zh-CN')][string]$Language='zh-CN'
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
if(Test-Path "$ApplicationDirectory\runtime-path.txt"){throw 'Use an uninstalled payload so the live check can run in isolation.'}
$settingsPath=Join-Path $SettingsDirectory 'settings.json'
$keyPath=Join-Path $SettingsDirectory 'ocr-credential.bin'
$settingsHash=(Get-FileHash $settingsPath).Hash
$keyHash=if(Test-Path $keyPath){(Get-FileHash $keyPath).Hash}else{''}
[void](New-Item -ItemType Directory -Force -Path "$ValidationDirectory\data\smoke-session")
Copy-Item $settingsPath "$ValidationDirectory\data\smoke-session\settings.json" -Force
if(Test-Path $keyPath){Copy-Item $keyPath "$ValidationDirectory\data\ocr-credential.bin" -Force}
$info=[Diagnostics.ProcessStartInfo]::new("$ApplicationDirectory\ClariBar.exe",'--smoke-test --desktop-tools-check --configured-translation-check --language='+$Language)
$info.WorkingDirectory=$ApplicationDirectory;$info.UseShellExecute=$false
$info.EnvironmentVariables['CLARIBAR_ROOT']=$ValidationDirectory
$info.EnvironmentVariables['CLARIBAR_DESKTOP_CHECK_ROOT']=$ValidationDirectory
$info.EnvironmentVariables['CLARIBAR_TRANSLATION_SAMPLE']=$SelectedImage
$process=[Diagnostics.Process]::Start($info)
if(!$process.WaitForExit(240000)){if(!$process.HasExited){$process.Kill()};throw 'Configured AI validation timed out.'}
if($process.ExitCode -ne 0){throw "Configured AI validation exited with $($process.ExitCode)."}
$report="$ValidationDirectory\logs\claribar-desktop-tools-check.json"
$result=Get-Content $report -Raw -Encoding UTF8 | ConvertFrom-Json
if(!$result.passed){throw "Configured AI validation failed: $($result.error)"}
if((Get-FileHash $settingsPath).Hash -ne $settingsHash){throw 'Original settings changed during validation.'}
if($keyHash -and (Get-FileHash $keyPath).Hash -ne $keyHash){throw 'The original encrypted API key changed during validation.'}
[ordered]@{passed=$true;checks=$result.checks.Count;report=$report;settingsUnchanged=$true;credentialUnchanged=$true} | ConvertTo-Json -Compress
