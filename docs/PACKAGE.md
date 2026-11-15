# Source package

[TravelAgent](https://github.com/Tobiansen/TravelAgent) contains the Android application, laptop research gateway and device firmware.

| Path | Contents |
| --- | --- |
| app/Assets, app/Packages, app/ProjectSettings | Editable Unity project, Android plugin and pinned package configuration |
| app/Backend | Python Exa gateway and tests |
| app/tools | Windows launcher, Android helpers and validation scripts |
| firmware | Board code, motor/IMU modules and dependency versions |
| hardware-tools | Firmware host harnesses and stubs |
| mechanical/stl | Case and dispenser print layouts |
| media, docs | Screenshots and technical documentation |

Start with [Setup](SETUP.md), [Architecture](ARCHITECTURE.md), [Hardware](HARDWARE.md) or the [APK downloads](RELEASES.md). Build outputs and local runtime configuration are ignored by Git. Source licensing and component notices are retained in the repository.

Gateway and Windows launcher checks run in GitHub Actions; [this recorded run](https://github.com/Tobiansen/TravelAgent/actions/runs/43681174396) passed. Unity and physical validation are scoped in [Testing](TESTING.md).
