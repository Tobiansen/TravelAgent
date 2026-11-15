# Architecture

TravelAgent has three independently running parts: the Atech device, the Android application and a laptop Exa gateway. Bluetooth does not depend on the laptop or Exa.

## Device and phone

The current firmware combines a stationary-bag guard with an authenticated BLE interface. Physical A/B codes and motor inhibition remain available independently of the phone. The app can read current state, configure button sequences and request Arm/Disarm; it waits for a board execution acknowledgement and authoritative readback before presenting success.

The live extension supplies nominal **5 Hz** measurements and discrete generic movement records while the phone is connected and authorized. This works while Disarmed, allowing the timeline demonstration without activating the alarm outputs.

Each sample carries device/boot identity, a monotonic sequence and device elapsed time. The plotted value is:

```text
abs(norm(acceleration) - 1 g)
```

It is acceleration-magnitude deviation, not reconstructed linear acceleration or an impact peak. Rotation can also produce a movement marker through the detector's gyro input. A marker prompts inspection; it does not establish damage.

The phone rejects invalid, stale, reordered and mismatched-device/boot samples. Missing samples, disconnects and unavailable intervals create gaps. A new boot starts a new elapsed-time axis. The chart never invents a quiet line across a missing interval or assigns an unverified UTC time to a device-only event.

## Trip and evidence state

The Unity project uses saved scenes, prefabs, TextMeshPro and normal uGUI controls. Runtime composition wires the UI to replaceable hardware, research and photo adapters; Core contains the storage and protocol models.

Real and Demo use separate local stores. The phone retains at most **3,000** received motion samples, roughly ten minutes at 5 Hz, saving periodically and on pause. It does not download a flight-long device history after reconnecting.

The hackathon configuration enables `freshDemoSessionOnLaunch`:

- A full app reopen starts a blank trip on the first visit to each provider.
- A provider round trip within that app instance keeps its current work.
- Home/camera/Gmail pause and resume keep the active run.
- Prior trip records are archived; original photo files are retained outside the new active trip.
- Native ownership, configured board codes and encrypted gateway settings remain saved.

Previously consumed incoming-share IDs remain retired even if Android inbox cleanup needs retrying. Fresh consumers discard queued old BLE messages. An old cross-journey camera recovery is not automatically presented as a new-session photo.

Set `freshDemoSessionOnLaunch=false` in the saved AppConfiguration for the durable-trip behavior used by a normal application.

## Gmail handoff

Android accepts shared plain/HTML text or selected plain text. It does not access the Gmail account or read email attachments. The local ticket parser fills supported, unambiguous airline and route fields immediately; ambiguous or unsupported text needs manual entry.

**This extraction is local code, not Exa.** A new share creates its own trip. Temporary shared text is removed after successful import cleanup or explicit discard, and is excluded from reporting exports and research requests.

## Exa research

After the traveler confirms damage, the phone sends only five short policy-context fields to the Python gateway:

```text
airline · country · route · issueType · collectionDate
```

The reporting endpoint does not accept the email body, passenger name, booking reference, photographs, damage description or raw sensor history. The Exa key stays on the laptop. The phone uses a separate gateway-session bearer saved through Android Keystore.

The gateway uses Exa Search for structured reporting requirements and Contents for source verification. Results retain their actual live/cached/fixture status and official citations. Missing context and uncertain freshness remain visible. A source-grounded photo requirement opens local camera/import controls; it does not upload or analyse the image.

The earlier airline-identity endpoint remains in the gateway source for compatibility, but the app's Before flow does not call it.

## Laptop launcher

The Windows status window selects an active physical private-network interface, excluding virtual/VPN adapters. It starts an authenticated Python gateway or reuses a healthy existing one. It privately checks the saved bearer with an empty-context request that is rejected before any Exa research.

Health monitoring and bounded recovery apply to its own Python child. It never terminates an unrelated listener or trusts an old PID as proof of ownership. The encrypted session token survives a private-IP change; the phone's service URL must still be updated manually. HTTP is a development LAN arrangement; hosted HTTPS is future work.

## Airline handoff

The Finnair path can open the reviewed public delayed/damaged report selector when matching source metadata is available. Otherwise the sourced airline guidance remains the fallback. The traveler completes any airline form themselves. A local draft, export, page open or traveler-entered reference does not constitute automatic submission.
