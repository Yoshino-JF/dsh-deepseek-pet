# split-cast.ps1 —— 把「三只并排立绘」的品红底设定图，自动抠图 + 按间隙切分成三张
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\split-cast.ps1 `
#       -Src <原图.jpg> -OutDir <输出目录> [-Names cloud,book,star] [-MaxWidth 520]
#
# 注意：本文件含中文，**必须保存为带 BOM 的 UTF-8**，否则 Windows PowerShell 5.1 会按 ANSI 解析而报语法错。
#
# 切分策略（两级退路）：
#   1) 对「整列不透明像素数」做 ±12 列滑动平均，得到平滑密度曲线；
#   2) 在画面 12%~88% 区间里找两个**谷底**（间隔至少画面宽的 15%）作为切线
#      —— 生成图有时会在人物之间画一条浅灰分隔线，平滑后这条线的尖峰会被压掉，谷底依然落在缝隙上；
#   3) 找不到（例如人物连在一起）就退化为按 1/3、2/3 固定切，并打印警告。
param(
  [Parameter(Mandatory = $true)][string]$Src,
  [Parameter(Mandatory = $true)][string]$OutDir,
  [string]$Names = "cloud,book,star",
  [int]$MaxWidth = 520
)
$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) -Parent
$tools = Join-Path $root "build\pet-tools.exe"
if (-not (Test-Path $tools)) { throw "找不到 pet-tools.exe，请先运行 build.cmd" }
Add-Type -AssemblyName System.Drawing

$keyed = Join-Path $env:TEMP ("cast-" + [guid]::NewGuid().ToString("N") + ".png")
$bmp = $null
try {
  & $tools key $Src $keyed --t0 60 --t1 150 | Out-Null
  Write-Host "抠图完成"

  $bmp = [System.Drawing.Bitmap]::FromFile($keyed)
  $w = $bmp.Width; $h = $bmp.Height
  $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
  $d = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $stride = $d.Stride
  $buf = New-Object byte[] ([Math]::Abs($stride) * $h)
  [System.Runtime.InteropServices.Marshal]::Copy($d.Scan0, $buf, 0, $buf.Length)
  $bmp.UnlockBits($d)

  # 逐列密度（隔 4 行采样）
  $col = New-Object 'double[]' $w
  for ($x = 0; $x -lt $w; $x++) {
    $c = 0.0
    for ($y = 0; $y -lt $h; $y += 4) { if ($buf[$y * $stride + $x * 4 + 3] -gt 24) { $c = $c + 1.0 } }
    $col[$x] = $c
  }
  # 滑动平均（窗口 ±12 列）：把"分隔线"那种细尖峰压掉，留下真正的缝隙谷底
  $win = 12
  $smooth = New-Object 'double[]' $w
  for ($x = 0; $x -lt $w; $x++) {
    $a = [Math]::Max(0, $x - $win); $b = [Math]::Min($w - 1, $x + $win)
    $s = 0.0
    for ($i = $a; $i -le $b; $i++) { $s = $s + $col[$i] }
    $smooth[$x] = $s / ($b - $a + 1)
  }

  $lo = [int]($w * 0.12); $hi = [int]($w * 0.88); $minSep = [int]($w * 0.15)
  $order = 0..($w - 1) | Where-Object { $_ -ge $lo -and $_ -le $hi } | Sort-Object { $smooth[$_] }
  $cuts = @()
  foreach ($x in $order) {
    $ok = $true
    foreach ($c in $cuts) { if ([Math]::Abs($c - $x) -lt $minSep) { $ok = $false; break } }
    if ($ok) { $cuts += $x }
    if ($cuts.Count -ge 2) { break }
  }
  $cuts = $cuts | Sort-Object
  if ($cuts.Count -lt 2) {
    Write-Warning "没找到两个可靠的谷底，退化为固定三等分切图"
    $cuts = @([int]($w / 3), [int]($w * 2 / 3))
  } else {
    Write-Host ("切线: x=" + $cuts[0] + " (平滑密度 " + [int]$smooth[$cuts[0]] + ") / x=" + $cuts[1] + " (平滑密度 " + [int]$smooth[$cuts[1]] + ")")
  }

  $ranges = @()
  $ranges += , @(0, ($cuts[0] - 1))
  $ranges += , @($cuts[0], ($cuts[1] - 1))
  $ranges += , @($cuts[1], ($w - 1))

  $nameList = $Names.Split(",")
  New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
  for ($i = 0; $i -lt 3; $i++) {
    $x0 = [int]$ranges[$i][0]; $x1 = [int]$ranges[$i][1]; $ww = ($x1 - $x0 + 1)
    $crop = New-Object System.Drawing.Bitmap $ww, $h
    $g = [System.Drawing.Graphics]::FromImage($crop)
    $srcR = New-Object System.Drawing.Rectangle $x0, 0, $ww, $h
    $dstR = New-Object System.Drawing.Rectangle 0, 0, $ww, $h
    $g.DrawImage($bmp, $dstR, $srcR, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $tmp = Join-Path $env:TEMP ("cast-part-$i.png")
    $crop.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png); $crop.Dispose()
    & $tools trim $tmp $tmp --pad 6 | Out-Null
    $name = if ($i -lt $nameList.Count) { $nameList[$i] } else { "cast$i" }
    & $tools scale $tmp (Join-Path $OutDir "$name.png") $MaxWidth | Write-Host
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
  }
  Write-Host "完成 -> $OutDir"
}
finally {
  # 失败也要清掉中间文件（上一版曾在 %TEMP% 留下 5 MB 的 cast-*.png）
  if ($bmp) { $bmp.Dispose() }
  Remove-Item $keyed -Force -ErrorAction SilentlyContinue
}
