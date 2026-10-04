# Detect key-cap rectangles on the painted keyboard of the flat preview.
# Strategy: caps are low-saturation light pixels on a dark keyboard body.
# Output: JSON of key rects (with row/col grouping) + an annotated PNG.
param(
  [string]$In     = "D:\DeepSeek\deepseek-pet\build\user_gemini_preview.png",
  [string]$Json   = "D:\DeepSeek\deepseek-pet\assets\desk\keyboard_keys.json",
  [string]$Out    = "D:\DeepSeek\deepseek-pet\build\_scratch\keys-detect-r1.png",
  [int]$MinArea   = 180,
  [int]$MaxArea   = 6000
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$bmp = New-Object System.Drawing.Bitmap($In)
$w = $bmp.Width; $h = $bmp.Height

# keyboard search window (from the occupancy map / grid overlay)
$x0 = 380; $x1 = 970; $y0 = 790; $y1 = 1000

# cap test: bright, low saturation
$mask = New-Object 'bool[,]' ($x1-$x0), ($y1-$y0)
for ($y = $y0; $y -lt $y1; $y++) {
  for ($x = $x0; $x -lt $x1; $x++) {
    $c = $bmp.GetPixel($x,$y)
    $max = [Math]::Max($c.R, [Math]::Max($c.G, $c.B))
    $min = [Math]::Min($c.R, [Math]::Min($c.G, $c.B))
    $sat = if ($max -eq 0) { 0 } else { ($max - $min) / $max }
    if ($max -ge 140 -and $sat -le 0.22) { $mask[($x-$x0), ($y-$y0)] = $true }
  }
}

# connected components (4-neighbour flood fill, iterative)
$labels = New-Object 'int[,]' ($x1-$x0), ($y1-$y0)
$rects = New-Object System.Collections.ArrayList
$cur = 0
$stack = New-Object System.Collections.Stack
for ($yy = 0; $yy -lt ($y1-$y0); $yy++) {
  for ($xx = 0; $xx -lt ($x1-$x0); $xx++) {
    if (-not $mask[$xx,$yy] -or $labels[$xx,$yy] -ne 0) { continue }
    $cur++
    $minX = $xx; $maxX = $xx; $minY = $yy; $maxY = $yy; $area = 0
    $stack.Clear(); $stack.Push(@($xx,$yy))
    $labels[$xx,$yy] = $cur
    while ($stack.Count -gt 0) {
      $p = $stack.Pop(); $px = $p[0]; $py = $p[1]
      $area++
      if ($px -lt $minX) { $minX = $px }; if ($px -gt $maxX) { $maxX = $px }
      if ($py -lt $minY) { $minY = $py }; if ($py -gt $maxY) { $maxY = $py }
      foreach ($d in @(@(1,0),@(-1,0),@(0,1),@(0,-1))) {
        $nx = $px + $d[0]; $ny = $py + $d[1]
        if ($nx -lt 0 -or $ny -lt 0 -or $nx -ge ($x1-$x0) -or $ny -ge ($y1-$y0)) { continue }
        if ($mask[$nx,$ny] -and $labels[$nx,$ny] -eq 0) { $labels[$nx,$ny] = $cur; $stack.Push(@($nx,$ny)) }
      }
    }
    if ($area -ge $MinArea -and $area -le $MaxArea) {
      [void]$rects.Add([pscustomobject]@{
        x = $minX + $x0; y = $minY + $y0
        w = ($maxX - $minX + 1); h = ($maxY - $minY + 1)
        area = $area; cx = [int](($minX + $maxX)/2) + $x0; cy = [int](($minY + $maxY)/2) + $y0
      })
    }
  }
}

# group into rows by cy
$sorted = $rects | Sort-Object cy
$rows = New-Object System.Collections.ArrayList
foreach ($r in $sorted) {
  $placed = $false
  foreach ($row in $rows) {
    if ([Math]::Abs($row.cy - $r.cy) -le 12) { [void]$row.items.Add($r); $row.cy = ($row.items | Measure-Object cy -Average).Average; $placed = $true; break }
  }
  if (-not $placed) {
    $row = [pscustomobject]@{ cy = [double]$r.cy; items = (New-Object System.Collections.ArrayList) }
    [void]$row.items.Add($r); [void]$rows.Add($row)
  }
}
$rows = $rows | Sort-Object cy

Write-Output ("caps found: {0} in {1} rows" -f $rects.Count, $rows.Count)
$ri = 0
foreach ($row in $rows) {
  $items = $row.items | Sort-Object x
  $ws = ($items | Measure-Object w -Average).Average
  $hs = ($items | Measure-Object h -Average).Average
  Write-Output ("row {0}: y~{1:N0}  n={2}  avgW={3:N0} avgH={4:N0}  x from {5} to {6}" -f $ri, $row.cy, $items.Count, $ws, $hs, $items[0].x, $items[-1].x)
  $ri++
}

# annotated image  (NB: PowerShell variables are case-insensitive - do NOT reuse $Out/$out)
$canvas = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.DrawImage($bmp, 0, 0, $w, $h)
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(220, 0, 255, 0), 1.5)
foreach ($r in $rects) { $g.DrawRectangle($pen, $r.x, $r.y, $r.w, $r.h) }
$g.Dispose()
$canvas.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose(); $bmp.Dispose()

$payload = [pscustomobject]@{
  source = (Split-Path $In -Leaf)
  searchWindow = @{ x0 = $x0; y0 = $y0; x1 = $x1; y1 = $y1 }
  capCount = $rects.Count
  rows = @($rows | ForEach-Object { [pscustomobject]@{ cy = [int]$_.cy; items = @($_.items | Sort-Object x) } })
}
$jsonText = $payload | ConvertTo-Json -Depth 6
$dir = Split-Path $Json -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
[System.IO.File]::WriteAllText($Json, $jsonText, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "json -> $Json"
Write-Output "annotated -> $Out"
