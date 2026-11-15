[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DemoLauncher.Core.ps1')
$script:demoTestCount = 0

function Assert-DemoTest {
    param([bool]$Condition, [string]$Name)
    if (-not $Condition) { throw ('FAILED: ' + $Name) }
    $script:demoTestCount++
}
function Assert-DemoThrows {
    param([scriptblock]$Action, [string]$Name)
    $threw = $false
    try { [void](& $Action) } catch { $threw = $true }
    Assert-DemoTest $threw $Name
}
function New-TestNetwork {
    param([string]$Address, [bool]$Wifi = $true, [bool]$Default = $true, [double]$Metric = 10, [bool]$Physical = $true)
    return [pscustomobject]@{ IPAddress = $Address; InterfaceIndex = 7; InterfaceAlias = 'Test adapter'; IsPhysical = $Physical; IsWifi = $Wifi; HasDefaultRoute = $Default; Metric = $Metric }
}
function New-TestHealth {
    param([hashtable]$Overrides = @{})
    $health = @{ service = 'TravelAgent Exa gateway'; status = 'ok'; schemaVersion = '1'; exaConfigured = $true; authRequired = $true; loopbackOnly = $false }
    foreach ($key in $Overrides.Keys) { $health[$key] = $Overrides[$key] }
    return [pscustomobject]$health
}
function New-SessionCase {
    param([string]$Name)
    $path = Join-Path $script:demoTestDirectory $Name
    [void][IO.Directory]::CreateDirectory($path)
    return $path
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$script:demoTestDirectory = Join-Path $tempRoot ('TravelAgent-DemoLauncher-Test-' + [guid]::NewGuid().ToString('N'))
try {
    foreach ($scriptPath in @((Join-Path $PSScriptRoot 'DemoLauncher.Core.ps1'), $PSCommandPath)) {
        $parseTokens = $null; $parseErrors = $null
        [void][Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$parseTokens, [ref]$parseErrors)
        Assert-DemoTest (@($parseErrors).Count -eq 0) ('PowerShell syntax: ' + [IO.Path]::GetFileName($scriptPath))
    }

    foreach ($address in @('10.0.0.1', '10.255.255.254', '172.16.0.1', '172.31.255.254', '192.168.0.1', '192.168.255.254')) {
        Assert-DemoTest (Test-DemoPrivateIpv4 $address) ('Private LAN accepted: ' + $address)
    }
    foreach ($address in @('', '8.8.8.8', '127.0.0.1', '169.254.1.5', '172.15.255.255', '172.32.0.1', '0.0.0.0', '255.255.255.255', '::1', '::ffff:10.0.0.1', '10.1', '010.0.0.1', '10.0.0.256', '10.0.0.1 ', 'not-an-address')) {
        Assert-DemoTest (-not (Test-DemoPrivateIpv4 $address)) ('Non-LAN/malformed address rejected: ' + $address)
    }
    Assert-DemoTest (-not (Test-DemoPrivateIpv4 $null)) 'Null address rejected'

    $wifi = New-TestNetwork '10.0.0.10' $true $true 30
    $wired = New-TestNetwork '192.168.0.10' $false $true 1
    $vpn = New-TestNetwork '10.100.0.2' $false $true 0 $false
    Assert-DemoTest ((Select-DemoNetwork @($wifi, $wired, $vpn) '').IPAddress -eq $wifi.IPAddress) 'Physical default-route Wi-Fi outranks Ethernet and VPN'
    Assert-DemoTest ((Select-DemoNetwork @($wifi, $wired) $wired.IPAddress).IPAddress -eq $wired.IPAddress) 'Valid previous physical default-route address is retained'
    Assert-DemoTest ((Select-DemoNetwork @($wifi, $vpn) $vpn.IPAddress).IPAddress -eq $wifi.IPAddress) 'Preferred VPN is never retained'
    $wifiLow = New-TestNetwork '10.0.0.11' $true $true 5
    Assert-DemoTest ((Select-DemoNetwork @($wifi, $wifiLow) '').IPAddress -eq $wifiLow.IPAddress) 'Lowest metric wins within the same network class'
    $noRouteWifi = New-TestNetwork '10.0.0.12' $true $false 1
    Assert-DemoTest ((Select-DemoNetwork @($noRouteWifi, $wired) $noRouteWifi.IPAddress).IPAddress -eq $wired.IPAddress) 'Stale preferred address without a default route cannot displace a routed adapter'
    Assert-DemoTest ((Select-DemoNetwork @($noRouteWifi, (New-TestNetwork '192.168.1.3' $false $false)) '').IPAddress -eq $noRouteWifi.IPAddress) 'Physical Wi-Fi fallback works without a default route'
    Assert-DemoTest ((Select-DemoNetwork @($wired) '10.0.0.99').IPAddress -eq $wired.IPAddress) 'Changed Wi-Fi address falls back to an available physical route'
    Assert-DemoTest ($null -eq (Select-DemoNetwork @($vpn, (New-TestNetwork '169.254.1.2'), (New-TestNetwork '8.8.8.8')) '')) 'Virtual, APIPA and public addresses are rejected'
    Assert-DemoTest ($null -eq (Select-DemoNetwork $null '')) 'No network produces no candidate'
    $down = New-TestNetwork '10.0.0.20'; $down | Add-Member IsActive $false
    $disabled = New-TestNetwork '10.0.0.21'; $disabled | Add-Member Status 'Disconnected'
    Assert-DemoTest ($null -eq (Select-DemoNetwork @($down, $disabled) '')) 'Explicitly inactive/down adapters are rejected'

    Assert-DemoTest (Get-DemoHealthAssessment (New-TestHealth) 400).Ready 'Exact healthy authenticated LAN gateway is ready'
    Assert-DemoTest ((Get-DemoHealthAssessment $null 0).Code -eq 'unreachable') 'No health response is not ready'
    Assert-DemoTest ((Get-DemoHealthAssessment (New-TestHealth) 0).Code -eq 'auth-unchecked') 'Health alone does not establish session authentication'
    Assert-DemoTest ((Get-DemoHealthAssessment (New-TestHealth) 401).Code -eq 'token-mismatch') 'Saved token mismatch is distinguished'
    Assert-DemoTest ((Get-DemoHealthAssessment (New-TestHealth) 500).Code -eq 'auth-check-failed') 'Unexpected session response is not ready'
    foreach ($case in @(
        @{ Override = @{ service = 'Other service' }; Code = 'wrong-service' },
        @{ Override = @{ service = 'travelagent exa gateway' }; Code = 'wrong-service' },
        @{ Override = @{ status = 'error' }; Code = 'unhealthy' },
        @{ Override = @{ schemaVersion = '2' }; Code = 'unsupported-schema' },
        @{ Override = @{ authRequired = $false }; Code = 'authentication-disabled' },
        @{ Override = @{ authRequired = 'true' }; Code = 'authentication-disabled' },
        @{ Override = @{ loopbackOnly = $true }; Code = 'loopback-only' },
        @{ Override = @{ loopbackOnly = $null }; Code = 'loopback-only' },
        @{ Override = @{ exaConfigured = $false }; Code = 'missing-key' },
        @{ Override = @{ exaConfigured = 'true' }; Code = 'missing-key' }
    )) {
        $assessment = Get-DemoHealthAssessment (New-TestHealth $case.Override) 400
        Assert-DemoTest (-not $assessment.Ready -and $assessment.Code -eq $case.Code) ('Unsafe health state rejected: ' + $case.Code)
    }

    # All DPAPI tests use disposable fixture sessions, never the project's Artifacts.
    $newDirectory = Join-Path $script:demoTestDirectory 'new session with spaces'
    Assert-DemoTest ($null -eq (Get-DemoSession $newDirectory '10.0.0.10')) 'No-create mode returns no session when absent'
    Assert-DemoTest (-not (Test-Path -LiteralPath $newDirectory)) 'No-create mode has no filesystem side effect'
    $created = Get-DemoSession $newDirectory '10.0.0.10' 8788 -Create
    Assert-DemoTest (-not $created.Reused -and $created.Token -match '^[A-F0-9]{64}$') 'A new fixture session has a random 32-byte bearer'
    $tokenPath = Join-Path $newDirectory 'lan-session-token.dpapi'
    $metadataPath = Join-Path $newDirectory 'lan-gateway-session.json'
    $ciphertext = [IO.File]::ReadAllText($tokenPath)
    $originalMetadata = [IO.File]::ReadAllText($metadataPath)
    Assert-DemoTest (-not $ciphertext.Contains($created.Token) -and -not $originalMetadata.Contains($created.Token)) 'Neither persisted file contains the plaintext fixture bearer'
    $same = Get-DemoSession $newDirectory '10.0.0.10'
    Assert-DemoTest ($same.Reused -and -not $same.AddressChanged -and $same.Token -ceq $created.Token) 'Current Windows user can recover the same DPAPI session'
    $moved = Get-DemoSession $newDirectory '192.168.1.20'
    Assert-DemoTest ($moved.Reused -and $moved.AddressChanged -and $moved.PreviousHost -eq '10.0.0.10' -and $moved.Token -ceq $created.Token) 'Wi-Fi address changes preserve the saved phone bearer'
    Assert-DemoTest ([IO.File]::ReadAllText($metadataPath) -ceq $originalMetadata) 'Reading a changed-address session does not rewrite metadata'
    $changedPort = Get-DemoSession $newDirectory '10.0.0.10' 8789
    Assert-DemoTest ($changedPort.AddressChanged -and $changedPort.Token -ceq $created.Token) 'Route port changes are reported without token rotation'
    Save-DemoSessionMetadata $newDirectory '192.168.1.20' 8788 $true 4201
    $saved = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    Assert-DemoTest ($saved.host -eq '192.168.1.20' -and $saved.port -eq 8788 -and $saved.launcherProcessId -eq 4201 -and $saved.tokenReused) 'Explicit start metadata records its new route and process'
    Assert-DemoTest ([IO.File]::ReadAllText($tokenPath) -ceq $ciphertext) 'Metadata update never rotates or rewrites the encrypted token'
    Assert-DemoTest (-not ([IO.File]::ReadAllText($metadataPath)).Contains($created.Token)) 'Updated metadata remains bearer-free'

    $onlyToken = New-SessionCase 'token only'
    [IO.File]::WriteAllText((Join-Path $onlyToken 'lan-session-token.dpapi'), $ciphertext)
    Assert-DemoThrows { Get-DemoSession $onlyToken '10.0.0.10' -Create } 'Partial token-only state fails closed even with Create'
    Assert-DemoTest (-not (Test-Path -LiteralPath (Join-Path $onlyToken 'lan-gateway-session.json'))) 'Token-only state is not silently repaired'
    Assert-DemoThrows { Save-DemoSessionMetadata $onlyToken '10.0.0.10' 8788 $true 4201 } 'Metadata saver cannot repair an incomplete session'
    $onlyMetadata = New-SessionCase 'metadata only'
    [IO.File]::WriteAllText((Join-Path $onlyMetadata 'lan-gateway-session.json'), $originalMetadata)
    Assert-DemoThrows { Get-DemoSession $onlyMetadata '10.0.0.10' -Create } 'Partial metadata-only state fails closed'
    Assert-DemoTest (-not (Test-Path -LiteralPath (Join-Path $onlyMetadata 'lan-session-token.dpapi'))) 'Metadata-only state is not given a replacement bearer'
    $corruptMetadata = New-SessionCase 'corrupt metadata'
    [IO.File]::WriteAllText((Join-Path $corruptMetadata 'lan-session-token.dpapi'), $ciphertext)
    [IO.File]::WriteAllText((Join-Path $corruptMetadata 'lan-gateway-session.json'), '{broken')
    Assert-DemoThrows { Get-DemoSession $corruptMetadata '10.0.0.10' -Create } 'Malformed metadata cannot rotate a valid token'
    Assert-DemoTest ([IO.File]::ReadAllText((Join-Path $corruptMetadata 'lan-gateway-session.json')) -ceq '{broken') 'Malformed metadata remains unchanged'
    $corruptToken = New-SessionCase 'corrupt token'
    [IO.File]::WriteAllText((Join-Path $corruptToken 'lan-session-token.dpapi'), 'not a DPAPI blob')
    [IO.File]::WriteAllText((Join-Path $corruptToken 'lan-gateway-session.json'), $originalMetadata)
    Assert-DemoThrows { Get-DemoSession $corruptToken '10.0.0.10' -Create } 'Unreadable DPAPI state fails closed'
    Assert-DemoTest ([IO.File]::ReadAllText((Join-Path $corruptToken 'lan-session-token.dpapi')) -ceq 'not a DPAPI blob') 'Unreadable DPAPI data is not overwritten'
    $shortToken = New-SessionCase 'invalid bearer format'
    $shortProtected = ConvertTo-SecureString 'fixture-invalid' -AsPlainText -Force
    try { [IO.File]::WriteAllText((Join-Path $shortToken 'lan-session-token.dpapi'), (ConvertFrom-SecureString $shortProtected)) }
    finally { $shortProtected.Dispose() }
    [IO.File]::WriteAllText((Join-Path $shortToken 'lan-gateway-session.json'), $originalMetadata)
    Assert-DemoThrows { Get-DemoSession $shortToken '10.0.0.10' -Create } 'Decrypted values must still have the expected bearer format'
    Assert-DemoThrows { Get-DemoSession $newDirectory '8.8.8.8' -Create } 'Session operations reject a public bind address'
    Assert-DemoThrows { Get-DemoSession $newDirectory '10.0.0.10' 0 -Create } 'Session operations reject an invalid port'

    Write-Output ('Passed: ' + $script:demoTestCount + ' demo launcher checks. No gateway, network, firewall or real session files were changed.')
} finally {
    if (Test-Path -LiteralPath $script:demoTestDirectory) {
        $resolvedTestDirectory = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $script:demoTestDirectory).Path)
        $expectedTestDirectory = [IO.Path]::GetFullPath($script:demoTestDirectory)
        if ($resolvedTestDirectory -cne $expectedTestDirectory -or
            -not $resolvedTestDirectory.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolvedTestDirectory) -notlike 'TravelAgent-DemoLauncher-Test-*') {
            throw 'Refusing to remove an unexpected test directory.'
        }
        Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force
    }
}
