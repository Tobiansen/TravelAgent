[CmdletBinding()]
param(
    [string]$JdkPath = "$env:LOCALAPPDATA\TravelAgentAndroidValidation\android-open-jdk-17.0.18+8",
    [string]$AndroidJar = "$env:LOCALAPPDATA\TravelAgentAndroidValidation\android-sdk-platforms-36\android-36\android.jar"
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$javaRoot = Join-Path $projectRoot 'Assets\Plugins\Android\TravelAgentBle.androidlib\src\main\java'
$classes = Join-Path $env:LOCALAPPDATA 'TravelAgentAndroidValidation\classes'
if (-not (Test-Path -LiteralPath (Join-Path $JdkPath 'bin\javac.exe'))) { throw 'Supply a JDK 17 path with -JdkPath.' }
if (-not (Test-Path -LiteralPath $AndroidJar)) { throw 'Supply an Android API 36+ android.jar with -AndroidJar.' }
New-Item -ItemType Directory -Force -Path $classes | Out-Null
$sources = @(Get-ChildItem -LiteralPath $javaRoot -Recurse -Filter '*.java' | ForEach-Object { $_.FullName })
& (Join-Path $JdkPath 'bin\javac.exe') -encoding UTF-8 -Xlint:deprecation -cp $AndroidJar -d $classes @sources (Join-Path $PSScriptRoot 'BleFramesTest.java') (Join-Path $PSScriptRoot 'ReportSharingTest.java') (Join-Path $PSScriptRoot 'PhotoCaptureTest.java') (Join-Path $PSScriptRoot 'IncomingShareTest.java') (Join-Path $PSScriptRoot 'GatewaySettingsTest.java') (Join-Path $PSScriptRoot 'LiveMotionTest.java')
if ($LASTEXITCODE -ne 0) { throw 'Native Java source compilation failed.' }
& (Join-Path $JdkPath 'bin\java.exe') -cp $classes BleFramesTest
if ($LASTEXITCODE -ne 0) { throw 'BLE frame tests failed.' }
& (Join-Path $JdkPath 'bin\java.exe') -cp $classes LiveMotionTest
if ($LASTEXITCODE -ne 0) { throw 'Live motion admission/queue-pressure tests failed.' }
& (Join-Path $JdkPath 'bin\java.exe') -cp $classes ReportSharingTest
if ($LASTEXITCODE -ne 0) { throw 'Report archive/path tests failed.' }
& (Join-Path $JdkPath 'bin\java.exe') -cp $classes PhotoCaptureTest
if ($LASTEXITCODE -ne 0) { throw 'Camera journal/path/orientation tests failed.' }
& (Join-Path $JdkPath 'bin\java.exe') -cp $classes IncomingShareTest
if ($LASTEXITCODE -ne 0) { throw 'Incoming-share input/inbox/lifecycle tests failed.' }
& (Join-Path $JdkPath 'bin\java.exe') -cp $classes GatewaySettingsTest
if ($LASTEXITCODE -ne 0) { throw 'Gateway settings validation/encryption tests failed.' }
Write-Output 'Java compilation, framing, report archive/path, camera, incoming-share and synthetic gateway encryption tests passed. This does not verify APK packaging, Android Keystore execution, camera, incoming Android intents or physical BLE/sharing.'
