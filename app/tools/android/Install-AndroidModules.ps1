[CmdletBinding()]
param([string]$UnityCli = '')
$ErrorActionPreference = 'Stop'
if (-not $UnityCli) {
    $unityCommand = Get-Command unity -ErrorAction SilentlyContinue
    if ($unityCommand) { $UnityCli = $unityCommand.Source }
    $packagedCli = Join-Path $env:LOCALAPPDATA 'Packages\UnityTechnologies.UnityHub_2vrhnee42bhxm\LocalCache\Local\Unity\bin\unity.exe'
    if (Test-Path -LiteralPath $packagedCli) { $UnityCli = $packagedCli }
}
if (-not $UnityCli -or -not (Test-Path -LiteralPath $UnityCli)) { throw 'Supply the Unity CLI executable using -UnityCli, or install it from Unity Hub Tools > Unity CLI.' }
# The installed Editor is under Program Files. Run this script from an administrator terminal.
# --no-elevate prevents an unexpected UAC dialog and reports an explicit rights error instead.
& $UnityCli install-modules --editor-version 6000.6.9f1 --module android --child-modules --accept-eula --yes --non-interactive --no-banner --no-elevate --timeout 60
if ($LASTEXITCODE -ne 0) { throw 'Android modules were not installed. Check administrator rights and the CLI error above.' }
