# DPI-aware screenshot helper for the desktop pet.
# Usage:
#   powershell -File shot.ps1 -Out <path> [-ProcName DeepSeekPet] [-Pad 0] [-Full]
# Notes: PowerShell/DSH shells are DPI-unaware by default; this script calls
#        SetProcessDpiAwareness(2) BEFORE measuring, so coordinates and the
#        capture are in real physical pixels (2560x1440 here, not 2048x1152).
#        Window lookup uses EnumWindows by PID (MainWindowHandle is 0 for this
#        ShowInTaskbar=false WPF window).
param(
  [string]$Out = "D:\DeepSeek\deepseek-pet\build\_scratch\shot.png",
  [string]$ProcName = "DeepSeekPet",
  [int]$Pad = 0,
  [switch]$Full
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$src = @"
using System;
using System.Runtime.InteropServices;
public class DshCap {
  public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
  static IntPtr found = IntPtr.Zero;
  static uint want = 0;
  public static IntPtr FindWindowFor(uint pid) {
    want = pid; found = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == want && IsWindowVisible(h)) {
        RECT r; GetWindowRect(h, out r);
        if ((r.Right - r.Left) > 50 && (r.Bottom - r.Top) > 50) { found = h; return false; }
      }
      return true;
    }, IntPtr.Zero);
    return found;
  }
  public static int[] GetRect(IntPtr hWnd) { RECT r; GetWindowRect(hWnd, out r); return new int[] { r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top }; }
}
"@
try { Add-Type -TypeDefinition $src -ErrorAction Stop } catch { }

[void][DshCap]::SetProcessDpiAwareness(2)

function Save-Region([int]$x, [int]$y, [int]$w, [int]$h, [string]$path) {
  $dir = Split-Path -Parent $path
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w, $h)))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

if ($Full) {
  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  Save-Region $b.X $b.Y $b.Width $b.Height $Out
  Write-Output ("full {0}x{1} -> {2}" -f $b.Width, $b.Height, $Out)
  exit 0
}

$p = Get-Process -Name $ProcName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "process $ProcName not running"; exit 1 }
$h = [DshCap]::FindWindowFor([uint32]$p.Id)
if ($h -eq [IntPtr]::Zero) { Write-Output "no visible window for $ProcName (pid $($p.Id))"; exit 2 }
$r = [DshCap]::GetRect($h)
$w = $r[2] + 2 * $Pad; $ht = $r[3] + 2 * $Pad
Save-Region ($r[0] - $Pad) ($r[1] - $Pad) $w $ht $Out
Write-Output ("pid={0} rect=({1},{2}) {3}x{4} dip={5}x{6} -> {7}" -f $p.Id, $r[0], $r[1], $r[2], $r[3], [math]::Round($r[2] / 1.25, 1), [math]::Round($r[3] / 1.25, 1), $Out)
