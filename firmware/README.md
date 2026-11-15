# Suitcase firmware

`suitcase-ble/` is the current ESP32-S3 firmware. It contains the alarm, bounded
drum control, owner-authenticated Bluetooth configuration/guard commands and
connected-session live motion stream used by the Android app.

Keep `suitcase-full/modules/` beside it: those two project-local motor/IMU
drivers are required by `suitcase-ble/project.yaml`. This folder does not contain
the older suitcase-full application. Source files and that relative dependency
are preserved unchanged from the demonstrated firmware.

## Build

Use Python 3.10 or newer and the [Atech SDK](https://pypi.org/project/atech/1.0.0a12/).
From the submission root, in PowerShell:

```powershell
python -m venv firmware/.venv
./firmware/.venv/Scripts/python.exe -m pip install -r firmware/requirements.lock.txt
./firmware/.venv/Scripts/atech.exe validate firmware/suitcase-ble
./firmware/.venv/Scripts/atech.exe build firmware/suitcase-ble
```

On macOS/Linux use the environment's `bin/python` and `bin/atech` equivalents.
The first build downloads the ESP32 toolchain and libraries. Generated files go
under `suitcase-ble/build/`; do not edit or commit that directory.

The demonstrated stack was Python **3.12.14**, Atech **1.0.0a12**, PlatformIO
**6.2.0**, Espressif32 platform **8.0.0**, Arduino framework package
**4.30312.261024** (Arduino-ESP32 3.3.12), NimBLE-Arduino **2.5.2**, ArduinoJson
**7.4.2** and Adafruit NeoPixel **1.15.5**. The Python environment snapshot is in
`requirements.lock.txt`; the two BLE/JSON library versions are in `project.yaml`.
The SDK-generated PlatformIO file uses the unversioned `espressif32` platform
name, so retain the tested platform/framework versions when reproducing the
build. This repository does not vendor the toolchain or promise byte-identical
binaries from a future dependency resolution.

## Upload and first connection

First confirm the port layout in [HARDWARE.md](../docs/HARDWARE.md), keep the
assembly still and disarmed, and keep hands clear of the drum. Run `atech ports`
to identify your own board. Then upload with your selected port:

```powershell
./firmware/.venv/Scripts/atech.exe ports
# Replace YOUR_BOARD_PORT with the port returned for your board.
./firmware/.venv/Scripts/atech.exe upload firmware/suitcase-ble --port YOUR_BOARD_PORT
```

Use the SDK's normal upload flow; no whole-flash erase or ownership reset is
needed to update existing firmware. A board with previously saved settings
retains them. A fresh board starts Disarmed with factory defaults **AABB** to
arm and **BABA** to disarm. The demonstration board was deliberately configured
to **ABAB / ABBA** through the app; those are not factory defaults.

While Disarmed, hold A and B together for **3 to less than 8 seconds**, then
release both, to open the 60-second enrollment window. Connect from the Android
app and authorize the phone. Enrollment does not replace an existing owner.

## Demonstrated version and checks

The uploaded application image was **962,352 bytes**, SHA-256:

```text
52321AC011195951717B719B7DCEEB498045A7ABD1069EC96A2B02D7126E27F5
```

The firmware build passed **62 compile-time assertions**. The full application
host harness's **22 protocol scenarios compiled to an object file only**;
Windows Application Control prevented execution of the latest host runner, so
these are not reported as runtime passes. Source for the harnesses is in
[`hardware-tools/`](../hardware-tools/README.md).

Physical checks established authenticated phone streaming, movement markers,
reconnection gaps and quiet Arm/Armed/Disarm operation for this firmware. Earlier
assembled empty-drum checks demonstrated lights, siren, rotation and stopping on
Disarm. See [HARDWARE.md](../docs/HARDWARE.md) for operating controls and
[Testing](../docs/TESTING.md) for the recorded checks.

The local driver READMEs retain their Atech-derived provenance. Dependencies and
reference geometry keep their respective upstream licenses; they are not made
first-party code by this submission.

Exact license/NOTICE files available from the demonstrated dependency caches are
preserved in [`third_party/firmware/`](../third_party/firmware/README.md), with
tested versions and upstream provenance. Full standard GNU LGPL-2.1/GPL-3.0
texts are supplied separately and clearly labelled.
