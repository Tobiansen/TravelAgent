# IMU sample-freshness extension

Copied from the installed Atech SDK 1.0.0a12 `icm40608` module. The package
declares the MIT license and author Atech. Original comments, chip detection,
sensor configuration and APIs are preserved.

This project adds a successful-read counter, last-success timestamp and failed
read counter to the existing background polling task. `hasFreshSample(300)`
checks successful read age rather than trusting the initialization-only
`isConnected()` flag. It counts I2C read operations, not unique chip data-ready
frames. The alarm refuses to arm without fresh/sane data and reports loss of
freshness after arming. The SDK installation has not been patched.
