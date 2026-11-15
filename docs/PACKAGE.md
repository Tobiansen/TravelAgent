# Publication package

This is the clean source package for
[Tobiansen/travelagent-crumbguard](https://github.com/Tobiansen/travelagent-crumbguard).
Local Git has been initialized with that public repository as its destination.
Assembling this snapshot did not modify the working app, board, installed phone
build or laptop gateway. Binary release attachments, the demo video and the
event submission have separate publication steps.

## Included

- Unity application assets, metadata, native Android plugin, package lockfiles,
  project settings and tests.
- The Python Exa gateway, its tests and the Windows double-click launcher. These
  belong in the source submission because the live research flow depends on them.
- Current board code and both required local motor/IMU module directories,
  dependency snapshot and host harness source.
- Curated public setup, demo, architecture, hardware and validation docs; generic
  app screenshots; a scoped MIT license and preserved third-party notices.
- Verified APK/firmware files in local, Git-ignored `dist/`, ready for a later
  release. Their filenames, sizes and hashes are in [RELEASES.md](RELEASES.md).

## Deliberately excluded

Unity caches, generated builds/toolchains, Python environments/caches, existing
Git history, internal agent instructions, captured serial logs, real ticket text,
passenger information, photos, trip archives, owner credentials, encrypted local
gateway tokens, signing keys and raw recordings are not publication source.
Old firmware experiments and the obsolete mechanical proposal are omitted.

The two actual STL print layouts have been added under `mechanical/stl` and
checked for parseability, bounds and watertight surfaces. Editable Fusion/STEP
source remains optional follow-up; `mechanical/README.md` records that gap.
The final demo-video link will be added when it exists.

## Packaging-only changes

The copied Unity cloud project/account linkage is blank. The copied desktop
launcher's Help points to the new public setup guide. Runtime application source,
saved interface, native plugin and firmware logic are unchanged. Source folder
relationships required by Unity, the launcher and firmware were preserved.

## Checks on this copy

- 55 gateway tests passed without a real Exa call.
- 72 launcher core and 37 supervisor fixture checks passed in Windows PowerShell.
- Read-only launcher diagnostics found Python and the active physical network;
  no new gateway started and no existing credential was decrypted or changed.
- The private session is intentionally absent in this copy. A fresh user must
  configure their own key and phone/gateway token as described in Setup.
- Secret-pattern, personal-evidence, metadata, local-link and generated-file
  checks found no public-source blocker. Such scans are a review aid, not a
  guarantee that arbitrary future additions are safe to publish.
- The prepared binary hashes match the documented installed app and deployed
  firmware. The copied firmware/dependency files match the original snapshot.

The included GitHub Actions workflow runs the gateway and Windows launcher
fixture suites. Its results are recorded per workflow run in the public
repository's Actions tab. Unity and physical-board checks retain the
build-specific scope in [TESTING.md](TESTING.md).

For subsequent updates, check the actual CAD inventory, team/title credit and
final source tree. Maintain this clean package rather than pushing the working
development folders wholesale.
