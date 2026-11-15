[CmdletBinding()]
param(
    [string]$AdbPath = 'C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$tokenPath = Join-Path $projectRoot 'Artifacts\lan-session-token.dpapi'
if (-not (Test-Path -LiteralPath $AdbPath)) { throw 'Android platform-tools adb.exe was not found.' }
if (-not (Test-Path -LiteralPath $tokenPath)) { throw 'Start the LAN demo gateway first.' }
$devices = @(& $AdbPath devices | Where-Object { $_ -match '^\S+\s+device$' })
if ($devices.Count -ne 1) { throw 'Connect exactly one USB-debugging phone.' }
$foreground = (& $AdbPath shell dumpsys activity activities | Select-String '(?:topResumedActivity|mResumedActivity|ResumedActivity)[:=]') -join "`n"
if ($foreground -notmatch 'com\.travelagent\.companion/') { throw 'Open TravelAgent and focus its Service access token field first.' }
$protectedToken = $null
$sessionToken = $null
$inputProcess = $null
try {
    $protectedToken = ConvertTo-SecureString ((Get-Content -LiteralPath $tokenPath -Raw).Trim())
    $sessionToken = [Net.NetworkCredential]::new('', $protectedToken).Password
    if ($sessionToken -notmatch '^[a-fA-F0-9]{64}$') { throw 'The saved gateway token has an invalid format.' }
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $AdbPath
    $startInfo.Arguments = 'shell -T sh'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $inputProcess = [Diagnostics.Process]::Start($startInfo)
    $inputProcess.StandardInput.WriteLine('input text ' + $sessionToken)
    $inputProcess.StandardInput.Close()
    if (-not $inputProcess.WaitForExit(15000)) { $inputProcess.Kill(); throw 'Phone input timed out.' }
    if ($inputProcess.ExitCode -ne 0) { throw 'Phone token entry failed.' }
    Start-Sleep -Milliseconds 400
    & $AdbPath shell input keyevent 66
    if ($LASTEXITCODE -ne 0) { throw 'Phone input could not be committed.' }
    Start-Sleep -Milliseconds 500
    # Unity can retain the field after the IME closes. A noninteractive header tap
    # explicitly ends editing so the encrypted-settings save listener runs.
    & $AdbPath shell input tap 80 140
    Start-Sleep -Milliseconds 900
    & $AdbPath shell run-as com.travelagent.companion test -s shared_prefs/travelagent_gateway_settings_v1.xml
    if ($LASTEXITCODE -ne 0) { throw 'The token was entered, but TravelAgent has not saved the connection. Leave the app open and report this message.' }
    Write-Host 'Gateway token entered and encrypted settings entry confirmed. No token was printed or saved in plaintext.'
}
finally {
    $sessionToken = $null
    if ($null -ne $protectedToken) { $protectedToken.Dispose() }
    if ($null -ne $inputProcess) { $inputProcess.Dispose() }
}
