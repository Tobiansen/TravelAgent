param(
    [Parameter(Mandatory)][ValidateSet('generate','upgrade','polish','refresh','save','clear','open','verify','play','stop','preview','capture','tests','resolution','live','ui-demo','build-android')][string]$Action,
    [string]$Argument = '',
    [switch]$NoWait
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$inboxPath = Join-Path $projectRoot 'Temp\travel-agent-command.json'
$outboxPath = Join-Path $projectRoot 'Artifacts\editor-response.json'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $inboxPath) | Out-Null
$queueDeadline = [DateTime]::UtcNow.AddSeconds(40)
while (Test-Path -LiteralPath $inboxPath) {
    if ([DateTime]::UtcNow -gt $queueDeadline) { throw 'Unity has not consumed the previous command. Open the Editor and resolve compilation before retrying.' }
    Start-Sleep -Milliseconds 200
}
$commandId = [guid]::NewGuid().ToString('N')
@{ id=$commandId; action=$Action; argument=$Argument } | ConvertTo-Json | Set-Content -LiteralPath $inboxPath -Encoding utf8
Write-Output "Queued $Action ($commandId). The open Unity Editor executes it after compilation."
if ($NoWait) { return }
$responseDeadline = [DateTime]::UtcNow.AddSeconds($(if($Action -eq 'build-android'){900}else{45}))
while ([DateTime]::UtcNow -lt $responseDeadline) {
    if (Test-Path -LiteralPath $outboxPath) {
        try { $response = Get-Content -Raw -LiteralPath $outboxPath | ConvertFrom-Json } catch { $response = $null }
        if ($response -and $response.id -eq $commandId) {
            if (-not $response.ok) { throw $response.message }
            Write-Output "$Action executed."
            return
        }
    }
    Start-Sleep -Milliseconds 200
}
throw 'Unity did not acknowledge the command before the deadline. Inspect Artifacts/editor-response.json and the Editor Console.'
