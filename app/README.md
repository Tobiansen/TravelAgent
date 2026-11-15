# TravelAgent application

Open **this `app` folder** in Unity **6000.6.9f1** and open
`Assets/_App/Scenes/App.unity`. The scene, prefabs, theme and configuration are
saved assets. Press Play for the Editor demonstration, select Demo in Device,
then use Presenter controls → New demo for labelled illustrative events.

The phone build uses the real Bluetooth transport. Its laptop Exa connection is
included here in `Backend/`, `tools/` and `TravelAgent Demo.cmd`; keep those paths
together. There are no Python package dependencies for the gateway.

- [Setup and build](../docs/SETUP.md)
- [Demo route](../docs/DEMO.md)
- [Architecture](../docs/ARCHITECTURE.md)
- [Test scope and limitations](../docs/TESTING.md)
- [Firmware and wiring](../docs/HARDWARE.md)

The hackathon configuration starts a fresh active trip after a full app restart.
Home/resume keeps the current trip. Ownership, confirmed device settings and
encrypted gateway configuration remain saved. Set
`freshDemoSessionOnLaunch=false` in the AppConfiguration asset for durable-trip
behavior. Original evidence is archived locally, never shipped with this project.
