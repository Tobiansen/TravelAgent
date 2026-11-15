[CmdletBinding()]
param(
    [string]$ApkPath = '',
    [string]$AndroidPlayer = 'C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Data\PlaybackEngines\AndroidPlayer'
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $ApkPath) { $ApkPath = Join-Path $projectRoot 'Builds\Android\TravelAgent-development.apk' }
$ApkPath = (Resolve-Path -LiteralPath $ApkPath).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem
$classes = [ordered]@{
    BleBridge='Lcom/travelagent/ble/BleBridge;'
    BagBleService='Lcom/travelagent/ble/BagBleService;'
    PhotoPickerActivity='Lcom/travelagent/ble/PhotoPickerActivity;'
    PhotoPickerBridge='Lcom/travelagent/ble/PhotoPickerBridge;'
    OwnerTokenStore='Lcom/travelagent/ble/OwnerTokenStore;'
    ReportSharingBridge='Lcom/travelagent/ble/ReportSharingBridge;'
    ReportShareActivity='Lcom/travelagent/ble/ReportShareActivity;'
    ReportShareProvider='Lcom/travelagent/ble/ReportShareProvider;'
    CameraCaptureBridge='Lcom/travelagent/ble/CameraCaptureBridge;'
    CameraCaptureActivity='Lcom/travelagent/ble/CameraCaptureActivity;'
    CameraCaptureProvider='Lcom/travelagent/ble/CameraCaptureProvider;'
    PhotoPreviewBridge='Lcom/travelagent/ble/PhotoPreviewBridge;'
    IncomingShareBridge='Lcom/travelagent/ble/IncomingShareBridge;'
    IncomingShareActivity='Lcom/travelagent/ble/IncomingShareActivity;'
    GatewaySettingsBridge='Lcom/travelagent/ble/GatewaySettingsBridge;'
}
$found = [ordered]@{}
foreach ($name in $classes.Keys) { $found[$name] = $false }
$archive = [IO.Compression.ZipFile]::OpenRead($ApkPath)
try {
    foreach ($entry in $archive.Entries | Where-Object FullName -Like '*.dex') {
        $stream = $entry.Open()
        $memory = New-Object IO.MemoryStream
        try {
            $stream.CopyTo($memory)
            $strings = [Text.Encoding]::GetEncoding(28591).GetString($memory.ToArray())
            foreach ($name in $classes.Keys) { if ($strings.Contains($classes[$name])) { $found[$name] = $true } }
        }
        finally { $stream.Dispose(); $memory.Dispose() }
    }
}
finally { $archive.Dispose() }
foreach ($name in $found.Keys) { if (-not $found[$name]) { throw "APK is missing native class $name." } }
$aapt = Join-Path $AndroidPlayer 'SDK\build-tools\36.0.0\aapt.exe'
$badging = (& $aapt dump badging $ApkPath) -join "`n"
if ($LASTEXITCODE -ne 0 -or -not $badging.Contains("package: name='com.travelagent.companion'")) { throw 'APK application ID validation failed.' }
if (-not $badging.Contains("sdkVersion:'26'") -or -not $badging.Contains("targetSdkVersion:'36'") -or -not $badging.Contains("native-code: 'arm64-v8a'")) { throw 'APK SDK/ABI validation failed.' }
$permissions = (& $aapt dump permissions $ApkPath) -join "`n"
foreach ($permission in @('BLUETOOTH_SCAN','BLUETOOTH_CONNECT','FOREGROUND_SERVICE_CONNECTED_DEVICE','INTERNET')) {
    if (-not $permissions.Contains("android.permission.$permission")) { throw "APK is missing permission $permission." }
}
foreach ($permission in @('CAMERA','READ_MEDIA_IMAGES','READ_EXTERNAL_STORAGE','WRITE_EXTERNAL_STORAGE','MANAGE_EXTERNAL_STORAGE')) {
    if ($permissions.Contains("android.permission.$permission'")) { throw "Delegated camera/photo flow must not request android.permission.$permission." }
}
$manifest = (& $aapt dump xmltree $ApkPath AndroidManifest.xml) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'APK merged manifest could not be inspected.' }
$components = [regex]::Matches($manifest, '(?ms)^([ ]*)E: (?:provider|activity) \(.*?(?=^\1E:|\z)')
$cameraManifest = [ordered]@{}
foreach ($name in @('CameraCaptureProvider','CameraCaptureActivity')) {
    $qualified = 'com.travelagent.ble.' + $name
    $block = @($components | Where-Object { $_.Value -match ('android:name\([^)]*\)="' + [regex]::Escape($qualified) + '"') })
    if ($block.Count -ne 1 -or $block[0].Value -notmatch 'android:exported\([^)]*\)=\(type 0x12\)0x0') { throw "Camera component $name must be private in the merged APK manifest." }
    $cameraManifest[$name + 'Private'] = $true
    if ($name -eq 'CameraCaptureProvider') {
        if ($block[0].Value -notmatch 'android:grantUriPermissions\([^)]*\)=\(type 0x12\)0xffffffff' -or -not $block[0].Value.Contains('"com.travelagent.companion.camera"') -or -not $block[0].Value.Contains('"/capture/"')) { throw 'Camera provider URI grant scope is incorrect in the merged manifest.' }
        $cameraManifest['temporaryScopedUriGrants'] = $true
    }
}
$shareManifest = [ordered]@{}
foreach ($name in @('ReportShareProvider','ReportShareActivity')) {
    $qualified = 'com.travelagent.ble.' + $name
    $block = @($components | Where-Object { $_.Value -match ('android:name\([^)]*\)="' + [regex]::Escape($qualified) + '"') })
    if ($block.Count -ne 1 -or $block[0].Value -notmatch 'android:exported\([^)]*\)=\(type 0x12\)0x0') { throw "Report-sharing component $name must be private in the merged APK manifest." }
    $shareManifest[$name + 'Private'] = $true
    if ($name -eq 'ReportShareProvider') {
        if ($block[0].Value -notmatch 'android:grantUriPermissions\([^)]*\)=\(type 0x12\)0xffffffff' -or -not $block[0].Value.Contains('"com.travelagent.companion.reports"') -or -not $block[0].Value.Contains('"/reports/"')) { throw 'Report provider URI grant scope is incorrect in the merged manifest.' }
        $shareManifest['temporaryScopedUriGrants'] = $true
    }
}
$receiver = @($components | Where-Object { $_.Value -match 'android:name\([^)]*\)="com\.travelagent\.ble\.IncomingShareActivity"' })
if ($receiver.Count -ne 1 -or $receiver[0].Value -notmatch 'android:exported\([^)]*\)=\(type 0x12\)0xffffffff') { throw 'The incoming-share entry must be explicitly exported in the merged APK manifest.' }
if ($receiver[0].Value -notmatch 'android:label\([^)]*\)="TravelAgent"') { throw 'The incoming-share target must have the visible label TravelAgent.' }
$filters = [regex]::Matches($receiver[0].Value, '(?ms)^([ ]*)E: intent-filter \(.*?(?=^\1E:|\z)')
if ($filters.Count -ne 2) { throw 'Incoming sharing must expose only its SEND and PROCESS_TEXT text filters.' }
$incomingManifest = [ordered]@{ exportedTextEntry=$true; visibleLabel='TravelAgent'; sendPlainAndHtml=$false; processSelectedPlainText=$false }
foreach ($filter in $filters) {
    $actions = @([regex]::Matches($filter.Value, 'android:name\([^)]*\)="(android\.intent\.action\.[^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    $mimes = @([regex]::Matches($filter.Value, 'android:mimeType\([^)]*\)="([^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    if ($actions.Count -ne 1 -or -not $filter.Value.Contains('"android.intent.category.DEFAULT"') -or $filter.Value.Contains('"android.intent.category.BROWSABLE"')) { throw 'Incoming sharing has an unexpected action/category.' }
    if ($filter.Value -match 'android:(?:scheme|host|path|pathPrefix|pathPattern)\(') { throw 'Incoming sharing cannot expose URI browsing filters.' }
    if ($actions[0] -eq 'android.intent.action.SEND' -and ($mimes -join ',') -eq 'text/html,text/plain') { $incomingManifest.sendPlainAndHtml=$true }
    elseif ($actions[0] -eq 'android.intent.action.PROCESS_TEXT' -and ($mimes -join ',') -eq 'text/plain') { $incomingManifest.processSelectedPlainText=$true }
    else { throw 'Incoming sharing must accept only SEND plain/html and PROCESS_TEXT plain text, without wildcard/file MIME types.' }
}
if (-not $incomingManifest.sendPlainAndHtml -or -not $incomingManifest.processSelectedPlainText) { throw 'Incoming text-sharing routes are incomplete.' }
$java = Join-Path $AndroidPlayer 'OpenJDK\bin\java.exe'
$signer = Join-Path $AndroidPlayer 'SDK\build-tools\36.0.0\lib\apksigner.jar'
$signature = & $java -jar $signer verify --verbose $ApkPath
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
$result = [ordered]@{
    status='passed'; testedAtUtc=[DateTime]::UtcNow.ToString('O'); apk=$ApkPath
    apkBytes=(Get-Item -LiteralPath $ApkPath).Length
    sha256=(Get-FileHash -LiteralPath $ApkPath -Algorithm SHA256).Hash
    applicationId='com.travelagent.companion'; minimumSdk=26; targetSdk=36; abi='arm64-v8a'
    nativeClasses=$found; signatureVerified=$true; phoneRuntimeTested=$false
    cameraManifest=$cameraManifest; cameraRuntimeTested=$false
    reportShareManifest=$shareManifest; incomingShareManifest=$incomingManifest
    incomingShareRuntimeTested=$false; gatewaySettingsRuntimeTested=$false
}
$artifactPath = Join-Path $projectRoot 'Artifacts\apk-verification.json'
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $artifactPath -Encoding utf8
$result | ConvertTo-Json -Depth 4
