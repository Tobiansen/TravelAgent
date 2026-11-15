# Hardware and firmware

The prototype combines an Atech 14-port ESP32-S3 board, two buttons, two 3×3 light
grids, an IMU, a speaker and one DC motor driving a printed breadcrumb drum.
The phone communicates directly with the board over Bluetooth; the laptop Exa
gateway has no role in guard control.

## Confirmed port map

| Component | Port | Note |
| --- | --- | --- |
| Button A | 1 | Physical sequence input |
| Left light grid | 2 | Alarm/status indication |
| Speaker | 3–4 | Two-port module |
| Right light grid | 5 | Alarm/status indication |
| IMU | 7 | Moves rigidly with the assembly |
| USB-C | 8 | Fixed power interface; omitted from firmware module declarations |
| Button B | 9 | Physical sequence input |
| DC motor | 11 | V2.3 module, mechanically supported separately |
| Reset | 12 | Fixed board control |

Change module connections with power removed. Keep the motor supported by its
mount rather than its electrical connector. The printed drum rotates about a
horizontal axis in the intended mounted orientation. The [mechanical directory](../mechanical/README.md)
contains the case and dispenser print layouts.

## Guard and local controls

- Fresh firmware settings: arm **AABB**, disarm **BABA**. Four fully released
  button presses enter a sequence. The demonstration board's app-configured
  **ABAB / ABBA** pair is retained in nonvolatile storage.
- Arming includes settling; keep the assembly still until Armed. Movement while
  Armed can produce a warning followed by lights, siren and bounded motor bursts.
- App Disarm or the configured physical disarm sequence stops the alarm outputs
  and motor. Physical controls remain available without the phone connected.
- Holding both physical buttons for about **500 ms during Alarm** inhibits the
  motor while the siren continues; enter the disarm code to silence/disarm.
- Enrollment: while Disarmed, hold both buttons for **3 to less than 8 seconds**
  and release them to open a 60-second window. The app claims/authenticates its
  owner connection. Existing ownership is not replaced just by opening a window.

Use the empty drum and keep fingers clear for initial tests. Disconnect power if
the mechanism binds. The firmware commands PWM; it has no shaft encoder, current
measurement or confirmed parked angle. Its bounded policy is eight 800 ms bursts
with 1200 ms gaps, including a 150 ms full-PWM start in each burst. Holding both
buttons inhibits subsequent motor operation until disarm.

## Live motion in the app

Authenticated connected phones receive nominal **5 Hz** measurements. The chart
plots `abs(norm(acceleration) - 1 g)` and generic sustained-movement flags. These
can be demonstrated while **Disarmed**, without running the siren or drum.
The plotted quantity is magnitude deviation, not reconstructed linear
acceleration or an event peak. Motion/rotation does not prove baggage damage.

Samples and markers use device elapsed time, not fabricated UTC. They have
separate boot-local sequences. The phone shows gaps on interruption and keeps a
bounded active-session history of up to 3,000 samples. The presentation build
starts a fresh trip/history after a full app close/reopen, while preserving
ownership and connection settings.

Firmware capabilities cover snapshot/ownership/authentication, button sequence
changes, guard control, live motion and live movement records. Measurements are
transmitted during an authenticated BLE connection.

## Reproduce and interpret the evidence

Build/upload instructions and the demonstrated image fingerprint are in
[`firmware/README.md`](../firmware/README.md). This submission preserves current
firmware code and its motor/IMU modules without changing their bytes. Required
third-party libraries are fetched by the Atech/PlatformIO toolchain.

The latest live-motion firmware passed build/static checks, normal upload hash
verification, retained-owner phone streaming, actual movement markers, reconnect
gaps and quiet Arm/Armed/Disarm checks. Its build included **62 compile-time
assertions**. The **22 latest full application/protocol host scenarios compiled
only**; the Windows host runner was blocked, not passed.

Earlier assembled-hardware testing covered empty-drum rotation, alarm outputs,
app/physical disarm, changed physical codes and operation without an active phone
connection. Those physical checks retain their earlier firmware scope; the
streaming and quiet guard checks above apply to the live-motion firmware.
