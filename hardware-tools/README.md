# Firmware host checks

These C++17 harnesses exercise the same headers shipped in `../firmware/`.
They use inert host substitutes and do not open USB/Bluetooth or operate a motor.

| Source | Boundary exercised |
| --- | --- |
| `test_ble_core.cpp` | Framing, configuration, request admission and controller checks |
| `test_ble_storage.cpp` | Fault-injected NVS storage/commit/readback |
| `test_ble_guard.cpp` | Full BLE JSON dispatcher, application interactions and live-motion cases |
| `ble_test_stubs/` | Arduino/NVS substitutes for storage tests |
| `ble_guard_test_stubs/` | Arduino/platform substitutes for application tests |

Use a C++17 compiler. For example, from the submission root on Linux/macOS with
`c++` available:

```sh
c++ -std=c++17 -O1 hardware-tools/test_ble_core.cpp -o /tmp/travelagent-ble-core
c++ -std=c++17 -O1 -I hardware-tools/ble_test_stubs hardware-tools/test_ble_storage.cpp -o /tmp/travelagent-ble-storage
```

The application harness additionally needs **ArduinoJson 7.4.2** headers. Replace
`PATH_TO_ARDUINOJSON_SRC` with that library's `src` directory:

```sh
c++ -std=c++17 -O1 -I hardware-tools/ble_guard_test_stubs -I hardware-tools/ble_test_stubs -I PATH_TO_ARDUINOJSON_SRC hardware-tools/test_ble_guard.cpp -o /tmp/travelagent-ble-guard
```

Run the resulting executables only where the operating system permits them.
The latest demonstrated Windows environment blocked newly built host runners;
the **22 full application/protocol scenarios were compiled only**, not executed.
The firmware build's **62 compile-time assertions** passed. Earlier runtime
observations do not establish a pass for a subsequently changed runner.

This folder includes source and inert test substitutes. Compiler binaries,
compiled runners, raw device captures and generated dependency libraries are
excluded.
