# Measure the layout of the flat preview so we can slice it into animatable layers.
# Outputs: a coarse occupancy map (magenta = background), plus a grid-annotated PNG.
param(
  [string]$In  = "D:\DeepSeek\deepseek-pet\build\user_gemini_preview.png",
  [string]$Out = "D:\DeepSeek\deepseek-pet\build\_scratch\preview-grid-r1.png",
  [int]$Cell = 32
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$src = New-Object System.Drawing.Bitmap($In)
$w = $src.Width; $h = $src.Height
Write-Output ("source {0}x{1}" -f $w, $h)

# magenta background test (the preview uses a saturated pink; tolerate grading)
function IsBg([System.Drawing.Color]$c) {
  return ($c.R -gt 170 -and $c.B -gt 170 -and $c.G -lt 120 -and ([Math]::Abs($c.R - $c.B) -lt 90))
}

# per-cell stats: bg share, mean color of non-bg pixels, count
$rows = [Math]::Ceiling($h / $Cell)
$cols = [Math]::Ceiling($w / $Cell)
Write-Output ""
Write-Output "cell map ($Cell px per cell): '.'=mostly bg  '#'=mostly content  '-'=mixed   (col index across the top)"
$header = "     " + (0..($cols-1) | ForEach-Object { if ($_ % 10 -eq 0) { [string]([int]($_/10)) } else { " " } }) -join ""
Write-Output $header
for ($cy = 0; $cy -lt $rows; $cy++) {
  $line = ""
  for ($cx = 0; $cx -lt $cols; $cx++) {
    $bgc = 0; $tot = 0
    for ($y = $cy*$Cell; $y -lt [Math]::Min($h, ($cy+1)*$Cell); $y += 4) {
      for ($x = $cx*$Cell; $x -lt [Math]::Min($w, ($cx+1)*$Cell); $x += 4) {
        $tot++
        if (IsBg $src.GetPixel($x,$y)) { $bgc++ }
      }
    }
    $f = $bgc / [Math]::Max(1,$tot)
    if ($f -gt 0.92) { $line += "." } elseif ($f -lt 0.25) { $line += "#" } else { $line += "-" }
  }
  Write-Output ("y{0,4} {1}" -f ($cy*$Cell), $line)
}

# annotate a copy with a grid + row/col labels for eyeballing
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.DrawImage($src, 0, 0, $w, $h)
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(120, 0, 255, 0), 1)
for ($x = 0; $x -lt $w; $x += $Cell) { $g.DrawLine($pen, $x, 0, $x, $h) }
for ($y = 0; $y -lt $h; $y += $Cell) { $g.DrawLine($pen, 0, $y, $w, $y) }
$penRed = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200, 255, 0, 0), 1)
for ($y = 0; $y -lt $h; $y += 128) { $g.DrawLine($penRed, 0, $y, $w, $y) }
$font = New-Object System.Drawing.Font("Consolas", 11)
$brush = [System.Drawing.Brushes]::Yellow
for ($y = 0; $y -lt $h; $y += 128) { $g.DrawString("y=$y", $font, $brush, 2, $y + 1) }
for ($x = 0; $x -lt $w; $x += 128) { $g.DrawString("x=$x", $font, $brush, $x + 1, 2) }
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose(); $src.Dispose()
Write-Output ""
Write-Output "annotated -> $Out"
