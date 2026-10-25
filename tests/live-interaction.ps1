param([int]$DesktopProcessId)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing,System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class LiveInputNative {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint data,UIntPtr extra);
}
'@
[LiveInputNative]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
& (Join-Path $PSScriptRoot 'TrayProbe.exe') $DesktopProcessId open
if($LASTEXITCODE -ne 0){throw 'Cannot open DSH'}
$taskWindow=(Get-Process -Id $DesktopProcessId).MainWindowHandle
$taskRect=[LiveInputNative+Rect]::new()
[LiveInputNative]::GetWindowRect($taskWindow,[ref]$taskRect) | Out-Null
$taskPointer=[LiveInputNative+Point]::new()
[LiveInputNative]::GetCursorPos([ref]$taskPointer) | Out-Null
function Save-TestFrame([string]$Name){
    $taskBitmap=[System.Drawing.Bitmap]::new($taskRect.Right-$taskRect.Left,$taskRect.Bottom-$taskRect.Top)
    $taskGraphics=[System.Drawing.Graphics]::FromImage($taskBitmap)
    try{$taskGraphics.CopyFromScreen($taskRect.Left,$taskRect.Top,0,0,$taskBitmap.Size);$taskBitmap.Save((Join-Path $PSScriptRoot $Name))}
    finally{$taskGraphics.Dispose();$taskBitmap.Dispose()}
}
function Click-TestPoint([double]$X,[double]$Y){
    if([LiveInputNative]::GetForegroundWindow() -ne $taskWindow){throw 'DSH lost foreground; refusing to click another app'}
    [LiveInputNative]::SetCursorPos([int]($taskRect.Left+($taskRect.Right-$taskRect.Left)*$X),[int]($taskRect.Top+($taskRect.Bottom-$taskRect.Top)*$Y)) | Out-Null
    [LiveInputNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    [LiveInputNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 450
}
try{
    # Positions checked against dsh-restored.png. Only switch tabs and type an
    # unsent marker into the previously empty editor; never press Enter.
    Save-TestFrame 'interaction-before.png'
    Click-TestPoint .298 .119
    Save-TestFrame 'interaction-trace.png'
    Click-TestPoint .253 .119
    Save-TestFrame 'interaction-dialogue.png'
    Click-TestPoint .55 .87
    [System.Windows.Forms.SendKeys]::SendWait('1234567890')
    Start-Sleep -Milliseconds 450
    Save-TestFrame 'interaction-typed.png'
    if([LiveInputNative]::GetForegroundWindow() -ne $taskWindow){throw 'DSH lost foreground; cannot clear test draft safely'}
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 150
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    Start-Sleep -Milliseconds 150
    [System.Windows.Forms.SendKeys]::SendWait('{BACKSPACE}')
    Start-Sleep -Milliseconds 350
    Save-TestFrame 'interaction-cleared.png'
}finally{[LiveInputNative]::SetCursorPos($taskPointer.X,$taskPointer.Y) | Out-Null}
Write-Output 'Saved trace/dialogue/typed/cleared frames; inspect them before declaring interaction success.'
