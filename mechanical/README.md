# Mechanical design

The assembled prototype uses a suitcase-mounted enclosure and a horizontally
rotating perforated breadcrumb drum with a separately wired motor mount.

The two STL files below are the print layouts used for the assembled prototype.

## Print files

| File | Mesh bodies | Triangles | Overall export extents |
| --- | ---: | ---: | --- |
| [01_case_frame_covers.stl](stl/01_case_frame_covers.stl) | 4 | 5,972 | 216 × 204 × 16 |
| [02_dispenser_12mm_outlets.stl](stl/02_dispenser_12mm_outlets.stl) | 5 | 12,954 | 165 × 127 × 63 |

Each file is a **multi-body print layout**. Both parse as watertight meshes.
Body counts describe disconnected mesh shells.

STL has no unit metadata. The design uses millimetres; import
consistently as mm and verify the scale in the slicer. The first layout's large
case body is about 106 × 136 × 14 in export coordinates. Preserve the supplied
layout, or split bodies deliberately in the slicer.

![Supplied STL print layouts, not an assembled view](print-layouts.png)

Detailed bounds, connected bodies and SHA-256 values are in
[mesh-inventory.json](mesh-inventory.json).
