# Shared, UI-free helpers for the double-click demo launcher. Windows PowerShell 5.1.
# Loading this file never starts a process, binds a listener or opens credentials.

function Get-DemoProperty {
    param([object]$Value, [string]$Name)
    if ($null -eq $Value) { return $null }
    if ($Value -is [Collections.IDictionary]) { return $Value[$Name] }
    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Test-DemoPrivateIpv4 {
    param([AllowNull()][string]$Address)
    if ([string]::IsNullOrWhiteSpace($Address) -or $Address -notmatch '^\d{1,3}(\.\d{1,3}){3}$') { return $false }
    $parsed = $null
    if (-not [Net.IPAddress]::TryParse($Address, [ref]$parsed) -or
        $parsed.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or
        $parsed.ToString() -cne $Address) { return $false }
    $bytes = $parsed.GetAddressBytes()
    return ($bytes[0] -eq 10 -or
        ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or
        ($bytes[0] -eq 192 -and $bytes[1] -eq 168))
}

function Select-DemoNetwork {
    param([AllowNull()][object[]]$Candidates, [string]$PreferredAddress = '')
    $ranked = @(
        foreach ($candidate in $Candidates) {
            $physical = Get-DemoProperty $candidate 'IsPhysical'
            $address = [string](Get-DemoProperty $candidate 'IPAddress')
            if ($physical -isnot [bool] -or -not $physical -or -not (Test-DemoPrivateIpv4 $address)) { continue }
            # The caller enumerates active adapters. Honour explicit activity fields too.
            $active = Get-DemoProperty $candidate 'IsActive'
            if ($null -ne $active -and ($active -isnot [bool] -or -not $active)) { continue }
            $status = Get-DemoProperty $candidate 'Status'
            if ($null -ne $status -and [string]$status -ine 'Up') { continue }
            $wifi = (Get-DemoProperty $candidate 'IsWifi') -is [bool] -and (Get-DemoProperty $candidate 'IsWifi')
            $default = (Get-DemoProperty $candidate 'HasDefaultRoute') -is [bool] -and (Get-DemoProperty $candidate 'HasDefaultRoute')
            $rank = 4
            if ($address -ceq $PreferredAddress -and $default) { $rank = 0 }
            elseif ($wifi -and $default) { $rank = 1 }
            elseif ($default) { $rank = 2 }
            elseif ($wifi) { $rank = 3 }
            $metric = [double]::PositiveInfinity
            $rawMetric = Get-DemoProperty $candidate 'Metric'
            if ($null -ne $rawMetric) {
                try {
                    $parsedMetric = [double]$rawMetric
                    if (-not [double]::IsNaN($parsedMetric) -and -not [double]::IsInfinity($parsedMetric) -and $parsedMetric -ge 0) { $metric = $parsedMetric }
                } catch { }
            }
            $index = [int]::MaxValue
            try { if ($null -ne (Get-DemoProperty $candidate 'InterfaceIndex')) { $index = [int](Get-DemoProperty $candidate 'InterfaceIndex') } } catch { }
            [pscustomobject]@{ Candidate = $candidate; Rank = $rank; Metric = $metric; InterfaceIndex = $index; Address = $address }
        }
    )
    if ($ranked.Count -eq 0) { return $null }
    return ($ranked | Sort-Object Rank, Metric, InterfaceIndex, Address | Select-Object -First 1).Candidate
}

function Get-DemoHealthAssessment {
    param([AllowNull()][object]$Health, [int]$AuthStatus = 0)
    $ready = $false
    $code = 'unreachable'
    $message = 'The laptop gateway is not responding. Use Start / Recover.'
    if ($null -ne $Health) {
        if ((Get-DemoProperty $Health 'service') -cne 'TravelAgent Exa gateway') {
            $code = 'wrong-service'; $message = 'This port belongs to another service. Close that service or use the original gateway window.'
        } elseif ((Get-DemoProperty $Health 'status') -cne 'ok') {
            $code = 'unhealthy'; $message = 'The gateway responded but is not healthy. Use Start / Recover.'
        } elseif ([string](Get-DemoProperty $Health 'schemaVersion') -cne '1') {
            $code = 'unsupported-schema'; $message = 'This is a different gateway version. Close its original window, then use Start / Recover.'
        } elseif ((Get-DemoProperty $Health 'authRequired') -isnot [bool] -or -not (Get-DemoProperty $Health 'authRequired')) {
            $code = 'authentication-disabled'; $message = 'This gateway is missing session authentication. Close its original window, then use Start / Recover.'
        } elseif ((Get-DemoProperty $Health 'loopbackOnly') -isnot [bool] -or (Get-DemoProperty $Health 'loopbackOnly')) {
            $code = 'loopback-only'; $message = 'This gateway is not configured for the phone over Wi-Fi. Use Start / Recover.'
        } elseif ((Get-DemoProperty $Health 'exaConfigured') -isnot [bool] -or -not (Get-DemoProperty $Health 'exaConfigured')) {
            $code = 'missing-key'; $message = 'The laptop Exa key is missing. Restore the configured EXA_API_KEY, then restart the gateway.'
        } elseif ($AuthStatus -eq 401) {
            $code = 'token-mismatch'; $message = 'The running gateway uses a different session token. Close its original window, then use Start / Recover.'
        } elseif ($AuthStatus -eq 0) {
            $code = 'auth-unchecked'; $message = 'Gateway found. Checking the saved session.'
        } elseif ($AuthStatus -ne 400) {
            $code = 'auth-check-failed'; $message = 'The saved-session check did not complete. Retry Start / Recover after a short wait.'
        } else {
            $ready = $true; $code = 'ready'; $message = 'Laptop gateway ready. Keep this window open and the phone on the same Wi-Fi. Exa research is checked when requested in the app.'
        }
    }
    return [pscustomobject]@{ Ready = $ready; Code = $code; Message = $message }
}

function Assert-DemoSessionLocation {
    param([string]$Directory, [string]$HostAddress, [int]$Port)
    if ([string]::IsNullOrWhiteSpace($Directory)) { throw 'Choose a folder for the encrypted demo session.' }
    if (-not (Test-DemoPrivateIpv4 $HostAddress)) { throw 'Choose a private IPv4 address assigned to this laptop.' }
    if ($Port -lt 1 -or $Port -gt 65535) { throw 'The gateway port must be between 1 and 65535.' }
    return [IO.Path]::GetFullPath($Directory)
}

function New-DemoSessionMetadata {
    param([string]$HostAddress, [int]$Port, [bool]$TokenReused, [int]$LauncherProcessId)
    return [ordered]@{
        launcherProcessId = $LauncherProcessId
        host = $HostAddress
        port = $Port
        tokenFile = 'Artifacts/lan-session-token.dpapi'
        tokenReused = $TokenReused
        startedAtUtc = [DateTime]::UtcNow.ToString('o')
    }
}

function Get-DemoSession {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$HostAddress,
        [int]$Port = 8788,
        [switch]$Create
    )
    $directoryPath = Assert-DemoSessionLocation $Directory $HostAddress $Port
    $tokenPath = Join-Path $directoryPath 'lan-session-token.dpapi'
    $metadataPath = Join-Path $directoryPath 'lan-gateway-session.json'
    $hasToken = Test-Path -LiteralPath $tokenPath
    $hasMetadata = Test-Path -LiteralPath $metadataPath
    if ($hasToken -xor $hasMetadata) {
        throw 'The saved gateway session is incomplete. Its files were left unchanged. Restore the matching session files before continuing.'
    }
    if (-not $hasToken) {
        if (-not $Create) { return $null }
        [void][IO.Directory]::CreateDirectory($directoryPath)
        $random = [Security.Cryptography.RandomNumberGenerator]::Create()
        $bytes = [byte[]]::new(32)
        $protected = $null
        $tokenTemp = Join-Path $directoryPath ('session-token-' + [guid]::NewGuid().ToString('N') + '.tmp')
        $metadataTemp = Join-Path $directoryPath ('session-metadata-' + [guid]::NewGuid().ToString('N') + '.tmp')
        try {
            $random.GetBytes($bytes)
            $token = [BitConverter]::ToString($bytes).Replace('-', '')
            $protected = ConvertTo-SecureString $token -AsPlainText -Force
            $encrypted = ConvertFrom-SecureString $protected
            [IO.File]::WriteAllText($tokenTemp, $encrypted, [Text.Encoding]::ASCII)
            $metadata = New-DemoSessionMetadata $HostAddress $Port $false $PID
            [IO.File]::WriteAllText($metadataTemp, ($metadata | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
            # Do not overwrite another launcher or a partial session created concurrently.
            [IO.File]::Move($tokenTemp, $tokenPath)
            [IO.File]::Move($metadataTemp, $metadataPath)
            return [pscustomobject]@{ Token = $token; Reused = $false; PreviousHost = ''; AddressChanged = $false }
        } finally {
            $random.Dispose()
            [Array]::Clear($bytes, 0, $bytes.Length)
            if ($null -ne $protected) { $protected.Dispose() }
            if (Test-Path -LiteralPath $tokenTemp -PathType Leaf) { Remove-Item -LiteralPath $tokenTemp }
            if (Test-Path -LiteralPath $metadataTemp -PathType Leaf) { Remove-Item -LiteralPath $metadataTemp }
        }
    }
    $protected = $null
    try {
        if (-not (Test-Path -LiteralPath $tokenPath -PathType Leaf) -or -not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
            throw 'Invalid session files.'
        }
        $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
        $oldHost = [string](Get-DemoProperty $metadata 'host')
        $oldPort = Get-DemoProperty $metadata 'port'
        if (-not (Test-DemoPrivateIpv4 $oldHost) -or $null -eq $oldPort -or
            [string]$oldPort -notmatch '^\d{1,5}$' -or [int]$oldPort -lt 1 -or [int]$oldPort -gt 65535) {
            throw 'Invalid session metadata.'
        }
        $protected = ConvertTo-SecureString ((Get-Content -LiteralPath $tokenPath -Raw).Trim())
        $token = [Net.NetworkCredential]::new('', $protected).Password
        if ($token -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid saved token format.' }
        # A LAN address identifies the route, not a new owner/session. Keep the
        # phone's encrypted bearer valid when DHCP changes this laptop's address.
        return [pscustomobject]@{ Token = $token; Reused = $true; PreviousHost = $oldHost; AddressChanged = ($oldHost -cne $HostAddress -or [int]$oldPort -ne $Port) }
    } catch {
        throw 'The saved encrypted gateway session could not be opened. Its files were left unchanged. Restore the matching session files for this Windows user.'
    } finally {
        if ($null -ne $protected) { $protected.Dispose() }
    }
}

function Save-DemoSessionMetadata {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$HostAddress,
        [int]$Port = 8788,
        [Parameter(Mandatory)][bool]$TokenReused,
        [Parameter(Mandatory)][int]$LauncherProcessId
    )
    $directoryPath = Assert-DemoSessionLocation $Directory $HostAddress $Port
    # Fail closed for missing, partial, corrupt or wrong-user sessions. Metadata
    # updates must never silently establish or repair a different credential.
    $session = Get-DemoSession -Directory $directoryPath -HostAddress $HostAddress -Port $Port
    if ($null -eq $session) { throw 'Create the encrypted demo session before saving its metadata.' }
    $session.Token = $null
    $metadataPath = Join-Path $directoryPath 'lan-gateway-session.json'
    $temporary = Join-Path $directoryPath ('session-metadata-' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $metadata = New-DemoSessionMetadata $HostAddress $Port $TokenReused $LauncherProcessId
        [IO.File]::WriteAllText($temporary, ($metadata | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $metadataPath -Force
    } finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) { Remove-Item -LiteralPath $temporary }
    }
}
