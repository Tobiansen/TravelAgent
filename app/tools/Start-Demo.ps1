[CmdletBinding()]
param(
    [switch]$CheckOnly,
    [switch]$Preview,
    [string]$PreviewImagePath = '',
    [ValidateRange(0,60)][int]$AutoCloseSeconds = 0
)

$ErrorActionPreference = 'Stop'
trap {
    if ($CheckOnly) { Write-Error 'The read-only laptop check could not complete.'; exit 1 }
    if ($Preview) { Write-Error $_; exit 1 }
    try {
        Add-Type -AssemblyName System.Windows.Forms
        [Windows.Forms.MessageBox]::Show('TravelAgent Demo could not open. Run the TravelAgent Demo shortcut again. If it still fails, see tools\Start-Demo.ps1 and ..\docs\SETUP.md. Saved access settings have not been reset.', 'TravelAgent Demo') | Out-Null
    } catch { }
    exit 1
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$sessionDirectory = Join-Path $projectRoot 'Artifacts'
. (Join-Path $PSScriptRoot 'DemoLauncher.Core.ps1')

function Get-DemoNetworks {
    $adapters = @(Get-NetAdapter -IncludeHidden -ErrorAction Stop | Where-Object { $_.Status -eq 'Up' -and $_.HardwareInterface })
    $routes = @(Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue)
    $addresses = @(Get-NetIPAddress -AddressFamily IPv4 -AddressState Preferred -ErrorAction Stop | Where-Object { -not $_.SkipAsSource })
    foreach ($adapter in $adapters) {
        foreach ($address in @($addresses | Where-Object { $_.InterfaceIndex -eq $adapter.ifIndex })) {
            $defaultRoutes = @($routes | Where-Object { $_.InterfaceIndex -eq $adapter.ifIndex })
            $metric = 10000
            if ($defaultRoutes.Count) { $metric = [int](($defaultRoutes | Measure-Object RouteMetric -Minimum).Minimum) }
            [pscustomobject]@{
                IPAddress = [string]$address.IPAddress; InterfaceIndex = [int]$adapter.ifIndex
                InterfaceAlias = [string]$adapter.Name; IsPhysical = $true
                IsWifi = ([string]$adapter.MediaType -eq 'Native 802.11' -or [string]$adapter.PhysicalMediaType -eq 'Native 802.11')
                HasDefaultRoute = ($defaultRoutes.Count -gt 0); Metric = $metric
            }
        }
    }
}

function Get-DemoPreferredAddress {
    try { return [string]((Get-Content -LiteralPath (Join-Path $sessionDirectory 'lan-gateway-session.json') -Raw | ConvertFrom-Json).host) }
    catch { return '' }
}

function Find-DemoPython {
    $candidates = @(
        (Join-Path $projectRoot 'Backend\.venv\Scripts\python.exe'),
        (Join-Path (Split-Path -Parent $projectRoot) 'Junction2026\.venv\Scripts\python.exe')
    )
    $node = Get-Command node.exe -ErrorAction SilentlyContinue
    if ($node) {
        $dependencyRoot = Split-Path (Split-Path (Split-Path $node.Source -Parent) -Parent) -Parent
        $candidates += Join-Path $dependencyRoot 'python\python.exe'
    }
    $candidates += @(Get-Command python.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate) -and $candidate -notlike '*\Microsoft\WindowsApps\python.exe') { return $candidate }
    }
    return $null
}

function Get-DemoExaKey {
    $key = [Environment]::GetEnvironmentVariable('EXA_API_KEY', 'Process')
    if ([string]::IsNullOrWhiteSpace($key)) { $key = [Environment]::GetEnvironmentVariable('EXA_API_KEY', 'User') }
    return $key
}

if ($CheckOnly) {
    $candidate = Select-DemoNetwork -Candidates @(Get-DemoNetworks) -PreferredAddress (Get-DemoPreferredAddress)
    $health = $null
    if ($candidate) {
        try { $health = Invoke-RestMethod -Uri ('http://{0}:8788/health' -f $candidate.IPAddress) -TimeoutSec 3 -MaximumRedirection 0 }
        catch { }
    }
    [ordered]@{
        startsGateway = $false; selectedAddress = $(if ($candidate) { $candidate.IPAddress } else { $null })
        interface = $(if ($candidate) { $candidate.InterfaceAlias } else { $null })
        pythonFound = [bool](Find-DemoPython); exaKeyPresent = -not [string]::IsNullOrWhiteSpace((Get-DemoExaKey))
        existingHealth = $health; savedTokenPresent = (Test-Path -LiteralPath (Join-Path $sessionDirectory 'lan-session-token.dpapi'))
        savedSessionDecryptedOrChanged = $false; paidExaRequest = $false
    } | ConvertTo-Json -Depth 4
    return
}
if (($AutoCloseSeconds -or $PreviewImagePath) -and -not $Preview) { throw 'Preview export and AutoCloseSeconds are supported only with Preview; they never start a gateway.' }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Net.Http
[Windows.Forms.Application]::EnableVisualStyles()
$mutex = New-Object Threading.Mutex($false, 'Local\TravelAgentDemoLauncher')
$hasMutex = $false
try { $hasMutex = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $hasMutex = $true }
if (-not $hasMutex) {
    [Windows.Forms.MessageBox]::Show('TravelAgent Demo is already open. Use its window on the taskbar.', 'TravelAgent Demo') | Out-Null
    $mutex.Dispose()
    return
}

$script:demoProcess = $null
$script:outputTask = $null
$script:errorTask = $null
$script:healthTask = $null
$script:healthResponse = $null
$script:healthRequest = $null
$script:healthCancellation = $null
$script:healthPhase = ''
$script:healthObject = $null
$script:authVerified = $false
$script:authExpiresAt = [DateTime]::MinValue
$script:authSession = $null
$script:address = ''
$script:gatewayUrl = ''
$script:retryCount = 0
$script:failures = 0
$script:nextAttempt = [DateTime]::UtcNow
$script:nextHealth = [DateTime]::UtcNow
$script:nextNetwork = [DateTime]::UtcNow
$script:healthySince = $null
$script:paused = $false
$script:checking = $false
$script:lastCode = ''
$script:lastMessage = ''
$script:lastKey = $null
$script:pythonPath = $null
$script:createdAt = [DateTime]::UtcNow
$httpHandler = New-Object Net.Http.HttpClientHandler
$httpHandler.UseProxy = $false
$httpHandler.AllowAutoRedirect = $false
$httpHandler.UseCookies = $false
$script:http = New-Object Net.Http.HttpClient($httpHandler)
$script:http.Timeout = [TimeSpan]::FromSeconds(3)
$script:http.MaxResponseContentBufferSize = 4096

$background = [Drawing.ColorTranslator]::FromHtml('#101116')
$panelColor = [Drawing.ColorTranslator]::FromHtml('#202129')
$textColor = [Drawing.ColorTranslator]::FromHtml('#F5F4F6')
$mutedColor = [Drawing.ColorTranslator]::FromHtml('#B8B8C3')
$red = [Drawing.ColorTranslator]::FromHtml('#FF304E')
$green = [Drawing.ColorTranslator]::FromHtml('#7FE1AE')
$form = New-Object Windows.Forms.Form
$form.Text = 'TravelAgent Demo'
$form.Size = New-Object Drawing.Size(790,690)
$form.MinimumSize = New-Object Drawing.Size(790,690)
$form.MaximumSize = New-Object Drawing.Size(790,690)
$form.StartPosition = 'CenterScreen'
$form.BackColor = $background
$form.ForeColor = $textColor
$form.Font = New-Object Drawing.Font('Segoe UI',11)
$form.MaximizeBox = $false
$form.AutoScaleMode = 'Dpi'

function New-DemoLabel([string]$Text, [int]$X, [int]$Y, [int]$Width, [int]$Height, [int]$Size = 11, [bool]$Bold = $false) {
    $label = New-Object Windows.Forms.Label
    $label.Text = $Text; $label.Location = New-Object Drawing.Point($X,$Y); $label.Size = New-Object Drawing.Size($Width,$Height)
    $style = [Drawing.FontStyle]::Regular; if ($Bold) { $style = [Drawing.FontStyle]::Bold }
    $label.Font = New-Object Drawing.Font('Segoe UI',$Size,$style)
    $label.ForeColor = $textColor
    $form.Controls.Add($label)
    return $label
}

function New-DemoButton([string]$Text, [int]$X, [int]$Y, [int]$Width, [bool]$Primary = $false) {
    $button = New-Object Windows.Forms.Button
    $button.Text = $Text; $button.Location = New-Object Drawing.Point($X,$Y); $button.Size = New-Object Drawing.Size($Width,46)
    $button.FlatStyle = 'Flat'; $button.FlatAppearance.BorderSize = 0
    $button.BackColor = $panelColor; if ($Primary) { $button.BackColor = $red }
    $button.ForeColor = $textColor; $button.Font = New-Object Drawing.Font('Segoe UI',11,[Drawing.FontStyle]::Bold)
    $form.Controls.Add($button)
    return $button
}

$null = New-DemoLabel 'TRAVEL AGENT' 28 24 650 28 16 $true
$subtitle = New-DemoLabel 'Laptop connection for Exa' 28 61 700 26 12
$subtitle.ForeColor = $mutedColor
$statusLabel = New-DemoLabel 'Checking connection...' 28 113 710 44 24 $true
$detailLabel = New-DemoLabel 'Finding this laptop on your Wi-Fi.' 28 169 710 69 11
$detailLabel.ForeColor = $mutedColor
$null = New-DemoLabel 'PHONE SERVICE ADDRESS' 28 257 640 26 10 $true
$addressBox = New-Object Windows.Forms.TextBox
$addressBox.Location = New-Object Drawing.Point(28,290); $addressBox.Size = New-Object Drawing.Size(528,40)
$addressBox.Font = New-Object Drawing.Font('Segoe UI',15)
$addressBox.BorderStyle = 'FixedSingle'; $addressBox.ReadOnly = $true
$addressBox.BackColor = $panelColor; $addressBox.ForeColor = $textColor
$addressBox.Text = 'Waiting for Wi-Fi...'
$form.Controls.Add($addressBox)
$copyButton = New-DemoButton 'Copy address' 576 286 160
$recoverButton = New-DemoButton 'Check / recover' 28 359 230 $true
$stopButton = New-DemoButton 'Stop connection' 274 359 220
$helpButton = New-DemoButton 'Connection help' 510 359 226
$notes = New-DemoLabel "Keep this window open; minimizing is fine.`r`nThe phone and laptop must use the same Wi-Fi." 28 439 704 59 12 $true
$guideLabel = New-DemoLabel "If Wi-Fi changes, paste the address above into the phone's`r`nBefore > Trip details > Connection settings. Its saved token stays valid." 28 515 710 57 11
$guideLabel.ForeColor = $mutedColor
$footer = New-DemoLabel 'Bluetooth connects directly to the bag. This window supports Exa only.' 28 605 710 24 10
$footer.ForeColor = $mutedColor

function Set-DemoStatus([string]$Code, [string]$Title, [string]$Message, [bool]$Ready = $false) {
    $statusLabel.Text = $Title
    $statusLabel.ForeColor = $red; if ($Ready) { $statusLabel.ForeColor = $green }
    $detailLabel.Text = $Message
    if ($script:lastCode -ne $Code -or $script:lastMessage -ne $Message) {
        if (-not $Preview) {
            try {
                [IO.Directory]::CreateDirectory($sessionDirectory) | Out-Null
                [IO.File]::AppendAllText((Join-Path $sessionDirectory 'demo-launcher.log'), ([DateTime]::UtcNow.ToString('o') + ' ' + $Code + ' ' + $script:gatewayUrl + [Environment]::NewLine))
                [ordered]@{ recordedAtUtc = [DateTime]::UtcNow.ToString('o'); code = $Code; title = $Title; message = $Message; ready = $Ready; phoneAddress = $script:gatewayUrl; launcherProcessId = $PID; ownsGateway = ($null -ne $script:demoProcess) } |
                    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sessionDirectory 'demo-launcher-status.json') -Encoding UTF8
            } catch { }
        }
        $script:lastCode = $Code; $script:lastMessage = $Message
    }
}

function Stop-DemoOwnedProcess {
    # The live Process object is authoritative. Never terminate a saved PID or an existing listener.
    if ($null -ne $script:demoProcess) {
        try { if (-not $script:demoProcess.HasExited) { $script:demoProcess.Kill(); $script:demoProcess.WaitForExit(2000) | Out-Null } } catch { }
        $script:demoProcess.Dispose(); $script:demoProcess = $null
    }
    $script:outputTask = $null; $script:errorTask = $null; $script:authVerified = $false
    $stopButton.Enabled = $false
}

function Clear-DemoHealth {
    if ($script:healthCancellation) { $script:healthCancellation.Cancel() }
    if ($script:healthTask -and $script:healthTask.IsCompleted -and -not $script:healthTask.IsFaulted -and -not $script:healthTask.IsCanceled -and -not $script:healthResponse) {
        $completedResponse = $script:healthTask.GetAwaiter().GetResult()
        if ($completedResponse) { $completedResponse.Dispose() }
    }
    if ($script:healthResponse) { $script:healthResponse.Dispose(); $script:healthResponse = $null }
    if ($script:healthRequest) { $script:healthRequest.Dispose(); $script:healthRequest = $null }
    if ($script:healthCancellation) { $script:healthCancellation.Dispose(); $script:healthCancellation = $null }
    $script:healthTask = $null; $script:healthPhase = ''
}

function Start-DemoHealth {
    $script:healthCancellation = New-Object Threading.CancellationTokenSource
    $script:healthRequest = New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Get, ($script:gatewayUrl + '/health'))
    $script:healthTask = $script:http.SendAsync($script:healthRequest,$script:healthCancellation.Token)
    $script:healthPhase = 'health'
}

function Start-DemoAuthProbe {
    try { $script:authSession = Get-DemoSession -Directory $sessionDirectory -HostAddress $script:address -Port 8788 }
    catch {
        Set-DemoStatus 'session-error' 'Saved connection needs attention' 'The encrypted access settings could not be opened. No settings were replaced. See Connection help.'
        $script:nextHealth = [DateTime]::UtcNow.AddSeconds(15); return
    }
    if (-not $script:authSession) {
        Set-DemoStatus 'session-missing' 'Different gateway is running' 'Close its old server window, then choose Check / recover. This launcher will create its own saved connection.'
        $script:nextHealth = [DateTime]::UtcNow.AddSeconds(15); return
    }
    $script:healthRequest = New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Post, ($script:gatewayUrl + '/api/claims/research'))
    $script:healthCancellation = New-Object Threading.CancellationTokenSource
    $script:healthRequest.Headers.Authorization = New-Object Net.Http.Headers.AuthenticationHeaderValue('Bearer',$script:authSession.Token)
    # Empty context is rejected before research. This checks authorization without an Exa request.
    $script:healthRequest.Content = New-Object Net.Http.StringContent('{}',[Text.Encoding]::UTF8,'application/json')
    $script:healthTask = $script:http.SendAsync($script:healthRequest,$script:healthCancellation.Token)
    $script:healthPhase = 'auth'
}

function Start-DemoOwnedProcess {
    $listeners = @(Get-NetTCPConnection -LocalPort 8788 -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalAddress -eq $script:address -or $_.LocalAddress -eq '0.0.0.0' -or $_.LocalAddress -eq '::' })
    if ($listeners.Count) {
        Set-DemoStatus 'port-in-use' 'Another connection is running' 'This launcher will not replace another program. Close an old gateway window if it is stuck, then choose Check / recover.'
        $script:nextAttempt = [DateTime]::UtcNow.AddSeconds(15); return
    }
    if ($script:retryCount -ge 3) {
        Set-DemoStatus 'retry-limit' 'Connection needs a check' 'Automatic recovery paused after three attempts. Check Wi-Fi, then choose Check / recover to try again.'
        return
    }
    $script:pythonPath = Find-DemoPython
    if (-not $script:pythonPath) {
        Set-DemoStatus 'python-missing' 'Python is missing' 'The laptop Python installation could not be found. Connection help explains which installation this launcher uses.'
        $script:nextAttempt = [DateTime]::UtcNow.AddSeconds(30); return
    }
    $key = Get-DemoExaKey
    if ([string]::IsNullOrWhiteSpace($key)) {
        Set-DemoStatus 'key-missing' 'Exa key is missing' 'The saved EXA_API_KEY is missing for this Windows user. The bag and Bluetooth still work; Exa needs its laptop key restored.'
        $script:nextAttempt = [DateTime]::UtcNow.AddSeconds(30); return
    }
    $session = $null
    try { $session = Get-DemoSession -Directory $sessionDirectory -HostAddress $script:address -Port 8788 -Create }
    catch {
        Set-DemoStatus 'session-error' 'Saved connection needs attention' 'The encrypted access settings could not be opened. No settings were replaced. See Connection help.'
        $script:nextAttempt = [DateTime]::UtcNow.AddSeconds(30); $key = $null; return
    }
    $info = $null
    $process = $null
    try {
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = $script:pythonPath
        $info.Arguments = '"' + (Join-Path $projectRoot 'Backend\server.py') + '" --host ' + $script:address + ' --port 8788'
        $info.WorkingDirectory = $projectRoot
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        $info.EnvironmentVariables['EXA_API_KEY'] = $key
        $info.EnvironmentVariables['TRAVELAGENT_DEMO_TOKEN'] = $session.Token
        $info.EnvironmentVariables['PYTHONUNBUFFERED'] = '1'
        $process = New-Object Diagnostics.Process
        $process.StartInfo = $info
        if (-not $process.Start()) { throw 'Process did not start.' }
        $script:demoProcess = $process
        $script:outputTask = $process.StandardOutput.ReadLineAsync()
        $script:errorTask = $process.StandardError.ReadLineAsync()
        Save-DemoSessionMetadata -Directory $sessionDirectory -HostAddress $script:address -Port 8788 -TokenReused $session.Reused -LauncherProcessId $PID
        $script:retryCount++; $script:failures = 0; $script:authVerified = $false; $script:healthySince = $null
        $script:nextHealth = [DateTime]::UtcNow.AddSeconds(1)
        $script:nextAttempt = [DateTime]::UtcNow.AddSeconds(10)
        $stopButton.Enabled = $true
        Set-DemoStatus 'starting' 'Starting connection...' 'The laptop is starting its Exa gateway. Your saved phone access token is preserved.'
    } catch {
        if ($process -and -not [object]::ReferenceEquals($process,$script:demoProcess)) { $process.Dispose() }
        Stop-DemoOwnedProcess
        $script:retryCount++; $script:nextAttempt = [DateTime]::UtcNow.AddSeconds(10 * $script:retryCount)
        Set-DemoStatus 'start-error' 'Could not start the connection' 'Windows could not start the gateway. Choose Connection help, then Check / recover after correcting the problem.'
    } finally {
        $key = $null; $session = $null
        if ($info) { $info.EnvironmentVariables.Remove('EXA_API_KEY'); $info.EnvironmentVariables.Remove('TRAVELAGENT_DEMO_TOKEN') }
    }
}

function Receive-DemoHealth {
    if (-not $script:healthTask -or -not $script:healthTask.IsCompleted) { return }
    $phase = $script:healthPhase
    try {
        $script:healthResponse = $script:healthTask.GetAwaiter().GetResult()
        if ($phase -eq 'health') {
            if (-not $script:healthResponse.IsSuccessStatusCode) { throw 'Health request failed.' }
            $body = $script:healthResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ($body.Length -gt 4096) { throw 'Unexpected health response.' }
            $script:healthObject = $body | ConvertFrom-Json
            if ([DateTime]::UtcNow -ge $script:authExpiresAt) { $script:authVerified = $false }
            $assessment = Get-DemoHealthAssessment -Health $script:healthObject -AuthStatus $(if ($script:authVerified) {400} else {0})
            Clear-DemoHealth
            if (-not $script:authVerified -and $assessment.Code -eq 'auth-unchecked') { Start-DemoAuthProbe; return }
        } else {
            $authStatus = [int]$script:healthResponse.StatusCode
            $assessment = Get-DemoHealthAssessment -Health $script:healthObject -AuthStatus $authStatus
            Clear-DemoHealth
            $script:authVerified = $assessment.Ready
            if ($assessment.Ready) { $script:authExpiresAt = [DateTime]::UtcNow.AddSeconds(60) }
            $script:authSession = $null
        }
        $script:nextHealth = [DateTime]::UtcNow.AddSeconds(5)
        if ($assessment.Ready) {
            $script:failures = 0
            if (-not $script:healthySince) { $script:healthySince = [DateTime]::UtcNow }
            if (([DateTime]::UtcNow - $script:healthySince).TotalSeconds -ge 30) { $script:retryCount = 0 }
            $detail = 'Gateway running; Exa key loaded. Airline research runs when you tap Exa on the phone.'
            if (-not $script:demoProcess) { $detail = 'Using the existing gateway. If its old window closes, this launcher will recover the connection.' }
            Set-DemoStatus 'ready' 'Laptop connection ready' $detail $true
        } else {
            $script:healthySince = $null
            $script:nextHealth = [DateTime]::UtcNow.AddSeconds(15)
            if ($phase -eq 'auth' -and $authStatus -eq 429) { $script:nextHealth = [DateTime]::UtcNow.AddSeconds(65) }
            Set-DemoStatus $assessment.Code 'Connection needs attention' $assessment.Message
        }
    } catch {
        Clear-DemoHealth
        $script:authSession = $null; $script:authVerified = $false; $script:healthySince = $null
        $script:failures++; $script:nextHealth = [DateTime]::UtcNow.AddSeconds(3)
        if ($script:failures -ge 3 -and $script:demoProcess) { Stop-DemoOwnedProcess }
        if (-not $script:demoProcess -and [DateTime]::UtcNow -ge $script:nextAttempt) { Start-DemoOwnedProcess }
        elseif ($script:demoProcess) { Set-DemoStatus 'checking' 'Checking connection...' 'Waiting for the laptop gateway. Automatic recovery is enabled.' }
    }
}

function Reset-DemoRecovery {
    # An explicit retry reloads configuration for our own process. Existing gateways remain untouched.
    Clear-DemoHealth
    Stop-DemoOwnedProcess
    $script:paused = $false; $script:retryCount = 0; $script:failures = 0
    $script:nextHealth = [DateTime]::UtcNow; $script:nextNetwork = [DateTime]::UtcNow; $script:nextAttempt = [DateTime]::UtcNow
    $script:authVerified = $false
    Set-DemoStatus 'checking' 'Checking connection...' 'Checking Wi-Fi and the laptop gateway. Your saved phone access token is preserved.'
}

$copyButton.Add_Click({
    if ($script:gatewayUrl) {
        try { [Windows.Forms.Clipboard]::SetText($script:gatewayUrl); $copyButton.Text = 'Copied' } catch { $addressBox.SelectAll(); $addressBox.Focus() }
    }
})
$recoverButton.Add_Click({
    if ($Preview) { return }
    Reset-DemoRecovery
})
$stopButton.Add_Click({
    if (-not $script:demoProcess) { return }
    $script:paused = $true
    Clear-DemoHealth
    Stop-DemoOwnedProcess
    Set-DemoStatus 'stopped' 'Laptop connection stopped' 'Choose Check / recover to start it again. Bluetooth and the bag are independent.'
})
$helpButton.Add_Click({
    $help = "1. Keep the phone and laptop on the same trusted Wi-Fi. Some guest networks block devices from reaching each other; a phone hotspot is a useful alternative.`r`n`r`n2. Copy the address shown here into TravelAgent: Journey > Before > Trip details > Connection settings. An address change does not change your saved access token.`r`n`r`n3. If Windows asks for Python network access, allow it on your demo network. This launcher never disables the firewall.`r`n`r`n4. Close a stuck older gateway window and choose Check / recover. This launcher only stops processes it started.`r`n`r`n5. Keep the laptop awake. A missing key or damaged encrypted settings cannot be repaired by restarting. The local setup guide is ..\docs\SETUP.md. No key or access token is shown in this window."
    [Windows.Forms.MessageBox]::Show($help,'TravelAgent connection help',[Windows.Forms.MessageBoxButtons]::OK,[Windows.Forms.MessageBoxIcon]::Information) | Out-Null
})

$stopButton.Enabled = $false
$copyButton.Enabled = $false
if ($Preview) {
    $addressBox.Text = 'Preview - no connection started'
    $recoverButton.Enabled = $false
    Set-DemoStatus 'preview' 'Preview only' 'This layout preview does not read credentials, contact the gateway or start a server.'
}

$timer = New-Object Windows.Forms.Timer
$timer.Interval = 500
$timer.Add_Tick({
    if ($script:checking) { return }
    $script:checking = $true
    try {
        $now = [DateTime]::UtcNow
        if ($Preview) {
            if ($AutoCloseSeconds -and ($now - $script:createdAt).TotalSeconds -ge $AutoCloseSeconds) { $form.Close() }
            return
        }
        # Drain child pipes so request logging can never block the gateway. Do not retain arbitrary output.
        foreach ($field in @('outputTask','errorTask')) {
            $task = Get-Variable -Scope Script -Name $field -ValueOnly
            $drained = 0
            while ($task -and $task.IsCompleted -and $drained -lt 20) {
                if ($task.IsFaulted -or $task.IsCanceled) { $task = $null; break }
                $line = $task.GetAwaiter().GetResult(); $drained++
                if ($null -eq $line -or -not $script:demoProcess) { $task = $null; break }
                if ($field -eq 'outputTask') { $task = $script:demoProcess.StandardOutput.ReadLineAsync() }
                else { $task = $script:demoProcess.StandardError.ReadLineAsync() }
            }
            Set-Variable -Scope Script -Name $field -Value $task
        }
        if ($script:demoProcess -and $script:demoProcess.HasExited) {
            Stop-DemoOwnedProcess
            $script:nextAttempt = $now.AddSeconds([Math]::Min(30,5 * [Math]::Max(1,$script:retryCount)))
            Set-DemoStatus 'exited' 'Recovering connection...' 'The gateway stopped. This launcher will retry automatically.'
        }
        if ($now -ge $script:nextNetwork) {
            $script:nextNetwork = $now.AddSeconds(15)
            $preferred = $script:address
            if (-not $preferred) { $preferred = Get-DemoPreferredAddress }
            $network = Select-DemoNetwork -Candidates @(Get-DemoNetworks) -PreferredAddress $preferred
            $newAddress = ''; if ($network) { $newAddress = [string]$network.IPAddress }
            if ($newAddress -ne $script:address) {
                Clear-DemoHealth
                Stop-DemoOwnedProcess
                $script:address = $newAddress; $script:retryCount = 0; $script:authVerified = $false
                $script:nextHealth = $now; $script:nextAttempt = $now
                $script:gatewayUrl = ''; if ($newAddress) { $script:gatewayUrl = 'http://' + $newAddress + ':8788' }
                $addressBox.Text = $script:gatewayUrl; $copyButton.Text = 'Copy address'
                $copyButton.Enabled = [bool]$script:gatewayUrl
                Set-DemoStatus 'network-change' 'Checking Wi-Fi connection...' 'If the phone service address changed, copy the new address into its connection settings. The saved token stays valid.'
            }
        }
        if (-not $script:address) {
            $addressBox.Text = 'Waiting for Wi-Fi...'
            Set-DemoStatus 'no-network' 'Connect the laptop to Wi-Fi' 'No active private Wi-Fi or Ethernet address was found. Connect the phone and laptop to the same network.'
            return
        }
        if ($script:paused) { return }
        Receive-DemoHealth
        if (-not $script:healthTask -and $now -ge $script:nextHealth) { Start-DemoHealth }
    } catch {
        Set-DemoStatus 'check-error' 'Connection check needs a retry' 'Windows could not complete the check. Choose Check / recover; your saved connection settings have not been replaced.'
        $script:nextNetwork = [DateTime]::UtcNow.AddSeconds(10)
    } finally { $script:checking = $false }
})
$form.Add_FormClosing({ $timer.Stop(); Clear-DemoHealth; Stop-DemoOwnedProcess })
if ($Preview -and $PreviewImagePath) {
    # Render our own fixture controls; this is not a desktop screenshot or a live readiness result.
    $form.Add_Shown({
        $bitmap = New-Object Drawing.Bitmap($form.Width,$form.Height)
        try {
            $form.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$form.Width,$form.Height)))
            $bitmap.Save([IO.Path]::GetFullPath($PreviewImagePath),[Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
    })
}
try {
    $timer.Start()
    [Windows.Forms.Application]::Run($form)
} finally {
    $timer.Dispose(); Clear-DemoHealth; Stop-DemoOwnedProcess
    $script:http.Dispose(); $httpHandler.Dispose()
    $script:authSession = $null; $script:lastKey = $null
    if ($hasMutex) { $mutex.ReleaseMutex() }
    $mutex.Dispose(); $form.Dispose()
}
