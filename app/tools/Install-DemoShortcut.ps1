[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $PSScriptRoot 'Start-Demo.ps1'
$desktopPath = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktopPath 'TravelAgent Demo.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
if ((Test-Path -LiteralPath $shortcutPath) -and $shortcut.Arguments -notlike ('*' + $scriptPath + '*')) {
    throw 'A different TravelAgent Demo shortcut already exists. Rename it before installing this shortcut.'
}
$shortcut.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$shortcut.Arguments = '-NoLogo -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $scriptPath + '"'
$shortcut.WorkingDirectory = $projectRoot
$shortcut.Description = 'Start and monitor the laptop connection for TravelAgent Exa research.'
$shortcut.IconLocation = (Join-Path $env:SystemRoot 'System32\shell32.dll') + ',135'
$shortcut.Save()
Write-Output "Created desktop shortcut: $shortcutPath"
