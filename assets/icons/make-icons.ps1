# Draws the Desktop Icon Grid icon set with WPF (no other tool needed) and writes PNG, ICO and a preview sheet.
#   pwsh -File make-icons.ps1            -> everything into this folder
# Design: four tiles (blue, amber, green, white) on a dark green rounded plate. Small sizes sit on the pixel grid.
# Variant "sendto": the fourth tile is a plus (add to the bar).
param([string]$Out = $PSScriptRoot)
Add-Type -AssemblyName PresentationCore, WindowsBase
$ErrorActionPreference = 'Stop'

function Brush([string]$hex, [double]$opacity = 1) {
  $b = [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($hex)); $b.Opacity = $opacity; $b.Freeze(); $b
}
function Grad([string]$top, [string]$bottom) {
  $b = [Windows.Media.LinearGradientBrush]::new(
    [Windows.Media.ColorConverter]::ConvertFromString($top), [Windows.Media.ColorConverter]::ConvertFromString($bottom), 90); $b.Freeze(); $b
}

# The layout for a side of $s pixels: margin, gap, tile, radii. Sizes up to 32 are set by hand.
function Layout([int]$s) {
  switch ($s) {
    16 { return @{ m = 2; g = 2; t = 5;  r = 1;   pr = 3 } }
    20 { return @{ m = 3; g = 2; t = 6;  r = 1;   pr = 4 } }
    24 { return @{ m = 3; g = 2; t = 8;  r = 1.5; pr = 5 } }
    32 { return @{ m = 5; g = 2; t = 10; r = 2;   pr = 6 } }
  }
  $m = [math]::Floor($s * 5 / 32 + 0.5); $g = [math]::Floor($s * 2 / 32 + 0.5)
  if ((($s - 2 * $m - $g) % 2) -ne 0) { $g++ }
  @{ m = $m; g = $g; t = ($s - 2 * $m - $g) / 2; r = $s * 2.2 / 32; pr = $s * 6 / 32 }
}

# Draws one icon of side $s at ($x, $y) into the drawing context.
function Draw-Icon($dc, [double]$x, [double]$y, [int]$s, [string]$variant = 'app') {
  $l = Layout $s; $rich = $s -ge 40
  $plate = [Windows.Rect]::new($x, $y, $s, $s)
  $plateBrush = if ($rich) { Grad '#FF3D4D42' '#FF222C26' } else { Brush '#FF2B372F' }
  $dc.DrawRoundedRectangle($plateBrush, $null, $plate, $l.pr, $l.pr)
  if ($rich) { # a thin light edge at the top of the plate
    $w = [math]::Max(1, $s / 128)
    $pen = [Windows.Media.Pen]::new((Brush '#FFFFFFFF' 0.10), $w); $pen.Freeze()
    $in = [Windows.Rect]::new($x + $w / 2, $y + $w / 2, $s - $w, $s - $w)
    $dc.DrawRoundedRectangle($null, $pen, $in, $l.pr - $w / 2, $l.pr - $w / 2)
  }
  $a = $l.m; $b = $l.m + $l.t + $l.g
  $tiles = @(
    @{ x = $a; y = $a; top = '#FF62A0FF'; bottom = '#FF3B79E8'; flat = '#FF4C8BF5' },   # blue
    @{ x = $b; y = $a; top = '#FFFFBC42'; bottom = '#FFEE9A12'; flat = '#FFF5A623' },   # amber
    @{ x = $a; y = $b; top = '#FF93E240'; bottom = '#FF68BF1A'; flat = '#FF7ED321' },   # green
    @{ x = $b; y = $b; top = '#FFFFFFFF'; bottom = '#FFD9DDE1'; flat = '#FFECEEF0' })   # white
  for ($i = 0; $i -lt 4; $i++) {
    $t = $tiles[$i]; $r = [Windows.Rect]::new($x + $t.x, $y + $t.y, $l.t, $l.t)
    if ($i -eq 3 -and $variant -eq 'sendto') {
      # a plus in the fourth place: arms as thick as about a quarter of the tile, same parity as the tile
      $k = [math]::Max(1, [math]::Floor($l.t * 0.26 + 0.5)); if ((($l.t - $k) % 2) -ne 0) { $k++ }
      $o = ($l.t - $k) / 2; $white = if ($rich) { Grad '#FFFFFFFF' '#FFE3E6E9' } else { Brush '#FFF4F5F6' }
      $kr = if ($rich) { $k / 4 } else { 0 }
      $dc.DrawRoundedRectangle($white, $null, [Windows.Rect]::new($r.X + $o, $r.Y, $k, $l.t), $kr, $kr)
      $dc.DrawRoundedRectangle($white, $null, [Windows.Rect]::new($r.X, $r.Y + $o, $l.t, $k), $kr, $kr)
      continue
    }
    if ($rich) {
      $d = [math]::Max(1, $s / 64)
      $dc.DrawRoundedRectangle((Brush '#FF000000' 0.28), $null, [Windows.Rect]::new($r.X, $r.Y + $d, $r.Width, $r.Height), $l.r, $l.r)
      $dc.DrawRoundedRectangle((Grad $t.top $t.bottom), $null, $r, $l.r, $l.r)
    } else {
      $dc.DrawRoundedRectangle((Brush $t.flat), $null, $r, $l.r, $l.r)
    }
  }
}

function Render([int]$w, [int]$h, [scriptblock]$paint) {
  $v = [Windows.Media.DrawingVisual]::new(); $dc = $v.RenderOpen(); & $paint $dc; $dc.Close()
  $bmp = [Windows.Media.Imaging.RenderTargetBitmap]::new($w, $h, 96, 96, [Windows.Media.PixelFormats]::Pbgra32); $bmp.Render($v); $bmp.Freeze(); $bmp
}
function PngBytes($bmp) {
  $e = [Windows.Media.Imaging.PngBitmapEncoder]::new(); $e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
  $ms = [IO.MemoryStream]::new(); $e.Save($ms); , $ms.ToArray()
}
function Save-Png($bmp, [string]$path) {
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null; [IO.File]::WriteAllBytes($path, (PngBytes $bmp))
}
function IconPng([int]$s, [string]$variant) { , (PngBytes (Render $s $s { param($dc) Draw-Icon $dc 0 0 $s $variant }.GetNewClosure())) }

# An .ico with PNG frames (Windows Vista and later read them at every size).
function Save-Ico([string]$path, [int[]]$sizes, [string]$variant) {
  $frames = foreach ($s in $sizes) { , (IconPng $s $variant) }
  $ms = [IO.MemoryStream]::new(); $bw = [IO.BinaryWriter]::new($ms)
  $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$frames[$i].Length); $bw.Write([uint32]$offset); $offset += $frames[$i].Length
  }
  foreach ($f in $frames) { $bw.Write($f) }
  [IO.File]::WriteAllBytes($path, $ms.ToArray())
}

# A tile for the Store package: the icon centred on a transparent or coloured ground.
function Save-Tile([string]$path, [int]$w, [int]$h, [double]$share, [string]$ground = $null) {
  $s = [int]([math]::Floor([math]::Min($w, $h) * $share / 2) * 2)
  $bmp = Render $w $h {
    param($dc)
    if ($ground) { $dc.DrawRectangle((Grad '#FF2E3A32' '#FF1B231E'), $null, [Windows.Rect]::new(0, 0, $w, $h)) }
    Draw-Icon $dc ([math]::Floor(($w - $s) / 2)) ([math]::Floor(($h - $s) / 2)) $s 'app'
  }.GetNewClosure()
  Save-Png $bmp $path
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
Save-Ico (Join-Path $Out 'app.ico') $sizes 'app'
Save-Ico (Join-Path $Out 'sendto.ico') $sizes 'sendto'
Save-Ico (Join-Path $Out 'tray.ico') @(16, 20, 24, 32, 40, 48) 'app'
foreach ($s in $sizes) {
  [IO.Directory]::CreateDirectory((Join-Path $Out 'png')) | Out-Null
  [IO.File]::WriteAllBytes((Join-Path $Out "png\app-$s.png"), (IconPng $s 'app'))
  [IO.File]::WriteAllBytes((Join-Path $Out "png\sendto-$s.png"), (IconPng $s 'sendto'))
}

# Store package assets (MSIX names), scales 100 / 200 / 400, and the unplated target sizes for the taskbar.
$store = Join-Path $Out 'store'
foreach ($scale in 100, 200, 400) {
  $k = $scale / 100
  Save-Tile (Join-Path $store "Square44x44Logo.scale-$scale.png")   (44 * $k)  (44 * $k)  1.0
  Save-Tile (Join-Path $store "Square71x71Logo.scale-$scale.png")   (71 * $k)  (71 * $k)  0.62
  Save-Tile (Join-Path $store "Square150x150Logo.scale-$scale.png") (150 * $k) (150 * $k) 0.56
  Save-Tile (Join-Path $store "Wide310x150Logo.scale-$scale.png")   (310 * $k) (150 * $k) 0.56
  Save-Tile (Join-Path $store "Square310x310Logo.scale-$scale.png") (310 * $k) (310 * $k) 0.50
  Save-Tile (Join-Path $store "StoreLogo.scale-$scale.png")         (50 * $k)  (50 * $k)  0.88
  Save-Tile (Join-Path $store "SplashScreen.scale-$scale.png")      (620 * $k) (300 * $k) 0.40
}
foreach ($s in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256) {
  $n = if ($s -in 16, 20, 24, 32) { $s } else { $s } # every size is drawn for itself, not scaled
  Save-Tile (Join-Path $store "Square44x44Logo.targetsize-$s.png") $s $s 1.0
  Copy-Item (Join-Path $store "Square44x44Logo.targetsize-$s.png") (Join-Path $store "Square44x44Logo.targetsize-${s}_altform-unplated.png") -Force
  Copy-Item (Join-Path $store "Square44x44Logo.targetsize-$s.png") (Join-Path $store "Square44x44Logo.targetsize-${s}_altform-lightunplated.png") -Force
}
# Store listing pictures (Partner Center): 1:1 tile 300, 1:1 box art 1080, 2:3 poster 720x1080
Save-Tile (Join-Path $store 'listing\AppTile-300x300.png') 300 300 0.60 'ground'
Save-Tile (Join-Path $store 'listing\BoxArt-1080x1080.png') 1080 1080 0.46 'ground'
Save-Tile (Join-Path $store 'listing\Poster-720x1080.png') 720 1080 0.50 'ground'

# Preview sheet: every size on a light and a dark ground, the two variants, and the tray at real size.
$face = [Windows.Media.Typeface]::new('Segoe UI')
function Text($dc, [string]$t, [double]$x, [double]$y, [string]$colour, [double]$size = 12) {
  $ft = [Windows.Media.FormattedText]::new($t, [Globalization.CultureInfo]::InvariantCulture, 'LeftToRight', $face, $size, (Brush $colour), 1.0)
  $dc.DrawText($ft, [Windows.Point]::new($x, $y))
}
$sheet = Render 1180 760 {
  param($dc)
  $dc.DrawRectangle((Brush '#FFF3F3F3'), $null, [Windows.Rect]::new(0, 0, 1180, 380))
  $dc.DrawRectangle((Brush '#FF202020'), $null, [Windows.Rect]::new(0, 380, 1180, 380))
  foreach ($row in @(@{ y = 0; ink = '#FF202020' }, @{ y = 380; ink = '#FFF0F0F0' })) {
    Text $dc 'Program icon (app.ico)' 24 ($row.y + 14) $row.ink 14
    Draw-Icon $dc 24 ($row.y + 44) 256 'app'
    $x = 304
    foreach ($s in 64, 48, 40, 32, 24, 20, 16) { Draw-Icon $dc $x ($row.y + 44) $s 'app'; Text $dc "$s" $x ($row.y + 44 + $s + 4) $row.ink 11; $x += $s + 18 }
    Text $dc 'Send to (sendto.ico)' 304 ($row.y + 150) $row.ink 14
    $x = 304
    foreach ($s in 64, 48, 40, 32, 24, 20, 16) { Draw-Icon $dc $x ($row.y + 178) $s 'sendto'; Text $dc "$s" $x ($row.y + 178 + $s + 4) $row.ink 11; $x += $s + 18 }
    Text $dc 'Send to, 256' 680 ($row.y + 14) $row.ink 14
    Draw-Icon $dc 680 ($row.y + 44) 256 'sendto'
    # tray: a strip like the taskbar corner, icons at 16 and at 24 (150 %)
    Text $dc 'Tray, 16 px and 24 px (150 %)' 960 ($row.y + 14) $row.ink 14
    $bar = if ($row.y -eq 0) { '#FFE6ECF2' } else { '#FF1C1F23' }
    $dc.DrawRectangle((Brush $bar), $null, [Windows.Rect]::new(960, $row.y + 44, 196, 48))
    Draw-Icon $dc 976 ($row.y + 60) 16 'app'; Draw-Icon $dc 1012 ($row.y + 56) 24 'app'
    Text $dc '16 px, enlarged 8x' 960 ($row.y + 110) $row.ink 11
    $small = Render 16 16 { param($d) Draw-Icon $d 0 0 16 'app' }
    $g =[Windows.Media.DrawingGroup]::new(); [Windows.Media.RenderOptions]::SetBitmapScalingMode($g, 'NearestNeighbor')
    $gc = $g.Open(); $gc.DrawImage($small, [Windows.Rect]::new(960, $row.y + 130, 128, 128)); $gc.Close(); $dc.DrawDrawing($g)
    $small2 = Render 16 16 { param($d) Draw-Icon $d 0 0 16 'sendto' }
    $g2 = [Windows.Media.DrawingGroup]::new(); [Windows.Media.RenderOptions]::SetBitmapScalingMode($g2, 'NearestNeighbor')
    $gc2 = $g2.Open(); $gc2.DrawImage($small2, [Windows.Rect]::new(960, $row.y + 268, 96, 96)); $gc2.Close(); $dc.DrawDrawing($g2)
  }
}
Save-Png $sheet (Join-Path $Out 'preview.png')

# GitHub readme: the logo alone (transparent, reads on GitHub's light and dark pages) and a header with the name.
$readme = Join-Path $Out 'readme'
Save-Tile (Join-Path $readme 'logo-128.png') 128 128 1.0
Save-Tile (Join-Path $readme 'logo-256.png') 256 256 1.0
$semibold = [Windows.Media.Typeface]::new([Windows.Media.FontFamily]::new('Segoe UI'), 'Normal', 'SemiBold', 'Normal')
$header = Render 1280 320 {
  param($dc)
  $dc.DrawRoundedRectangle((Grad '#FF2E3A32' '#FF1B231E'), $null, [Windows.Rect]::new(0, 0, 1280, 320), 24, 24)
  Draw-Icon $dc 96 64 192 'app'
  $name = [Windows.Media.FormattedText]::new('Desktop Icon Grid', [Globalization.CultureInfo]::InvariantCulture, 'LeftToRight', $semibold, 84, (Brush '#FFF4F5F6'), 1.0)
  $dc.DrawText($name, [Windows.Point]::new(340, 78))
  $line = [Windows.Media.FormattedText]::new('A launcher bar for Windows: shortcuts, groups, live tiles, sync', [Globalization.CultureInfo]::InvariantCulture, 'LeftToRight', $face, 30, (Brush '#FFB9C4BC'), 1.0)
  $dc.DrawText($line, [Windows.Point]::new(344, 196))
}
Save-Png $header (Join-Path $readme 'header-1280x320.png')
# the picture GitHub shows when the repository is shared (Settings > Social preview): 1280x640
$social = Render 1280 640 {
  param($dc)
  $dc.DrawRectangle((Grad '#FF2E3A32' '#FF1B231E'), $null, [Windows.Rect]::new(0, 0, 1280, 640))
  Draw-Icon $dc 512 96 256 'app'
  $name = [Windows.Media.FormattedText]::new('Desktop Icon Grid', [Globalization.CultureInfo]::InvariantCulture, 'LeftToRight', $semibold, 76, (Brush '#FFF4F5F6'), 1.0)
  $dc.DrawText($name, [Windows.Point]::new((1280 - $name.Width) / 2, 400))
  $line = [Windows.Media.FormattedText]::new('A launcher bar for Windows', [Globalization.CultureInfo]::InvariantCulture, 'LeftToRight', $face, 32, (Brush '#FFB9C4BC'), 1.0)
  $dc.DrawText($line, [Windows.Point]::new((1280 - $line.Width) / 2, 508))
}
Save-Png $social (Join-Path $readme 'social-1280x640.png')
'done: ' + (Get-ChildItem $Out -Recurse -File | Measure-Object).Count + ' files'
