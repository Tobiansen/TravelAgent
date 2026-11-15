# Firmware dependency notices

This directory preserves the upstream license/NOTICE files available in the
dependency caches used for the demonstrated firmware. Their bytes are unchanged;
subdirectories retain upstream relative paths where there are nested notices.
No toolchain binaries, generated libraries or dependency source trees are vendored.
The repository's first-party licensing does not replace these upstream terms.

| Component | Demonstrated version | License / retained files | Upstream provenance |
| --- | --- | --- | --- |
| Atech Python SDK | 1.0.0a12 | Installed package metadata and the public release declare MIT. No standalone SDK license file was present in the installed wheel; none is fabricated here. | [Exact PyPI release](https://pypi.org/project/atech/1.0.0a12/). The copied motor/IMU module READMEs retain their Atech-derived provenance. |
| Arduino-ESP32 framework | PlatformIO package 4.30312.261024, Arduino core 3.3.12 | Package metadata declares LGPL-2.1-or-later. The installed package contains no framework-root LICENSE/COPYING. The standard LGPL-2.1 terms are supplied separately in `standard-licenses/LGPL-2.1.txt`. Available component notices `cores/esp32/libb64/LICENSE` and `libraries/BLE/LICENSE` + `NOTICE` are retained under `arduino-esp32/`; they cover their respective components, not the entire framework. | [Espressif Arduino-ESP32](https://github.com/espressif/arduino-esp32), fetched by PlatformIO. |
| NimBLE-Arduino, including bundled Apache Mynewt NimBLE | 2.5.2 | Apache-2.0. `nimble-arduino/LICENSE`, `NOTICE`, and `src/nimble/LICENSE` + `NOTICE`. Preserve both levels of copyright/attribution. | [NimBLE-Arduino](https://github.com/h2zero/NimBLE-Arduino). |
| TinyCrypt bundled with NimBLE-Arduino | Version supplied inside NimBLE-Arduino 2.5.2; no separate version is asserted | BSD-3-Clause. `nimble-arduino/src/nimble/ext/tinycrypt/LICENSE`. | [Bundled TinyCrypt source location](https://github.com/h2zero/NimBLE-Arduino/tree/2.5.2/src/nimble/ext/tinycrypt). |
| ArduinoJson | 7.4.2 | MIT. `arduinojson/LICENSE.txt`. | [ArduinoJson](https://github.com/bblanchon/ArduinoJson). |
| Adafruit NeoPixel | 1.15.5 | LGPL-3.0-or-later. `adafruit-neopixel/COPYING` is the full upstream file from the installed release. It incorporates GPL-3.0, whose standard terms are supplied in `standard-licenses/GPL-3.0.txt`. | [Adafruit NeoPixel](https://github.com/adafruit/Adafruit_NeoPixel). |

Additional Arduino package notices are preserved as an inventory of the installed
package; their presence does not assert that every package component was linked
into this firmware. The libb64 file contains its upstream public-domain dedication;
the framework BLE sublibrary carries its own Apache license and attribution.
Those component notices do not replace the framework-wide LGPL terms.

## Standard GNU license texts

The following full, unmodified standard license texts were downloaded directly
from GNU on 15 November 2026. They are explicitly separate from the exact cached
component notices above; no missing framework or SDK copyright notice has been
invented or represented as recovered from the cache.

| File | Authoritative source | SHA-256 |
| --- | --- | --- |
| `standard-licenses/LGPL-2.1.txt` | [GNU LGPL 2.1 text](https://www.gnu.org/licenses/old-licenses/lgpl-2.1.txt) | `20E50FE7AAE3E56378EBF0417D9DE904F55A0E61E4DF315333E632A4D3555D95` |
| `standard-licenses/GPL-3.0.txt` | [GNU GPL 3.0 text](https://www.gnu.org/licenses/gpl-3.0.txt) | `3972DC9744F6499F0F9B2DBF76696F2AE7AD8AF9B23DDE66D6AF86C9DFB36986` |

## Source, rebuilding and binary releases

The maintained firmware, modified local modules, dependency versions and build
instructions are in [`firmware/README.md`](../../firmware/README.md). The Python
lock file pins the SDK and PlatformIO environment; `project.yaml` pins the two
direct BLE/JSON libraries. The firmware guide records the tested framework,
platform and NeoPixel versions as well. Upstream source can be obtained from the
versioned package releases and repositories above rather than from this notice
directory.

Compiled firmware redistribution must retain the applicable licenses and notices,
with corresponding source and build or relinking materials as required by each
dependency, including the LGPL components.
