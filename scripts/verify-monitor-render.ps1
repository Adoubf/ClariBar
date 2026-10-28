$ErrorActionPreference='Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
public static class ClariBarGlyphPixels {
 public static int[] Read(string path,int bandHeight) {
  using(var bitmap=new Bitmap(path)) {
   int[] rows=new int[bandHeight];
   for(int y=0;y<bandHeight;y++)for(int x=0;x<bitmap.Width;x++) {
    Color p=bitmap.GetPixel(x,bitmap.Height-bandHeight+y);
    if(Math.Abs(p.R-112)<40 && Math.Abs(p.G-216)<40 && Math.Abs(p.B-238)<40)rows[y]++;
   }
   return rows;
  }
 }
}
'@
$layout=Get-Content "$env:CLARIBAR_ROOT\logs\claribar-preview-layout.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$checks=@()
foreach($entry in $layout){
 $height=[int][Math]::Round($entry.height)
 $rows=[ClariBarGlyphPixels]::Read("$env:CLARIBAR_ROOT\artifacts\ClariBar\screenshots\$($entry.image)",$height)
 $bands=@();$start=-1
 for($y=0;$y -le $height;$y++){
  if($y -lt $height -and $rows[$y] -gt 0){if($start -lt 0){$start=$y}}
  elseif($start -ge 0){$bands+=,[pscustomobject]@{top=$start;bottom=$y-1};$start=-1}
 }
 $gap=if($bands.Count -eq 2){$bands[1].top-$bands[0].bottom-1}else{-1}
 $top=if($bands.Count){$bands[0].top}else{-1}
 $bottom=if($bands.Count){$height-1-$bands[-1].bottom}else{-1}
 $centerError=if($bands.Count){[Math]::Abs(($bands[0].top+$bands[-1].bottom+1)/2-($height/2-$entry.up))}else{999}
 $passed=$bands.Count -eq 2 -and $gap -ge 3 -and $top -ge 1 -and $bottom -ge 1 -and $centerError -le 2
 $checks+=,[pscustomobject][ordered]@{font=$entry.font;image=$entry.image;height=$height;up=$entry.up;bands=$bands;gap=$gap;top=$top;bottom=$bottom;centerError=$centerError;passed=$passed}
}
$report=[ordered]@{capturedAt=[DateTimeOffset]::Now.ToString('o');passed=$checks.Count -eq 4 -and !($checks | Where-Object {!$_.passed});checks=$checks}
$report | ConvertTo-Json -Depth 6 | Set-Content "$env:CLARIBAR_ROOT\logs\claribar-monitor-pixels.json" -Encoding UTF8
$report | ConvertTo-Json -Depth 6
if(!$report.passed){throw 'Rendered glyph spacing, bounds or optical alignment check failed.'}
