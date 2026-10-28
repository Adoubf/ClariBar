$ErrorActionPreference='Stop'
. "$PSScriptRoot\Enter-ClariBarDev.ps1"
Add-Type -AssemblyName PresentationCore,WindowsBase,PresentationFramework
$root=Split-Path $PSScriptRoot
$output=Join-Path $root 'assets\branding'
[void](New-Item -ItemType Directory -Force -Path $output)
function Color([string]$hex){[Windows.Media.ColorConverter]::ConvertFromString($hex)}
function Brush([string]$hex){[Windows.Media.SolidColorBrush]::new((Color $hex))}
function Rect($x,$y,$w,$h){[Windows.Rect]::new([double]$x,[double]$y,[double]$w,[double]$h)}
$drawing=[Windows.Media.DrawingGroup]::new()
$dc=$drawing.Open()
$fill=[Windows.Media.LinearGradientBrush]::new((Color '#13B8A6'),(Color '#1763D8'),45)
$dc.DrawRoundedRectangle($fill,$null,(Rect 24 24 464 464),112,112)
$glass=[Windows.Media.LinearGradientBrush]::new((Color '#38FFFFFF'),(Color '#00FFFFFF'),90)
$dc.DrawRoundedRectangle($glass,[Windows.Media.Pen]::new((Brush '#2EFFFFFF'),2),(Rect 26 26 460 460),110,110)
$pen=[Windows.Media.Pen]::new((Brush '#FFFFFF'),40)
$pen.StartLineCap=$pen.EndLineCap=[Windows.Media.PenLineCap]::Round
$dc.DrawGeometry($null,$pen,[Windows.Media.Geometry]::Parse('M 337 158 C 308 130 267 117 225 126 C 165 139 126 194 130 254 C 134 313 181 361 240 364 C 277 366 307 355 332 334'))
foreach($bar in @(@(218,240,30,64),@(272,210,30,94),@(326,180,30,124))){$dc.DrawRoundedRectangle((Brush '#B8FFEB'),$null,(Rect @bar),15,15)}
$dc.DrawRoundedRectangle((Brush '#70FFFFFF'),$null,(Rect 128 399 256 20),10,10)
$dc.DrawRoundedRectangle((Brush '#FFFFFF'),$null,(Rect 128 399 90 20),10,10)
$dc.Close();$drawing.Freeze()
function Save-Bitmap($bitmap,[string]$path,[switch]$Bmp){
 $encoder=if($Bmp){[Windows.Media.Imaging.BmpBitmapEncoder]::new()}else{[Windows.Media.Imaging.PngBitmapEncoder]::new()}
 $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $stream=[IO.File]::Create($path);try{$encoder.Save($stream)}finally{$stream.Dispose()}
}
function Render-Logo([int]$size){
 $visual=[Windows.Media.DrawingVisual]::new();$ctx=$visual.RenderOpen()
 $ctx.PushTransform([Windows.Media.ScaleTransform]::new($size/512.0,$size/512.0));$ctx.DrawDrawing($drawing);$ctx.Pop();$ctx.Close()
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($visual);return $bitmap
}
$sizes=@(16,20,24,32,40,48,64,128,256)
$frames=@()
foreach($size in ($sizes+512+1024)){
 $bitmap=Render-Logo $size;$path=Join-Path $output "claribar-$size.png";Save-Bitmap $bitmap $path
 if($size -le 256){$frames+=,@([IO.File]::ReadAllBytes($path),$size)}
}
$ico=[IO.BinaryWriter]::new([IO.File]::Create((Join-Path $root 'assets\branding\ClariBar.ico')))
try{
 $ico.Write([uint16]0);$ico.Write([uint16]1);$ico.Write([uint16]$frames.Count);$offset=6+16*$frames.Count
 foreach($frame in $frames){$size=[int]$frame[1];$bytes=[byte[]]$frame[0];$dimension=if($size -eq 256){0}else{$size};$ico.Write([byte]$dimension);$ico.Write([byte]$dimension);$ico.Write([byte]0);$ico.Write([byte]0);$ico.Write([uint16]1);$ico.Write([uint16]32);$ico.Write([uint32]$bytes.Length);$ico.Write([uint32]$offset);$offset+=$bytes.Length}
 foreach($frame in $frames){$ico.Write([byte[]]$frame[0])}
}finally{$ico.Dispose()}
# Installer graphics use the same vector drawing. No generated text is baked into
# the application's icon, so the small-size and translated UI stay sharp.
$visual=[Windows.Media.DrawingVisual]::new();$ctx=$visual.RenderOpen()
$ctx.DrawRectangle([Windows.Media.LinearGradientBrush]::new((Color '#062D43'),(Color '#106D84'),90),$null,(Rect 0 0 328 628))
$ctx.PushTransform([Windows.Media.TranslateTransform]::new(28,78));$ctx.PushTransform([Windows.Media.ScaleTransform]::new(272/512.0,272/512.0));$ctx.DrawDrawing($drawing);$ctx.Pop();$ctx.Pop()
$face=[Windows.Media.Typeface]::new('Microsoft YaHei UI')
function Label([string]$value,[double]$size,[double]$x,[double]$y,[string]$color){$text=[Windows.Media.FormattedText]::new($value,[Globalization.CultureInfo]::GetCultureInfo('zh-CN'),[Windows.FlowDirection]::LeftToRight,$face,$size,(Brush $color),1);$ctx.DrawText($text,[Windows.Point]::new($x,$y))}
Label '澄栏' 42 38 366 '#FFFFFF';Label 'ClariBar' 27 39 425 '#B8FFEB';Label '清晰掌握你的桌面' 18 39 516 '#D4E8EF'
$ctx.Close();$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(328,628,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($visual);Save-Bitmap $bitmap (Join-Path $output 'installer-banner.bmp') -Bmp
# Review sheet shows both backgrounds and actual small icon sizes.
$visual=[Windows.Media.DrawingVisual]::new();$ctx=$visual.RenderOpen()
$ctx.DrawRectangle((Brush '#F3F6FA'),$null,(Rect 0 0 1440 800));$ctx.DrawRectangle((Brush '#102534'),$null,(Rect 720 0 720 800))
Label '澄栏 ClariBar' 36 54 38 '#113248';Label '全新应用图标' 20 56 96 '#4D6475'
foreach($x in @(160,880)){$ctx.PushTransform([Windows.Media.TranslateTransform]::new($x,175));$ctx.PushTransform([Windows.Media.ScaleTransform]::new(.8,.8));$ctx.DrawDrawing($drawing);$ctx.Pop();$ctx.Pop()}
Label '任务栏 · 监控 · 清晰可见' 20 900 65 '#C5E4ED'
$x=115
foreach($size in @(16,24,32,48,64,128)){$ctx.DrawImage((Render-Logo $size),(Rect $x 665 $size $size));$x+=$size+40}
$ctx.Close();$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(1440,800,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($visual);Save-Bitmap $bitmap (Join-Path $env:CLARIBAR_ROOT 'artifacts\ClariBar\branding-preview.png')
Write-Output 'Brand assets and multi-resolution ICO generated.'
