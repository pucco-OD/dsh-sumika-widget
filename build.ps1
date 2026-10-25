param([switch]$Release)
$ErrorActionPreference = 'Stop'
$rootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkDir = Split-Path -Parent $compilerPath
$wpfDir = Join-Path $frameworkDir 'WPF'
$refNames = @('System.dll','System.Core.dll','System.Xaml.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll')
$references = @($refNames | ForEach-Object { '/reference:' + (Join-Path $frameworkDir $_) })
$references += @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','UIAutomationTypes.dll') | ForEach-Object { '/reference:' + (Join-Path $wpfDir $_) }
$argsList = @('/nologo','/target:winexe','/optimize+','/codepage:65001','/platform:x64',('/win32manifest:' + (Join-Path $rootDir 'app.manifest')),('/out:' + (Join-Path $rootDir 'CodexPet.exe')))
if (Test-Path (Join-Path $rootDir 'pet.ico')) { $argsList += '/win32icon:' + (Join-Path $rootDir 'pet.ico') }
if($Release){$argsList+='/define:SUMIKA_RELEASE'}
& $compilerPath @argsList @references (Join-Path $rootDir 'src\Playback.cs') (Join-Path $rootDir 'src\DesktopPet.cs') (Join-Path $rootDir 'src\DshLifetime.cs') (Join-Path $rootDir 'src\DshActivation.cs') (Join-Path $rootDir 'src\WebpFrames.cs') (Join-Path $rootDir 'src\QuestionWindow.cs') (Join-Path $rootDir 'src\QuestionOption.cs') (Join-Path $rootDir 'src\SmoothPixelScroll.cs')
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
$testArgs = @('/nologo','/target:exe','/optimize+','/codepage:65001',('/out:' + (Join-Path $rootDir 'tests\PlaybackTests.exe')))
& $compilerPath @testArgs @references (Join-Path $rootDir 'src\Playback.cs') (Join-Path $rootDir 'tests\PlaybackTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
if($Release){
    # In this repository layout the plugin package is the repository root.
    $runtimePath=Join-Path $rootDir 'runtime\Sumika.exe'
    Copy-Item -LiteralPath (Join-Path $rootDir 'CodexPet.exe') -Destination $runtimePath -Force
}
Write-Output 'Build completed.'
