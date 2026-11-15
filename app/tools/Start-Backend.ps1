[CmdletBinding()]
param(
    [string]$BindAddress = '127.0.0.1',
    [ValidateRange(1,65535)][int]$Port = 8787,
    [string]$PythonPath = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

# Import the privately configured key without writing or displaying it.
if ([string]::IsNullOrWhiteSpace($env:EXA_API_KEY)) {
    $env:EXA_API_KEY = [Environment]::GetEnvironmentVariable('EXA_API_KEY', 'User')
}
if ([string]::IsNullOrWhiteSpace($env:TRAVELAGENT_DEMO_TOKEN)) {
    $env:TRAVELAGENT_DEMO_TOKEN = [Environment]::GetEnvironmentVariable('TRAVELAGENT_DEMO_TOKEN', 'User')
}
$bindIp = $null
$isLocalOnly = $BindAddress -eq 'localhost'
if ([System.Net.IPAddress]::TryParse($BindAddress, [ref]$bindIp)) {
    if ($bindIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        throw 'This gateway currently binds IPv4 addresses. Use 127.0.0.1 or the reachable private IPv4 interface.'
    }
    $isLocalOnly = [System.Net.IPAddress]::IsLoopback($bindIp)
} elseif (-not $isLocalOnly) {
    throw 'BindAddress must be localhost or a literal private IPv4 address.'
}
$sessionToken = $env:TRAVELAGENT_DEMO_TOKEN
if ($sessionToken -and $sessionToken -notmatch '^[\x21-\x7e]{1,256}$') {
    throw 'The session token must be printable ASCII without spaces, at most 256 characters. Keep it out of logs and command-line arguments.'
}
if (-not $isLocalOnly -and (!$sessionToken -or $sessionToken.Length -lt 16)) {
    throw 'LAN binding requires a privately generated TRAVELAGENT_DEMO_TOKEN of at least 16 characters in the launching environment.'
}
if ([string]::IsNullOrWhiteSpace($env:EXA_API_KEY)) {
    Write-Warning 'EXA_API_KEY is missing. The server can start, but live research will show a configuration error.'
}

$pythonCandidates = @()
if ($PythonPath) { $pythonCandidates += $PythonPath }
$pythonCandidates += (Join-Path $projectRoot 'Backend\.venv\Scripts\python.exe')
$pythonCandidates += (Join-Path (Split-Path $projectRoot -Parent) 'Junction2026\.venv\Scripts\python.exe')
$nodeCommand = Get-Command node.exe -ErrorAction SilentlyContinue
if ($nodeCommand) {
    $dependencyRoot = Split-Path (Split-Path (Split-Path $nodeCommand.Source -Parent) -Parent) -Parent
    $pythonCandidates += (Join-Path $dependencyRoot 'python\python.exe')
}
$pythonCandidates += (Get-Command python.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
$pythonExecutable = $pythonCandidates | Where-Object {
    $_ -and (Test-Path -LiteralPath $_) -and ($_ -notlike '*\Microsoft\WindowsApps\python.exe')
} | Select-Object -First 1
if (!$pythonExecutable) { throw 'Install Python 3.10+ or pass -PythonPath with a real Python executable.' }

# Foreground intentionally: Ctrl+C stops the service and its private environment.
& $pythonExecutable (Join-Path $projectRoot 'Backend\server.py') --host $BindAddress --port $Port
exit $LASTEXITCODE
