# Build a FACE DIFFERENTIAL patch frame from a model-generated expression variant.
#   base    : the flat preview (assets source of truth)
#   variant : same framing, only the expression changed (JPEG from the image model)
#   output  : full-frame RGBA PNG the size of sit_watch.png, opaque ONLY where the
#             expression actually changed (mask = significant pixel delta, dilated),
#             so the pet can just draw it over the base -> no seams anywhere else.
param(
  [string]$Base    = "D:\DeepSeek\deepseek-pet\build\user_gemini_preview.png",
  [Parameter(Mandatory=$true)][string]$Variant,
  [Parameter(Mandatory=$true)][string]$Name,          # e.g. blink / happy / worry
  [string]$OutDir  = "D:\DeepSeek\deepseek-pet\assets\desk",
  [int]$Threshold  = 200,     # sum of |dR|+|dG|+|dB| that counts as a real change (beats JPEG noise)
  [int]$MinBlob    = 60,      # drop connected components smaller than this (JPEG ringing specks)
  [int]$Dilate     = 3,       # grow the mask so the seam sits inside neutral pixels
  [string]$Check   = ""
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$ref = New-Object System.Drawing.Bitmap((Join-Path $OutDir "sit_watch.png"))
$W = $ref.Width; $H = $ref.Height; $ref.Dispose()

$bmpB = New-Object System.Drawing.Bitmap($Base)
$bmpV = New-Object System.Drawing.Bitmap($Variant)
Write-Output ("base {0}x{1}   variant {2}x{3}" -f $bmpB.Width, $bmpB.Height, $bmpV.Width, $bmpV.Height)

# 1) raw change mask over the shared frame
$mask = New-Object 'bool[,]' $W, $H
$changed = 0
for ($y = 0; $y -lt $H; $y++) {
  for ($x = 0; $x -lt $W; $x++) {
    $cb = $bmpB.GetPixel($x, $y); $cv = $bmpV.GetPixel($x, $y)
    $d = [Math]::Abs($cb.R - $cv.R) + [Math]::Abs($cb.G - $cv.G) + [Math]::Abs($cb.B - $cv.B)
    if ($d -gt $Threshold) { $mask[$x, $y] = $true; $changed++ }
  }
}
Write-Output ("changed pixels over threshold {0}: {1}" -f $Threshold, $changed)

# 1b) drop specks: keep only connected components >= MinBlob px (JPEG ringing on
#     high-contrast edges otherwise leaves thousands of 1-3 px islands all over
#     the figure, which would speckle the composite).
if ($MinBlob -gt 1) {
  $seen = New-Object 'bool[,]' $W, $H
  $keep = New-Object 'bool[,]' $W, $H
  $stack = New-Object System.Collections.Stack
  $dropped = 0; $keptBlobs = 0
  for ($y = 0; $y -lt $H; $y++) {
    for ($x = 0; $x -lt $W; $x++) {
      if (-not $mask[$x, $y] -or $seen[$x, $y]) { continue }
      $cells = New-Object System.Collections.ArrayList
      $stack.Clear(); $stack.Push(@($x, $y)); $seen[$x, $y] = $true
      while ($stack.Count -gt 0) {
        $p = $stack.Pop(); $px = $p[0]; $py = $p[1]
        [void]$cells.Add($p)
        foreach ($d in @(@(1,0),@(-1,0),@(0,1),@(0,-1))) {
          $nx = $px + $d[0]; $ny = $py + $d[1]
          if ($nx -lt 0 -or $ny -lt 0 -or $nx -ge $W -or $ny -ge $H) { continue }
          if ($mask[$nx, $ny] -and -not $seen[$nx, $ny]) { $seen[$nx, $ny] = $true; $stack.Push(@($nx, $ny)) }
        }
      }
      if ($cells.Count -ge $MinBlob) {
        $keptBlobs++
        foreach ($c in $cells) { $keep[$c[0], $c[1]] = $true }
      } else {
        $dropped += $cells.Count
      }
    }
  }
  $mask = $keep
  Write-Output ("blob filter (>= {0} px): kept {1} blobs, dropped {2} speckle px" -f $MinBlob, $keptBlobs, $dropped)
}

# 2) dilate (square kernel, Dilate passes)
for ($pass = 0; $pass -lt $Dilate; $pass++) {
  $next = New-Object 'bool[,]' $W, $H
  for ($y = 0; $y -lt $H; $y++) {
    for ($x = 0; $x -lt $W; $x++) {
      if (-not $mask[$x, $y]) { continue }
      for ($dy = -1; $dy -le 1; $dy++) {
        for ($dx = -1; $dx -le 1; $dx++) {
          $nx = $x + $dx; $ny = $y + $dy
          if ($nx -ge 0 -and $ny -ge 0 -and $nx -lt $W -and $ny -lt $H) { $next[$nx, $ny] = $true }
        }
      }
    }
  }
  $mask = $next
}

# 3) mask bbox + write the patch
$minX = 99999; $maxX = -1; $minY = 99999; $maxY = -1; $kept = 0
$out = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($out)
$g.Clear([System.Drawing.Color]::Transparent)
for ($y = 0; $y -lt $H; $y++) {
  for ($x = 0; $x -lt $W; $x++) {
    if (-not $mask[$x, $y]) { continue }
    $kept++
    if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
    if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
    $cv = $bmpV.GetPixel($x, $y)
    $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $cv.R, $cv.G, $cv.B))
  }
}
$g.Dispose()
$path = Join-Path $OutDir ("face_" + $Name + ".png")
$out.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$out.Dispose()
Write-Output ("face_{0}.png  mask px={1}  bbox x {2}..{3} (w={4})  y {5}..{6} (h={7})  {8} KB" -f `
  $Name, $kept, $minX, $maxX, ($maxX - $minX + 1), $minY, $maxY, ($maxY - $minY + 1), [Math]::Round((Get-Item $path).Length / 1KB))

if ($Check -ne "") {
  # composite: base + patch, cropped around the face, next to the plain base
  $pad = 40
  $x0 = [Math]::Max(0, $minX - $pad); $y0 = [Math]::Max(0, $minY - $pad)
  $x1 = [Math]::Min($W, $maxX + $pad); $y1 = [Math]::Min($H, $maxY + $pad)
  $cw = $x1 - $x0; $ch = $y1 - $y0
  $comp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $gc = [System.Drawing.Graphics]::FromImage($comp)
  $gc.DrawImage($bmpB, 0, 0, $W, $H)
  $patch = [System.Drawing.Image]::FromFile($path)
  $gc.DrawImage($patch, 0, 0, $W, $H)
  $patch.Dispose(); $gc.Dispose()
  $k = 2
  $sheet = New-Object System.Drawing.Bitmap(($cw * $k * 2 + 12), ($ch * $k))
  $gs = [System.Drawing.Graphics]::FromImage($sheet)
  $gs.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
  $gs.Clear([System.Drawing.Color]::FromArgb(255, 15, 15, 15))
  $gs.DrawImage($bmpB, (New-Object System.Drawing.Rectangle(0, 0, ($cw * $k), ($ch * $k))), (New-Object System.Drawing.Rectangle($x0, $y0, $cw, $ch)), [System.Drawing.GraphicsUnit]::Pixel)
  $gs.DrawImage($comp, (New-Object System.Drawing.Rectangle(($cw * $k + 12), 0, ($cw * $k), ($ch * $k))), (New-Object System.Drawing.Rectangle($x0, $y0, $cw, $ch)), [System.Drawing.GraphicsUnit]::Pixel)
  $gs.Dispose()
  $sheet.Save($Check, [System.Drawing.Imaging.ImageFormat]::Png)
  $sheet.Dispose(); $comp.Dispose()
  Write-Output ("check (left=base right=with patch) -> " + $Check)
}

$bmpB.Dispose(); $bmpV.Dispose()
