# Build "pressed" differential frames from the flat preview.
#   - each output is a FULL-FRAME (1024x1024) transparent PNG whose only opaque
#     area is the animated patch, so the pet can simply draw it over the base.
#   - press look = patch content shifted DOWN by dy, the strip that opens at the
#     top filled from the rows just above the patch (seam-free), then darkened.
#   - key patches additionally get a darker "socket" strip at the top.
param(
  [string]$In    = "D:\DeepSeek\deepseek-pet\build\user_gemini_preview.png",
  [string]$OutDir= "D:\DeepSeek\deepseek-pet\assets\desk",
  [string]$Check = "D:\DeepSeek\deepseek-pet\build\_scratch\press-check-r1.png"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$src = New-Object System.Drawing.Bitmap($In)

# CRITICAL: the patch frames must have EXACTLY the same pixel size as the base
# sprite (sit_watch.png = 1024x1020), otherwise WPF's Uniform stretch maps them
# with a slightly different scale and every patch lands 1-2 display px off.
$refSprite = Join-Path $OutDir "sit_watch.png"
$ref = New-Object System.Drawing.Bitmap($refSprite)
$W = $ref.Width; $H = $ref.Height
$ref.Dispose()
Write-Output ("frame size = {0}x{1} (matches sit_watch.png)" -f $W, $H)

# --- patch definitions -------------------------------------------------------
# key rects come from tools\keys-detect.ps1 (cap TOP faces); we add margin so the
# whole cap (top face + a hint of its front face) moves together.
function KeyPatch([string]$name, [int]$cx, [int]$cy, [int]$w, [int]$h, [int]$dy) {
  return [pscustomobject]@{
    name = $name; kind = 'key'
    x = $cx; y = $cy; w = $w; h = $h; dy = $dy
  }
}
$patches = @(
  # front-most visible letter row (cy 884) - clear of her hand
  (KeyPatch 'd'     618 878 23 13 5)
  (KeyPatch 'a'     582 878 22 13 5)
  (KeyPatch 'w'     547 878 23 13 5)
  # space bar (top row, her side) - wide bar detected at x582..693 y826..848
  (KeyPatch 'space' 582 826 111 22 5)
  # mouse: her fingers rest on the rear, so the patch starts below them
  ([pscustomobject]@{ name = 'mouse'; kind = 'mouse'; x = 158; y = 846; w = 132; h = 74; dy = 6 })
)

function Save-Patch($patch) {
  $bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::Transparent)

  # NB: PowerShell variables are case-insensitive - never reuse $w/$h/$H/$W here.
  $px = $patch.x; $py = $patch.y
  $pw = $patch.w; $ph = $patch.h
  $dy = $patch.dy
  $tx = [Math]::Max(0, $px)
  $ty = [Math]::Max(0, $py)
  $tw = [Math]::Min($W - $tx, $pw)
  $th = [Math]::Min($H - $ty, $ph + $dy)             # grow downward so the shifted content fits

  # socket colour for keys: sample just left of the patch, mid height, darkened
  $sy0 = [Math]::Max(0, [Math]::Min($src.Height - 1, $py + [int]($ph / 2)))
  $sx0 = [Math]::Max(0, [Math]::Min($src.Width  - 1, $px - 6))
  $sample = $src.GetPixel($sx0, $sy0)
  $socket = [System.Drawing.Color]::FromArgb(255,
              [int]($sample.R * 0.45), [int]($sample.G * 0.45), [int]($sample.B * 0.45))

  for ($ix = 0; $ix -lt $tw; $ix++) {
    for ($iy = 0; $iy -lt $th; $iy++) {
      $sx = $tx + $ix
      $sy = $ty + $iy - $dy
      if ($sy -lt 0) { continue }
      if ($sy -lt $ty) {
        $c = if ($patch.kind -eq 'key') { $socket } else { $src.GetPixel($sx, [Math]::Max(0, $ty - 1)) }
      } else {
        $c = $src.GetPixel($sx, $sy)
        if ($patch.kind -eq 'key') {
          $c = [System.Drawing.Color]::FromArgb(255, [int]($c.R * 0.74), [int]($c.G * 0.74), [int]($c.B * 0.74))
        } else {
          $c = [System.Drawing.Color]::FromArgb(255, [int]($c.R * 0.90), [int]($c.G * 0.90), [int]($c.B * 0.94))
        }
      }
      $bmp.SetPixel($sx, $ty + $iy, $c)
    }
  }
  $g.Dispose()
  $path = Join-Path $OutDir ("press_" + $patch.name + ".png")
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  $kb = [Math]::Round((Get-Item $path).Length / 1KB)
  Write-Output ("{0,-22} patch {1},{2} {3}x{4} dy={5} -> {6} KB" -f $patch.name, $px, $py, $pw, $ph, $dy, $kb)
}

foreach ($p in $patches) { Save-Patch $p }

# --- verification composite: base + every patch at once ----------------------
$comp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($comp)
$g.DrawImage($src, 0, 0, $W, $H)
foreach ($p in $patches) {
  $layer = [System.Drawing.Image]::FromFile((Join-Path $OutDir ("press_" + $p.name + ".png")))
  $g.DrawImage($layer, 0, 0, $W, $H)
  $layer.Dispose()
}
$g.Dispose()
$comp.Save($Check, [System.Drawing.Imaging.ImageFormat]::Png)
$comp.Dispose(); $src.Dispose()
Write-Output "composite -> $Check"
