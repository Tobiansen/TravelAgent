[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
. (Join-Path $PSScriptRoot 'DemoLauncher.Core.ps1')

# Extract function declarations only. Never execute the launcher's top-level
# UI, credential lookup, network enumeration, timer or process setup.
$sourcePath = Join-Path $PSScriptRoot 'Start-Demo.ps1'
$parseTokens = $null; $parseErrors = $null
$sourceAst = [Management.Automation.Language.Parser]::ParseFile($sourcePath, [ref]$parseTokens, [ref]$parseErrors)
if (@($parseErrors).Count) { throw 'Start-Demo.ps1 has PowerShell syntax errors.' }
$requiredFunctions = @('Stop-DemoOwnedProcess','Clear-DemoHealth','Start-DemoHealth','Start-DemoAuthProbe','Start-DemoOwnedProcess','Receive-DemoHealth','Reset-DemoRecovery')
foreach ($name in $requiredFunctions) {
    $declaration = $sourceAst.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $true) | Where-Object { $_.Name -eq $name } | Select-Object -First 1
    if (-not $declaration) { throw ('Missing supervisor function: ' + $name) }
    . ([scriptblock]::Create($declaration.Extent.Text))
}

$script:supervisorChecks = 0
function Assert-Supervisor {
    param([bool]$Condition, [string]$Name)
    if (-not $Condition) { throw ('FAILED: ' + $Name) }
    $script:supervisorChecks++
}
function New-CompletedHttpTask {
    param([int]$Status = 200, [string]$Body = '')
    # .NET Framework's enum predates 429, although HttpClient can receive it.
    $statusValue = [Enum]::ToObject([Net.HttpStatusCode], $Status)
    $response = [Net.Http.HttpResponseMessage]::new($statusValue)
    $response.Content = [Net.Http.StringContent]::new($Body, [Text.Encoding]::UTF8, 'application/json')
    $completion = [Threading.Tasks.TaskCompletionSource[Net.Http.HttpResponseMessage]]::new()
    $completion.SetResult($response)
    return $completion.Task
}
function New-FailedHttpTask {
    $completion = [Threading.Tasks.TaskCompletionSource[Net.Http.HttpResponseMessage]]::new()
    $completion.SetException([Exception]::new('Fixture transport failure'))
    return $completion.Task
}
function New-FakeStream {
    $stream = [pscustomobject]@{}
    $stream | Add-Member -MemberType ScriptMethod -Name ReadLineAsync -Value {
        $completion = [Threading.Tasks.TaskCompletionSource[string]]::new()
        return $completion.Task
    }
    return $stream
}
function New-FakeProcess {
    $process = [pscustomobject]@{
        HasExited = $false; Disposed = $false; Killed = $false; Started = $false
        StartSucceeds = $true; StartInfo = $null; LaunchArguments = ''; LaunchToken = ''; LaunchKey = ''
        StandardOutput = (New-FakeStream); StandardError = (New-FakeStream)
        WaitMilliseconds = 0
    }
    $process | Add-Member -MemberType ScriptMethod -Name Start -Value {
        $this.Started = $true
        $this.LaunchArguments = $this.StartInfo.Arguments
        $this.LaunchToken = $this.StartInfo.EnvironmentVariables['TRAVELAGENT_DEMO_TOKEN']
        $this.LaunchKey = $this.StartInfo.EnvironmentVariables['EXA_API_KEY']
        return $this.StartSucceeds
    }
    $process | Add-Member -MemberType ScriptMethod -Name Kill -Value { $this.Killed = $true; $this.HasExited = $true }
    $process | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { param([int]$Milliseconds) $this.WaitMilliseconds = $Milliseconds; return $true }
    $process | Add-Member -MemberType ScriptMethod -Name Dispose -Value { $this.Disposed = $true }
    return $process
}

# These replacements are local to this test process. All I/O boundaries use
# disposable in-memory fixtures, including process construction and credentials.
function New-Object {
    param([string]$TypeName, [object[]]$ArgumentList)
    if ($TypeName -eq 'Diagnostics.Process') { $script:processCreations++; return $script:nextProcess }
    if ($TypeName -eq 'Diagnostics.ProcessStartInfo') {
        return [pscustomobject]@{ FileName = ''; Arguments = ''; WorkingDirectory = ''; UseShellExecute = $true; CreateNoWindow = $false; RedirectStandardOutput = $false; RedirectStandardError = $false; EnvironmentVariables = [Collections.Specialized.StringDictionary]::new() }
    }
    if ($null -ne $ArgumentList) { return Microsoft.PowerShell.Utility\New-Object -TypeName $TypeName -ArgumentList $ArgumentList }
    return Microsoft.PowerShell.Utility\New-Object -TypeName $TypeName
}
function Get-NetTCPConnection {
    [CmdletBinding()]
    param([int]$LocalPort, [string]$State)
    $script:listenerQueries++
    return $script:testListeners
}
function Find-DemoPython { return $script:testPython }
function Get-DemoExaKey { return $script:testKey }
function Get-DemoSession {
    param([string]$Directory, [string]$HostAddress, [int]$Port, [switch]$Create)
    $script:sessionReads++
    if ($script:sessionFails) { throw 'Fixture session failure' }
    return $script:testSession
}
function Save-DemoSessionMetadata {
    param([string]$Directory, [string]$HostAddress, [int]$Port, [bool]$TokenReused, [int]$LauncherProcessId)
    $script:metadataWrites++
    if ($script:metadataFails) { throw 'Fixture metadata failure' }
}
function Set-DemoStatus {
    param([string]$Code, [string]$Title, [string]$Message, [bool]$Ready = $false)
    $script:observedStatus = [pscustomobject]@{ Code = $Code; Title = $Title; Message = $Message; Ready = $Ready }
}
function Reset-SupervisorFixture {
    $script:demoProcess = $null; $script:outputTask = $null; $script:errorTask = $null
    $script:healthTask = $null; $script:healthResponse = $null; $script:healthRequest = $null
    $script:healthCancellation = $null; $script:healthPhase = ''; $script:healthObject = $null
    $script:authVerified = $false; $script:authExpiresAt = [DateTime]::MinValue; $script:authSession = $null
    $script:address = '10.0.0.10'; $script:gatewayUrl = 'http://10.0.0.10:8788'
    $script:retryCount = 0; $script:failures = 0; $script:healthySince = $null; $script:paused = $false
    $script:nextAttempt = [DateTime]::UtcNow.AddMinutes(1); $script:nextHealth = [DateTime]::UtcNow.AddMinutes(1); $script:nextNetwork = [DateTime]::UtcNow.AddMinutes(1)
    $script:listenerQueries = 0; $script:processCreations = 0; $script:sessionReads = 0; $script:metadataWrites = 0
    $script:testListeners = @(); $script:nextProcess = New-FakeProcess; $script:testPython = 'C:\fixture only\python.exe'
    $script:testKey = 'fixture-exa-key'; $script:testSession = [pscustomobject]@{ Token = ('A' * 64); Reused = $true; PreviousHost = '10.0.0.10'; AddressChanged = $false }
    $script:sessionFails = $false; $script:metadataFails = $false; $script:observedStatus = $null
    $script:stopButton = [pscustomobject]@{ Enabled = $false }
    $script:http = [pscustomobject]@{ Calls = [Collections.ArrayList]::new(); Responses = [Collections.Queue]::new() }
    $script:http | Add-Member -MemberType ScriptMethod -Name SendAsync -Value {
        param($Request, $CancellationToken)
        [void]$this.Calls.Add([pscustomobject]@{ Request = $Request; CancellationToken = $CancellationToken })
        if ($this.Responses.Count -eq 0) { throw 'No fixture HTTP response was supplied.' }
        return $this.Responses.Dequeue()
    }
}
function Add-FixtureHealthResponse {
    $health = @{ service = 'TravelAgent Exa gateway'; status = 'ok'; schemaVersion = '1'; exaConfigured = $true; authRequired = $true; loopbackOnly = $false } | ConvertTo-Json -Compress
    $script:http.Responses.Enqueue((New-CompletedHttpTask 200 $health))
}
function Set-FixtureAuthResponse {
    param([int]$Status)
    $script:healthObject = [pscustomobject]@{ service = 'TravelAgent Exa gateway'; status = 'ok'; schemaVersion = '1'; exaConfigured = $true; authRequired = $true; loopbackOnly = $false }
    $script:healthTask = New-CompletedHttpTask $Status '{}'
    $script:healthPhase = 'auth'
}

$projectRoot = 'C:\fixture project with spaces'
$sessionDirectory = 'C:\fixture session - never opened'
try {
    Reset-SupervisorFixture
    Stop-DemoOwnedProcess
    Assert-Supervisor ($script:listenerQueries -eq 0 -and $script:processCreations -eq 0) 'Stopping without ownership does not inspect or stop unrelated processes'
    $owned = New-FakeProcess; $script:demoProcess = $owned; $script:authVerified = $true; $script:stopButton.Enabled = $true
    Stop-DemoOwnedProcess
    Assert-Supervisor ($owned.Killed -and $owned.Disposed -and $owned.WaitMilliseconds -eq 2000) 'Stopping owned child kills, waits and disposes the same process object'
    Assert-Supervisor ($null -eq $script:demoProcess -and -not $script:authVerified -and -not $script:stopButton.Enabled) 'Owned-stop clears ownership, authentication and stop control'
    $exited = New-FakeProcess; $exited.HasExited = $true; $script:demoProcess = $exited
    Stop-DemoOwnedProcess
    Assert-Supervisor (-not $exited.Killed -and $exited.Disposed) 'Already-exited owned child is disposed without another kill'

    Reset-SupervisorFixture
    $script:healthCancellation = [Threading.CancellationTokenSource]::new()
    $token = $script:healthCancellation.Token
    $pending = [Threading.Tasks.TaskCompletionSource[Net.Http.HttpResponseMessage]]::new()
    $script:healthTask = $pending.Task
    Clear-DemoHealth
    Assert-Supervisor ($token.IsCancellationRequested -and $null -eq $script:healthTask -and $null -eq $script:healthCancellation) 'Clearing an unfinished probe cancels its token and drops request state'

    Reset-SupervisorFixture
    Add-FixtureHealthResponse
    $script:http.Responses.Enqueue((New-CompletedHttpTask 400 '{}'))
    Start-DemoHealth
    Assert-Supervisor ($script:http.Calls.Count -eq 1 -and $script:http.Calls[0].Request.Method.Method -eq 'GET') 'Initial asynchronous probe requests health only'
    Assert-Supervisor $script:http.Calls[0].CancellationToken.CanBeCanceled 'Each HTTP request has a cancellation token'
    Receive-DemoHealth
    Assert-Supervisor ($script:healthPhase -eq 'auth' -and $script:http.Calls.Count -eq 2 -and -not $script:authVerified) 'Healthy response starts a separate session probe before declaring Ready'
    $probe = $script:http.Calls[1].Request
    Assert-Supervisor ($probe.Method.Method -eq 'POST' -and $probe.RequestUri.AbsolutePath -eq '/api/claims/research' -and $probe.Content.ReadAsStringAsync().GetAwaiter().GetResult() -ceq '{}') 'Authorization probe uses empty invalid context, not a research request'
    Assert-Supervisor ($probe.Headers.Authorization.Scheme -eq 'Bearer' -and $probe.Headers.Authorization.Parameter -ceq $script:testSession.Token) 'Probe uses only its fixture saved-session bearer'
    Receive-DemoHealth
    Assert-Supervisor ($script:authVerified -and $script:observedStatus.Ready -and $script:observedStatus.Code -eq 'ready') 'Expected HTTP400 establishes authenticated Ready state'
    Assert-Supervisor (($script:authExpiresAt - [DateTime]::UtcNow).TotalSeconds -gt 55) 'Authentication is cached for approximately one minute'
    Assert-Supervisor ($null -eq $script:healthTask -and $null -eq $script:authSession) 'Completed probe releases task and plaintext session references'

    Reset-SupervisorFixture
    $script:authVerified = $true; $script:authExpiresAt = [DateTime]::UtcNow.AddSeconds(40)
    Add-FixtureHealthResponse; Start-DemoHealth; Receive-DemoHealth
    Assert-Supervisor ($script:observedStatus.Ready -and $script:http.Calls.Count -eq 1 -and $script:sessionReads -eq 0) 'Fresh authentication avoids rate-consuming probes on each health tick'
    Clear-DemoHealth
    Reset-SupervisorFixture
    $script:authVerified = $true; $script:authExpiresAt = [DateTime]::UtcNow.AddSeconds(-1)
    Add-FixtureHealthResponse; $script:http.Responses.Enqueue((New-CompletedHttpTask 400 '{}'))
    Start-DemoHealth; Receive-DemoHealth
    Assert-Supervisor (-not $script:authVerified -and $script:healthPhase -eq 'auth' -and $script:http.Calls.Count -eq 2) 'Expired authentication requires a new saved-session probe'
    Clear-DemoHealth

    Reset-SupervisorFixture
    Set-FixtureAuthResponse 401; Receive-DemoHealth
    Assert-Supervisor (-not $script:authVerified -and -not $script:observedStatus.Ready -and $script:observedStatus.Code -eq 'token-mismatch') 'HTTP401 cannot be presented as Ready'
    Assert-Supervisor ($script:processCreations -eq 0 -and $script:listenerQueries -eq 0) 'Authentication failure does not replace an unmanaged gateway'
    Reset-SupervisorFixture
    Set-FixtureAuthResponse 429; Receive-DemoHealth
    Assert-Supervisor (($script:nextHealth - [DateTime]::UtcNow).TotalSeconds -gt 60) 'Rate limiting waits beyond the one-minute rate window'

    Reset-SupervisorFixture
    $script:healthTask = New-FailedHttpTask; $script:healthPhase = 'health'; $script:authVerified = $true
    Receive-DemoHealth
    Assert-Supervisor ($script:failures -eq 1 -and -not $script:authVerified -and $script:processCreations -eq 0) 'Transport failure clears authentication and respects future restart backoff'
    Reset-SupervisorFixture
    $owned = New-FakeProcess; $script:demoProcess = $owned; $script:failures = 2
    $script:healthTask = New-FailedHttpTask; $script:healthPhase = 'health'
    Receive-DemoHealth
    Assert-Supervisor ($owned.Killed -and $null -eq $script:demoProcess -and $script:processCreations -eq 0) 'Third transport failure stops only the owned child and respects backoff'
    Reset-SupervisorFixture
    $old = New-FakeProcess; $script:demoProcess = $old; $script:failures = 2; $script:retryCount = 2; $script:nextAttempt = [DateTime]::UtcNow.AddSeconds(-1)
    $replacement = $script:nextProcess
    $script:healthTask = New-FailedHttpTask; $script:healthPhase = 'health'
    Receive-DemoHealth
    Assert-Supervisor ($old.Killed -and $replacement.Started -and $script:demoProcess -eq $replacement) 'Eligible failure recovery replaces its own child with an owned fixture process'
    Assert-Supervisor ($script:retryCount -eq 3 -and $script:metadataWrites -eq 1) 'Successful replacement consumes exactly one recovery attempt'
    Assert-Supervisor (($script:nextAttempt - [DateTime]::UtcNow).TotalSeconds -gt 5) 'Restart establishes a future backoff deadline'
    Assert-Supervisor (-not $replacement.LaunchArguments.Contains($script:testKey) -and -not $replacement.LaunchArguments.Contains($script:testSession.Token) -and $replacement.LaunchArguments.StartsWith('"C:\fixture project with spaces\Backend\server.py"')) 'Quoted process arguments contain no key or bearer'
    Assert-Supervisor ($replacement.LaunchKey -ceq $script:testKey -and $replacement.LaunchToken -ceq $script:testSession.Token -and -not $replacement.StartInfo.EnvironmentVariables.ContainsKey('EXA_API_KEY') -and -not $replacement.StartInfo.EnvironmentVariables.ContainsKey('TRAVELAGENT_DEMO_TOKEN')) 'Fixture credentials are handed through environment and removed from retained process configuration'
    Stop-DemoOwnedProcess

    Reset-SupervisorFixture
    $script:retryCount = 3; $script:nextAttempt = [DateTime]::UtcNow.AddSeconds(-1)
    $script:healthTask = New-FailedHttpTask; $script:healthPhase = 'health'
    Receive-DemoHealth
    Assert-Supervisor ($script:processCreations -eq 0 -and $script:observedStatus.Code -eq 'retry-limit') 'Automatic recovery never exceeds three attempted starts'
    Reset-SupervisorFixture
    $script:nextProcess.StartSucceeds = $false
    Start-DemoOwnedProcess
    Assert-Supervisor ($script:retryCount -eq 1 -and $script:processCreations -eq 1 -and $script:observedStatus.Code -eq 'start-error') 'Failed process start counts once and reports a recovery error'
    Assert-Supervisor (($script:nextAttempt - [DateTime]::UtcNow).TotalSeconds -gt 5) 'Failed process start also backs off'
    Reset-SupervisorFixture
    $script:metadataFails = $true; $attempt = $script:nextProcess
    Start-DemoOwnedProcess
    Assert-Supervisor ($attempt.Started -and $attempt.Killed -and $attempt.Disposed -and $script:retryCount -eq 1) 'Metadata failure stops the child that was just started and counts once'

    Reset-SupervisorFixture
    $foreign = New-FakeProcess
    $script:testListeners = @([pscustomobject]@{ LocalAddress = '10.0.0.10'; OwningProcess = 99999 })
    Start-DemoOwnedProcess
    Assert-Supervisor ($script:processCreations -eq 0 -and $script:sessionReads -eq 0 -and $script:observedStatus.Code -eq 'port-in-use' -and -not $foreign.Killed) 'An existing listener is not replaced or assigned a new session'
    Reset-SupervisorFixture
    $script:testPython = $null; Start-DemoOwnedProcess
    Assert-Supervisor ($script:observedStatus.Code -eq 'python-missing' -and $script:processCreations -eq 0 -and $script:sessionReads -eq 0) 'Missing Python is reported before credentials or processes are touched'
    Reset-SupervisorFixture
    $script:testKey = ''; Start-DemoOwnedProcess
    Assert-Supervisor ($script:observedStatus.Code -eq 'key-missing' -and $script:processCreations -eq 0 -and $script:sessionReads -eq 0) 'Missing Exa key does not create a misleading running gateway'
    Reset-SupervisorFixture
    $script:sessionFails = $true; Start-DemoOwnedProcess
    Assert-Supervisor ($script:observedStatus.Code -eq 'session-error' -and $script:processCreations -eq 0 -and $script:metadataWrites -eq 0) 'Unreadable session is preserved without starting a replacement process'

    Reset-SupervisorFixture
    $owned = New-FakeProcess; $script:demoProcess = $owned; $script:retryCount = 3; $script:failures = 3; $script:paused = $true; $script:authVerified = $true
    Reset-DemoRecovery
    Assert-Supervisor ($owned.Killed -and $owned.Disposed -and $script:retryCount -eq 0 -and $script:failures -eq 0 -and -not $script:paused -and -not $script:authVerified) 'Explicit Recover reloads its own child and clears the retry/pause state'
    Assert-Supervisor ($script:nextHealth -le [DateTime]::UtcNow -and $script:nextAttempt -le [DateTime]::UtcNow) 'Explicit Recover schedules immediate health and restart attempts'
    Reset-SupervisorFixture
    $foreign = New-FakeProcess; Reset-DemoRecovery
    Assert-Supervisor (-not $foreign.Killed -and $script:listenerQueries -eq 0 -and $script:processCreations -eq 0) 'Explicit Recover never stops or starts an unowned process directly'

    Reset-SupervisorFixture
    $script:retryCount = 3; $script:healthySince = [DateTime]::UtcNow.AddSeconds(-31); $script:authVerified = $true; $script:authExpiresAt = [DateTime]::UtcNow.AddSeconds(40)
    Add-FixtureHealthResponse; Start-DemoHealth; Receive-DemoHealth
    Assert-Supervisor ($script:retryCount -eq 0 -and $script:observedStatus.Ready) 'Thirty seconds of authenticated health clears the temporary recovery budget'

    Write-Output ('Passed: ' + $script:supervisorChecks + ' supervisor checks from actual launcher functions. No UI, gateway, real network, processes, credentials or session files were used.')
} finally {
    Clear-DemoHealth
    Stop-DemoOwnedProcess
}
