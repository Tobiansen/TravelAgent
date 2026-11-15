[CmdletBinding()]
param([string]$EditorData = 'C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Data')
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$validationRoot = Join-Path $env:LOCALAPPDATA 'TravelAgentAndroidValidation'
New-Item -ItemType Directory -Force -Path $validationRoot | Out-Null
$compiler = Join-Path $EditorData 'DotNetSdk\sdk\8.0.318\Roslyn\bincore\csc.dll'
$runtime = Join-Path $EditorData 'NetCoreRuntime\dotnet.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'This check requires the Roslyn compiler bundled with Unity 6000.6.9f1.' }
$references = @(Get-ChildItem -LiteralPath (Join-Path $EditorData 'NetStandard\ref\2.1.0') -Filter '*.dll' | ForEach-Object { '/reference:' + $_.FullName })
$references += @(Get-ChildItem -LiteralPath (Join-Path $EditorData 'Managed\UnityEngine') -Filter '*.dll' | ForEach-Object { '/reference:' + $_.FullName })
$sources = @((Join-Path $projectRoot 'Assets\_App\Scripts\Core\DeviceModels.cs'), (Join-Path $projectRoot 'Assets\_App\Scripts\Integrations\AndroidBagTransport.cs'))
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets\_App\Scripts\Integrations\Photos') -Filter '*.cs' | ForEach-Object { $_.FullName })
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets\_App\Scripts\Integrations\Sharing') -Filter '*.cs' | ForEach-Object { $_.FullName })
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets\_App\Scripts\Integrations\Import') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $runtime $compiler /nologo /noconfig /nostdlib+ /target:library /langversion:9 /define:UNITY_ANDROID /warnaserror+ ('/out:' + (Join-Path $validationRoot 'BleAndroidCSharp.dll')) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Android-conditional C# source validation failed.' }
& $runtime $compiler /nologo /noconfig /nostdlib+ /target:library /langversion:9 /define:UNITY_EDITOR /warnaserror+ ('/out:' + (Join-Path $validationRoot 'BleEditorCSharp.dll')) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Editor-conditional C# source validation failed.' }
$photoChecks = Join-Path $validationRoot 'PhotoHeaderChecks.dll'
& $runtime $compiler /nologo /noconfig /nostdlib+ /target:exe /langversion:9 /warnaserror+ ('/out:' + $photoChecks) @references (Join-Path $projectRoot 'Assets\_App\Scripts\Integrations\Photos\PhotoHeader.cs') (Join-Path $PSScriptRoot 'PhotoHeaderTest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Photo metadata test compilation failed.' }
$runtimeVersion = @(Get-ChildItem -LiteralPath (Join-Path $EditorData 'NetCoreRuntime\shared\Microsoft.NETCore.App') -Directory | Sort-Object Name -Descending)[0].Name
@{runtimeOptions=@{tfm='net8.0';framework=@{name='Microsoft.NETCore.App';version=$runtimeVersion}}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $validationRoot 'PhotoHeaderChecks.runtimeconfig.json') -Encoding utf8
& $runtime $photoChecks (Join-Path $projectRoot 'Assets\_App\Art\Images\fixture-before.png') (Join-Path $projectRoot 'Assets\_App\Art\Images\fixture-after.png')
if ($LASTEXITCODE -ne 0) { throw 'Photo metadata/EXIF/bounds tests failed.' }
Write-Output 'Android and Editor C# adapters/photo services compile with warnings treated as errors against the installed Unity reference assemblies. This is not an Android Player build.'
