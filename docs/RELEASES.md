# Release files

The public source repository is
[Tobiansen/travelagent-crumbguard](https://github.com/Tobiansen/travelagent-crumbguard).
Its binary release attachments are not published yet. The locally assembled
`dist/` directory is intentionally ignored by Git; the verified files below are
prepared for GitHub Release attachment rather than source history.
See [GitHub's release documentation](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases).

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| TravelAgent-Android-demo.apk | 59,230,235 | `CDCE55B9464D972FF13D2DDEAF0B822156BE6B9E402F80277207BDA1C294C1B7` |
| CrumbGuard-ESP32S3-firmware.bin | 962,352 | `52321AC011195951717B719B7DCEEB498045A7ABD1069EC96A2B02D7126E27F5` |

The APK is the installed ARM64 development build. It contains no runtime trips,
owner credentials or Exa API key. The firmware binary alone is not a complete
generic flash bundle: use the source/SDK upload workflow for bootloader and
partition handling. A download link will be added after the attachments are
published; the table above identifies the prepared files, not available downloads.
