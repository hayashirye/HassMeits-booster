param(
  [string]$Title = 'valorant_boost',
  [string]$Exe   = 'valorant_boost.exe',
  [string]$Out   = 'fx.png',
  [string]$Click = '',
  [int]$AfterClickMs = 3000
)
# Screenshot / click helper for the Flutter build.
# ASCII only on purpose: PS 5.1 reads a BOM-less .ps1 as GBK, so any non-ASCII
# byte in here would break the parser.
#
# Window lookup, in order:
#   1. FindWindow(NULL, $Title)          -- exact title match
#   2. first visible top-level window of a process named $Exe
# Step 2 exists because the window title is Chinese (see windows\runner\main.cpp,
# where it is written as \u escapes) and this file must not contain non-ASCII
# bytes. Pass -Title itself if you need to pin an exact title from the command
# line; the command line is passed as UTF-16 so Chinese works there.
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class FxW {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)]
  public static extern IntPtr FindWindow(string cls, string name);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref PT p);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)]
  public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);

  public static IntPtr g_found = IntPtr.Zero;

  public static IntPtr FindByPids(int[] pids) {
    g_found = IntPtr.Zero;
    EnumWindows(new EnumProc(delegate(IntPtr h, IntPtr l) {
      if (!IsWindowVisible(h)) return true;
      if (GetWindowTextLength(h) <= 0) return true;
      uint pid;
      GetWindowThreadProcessId(h, out pid);
      if (Array.IndexOf(pids, (int)pid) >= 0) { g_found = h; return false; }
      return true;
    }), IntPtr.Zero);
    return g_found;
  }

  public static string TextOf(IntPtr h) {
    int n = GetWindowTextLength(h);
    if (n <= 0) return "";
    StringBuilder sb = new StringBuilder(n + 2);
    GetWindowText(h, sb, sb.Capacity);
    return sb.ToString();
  }

  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,Ri,B; }
  [StructLayout(LayoutKind.Sequential)] public struct PT { public int X,Y; }
}
"@

$h = [FxW]::FindWindow([NullString]::Value, $Title)

if ($h -eq [IntPtr]::Zero) {
  $base = [System.IO.Path]::GetFileNameWithoutExtension($Exe)
  $pids = @(Get-Process -Name $base -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
  if ($pids.Count -gt 0) {
    $h = [FxW]::FindByPids([int[]]$pids)
    if ($h -ne [IntPtr]::Zero) { "found by process name ($($pids.Count) pid)" }
  }
}

if ($h -eq [IntPtr]::Zero) { "WINDOW NOT FOUND: title='$Title' exe='$Exe'"; exit 1 }

"title='$([FxW]::TextOf($h))'"
$cr = New-Object FxW+RECT
[void][FxW]::GetClientRect($h, [ref]$cr)
$wr = New-Object FxW+RECT
[void][FxW]::GetWindowRect($h, [ref]$wr)
$cw = $cr.Ri - $cr.L
$ch = $cr.B - $cr.T
"window=$h  client=${cw}x${ch}  rect L=$($wr.L) T=$($wr.T)  dpi-relative"

[void][FxW]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 700

if ($Click -ne '') {
  foreach ($one in $Click.Split(';')) {
    $xy = $one.Split(',')
    $cx = [int]$xy[0]; $cy = [int]$xy[1]
    $pt = New-Object FxW+PT
    $pt.X = $cx; $pt.Y = $cy
    [void][FxW]::ClientToScreen($h, [ref]$pt)
    [FxW]::SetCursorPos($pt.X, $pt.Y)
    Start-Sleep -Milliseconds 120
    [FxW]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)   # LEFTDOWN
    Start-Sleep -Milliseconds 60
    [FxW]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)   # LEFTUP
    Start-Sleep -Milliseconds 250
  }
  Start-Sleep -Milliseconds $AfterClickMs
}

$bmp = New-Object System.Drawing.Bitmap($cw, $ch)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
# 3 = PW_RENDERFULLCONTENT (works for GPU-composited windows)
[void][FxW]::PrintWindow($h, $hdc, 3)
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"SAVED $Out"
