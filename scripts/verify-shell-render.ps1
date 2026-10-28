$ErrorActionPreference='Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
Add-Type -AssemblyName System.Drawing
$directory="$env:CLARIBAR_ROOT\artifacts\ClariBar\screenshots"
$bitmap=[Drawing.Bitmap]::new("$directory\shell-compact.png")
try {
 $ink=0
 for($y=60;$y -lt 320;$y++){for($x=18;$x -lt 50;$x++){$pixel=$bitmap.GetPixel($x,$y);if($pixel.R -lt 120 -and $pixel.G -lt 120 -and $pixel.B -lt 120){$ink++}}}
 $header=$bitmap.GetPixel(180,20)
 $sidebar=$bitmap.GetPixel(30,500)
 $toolbarColor=('#{0:X2}{1:X2}{2:X2}' -f $header.R,$header.G,$header.B)
 $sidebarColor=('#{0:X2}{1:X2}{2:X2}' -f $sidebar.R,$sidebar.G,$sidebar.B)
 $report=[ordered]@{capturedAt=[DateTimeOffset]::Now.ToString('o');passed=$ink -ge 100 -and $toolbarColor -eq '#ECF6F9' -and $sidebarColor -eq '#F3F3F3';navigationIconPixels=$ink;toolbar=$toolbarColor;sidebar=$sidebarColor}
 $report | ConvertTo-Json | Set-Content "$env:CLARIBAR_ROOT\logs\claribar-shell-pixels.json" -Encoding UTF8
 $report | ConvertTo-Json
 if(!$report.passed){throw 'Collapsed navigation icons or reference palette verification failed.'}
} finally {$bitmap.Dispose()}
