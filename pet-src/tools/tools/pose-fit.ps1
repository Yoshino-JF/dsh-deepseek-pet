# 姿态素材一键对齐：把抠好的姿态图放进与站立立绘同规格的画布（720×1090），
# 水平居中 + 底边对齐（脚底贴画布底边），这样切姿态时她不会跳、不会忽大忽小。
# 用法：
#   powershell -File tools\pose-fit.ps1 -In <抠好的png> -Name doze [-Check <对比图>]
param(
  [Parameter(Mandatory=$true)][string]$In,
  [Parameter(Mandatory=$true)][string]$Name,
  [string]$PosesDir = "D:\DeepSeek\deepseek-pet\assets\poses",
  [string]$Ref      = "D:\DeepSeek\deepseek-pet\assets\idle.png",
  [string]$Check    = ""
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$ref = New-Object System.Drawing.Bitmap($Ref)
$CW = $ref.Width; $CH = $ref.Height      # 720 x 1090
$src = New-Object System.Drawing.Bitmap($In)

# 1) 只缩不放（素材已经是最终分辨率时保持原样），保证不超出画布
$k = [Math]::Min(1.0, [Math]::Min($CW / $src.Width, $CH / $src.Height))
$dw = [int][Math]::Round($src.Width * $k); $dh = [int][Math]::Round($src.Height * $k)

# 2) 贴到 720x1090：水平居中、底边对齐（脚底贴底边）
$canvas = New-Object System.Drawing.Bitmap([int]$CW, [int]$CH)   # 默认就是 32bppArgb（带 alpha）
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.Clear([System.Drawing.Color]::Transparent)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$dx = [int](($CW - $dw) / 2); $dy = $CH - $dh
$g.DrawImage($src, (New-Object System.Drawing.Rectangle($dx, $dy, $dw, $dh)))
$g.Dispose()

$out = Join-Path $PosesDir ($Name + ".png")
$canvas.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)

# 3) 校验：不透明内容框 + 与站立立绘 50% 叠加（看脚底是否齐、有没有重影）
$minX = $CW; $maxX = -1; $minY = $CH; $maxY = -1
for ($y = 0; $y -lt $CH; $y++) {
  for ($x = 0; $x -lt $CW; $x++) {
    if ($canvas.GetPixel($x, $y).A -gt 8) {
      if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
      if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
    }
  }
}
Write-Output ("{0}: canvas {1}x{2}  content x {3}..{4} (w={5})  y {6}..{7} (h={8})" -f `
  ($Name + ".png"), $CW, $CH, $minX, $maxX, ($maxX - $minX + 1), $minY, $maxY, ($maxY - $minY + 1))

if ($Check -ne "") {
  $k2 = 0.6
  $ow = [int]($CW * $k2); $oh = [int]($CH * $k2)
  $sheet = New-Object System.Drawing.Bitmap(($ow * 3 + 16), $oh)
  $gs = [System.Drawing.Graphics]::FromImage($sheet)
  $gs.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $gs.Clear([System.Drawing.Color]::FromArgb(255, 18, 18, 22))
  $gs.DrawImage($ref,   (New-Object System.Drawing.Rectangle(0, 0, $ow, $oh)))
  $gs.DrawImage($canvas,(New-Object System.Drawing.Rectangle(($ow + 8), 0, $ow, $oh)))
  $gs.DrawImage($ref,   (New-Object System.Drawing.Rectangle(($ow * 2 + 16), 0, $ow, $oh)))
  $gs.DrawImage($canvas,(New-Object System.Drawing.Rectangle(($ow * 2 + 16), 0, $ow, $oh)))
  $font = New-Object System.Drawing.Font("Consolas", 13)
  $gs.DrawString("idle (ref)", $font, [System.Drawing.Brushes]::Yellow, 4, 4)
  $gs.DrawString(($Name + " (fitted)"), $font, [System.Drawing.Brushes]::Yellow, ($ow + 12), 4)
  $gs.DrawString("50% overlay", $font, [System.Drawing.Brushes]::Yellow, ($ow * 2 + 20), 4)
  $gs.Dispose()
  $sheet.Save($Check, [System.Drawing.Imaging.ImageFormat]::Png)
  $sheet.Dispose()
  Write-Output ("check -> " + $Check)
}
$canvas.Dispose(); $src.Dispose(); $ref.Dispose()
