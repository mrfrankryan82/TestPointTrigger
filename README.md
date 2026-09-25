# TestPoint Trigger — PCB Pad Finder (v2.0.0)

Finds candidate **gold and white/tinned solder test pads** on a motherboard photo, numbers them in reading order and exports a labelled image plus a CSV elimination checklist.

> v2.0.0 is a full rewrite. The v1.x USB hub disable/re-enable trigger is kept in git history (tag `v1-usb-trigger`).

## Features
- Detection ported from the HaKDMoDz test-point-pad-detector skill (OpenCV): gold HSV band + bright/unsaturated tinned mask, roundness filter, and rejection of screw holes/"O" glyphs, silkscreen text on the bezel, and pads embedded in shield metal.
- Crop to board (exclude battery label/bezel) — detection runs only inside the crop.
- Live tuning sliders (debounced re-detect).
- Manual edits: left-click adds a pad that **snaps to its true centre**; right-click / Delete removes. Removed auto-pads stay removed after re-detect; duplicates merge automatically; Ctrl+Z undo.
- Label placement tries 8 positions around each pad and never covers a pad or another label.
- Styles: **Labels only** (pads untouched) or **Circles + labels**.
- Export upscaled PNG (1–6×) with credit footer, and CSV (`id,x,y,radius,source,circularity,probed,notes`).
- Open / drag-drop / paste (Ctrl+V); EXIF orientation of phone photos handled.

## Keys
`E` edit · `P`/`Space` pan · `C` crop · `F` fit · `D` detect · wheel zoom · middle-drag pan · `Ctrl+Z` undo · `Ctrl+S` export PNG

## Build
Visual Studio 2022 (or `dotnet build -c Release`), .NET Framework 4.8, x64. NuGet: OpenCvSharp4 + OpenCvSharp4.runtime.win.

---
Developer: **HaKDMoDz™** · v2.0.0 · 2026-09-23
