# 桌面精修阶段（v0.1.x）的清退：把不再需要的中间素材/调试残留移入 .trash（可逆，不真删）
# 约定（用户订立）：任何删除先入 .trash 或打包快照 → 列清单 → 用户二次确认后才真删。
# 本脚本只做第一步（移动 + 记账），不做真删。
$ErrorActionPreference = 'Stop'
$root  = 'D:\DeepSeek\deepseek-pet'
$stamp = 'pet-desk-phase-20261003'
$trash = Join-Path 'D:\DeepSeek\.trash' $stamp
New-Item -ItemType Directory -Path $trash -Force | Out-Null

$moved = New-Object System.Collections.ArrayList

function Move-Into($relPath, $why) {
  $src = Join-Path $root $relPath
  if (-not (Test-Path $src)) { return }
  $item = Get-Item $src
  $isDir = $item.PSIsContainer
  $size = if ($isDir) { (Get-ChildItem $src -Recurse -File | Measure-Object -Sum Length).Sum } else { $item.Length }
  $dst = Join-Path $trash $relPath
  $dstDir = Split-Path $dst -Parent
  if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
  Move-Item -LiteralPath $src -Destination $dst -Force
  [void]$moved.Add([pscustomobject]@{ path = $relPath; kind = $(if ($isDir) { 'dir' } else { 'file' }); mb = [Math]::Round($size/1MB, 2); why = $why })
  Write-Output ("  moved  {0,-46} {1,8:N2} MB  ({2})" -f $relPath, ($size/1MB), $why)
}

Write-Output "== A. 调试残留（截图/中间产物/探针/备份）=="
Move-Into 'build\_scratch'        '调试截图与中间产物（每轮验收图的草稿）'
Move-Into 'src\_probe'            '一次性探针程序（分层窗口/透明度排查用完）'
Move-Into 'src\McScreen.cs.bak'   '源码备份（当前 McScreen.cs 是活的）'

Write-Output "== B. 桌面场景里代码不再引用的素材（房间背景/旧键盘/中间版立绘）=="
foreach ($f in @(
  'assets\desk\desk_bg.jpg',            # 夜晚书房背景（房间场景已废弃）
  'assets\desk\desk_top.jpg',           # 房间背景拆分版
  'assets\desk\desk_fg.jpg',            # 房间前景桌面
  'assets\desk\desk_props.png',         # 房间道具（键鼠）
  'assets\desk\desk_keyboard.png',      # 旧键盘素材
  'assets\desk\sit_watch_tall.png',     # 立绘中间版（白桌面重染）
  'assets\desk\sit_watch_scene.png',    # 场景合成中间版
  'assets\desk\sit_look_scene.png',     # 场景合成中间版
  'assets\desk\bongo_desk_idle.png',    # Bongo Cat 工位试验版
  'assets\desk\bongo_desk_600.png'      # 同上（缩图）
)) { Move-Into $f '桌面精修阶段素材，新方向（换姿态）不再需要' }

Write-Output "== C. build 下已被取代的桌面/探头迭代证据图 =="
$kill = @()
$kill += Get-ChildItem (Join-Path $root 'build') -File -Filter 'desk-preview-*.png' -ErrorAction SilentlyContinue
$kill += Get-ChildItem (Join-Path $root 'build') -File -Filter 'peek-bongocat-*.png' -ErrorAction SilentlyContinue
$kill += Get-ChildItem (Join-Path $root 'build') -File -Filter 'peek-allinone-*.png' -ErrorAction SilentlyContinue
$kill += Get-ChildItem (Join-Path $root 'build') -File -Filter 'peek-mode-*.png' -ErrorAction SilentlyContinue
$kill += Get-ChildItem (Join-Path $root 'build') -File -Filter 'peek-desk-*.png' -ErrorAction SilentlyContinue
$kill += Get-ChildItem (Join-Path $root 'build') -File -Filter 'peek-layout-*.png' -ErrorAction SilentlyContinue
$kill += Get-ChildItem (Join-Path $root 'build') -File -Filter 'peek-3dkbd-*.png' -ErrorAction SilentlyContinue
$kill += Get-ChildItem (Join-Path $root 'build') -File -Filter 'desk-layout-*.png' -ErrorAction SilentlyContinue
$kill += Get-ChildItem (Join-Path $root 'build') -File -Filter '_desk*.png' -ErrorAction SilentlyContinue
$kill += Get-ChildItem (Join-Path $root 'build') -File -Filter 'desk-bg-*.png' -ErrorAction SilentlyContinue
$kill += Get-ChildItem (Join-Path $root 'build') -File -Filter 'desk-pet-*.png' -ErrorAction SilentlyContinue
$kill += Get-ChildItem (Join-Path $root 'build') -File -Filter 'deskline-ruler.png' -ErrorAction SilentlyContinue
foreach ($f in ($kill | Sort-Object FullName -Unique)) {
  Move-Into ('build\' + $f.Name) '桌面精修阶段的迭代证据图（已被取代）'
}

Write-Output "== D. 保留（明明不需要也不动）=="
Write-Output "  assets\idle/blink/happy/worry/sleepy.png  —— v1 站立立绘，新方案的主角"
Write-Output "  assets\desk\sit_watch.png / sit_look.png —— 代码仍在引用，等姿态集改造完成后一起清"
Write-Output "  assets\desk\press_*.png / face_*.png     —— 同上（现在是桌宠的活素材）"
Write-Output "  assets\desk\local\yuzusoft_official.png  —— 用户自备素材，永不清理"
Write-Output "  build\user_gemini_preview.png            —— 用户提供的原图"
Write-Output "  build\_archive\*、build\sheet-*、mc-*.png —— 代码游戏屏的证据图（王牌功能，留）"

$totalMb = ($moved | Measure-Object -Sum mb).Sum
$manifest = New-Object System.Text.StringBuilder
[void]$manifest.AppendLine("# 清退清单（$stamp）")
[void]$manifest.AppendLine("")
[void]$manifest.AppendLine("生成时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$manifest.AppendLine("共移入 $($moved.Count) 项 / $([Math]::Round($totalMb,1)) MB。**尚未真删**，确认后再删。")
[void]$manifest.AppendLine("")
[void]$manifest.AppendLine("| 原路径 | 类型 | 大小(MB) | 原因 |")
[void]$manifest.AppendLine("|---|---|---|---|")
foreach ($m in $moved) { [void]$manifest.AppendLine("| ``$($m.path)`` | $($m.kind) | $($m.mb) | $($m.why) |") }
[void]$manifest.AppendLine("")
[void]$manifest.AppendLine("## 恢复方法（全部可逆）")
[void]$manifest.AppendLine('```powershell')
[void]$manifest.AppendLine("`$trash = 'D:\DeepSeek\.trash\$stamp'")
[void]$manifest.AppendLine("Get-ChildItem `$trash -Recurse -File | ForEach-Object { `$rel = `$_.FullName.Substring(`$trash.Length+1); `$dst = Join-Path 'D:\DeepSeek\deepseek-pet' (Split-Path `$rel -Parent); New-Item -ItemType Directory -Path `$dst -Force | Out-Null; Copy-Item `$_.FullName `$dst -Force }")
[void]$manifest.AppendLine('```')
[void]$manifest.AppendLine("（目录类项目恢复时把里面的文件按相对路径拷回即可。）")
[System.IO.File]::WriteAllText((Join-Path $trash 'MANIFEST.md'), $manifest.ToString(), (New-Object System.Text.UTF8Encoding($true)))

Write-Output ""
Write-Output ("== 合计移入 {0} 项 / {1:N1} MB ==" -f $moved.Count, $totalMb)
Write-Output ("清单：{0}" -f (Join-Path $trash 'MANIFEST.md'))
