# Local motor driver

Based on the installed Atech 1.0.0a12 `dc_motor` module, inspected 2026-11-14.
The catalog instance still takes IN1 from Line B and IN2 from Line A. Atech port
resolution supplies the pins. No pin numbers or assumed supply voltage are added.

Changes for this project:

- Use the Arduino-ESP32 3.x pin-based LEDC API; original calls target core 2.x.
- Report PWM attachment readiness and leave outputs low if setup fails.
- Disable the inactive input before writing the active PWM input.
- Omit raw, unbounded USB motor actions. Bench controls own every motor pulse.
- Keep the public direction/coast/brake API, with readiness/command getters.

`isReady()` confirms software PWM setup only. There is no electrical readback of
motor presence, motor current, shaft speed or direction. `getSpeed()` is a command.
