$ErrorActionPreference='Stop'
$taskVersion=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskData=Join-Path $PSScriptRoot 'live-session'
$taskOriginalData=Join-Path $env:LOCALAPPDATA 'Sumika'
New-Item -ItemType Directory -Path $taskData -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskOriginalData '.sumika-host.json'),(Join-Path $taskOriginalData '.sumika-bridge.json') -Destination $taskData
$taskOwner=Get-Content -LiteralPath (Join-Path $taskData '.sumika-host.json') -Raw | ConvertFrom-Json
@{enabled=$true;height=320;margin=24;volume=0;muted=$false;left=100;top=300;hasPosition=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskData 'settings.json') -Encoding utf8
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WidgetLiveNative {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint data,UIntPtr extra);
}
'@
[WidgetLiveNative]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
$taskPointer=[WidgetLiveNative+Point]::new()
[WidgetLiveNative]::GetCursorPos([ref]$taskPointer) | Out-Null
$taskExecutable=Join-Path $taskVersion 'Sumika/runtime/Sumika.exe'
$taskChild=$null
$taskDiagnostic=$null
$taskResults=@()
try{
    $taskChild=Start-Process -FilePath $taskExecutable -ArgumentList @('--data-dir',('"'+$taskData+'"'),'--host-session',$taskOwner.instance) -WindowStyle Hidden -PassThru
    $taskDeadline=[DateTime]::UtcNow.AddSeconds(15)
    do{
        Start-Sleep -Milliseconds 150
        if($taskChild.HasExited){throw 'Test widget exited during startup'}
        try{$taskDiagnostic=Get-Content -LiteralPath (Join-Path $taskData 'diagnostics.json') -Raw | ConvertFrom-Json}catch{}
    }while(($null -eq $taskDiagnostic -or $taskDiagnostic.pid -ne $taskChild.Id) -and [DateTime]::UtcNow -lt $taskDeadline)
    if($null -eq $taskDiagnostic -or $taskDiagnostic.pid -ne $taskChild.Id){throw 'Test widget did not produce diagnostics'}
    Start-Sleep -Milliseconds 1600
    $taskDesktop=(Get-Process -Id $taskOwner.desktopPid).MainWindowHandle
    if($taskDesktop -eq [IntPtr]::Zero){throw 'DSH must initially be visible'}
    for($taskCycle=1;$taskCycle -le 3;$taskCycle++){
        [WidgetLiveNative]::PostMessage($taskDesktop,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
        $taskDeadline=[DateTime]::UtcNow.AddSeconds(3)
        while([WidgetLiveNative]::IsWindowVisible($taskDesktop) -and [DateTime]::UtcNow -lt $taskDeadline){Start-Sleep -Milliseconds 50}
        if([WidgetLiveNative]::IsWindowVisible($taskDesktop)){throw 'DSH did not enter tray'}
        $taskLog=Join-Path $taskData 'runtime.log'
        $taskBefore=([regex]::Matches([IO.File]::ReadAllText($taskLog),'desktop-restored ')).Count
        $taskDiagnostic=Get-Content -LiteralPath (Join-Path $taskData 'diagnostics.json') -Raw | ConvertFrom-Json
        $taskRect=$taskDiagnostic.rect
        $taskHit=$null
        foreach($taskY in @(.50,.60,.40,.70,.80,.30)){
            foreach($taskX in @(.50,.60,.40,.70,.30)){
                $taskCandidate=[WidgetLiveNative+Point]::new()
                $taskCandidate.X=[int]($taskRect.Left+($taskRect.Right-$taskRect.Left)*$taskX)
                $taskCandidate.Y=[int]($taskRect.Top+($taskRect.Bottom-$taskRect.Top)*$taskY)
                if([WidgetLiveNative]::WindowFromPoint($taskCandidate) -eq [IntPtr]$taskDiagnostic.hwnd){$taskHit=$taskCandidate;break}
            }
            if($null -ne $taskHit){break}
        }
        if($null -eq $taskHit){throw 'Cannot find a visible, opaque point on the test widget'}
        [WidgetLiveNative]::SetCursorPos($taskHit.X,$taskHit.Y) | Out-Null
        for($taskClick=0;$taskClick -lt 2;$taskClick++){
            [WidgetLiveNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
            Start-Sleep -Milliseconds 30
            [WidgetLiveNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
            Start-Sleep -Milliseconds 70
        }
        $taskDeadline=[DateTime]::UtcNow.AddSeconds(4)
        do{
            Start-Sleep -Milliseconds 100
            $taskAfter=([regex]::Matches([IO.File]::ReadAllText($taskLog),'desktop-restored ')).Count
        }while($taskAfter -le $taskBefore -and [DateTime]::UtcNow -lt $taskDeadline)
        $taskVisible=[WidgetLiveNative]::IsWindowVisible($taskDesktop)
        if(-not $taskVisible -or $taskAfter -le $taskBefore){throw ('Double-click restore failed in cycle '+$taskCycle)}
        $taskResults += [pscustomobject]@{cycle=$taskCycle;widgetPid=$taskChild.Id;realCloseHidden=$true;physicalDoubleClick=$true;restoredVisible=$taskVisible;restoreLogIncrement=$taskAfter-$taskBefore}
        Start-Sleep -Milliseconds 500
    }
    $taskResults | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'live-widget-result.json') -Encoding utf8
    Copy-Item -LiteralPath $taskLog -Destination (Join-Path $PSScriptRoot 'live-widget.log')
    $taskResults | ConvertTo-Json -Depth 5
}finally{
    [WidgetLiveNative]::SetCursorPos($taskPointer.X,$taskPointer.Y) | Out-Null
    if($null -ne $taskChild -and -not $taskChild.HasExited){
        if($null -ne $taskDiagnostic){[WidgetLiveNative]::PostMessage([IntPtr]$taskDiagnostic.hwnd,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null}
        if(-not $taskChild.WaitForExit(3000)){Stop-Process -Id $taskChild.Id}
    }
    # Remove only our copied credentials, never the original plugin's files.
    foreach($taskName in @('.sumika-host.json','.sumika-bridge.json')){
        $taskCredential=Join-Path $taskData $taskName
        if(Test-Path -LiteralPath $taskCredential){Remove-Item -LiteralPath $taskCredential -Force}
    }
    & (Join-Path $PSScriptRoot 'TrayProbe.exe') $taskOwner.desktopPid open
}
