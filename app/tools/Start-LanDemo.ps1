[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
param(
    [Parameter(Mandatory)][string]$BindAddress,
    [ValidateRange(1, 65535)][int]$Port = 8788,
    [switch]$RotateToken
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$bindIp = $null
if (-not [Net.IPAddress]::TryParse($BindAddress, [ref]$bindIp) -or
    $bindIp.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) {
    throw 'Choose a literal IPv4 address on this laptop.'
}
$addressBytes = $bindIp.GetAddressBytes()
$isPrivate = $addressBytes[0] -eq 10 -or
    ($addressBytes[0] -eq 172 -and $addressBytes[1] -ge 16 -and $addressBytes[1] -le 31) -or
    ($addressBytes[0] -eq 192 -and $addressBytes[1] -eq 168)
if (-not $isPrivate) { throw 'This demo launcher accepts private LAN addresses only.' }
if (-not (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -eq $BindAddress })) {
    throw 'That address is not assigned to this laptop. Recheck its current Wi-Fi address.'
}
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    throw 'That port already has a listener. Keep its existing session or choose another port.'
}
if (-not $PSCmdlet.ShouldProcess("http://${BindAddress}:$Port", 'Start the authenticated LAN Exa gateway')) { return }

$artifactDirectory = Join-Path $projectRoot 'Artifacts'
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$tokenBytes = $null
$sessionToken = $null
$tokenReused = $false
$tokenPath = Join-Path $artifactDirectory 'lan-session-token.dpapi'
$metadataPath = Join-Path $artifactDirectory 'lan-gateway-session.json'
if (-not $RotateToken -and (Test-Path -LiteralPath $tokenPath) -and (Test-Path -LiteralPath $metadataPath)) {
    $protectedSession = $null
    try {
        $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
        if ($metadata.host -eq $BindAddress -and [int]$metadata.port -eq $Port) {
            $protectedSession = ConvertTo-SecureString ((Get-Content -LiteralPath $tokenPath -Raw).Trim())
            $sessionToken = [Net.NetworkCredential]::new('', $protectedSession).Password
            if ($sessionToken -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid saved session format.' }
            $tokenReused = $true
        }
    } catch {
        throw 'The saved encrypted session could not be opened. Use -RotateToken to create a new session and update the phone.'
    } finally { if ($null -ne $protectedSession) { $protectedSession.Dispose() } }
}
if (-not $tokenReused) {
    $tokenBytes = [byte[]]::new(32)
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($tokenBytes) } finally { $random.Dispose() }
    $sessionToken = [BitConverter]::ToString($tokenBytes).Replace('-', '')
}
# Windows DPAPI binds this encrypted handoff to the current Windows user.
# Neither this bearer token nor the Exa key is printed or placed in arguments.
if (-not $tokenReused) {
    $sessionToken | ConvertTo-SecureString -AsPlainText -Force | ConvertFrom-SecureString |
        Set-Content -LiteralPath $tokenPath -Encoding ascii
}
[ordered]@{
    launcherProcessId = $PID
    host = $BindAddress
    port = $Port
    tokenFile = 'Artifacts/lan-session-token.dpapi'
    tokenReused = $tokenReused
    startedAtUtc = [DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8

$previousToken = $env:TRAVELAGENT_DEMO_TOKEN
try {
    $env:TRAVELAGENT_DEMO_TOKEN = $sessionToken
    Write-Host "Starting the LAN demo at http://${BindAddress}:$Port. Keep this window open; Ctrl+C stops it."
    & (Join-Path $PSScriptRoot 'Start-Backend.ps1') -BindAddress $BindAddress -Port $Port
} finally {
    $env:TRAVELAGENT_DEMO_TOKEN = $previousToken
    if ($null -ne $tokenBytes) { [Array]::Clear($tokenBytes, 0, $tokenBytes.Length) }
    $sessionToken = $null
}
