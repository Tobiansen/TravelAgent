# Release files

The public source repository is
[Tobiansen/travelagent-crumbguard](https://github.com/Tobiansen/travelagent-crumbguard).
Its APK release attachment is not published yet. The locally assembled `dist/`
directory is intentionally ignored by Git. The APK is prepared for a download
release; the firmware binary stays local pending review of upstream build and
redistribution materials. Firmware source and SDK build instructions are included.
See [GitHub's release documentation](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases).

| File | Bytes | SHA-256 | Availability |
| --- | ---: | --- | --- |
| TravelAgent-Android-demo.apk | 59,230,235 | `CDCE55B9464D972FF13D2DDEAF0B822156BE6B9E402F80277207BDA1C294C1B7` | Prepared APK release; not published yet |
| CrumbGuard-ESP32S3-firmware.bin | 962,352 | `52321AC011195951717B719B7DCEEB498045A7ABD1069EC96A2B02D7126E27F5` | Local only; upstream-material review pending |

The APK is the installed ARM64 development build. It contains no runtime trips,
owner credentials or Exa API key. The firmware binary alone is not a complete
generic flash bundle: use the source/SDK upload workflow for bootloader and
partition handling. An APK download link will be added after its release is
published; the table above identifies the prepared files, not available downloads.
