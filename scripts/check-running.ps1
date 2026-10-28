$ErrorActionPreference='Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
# Read only our own application's private diagnostic mapping. No UI automation,
# Explorer mutation, or second XAML diagnostics session is started here.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ClariBarMappingReader {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]public static extern IntPtr OpenFileMapping(uint access,bool inherit,string name);
 [DllImport("kernel32.dll",SetLastError=true)]public static extern IntPtr MapViewOfFile(IntPtr map,uint access,uint high,uint low,UIntPtr size);
 [DllImport("kernel32.dll")]public static extern bool UnmapViewOfFile(IntPtr view);
 [DllImport("kernel32.dll")]public static extern bool CloseHandle(IntPtr handle);
}
'@
$app=Get-Process -Name ClariBar -ErrorAction Stop | Select-Object -First 1
$prefix='Local\ClariBar.Xaml.'+$app.Id+'.'
$logs=@(Get-Item "$env:CLARIBAR_ROOT\logs\tap-bootstrap.log" -ErrorAction SilentlyContinue)
$logs+=@(Get-ChildItem "$env:CLARIBAR_ROOT\artifacts\ClariBar\adapters" -Filter tap-bootstrap.log -File -Recurse -ErrorAction SilentlyContinue)
$logs+=@(Get-ChildItem "$env:CLARIBAR_ROOT\adapters" -Filter tap-bootstrap.log -File -Recurse -ErrorAction SilentlyContinue)
$names=@($logs | Sort-Object LastWriteTime | ForEach-Object {Get-Content $_.FullName -Encoding Unicode} | Where-Object {$_ -like "*Initialization data $prefix*"})
if(!$names){throw 'The running application has not connected to its taskbar adapter.'}
$name=($names[-1] -split 'Initialization data ',2)[1].Trim()
$handle=[ClariBarMappingReader]::OpenFileMapping(4,$false,$name)
if($handle -eq [IntPtr]::Zero){throw 'Could not open the application diagnostic mapping.'}
$view=[IntPtr]::Zero
try {
 $view=[ClariBarMappingReader]::MapViewOfFile($handle,4,0,0,[UIntPtr]::Zero)
 if($view -eq [IntPtr]::Zero){throw 'Could not read the diagnostic mapping.'}
 # Private x64 ABI v3: config offset 32 / size 3372; status offset 3408 /
 # size 80; UTF-16 dump offset 3492 / capacity 60000. Reject other layouts.
 if([Runtime.InteropServices.Marshal]::ReadInt32($view,0) -ne 0x43425832 -or
    [Runtime.InteropServices.Marshal]::ReadInt32($view,4) -ne 3 -or
    [Runtime.InteropServices.Marshal]::ReadInt32($view,8) -ne 123496){throw 'Unsupported diagnostic ABI.'}
 $data=New-Object byte[] 123496
 $stable=$false
 for($attempt=0;$attempt -lt 10;$attempt++) {
  $before=@(16,3404,3488) | ForEach-Object {[Runtime.InteropServices.Marshal]::ReadInt32($view,$_)}
  if($before | Where-Object {($_ -band 1) -ne 0}){continue}
  [Runtime.InteropServices.Marshal]::Copy($view,$data,0,$data.Length)
  $after=@(16,3404,3488) | ForEach-Object {[Runtime.InteropServices.Marshal]::ReadInt32($view,$_)}
  if(($before -join ',') -eq ($after -join ',')){$stable=$true;break}
 }
 if(!$stable){throw 'Diagnostic snapshot changed during reading; retry.'}
 $tree=[Text.Encoding]::Unicode.GetString($data,3492,120000).Split([char]0)[0]
 $status=[ordered]@{}
 $fields=@('connected','explorerPid','frames','backgrounds','times','dates','systemIcons','networks','volumes','inputs','batteries','bitmapIcons','panels','metricCount','patchedProperties','appliedRevision','restored')
 for($i=0;$i -lt $fields.Length;$i++){$status[$fields[$i]]=[BitConverter]::ToUInt32($data,3408+4*$i)}
 $status.lastError=[BitConverter]::ToInt32($data,3476)
 $report=[ordered]@{
  capturedAt=[DateTimeOffset]::Now.ToString('o');processId=$app.Id;executable=$app.Path
  backgroundMode=[BitConverter]::ToUInt32($data,36)
  backgroundArgb=('{0:X8}' -f [BitConverter]::ToUInt32($data,40))
  embedded=[BitConverter]::ToUInt32($data,44);layout=[BitConverter]::ToUInt32($data,48)
  clickThrough=[BitConverter]::ToUInt32($data,52);fontSize=[BitConverter]::ToUInt32($data,56)
  autoWidth=[BitConverter]::ToUInt32($data,3204)
  fontWeight=[BitConverter]::ToUInt32($data,3208)
  fontFamily=[Text.Encoding]::Unicode.GetString($data,3212,192).Split([char]0)[0]
  status=$status
  transparentBackgrounds=([regex]::Matches($tree,'#BackgroundFill[^\r\n]*fill=#00[A-F0-9]{6}')).Count
  transparentBorders=([regex]::Matches($tree,'#BackgroundStroke[^\r\n]*fill=#00[A-F0-9]{6}')).Count
  panelWidths=@([regex]::Matches($tree,'#ClariBarEmbeddedMonitor \[(\d+)x') | ForEach-Object {[int]$_.Groups[1].Value})
  passThroughPanels=([regex]::Matches($tree,'#ClariBarEmbeddedMonitor[^\r\n]*hitTest=0')).Count
  trimmedMetrics=([regex]::Matches($tree,'#ClariBarMetric\d+[^\r\n]*trimmed=1')).Count
  twoColumnPassThroughPanels=([regex]::Matches($tree,'#ClariBarEmbeddedMonitor[^\r\n]*columns=2[^\r\n]*hitTest=0')).Count
 }
 $report | ConvertTo-Json -Depth 5 | Set-Content "$env:CLARIBAR_ROOT\logs\claribar-running-check.json" -Encoding UTF8
 $tree | Set-Content "$env:CLARIBAR_ROOT\logs\claribar-running-tree.txt" -Encoding UTF8
 $report | ConvertTo-Json -Depth 5
} finally {
 if($view -ne [IntPtr]::Zero){[void][ClariBarMappingReader]::UnmapViewOfFile($view)}
 [void][ClariBarMappingReader]::CloseHandle($handle)
}
