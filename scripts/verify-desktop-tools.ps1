param([Parameter(Mandatory=$true)][string]$ApplicationDirectory,[string]$ValidationDirectory,[switch]$SnipasteParity,[switch]$CaptureUi,[switch]$PinEditor,[switch]$Interaction,[switch]$AiOcr,[switch]$Translation,[switch]$TranslationLive,[switch]$CaptionHover,[switch]$CopyTranslation,[switch]$Streaming,[switch]$Responsiveness,[switch]$CapturePerformance,[switch]$CopyMenu,[switch]$CopyDismiss,[switch]$Stability,[switch]$TaskbarCompatibility,[switch]$MetricOrder,[switch]$SettingsRefinement)
$ErrorActionPreference='Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
$ApplicationDirectory=[IO.Path]::GetFullPath($ApplicationDirectory)
if(!(Test-Path "$ApplicationDirectory\ClariBar.exe")){throw 'ApplicationDirectory must contain a self-contained ClariBar payload.'}
if(Test-Path "$ApplicationDirectory\runtime-path.txt"){throw 'Use an uninstalled payload: installed runtime-path.txt would override the isolated test directory.'}
if(!$ValidationDirectory){$ValidationDirectory=Join-Path $env:CLARIBAR_ROOT ('temp\desktop-tools-check-'+[Guid]::NewGuid().ToString('N'))}
$ValidationDirectory=[IO.Path]::GetFullPath($ValidationDirectory)
foreach($locale in @('en-US','zh-CN')){
 $session=Join-Path $ValidationDirectory $locale
 $report=Join-Path $session 'logs\claribar-desktop-tools-check.json'
 $arguments='--smoke-test --desktop-tools-check --language='+$locale
 if($SnipasteParity){$arguments+=' --snipaste-parity-check'}
 if($CaptureUi){$arguments+=' --capture-ui-check'}
 if($PinEditor){$arguments+=' --pin-editor-check'}
 if($Interaction){$arguments+=' --interaction-check'}
 if($AiOcr){$arguments+=' --ai-ocr-check'}
 if($Translation){$arguments+=' --translation-check'}
 if($TranslationLive){$arguments+=' --translation-check --translation-live-check'}
 if($CaptionHover){$arguments+=' --caption-hover-check'}
 if($CopyTranslation){$arguments+=' --copy-translation-check'}
 if($CopyMenu){$arguments+=' --copy-menu-check'}
 if($CopyDismiss){$arguments+=' --copy-dismiss-check'}
 if($Stability){$arguments+=' --stability-check'}
 if($TaskbarCompatibility){$arguments+=' --taskbar-compat-check'}
 if($MetricOrder){$arguments+=' --metric-order-check'}
 if($SettingsRefinement){$arguments+=' --settings-refinement-check'}
 if($Streaming){$arguments+=' --streaming-translation-check'}
 if($Responsiveness){$arguments+=' --responsiveness-check'}
 if($CapturePerformance){$arguments+=' --capture-performance-check'}
 $info=[Diagnostics.ProcessStartInfo]::new("$ApplicationDirectory\ClariBar.exe",$arguments)
 $info.WorkingDirectory=$ApplicationDirectory;$info.UseShellExecute=$false
 $info.EnvironmentVariables['CLARIBAR_ROOT']=$session
 $info.EnvironmentVariables['CLARIBAR_DESKTOP_CHECK_ROOT']=$session
 if($CopyTranslation -or $CopyMenu -or $CopyDismiss){$info.EnvironmentVariables['CLARIBAR_COPY_PROBE']="$env:CLARIBAR_ROOT\build\claribar-native\ClariBar.CopyProbe.exe"}
 $info.EnvironmentVariables['PATH']="$env:SystemRoot\System32;$env:SystemRoot"
 $info.EnvironmentVariables['DOTNET_ROOT']=Join-Path $session 'no-development-runtime'
 $info.EnvironmentVariables['DOTNET_ROOT_X64']=$info.EnvironmentVariables['DOTNET_ROOT']
 if(Test-Path $report){Remove-Item $report}
 $process=[Diagnostics.Process]::Start($info)
 if(!$process.WaitForExit($(if($Stability){240000}else{120000}))){
  if(!$process.HasExited){$process.Kill()}
  throw "Desktop tool checks timed out for $locale. Inspect $session."
 }
 if($process.ExitCode -ne 0){throw "Desktop tool process exited with $($process.ExitCode) for $locale."}
 $result=Get-Content $report -Raw -Encoding UTF8 | ConvertFrom-Json
 if(!$result.passed){throw "Desktop tool checks failed for ${locale}: $($result.error)"}
 [ordered]@{language=$locale;passed=$true;checks=@($result.checks).Count;report=$report} | ConvertTo-Json -Compress
}
