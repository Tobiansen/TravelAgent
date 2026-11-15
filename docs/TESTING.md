# Validation

## Android build

The released ARM64 development APK is **59,230,235 bytes**, SHA-256
CDCE55B9464D972FF13D2DDEAF0B822156BE6B9E402F80277207BDA1C294C1B7.
It built on **15 November 2026 at 06:41:06 Europe/Helsinki** with **0 errors / 4 warnings**. Package/signature, 15 native entry classes, private providers and Android API 26/36 checks passed. [Download details](RELEASES.md).

## Source checks

| Layer | Recorded result |
| --- | --- |
| Unity EditMode | **213 passed**, no failures/skips |
| Unity PlayMode | **49 passed**, no failures/skips; includes seven fresh-session UI/lifecycle cases |
| C# compilation | Full Core and Editor/Android Runtime branches compiled with warnings treated as errors |
| Android Java | **718 framing, 42 live-motion, 29 archive/path, 56 camera-journal/orientation, 43 incoming-share and 34 gateway-settings assertions** |
| Python gateway | **55 tests passed** |
| Windows launcher | **72 core + 37 supervisor checks** using disposable DPAPI and in-memory HTTP/process fixtures |
| Firmware | Build and **62 compile-time assertions** passed; **22 host scenarios compiled**, without a host execution result |

Tests are included under app/Assets/_App/Tests, app/Backend/tests, app/tools and hardware-tools. Gateway and launcher suites also run in [GitHub Actions](https://github.com/Tobiansen/TravelAgent/actions/runs/43681174396).

## Released APK: phone lifecycle

Installation preserved application data. The first launch archived an existing trip and displayed a fresh airline/inspection/photo/event/motion state. The preceding active trip contained an airline, a recorded damage choice, 14 events and 224 motion samples.

A new synthetic Android text share imported Finnair / CPH → HEL. Both inspection choices started neutral; selecting Yes accented only Yes. Home/reopen retained the trip and answer, while force-stop/reopen created a fresh trip. Native ownership and gateway preference files remained byte-identical, and the previous trip was present in the archive. The inspected runtime log had no fatal/reference/JNI/memory errors.

This check covers the released APK's lifecycle behavior. Radio, camera and Exa observations below retain their own build scope.

## Device motion and guard

The current firmware, identified by SHA-256
52321AC011195951717B719B7DCEEB498045A7ABD1069EC96A2B02D7126E27F5,
was physically checked with the preceding motion APK (40E604FF…1282). Results included:

- Saved-owner authorization and confirmed configuration readback.
- **755 fresh captured samples** and **eight movement markers**.
- User-confirmed trace/marker response to movement while Disarmed.
- Chart selection opening event details and inspection.
- Saved history after disconnect and a visible **53,097 ms** reconnect gap.
- Quiet Arm → Arming → Armed → Disarm while streaming.
- Independent passive telemetry ending Disarmed, motor Idle, PWM 0 and sensor fresh.

Earlier assembled-hardware testing covered empty-drum rotation, alarm outputs, app/physical disarm, physical-code changes and battery restart. These observations apply to their recorded firmware/app pairings.

## Supporting checks

Earlier phone builds exercised actual Exa reporting research, official Finnair handoff, camera capture/cancel/reviewed recovery and local export. Results retained their cached-source labels. The airline handoff was observed through its public report selector.

The two STL print layouts under mechanical/stl parse as watertight meshes, containing four and five disconnected bodies respectively. These are geometry checks; the mechanical inventory records bounds and body counts.
