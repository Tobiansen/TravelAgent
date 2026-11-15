# Setup and build

## Editor demonstration

1. Install Unity **6000.6.9f1** and open the repository's **`app` folder** in Hub.
2. Open `Assets/_App/Scenes/App.unity` and press Play.
3. In Device choose Demo. Presenter controls → New demo provides explicitly
   illustrative events. Enter an airline before moving through inspection/report.
4. Offline fixture guidance is labelled. Select live Exa only after configuring
   the gateway below.

`Assets`, `Packages`, `ProjectSettings` and all asset `.meta` files are included.
Unity regenerates `Library` and other caches. The package versions are pinned in
`app/Packages/packages-lock.json`. Toolchains and Unity licenses are not bundled.

## Laptop connection for live Exa

The phone talks directly to the board by Bluetooth. Only airline reporting
research needs the laptop gateway. The Exa API key stays on the laptop; the email
body, photographs and raw motion samples are not sent to Exa.

On Windows, install Python **3.10 or newer**, then run these one-time setup steps
from the repository root. The key below is a placeholder for your own key:

```powershell
python -m venv app\Backend\.venv
[Environment]::SetEnvironmentVariable('EXA_API_KEY', 'YOUR_EXA_API_KEY', 'User')
```

No `pip install` is required for the gateway. It uses Python's standard library.
Double-click **TravelAgent Demo.cmd** at the repository root. Wait for **Laptop
connection ready**, leave the window open/minimized, and keep the laptop awake.
It discovers a physical private Wi-Fi/Ethernet address, starts or reuses a gateway
and attempts bounded recovery. **Check / recover** reloads a process it owns.
The GUI stops only its own process, never an arbitrary existing listener.

The gateway requires a random session token for LAN access and stores its local
handoff encrypted for the current Windows user in ignored `app/Artifacts`.
This public copy does **not** include the original team's key, token or phone
ownership. A new checkout establishes its own session.

For a phone, keep both devices on the same reachable network. Copy the address
from the launcher into **Journey → Before → Trip details → Connection settings**.
Enter that gateway's session token once. For the development APK, the included
USB helper can perform this private handoff after you focus the masked token field:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File app\tools\android\Send-GatewayTokenToPhone.ps1 -AdbPath "PATH_TO_ANDROID_PLATFORM_TOOLS\adb.exe"
```

This helper requires one unlocked, USB-debugging-authorized phone and the
development APK. It reads the encrypted local gateway handoff without printing
the value. The phone stores the resulting configuration using Android Keystore.
USB is not required during the wireless demo afterward.

An address change preserves the desktop launcher's token, but you must update the
phone URL using **Copy address**. A guest network that isolates devices will not
work for direct laptop-to-phone HTTP. The gateway uses HTTP on the local network.
The launcher does not alter firewall settings.

The earlier terminal-only scripts remain in `app/tools`: `Start-Backend.ps1`
defaults to loopback `127.0.0.1:8787`; `Start-LanDemo.ps1` is the manual foreground
LAN alternative. The normal desktop launcher uses **8788**. The older manual
script's token rotation rules differ; use the desktop launcher for the demo.

## Build Android

Install Android Build Support, SDK/NDK and OpenJDK for the exact Unity editor.
In Unity choose **Tools → Travel Agent → Build Android development APK**.
The output is `app/Builds/Android/TravelAgent-development.apk`: Android **8.0 / API
26 or newer**, ARM64; target API **36**, IL2CPP. This development build permits
the local HTTP gateway. No production signing key is included.

Native plugin source is in
`app/Assets/Plugins/Android/TravelAgentBle.androidlib`. Connect to the board from
Device and use its physical enrollment window for a new owner. Follow
[hardware setup](HARDWARE.md); an existing owner's private token cannot be cloned
from this repository.

## Tests

Gateway tests need no key, paid request or hardware:

```powershell
cd app\Backend
python -m unittest discover -s tests -v
```

Run Unity EditMode and PlayMode suites from Test Runner. Their storage overrides
keep fixture evidence separate. Windows launcher checks are
`app/tools/Test-DemoLauncher.ps1` and `Test-DemoSupervisor.ps1`; they use disposable
fixtures. See [testing scope](TESTING.md) before equating a source test with a
physical hardware pass. Firmware setup is in [firmware/README.md](../firmware/README.md).
