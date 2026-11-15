# TravelAgent

TravelAgent links an Atech ESP32-S3 suitcase alarm to a Unity Android app over Bluetooth. The app controls arming and A/B button codes and shows live motion with event markers. While armed, the board responds to disturbances with lights, a siren and a motorized breadcrumb drum.

Ticket text shared from Gmail supplies the airline. After the traveler inspects the bag and confirms damage, Exa Search and Contents retrieve official reporting steps, photo requirements and the reporting page. Evidence photos stay local; the traveler completes the airline's form.

## Hardware

<table>
  <tr><th>Atech layout</th><th>Fusion design</th><th>Built device</th></tr>
  <tr>
    <td align="center"><a href="media/hardware/01-atech-layout.jpg"><img src="media/hardware/01-atech-layout.jpg" alt="TravelAgent electronics in Atech's board viewer" width="280"></a></td>
    <td align="center"><a href="media/hardware/02-fusion-design.png"><img src="media/hardware/02-fusion-design.png" alt="Fusion enclosure and drum assembly" width="280"></a></td>
    <td align="center"><a href="media/hardware/03-built-device.jpg"><img src="media/hardware/03-built-device.jpg" alt="Assembled TravelAgent device with wired drum motor" height="220"></a></td>
  </tr>
</table>

## Integrations

- **[Atech.dev](https://atech.dev/):** Its SDK builds the firmware, and its hardware models guided the Fusion enclosure and motor mount. We used the visual designer and board viewer to document the connections.
- **[Exa](https://exa.ai/):** The gateway uses Search to find official airline reporting guidance and Contents to check the cited pages. The app presents photo requirements and a direct reporting link.

## Run

- **Android:** download the [APK](https://github.com/Tobiansen/TravelAgent/releases/download/v0.1.0-junction2026/TravelAgent-Android-demo.apk).
- **Unity:** open `app/` in **Unity 6000.6.9f1**, load `Assets/_App/Scenes/App.unity` and press Play.
- **Hardware and Exa:** follow [Setup](docs/SETUP.md) and [Hardware](docs/HARDWARE.md). The Windows gateway starts with `TravelAgent Demo.cmd` and keeps the Exa key on the laptop.

## Source

| Folder | Contents |
| --- | --- |
| `app/` | Unity app, Android Bluetooth plugin, Python Exa gateway and Windows launcher |
| `firmware/` | ESP32-S3 firmware and custom motor/IMU modules |
| `mechanical/` | Case and dispenser STL print layouts |
| `hardware-tools/` | Firmware test harnesses |
| `docs/` | Setup, architecture, hardware and testing |

See [Architecture](docs/ARCHITECTURE.md) for the Bluetooth protocol and Exa integration.
