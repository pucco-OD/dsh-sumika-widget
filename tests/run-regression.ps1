$ErrorActionPreference='Stop'
$taskDev=Split-Path -Parent $PSScriptRoot
& (Join-Path $taskDev 'build.ps1') -Release
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskFramework=Split-Path -Parent $taskCompiler
$taskWpf=Join-Path $taskFramework 'WPF'
$taskReferences=@('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll') | ForEach-Object {'/reference:'+(Join-Path $taskFramework $_)}
$taskReferences+=@('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','UIAutomationTypes.dll') | ForEach-Object {'/reference:'+(Join-Path $taskWpf $_)}
& $taskCompiler /nologo /target:exe /platform:x64 /codepage:65001 ('/out:'+(Join-Path $PSScriptRoot 'QuestionWindowInteractionTests.exe')) @taskReferences (Join-Path $taskDev 'src/QuestionWindow.cs') (Join-Path $taskDev 'src/QuestionOption.cs') (Join-Path $taskDev 'src/SmoothPixelScroll.cs') (Join-Path $PSScriptRoot 'QuestionWindowInteractionTests.cs')
if($LASTEXITCODE -ne 0){throw 'Question window test build failed'}
& $taskCompiler /nologo /target:exe /platform:x64 /codepage:65001 ('/out:'+(Join-Path $PSScriptRoot 'TrayProbe.exe')) (Join-Path $taskDev 'src/DshActivation.cs') (Join-Path $PSScriptRoot 'TrayProbe.cs')
if($LASTEXITCODE -ne 0){throw 'Tray probe build failed'}
& (Join-Path $PSScriptRoot 'PlaybackTests.exe')
if($LASTEXITCODE -ne 0){throw 'Playback tests failed'}
& (Join-Path $PSScriptRoot 'QuestionWindowInteractionTests.exe')
if($LASTEXITCODE -ne 0){throw 'Question window tests failed'}
