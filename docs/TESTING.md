# Validation and current limits

This record distinguishes source checks, phone behavior and physical observations. Test counts alone do not establish a working radio, camera, motor or airline submission. Runtime captures, private settings and personal evidence are intentionally excluded from this public source package.

## Prepared binaries

| Component | Size | SHA-256 |
| --- | ---: | --- |
| Android development APK | 59,230,235 bytes | `CDCE55B9464D972FF13D2DDEAF0B822156BE6B9E402F80277207BDA1C294C1B7` |
| ESP32-S3 firmware | 962,352 bytes | `52321AC011195951717B719B7DCEEB498045A7ABD1069EC96A2B02D7126E27F5` |

The APK built on **15 November 2026 at 06:41:06 Europe/Helsinki** with **0 errors / 4 warnings**. Package/signature, 15 native entry classes, private providers, ARM64 and Android API 26/36 checks passed. These local binaries are staged under ignored `dist/` for a later release, separate from source history.

## Recorded automated checks

| Layer | Result and scope |
| --- | --- |
| Unity EditMode | **213 passed**, no failures/skips |
| Unity PlayMode | **49 passed**, no failures/skips; includes seven fresh-session UI/lifecycle cases |
| C# compilation | Full Core and Editor/Android Runtime branches compiled with warnings treated as errors |
| Android Java | **718 framing, 42 live-motion, 29 archive/path, 56 camera-journal/orientation, 43 incoming-share and 34 gateway-settings assertions** |
| Python gateway | **55 tests passed** on the unchanged gateway implementation |
| Windows launcher | **72 core + 37 supervisor checks** using disposable DPAPI and in-memory HTTP/process fixtures |
| Firmware | Build and **62 compile-time assertions** passed; **22 host scenarios compiled only**, not executed |

The source tests are included under `app/Assets/_App/Tests`, `app/Backend/tests`, `app/tools` and `hardware-tools`. SDK/toolchain requirements for firmware are in [Hardware](HARDWARE.md).

## Latest APK: actual phone acceptance

The latest APK was installed without clearing application data. Its first launch archived an existing trip and showed no old airline, inspection, photos, events or trace. The prior active store contained a confirmed airline/damage choice, 14 events and 224 motion samples.

A new synthetic Android text share imported Finnair / CPH → HEL. Both inspection choices were visibly neutral; choosing Yes accented only Yes. Home/reopen retained that same trip and answer. Force-stop/reopen created another blank trip with zero photos/events/samples and no airline or inspection selection.

Native ownership and gateway preference files were byte-identical before/after the update and restart. Cached button configuration remained retained; this was not a fresh board readback. The previous trip was found in the local archive. No fatal/reference/JNI/memory errors were observed in the inspected runtime log window.

This acceptance did **not** repeat physical BLE motion, camera capture or a paid Exa query on the latest hash.

## Real motion and guard: preceding matching app build

With the same current firmware and the preceding motion APK (`40E604FF…1282`), actual phone acceptance established:

- Saved-owner authorization and confirmed code readback.
- A measured live trace with **755 fresh captured samples** and **eight genuine movement markers**.
- User-confirmed trace/marker response to movement while Disarmed.
- Chart taps opening original event details and the inspection action.
- Saved-session display after disconnect and a visible **53,097 ms** gap on reconnect, without fabricated backfill.
- Quiet Arm → Arming → Armed → Disarm while streaming.
- Final independent passive telemetry showing Disarmed, motor Idle, PWM 0 and a fresh sensor.

These observations belong to that firmware/app pairing. The subsequent fresh-session APK changes presentation lifecycle and clears old callback queues; it was not used for another full physical run.

## Earlier supporting demonstrations

Earlier phone builds exercised actual Exa reporting research, official Finnair handoff, camera capture/cancel/reviewed recovery and local export. The result labels retained cached-source uncertainty. No real airline claim was sent.

The assembled empty drum was reported to rotate with alarm outputs and stop on Disarm. Physical-code changes, offline physical disarm and battery restart were checked in the earlier assembled-hardware run. These are scoped observations, not a reliability or endurance certification.

## Not established

- Device-side offline journaling, disconnected event replay, flight-long recording, variable resolution or quiet transit recording.
- Loaded breadcrumb dispensing, quantity, jam resistance, battery endurance or motor-stop latency during an active pulse.
- A prolonged assembled siren/automatic-cutoff repeat; it was skipped at the user's request.
- Damage cause, liability, compensation, automatic legal deadline calculation or automatic airline submission.
- Failure-free operation across all Android devices, permission states, networks and background restrictions.
- A timed rehearsal of the final combined Gmail → real motion → Exa route.

The two actual STL print layouts are included under `mechanical/stl`. Both parse
as watertight meshes, with four and five disconnected bodies respectively.
These geometry checks do not establish physical fit or loaded dispensing.
Editable Fusion/STEP source has not yet been provided. The earlier vertical-axis
prototype is not included as evidence of the assembled drum.
