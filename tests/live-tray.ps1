param([int]$DesktopProcessId, [int]$Cycles=3)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class LiveTrayNative {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
[LiveTrayNative]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
$taskWindow=(Get-Process -Id $DesktopProcessId).MainWindowHandle
if($taskWindow -eq [IntPtr]::Zero){throw 'DSH must start visible for the close-to-tray test'}
$taskResults=@()
for($taskCycle=1;$taskCycle -le $Cycles;$taskCycle++){
    # WM_CLOSE follows Electron's actual close handler, unlike ShowWindow(SW_HIDE).
    if(-not [LiveTrayNative]::PostMessage($taskWindow,0x10,[IntPtr]::Zero,[IntPtr]::Zero)){throw 'WM_CLOSE failed'}
    $taskDeadline=[DateTime]::UtcNow.AddSeconds(3)
    while([LiveTrayNative]::IsWindowVisible($taskWindow) -and [DateTime]::UtcNow -lt $taskDeadline){Start-Sleep -Milliseconds 50}
    if([LiveTrayNative]::IsWindowVisible($taskWindow)){throw 'DSH did not hide through its own close handler'}
    $taskOutput=@(& (Join-Path $PSScriptRoot 'TrayProbe.exe') $DesktopProcessId open)
    if($LASTEXITCODE -ne 0){throw ('Restore failed: '+($taskOutput -join ' '))}
    Start-Sleep -Milliseconds 400
    $taskVisible=[LiveTrayNative]::IsWindowVisible($taskWindow)
    $taskForeground=[LiveTrayNative]::GetForegroundWindow() -eq $taskWindow
    $taskResults += [pscustomobject]@{cycle=$taskCycle;realCloseHidden=$true;restoredVisible=$taskVisible;foreground=$taskForeground;log=$taskOutput}
    if(-not $taskVisible){throw 'Restored window was not visible'}
}
$taskRect=[LiveTrayNative+Rect]::new()
[LiveTrayNative]::GetWindowRect($taskWindow,[ref]$taskRect) | Out-Null
$taskBitmap=[System.Drawing.Bitmap]::new($taskRect.Right-$taskRect.Left,$taskRect.Bottom-$taskRect.Top)
$taskGraphics=[System.Drawing.Graphics]::FromImage($taskBitmap)
try{
    $taskGraphics.CopyFromScreen($taskRect.Left,$taskRect.Top,0,0,$taskBitmap.Size)
    $taskBitmap.Save((Join-Path $PSScriptRoot 'dsh-restored.png'))
}finally{$taskGraphics.Dispose();$taskBitmap.Dispose()}
$taskResults | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'live-tray-result.json') -Encoding utf8
$taskResults | ConvertTo-Json -Depth 5
