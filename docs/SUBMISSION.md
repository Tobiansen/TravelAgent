# Submission handoff

Public source repository: [Tobiansen/travelagent-crumbguard](https://github.com/Tobiansen/travelagent-crumbguard). This clean package contains the application, laptop gateway/launcher, current firmware and actual STL print layouts. Publishing source does not complete the event form, demo-video or Discord handoff.

## What belongs in the repository

| Include | Why |
| --- | --- |
| Unity `Assets`, `Packages`, `ProjectSettings` and `.meta` files | Lets reviewers open the actual saved application and reproduce dependencies |
| Native Android plugin, firmware and custom module source | Shows how the physical device and phone communicate |
| Exa gateway and Windows launcher source | Required to reproduce the live research flow; these are part of the project |
| Setup, architecture, demo and honest validation notes | Explains what to run, what is real and where the prototype ends |
| Final printable STLs and editable CAD, when available and shareable | Makes the team's physical mechanism reproducible |
| Selected clean screenshots, dependency locks and licenses/notices | Helps judges understand and reuse the work |

STLs are a useful hardware-project deliverable, not a universal requirement of every hackathon. The two actual case/dispenser print layouts are now included under `mechanical/stl`, with mesh bounds, body counts and a preview. Editable Fusion/STEP source is valuable in addition to printable meshes and can be added later; it has not yet been provided. An older prototype must not be labelled as the final design.

Do not publish credentials, native owner/session settings, real emails, user photos, local trip archives, recordings, machine-specific logs, Unity caches, build directories, virtual environments or unrelated experiments.

## Binaries and video

Keep source in Git. Use a versioned release for the APK and firmware binary, with checksums and build notes. GitHub describes releases as the place to package deployable software and binary downloads: [About releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases).

`dist/` is local staging, excluded from Git. Release attachments have not been published yet; [Release files](RELEASES.md) lists the prepared binary hashes. Do not copy a laptop's encrypted session token into a release. A new user configures their own key/session using the setup instructions.

Add the finished demo-video link after it exists. Avoid committing large raw takes; review the recording for names, booking references, tokens, notifications and unrelated screens before publishing it.

## Remaining publication and event handoff

- Confirm the title, team credit and redistribution rights; add the editable Fusion design if available.
- Review the source, copied settings, license boundaries and secret-scan result.
- Maintain the public repository from this clean tree; do not push the original development folders wholesale.
- Open the new repository while signed out and check that README links, screenshots and source downloads work.
- Attach the verified binaries to a release and add the actual demo-video link.

The [official Junction 2026 site](https://2026.hackjunction.com/) states in its ownership FAQ that submissions are open-sourced under MIT. The team's license cannot override third-party font, Unity, SDK or model licenses; preserve their notices and keep reference geometry distinct from team-created parts.

## Event handoff to confirm

The team's existing Atech challenge brief calls for meaningful live hardware, a public GitHub repository or exported Atech project, an approximately two-minute video/live demonstration, and a linked summary/post in Atech's Discord. Confirm the exact current fields and deadlines in the official Junction Platform before final submission. A community challenge mirror is background information, not the authoritative submission form.

For the Exa story, show the actual reporting-research request, useful official sources, required evidence and final traveler handoff. Do not attribute local ticket parsing to Exa, claim photo analysis or present fixtures as live research.

The public repository has been created. Binary attachments, a finished demo-video link, a summary post and the final event form remain separate tasks; this document does not mark them complete.
