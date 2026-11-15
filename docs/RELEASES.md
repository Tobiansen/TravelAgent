# Release files

The public source repository is
[Tobiansen/travelagent-crumbguard](https://github.com/Tobiansen/travelagent-crumbguard).
The Android build is available in the [v0.1.0-junction2026 prerelease](https://github.com/Tobiansen/travelagent-crumbguard/releases/tag/v0.1.0-junction2026).
The locally assembled `dist/` directory is intentionally ignored by Git. The
firmware binary stays local pending review of upstream build and
redistribution materials. Firmware source and SDK build instructions are included.
See [GitHub's release documentation](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases).

## Download

- [TravelAgent-Android-demo.apk](https://github.com/Tobiansen/travelagent-crumbguard/releases/download/v0.1.0-junction2026/TravelAgent-Android-demo.apk)
- [TravelAgent-Android-demo.apk.sha256](https://github.com/Tobiansen/travelagent-crumbguard/releases/download/v0.1.0-junction2026/TravelAgent-Android-demo.apk.sha256)
- [TravelAgent-license-notices.zip](https://github.com/Tobiansen/travelagent-crumbguard/releases/download/v0.1.0-junction2026/TravelAgent-license-notices.zip)

GitHub's server-reported SHA-256 digests matched the corresponding local files
before the prerelease was published.

| File | Bytes | SHA-256 | Availability |
| --- | ---: | --- | --- |
| TravelAgent-Android-demo.apk | 59,230,235 | `CDCE55B9464D972FF13D2DDEAF0B822156BE6B9E402F80277207BDA1C294C1B7` | Published prerelease download |
| CrumbGuard-ESP32S3-firmware.bin | 962,352 | `52321AC011195951717B719B7DCEEB498045A7ABD1069EC96A2B02D7126E27F5` | Local only; upstream-material review pending |

The APK is the installed ARM64 development build. It contains no runtime trips,
owner credentials or Exa API key. The firmware binary alone is not a complete
generic flash bundle: use the source/SDK upload workflow for bootloader and
partition handling. No firmware binary download is published.
