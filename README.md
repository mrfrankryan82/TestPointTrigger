# Mobile Surgery — Modular Bench Suite (v3.7.0)

A Windows bench toolkit for phone board work, built as one shell with pluggable modules. Formerly **TestPoint Trigger**.

## Modules
- **USB Hub Trigger** — disable a dedicated USB hub, then re-enable it on a global hotkey (Ctrl+Alt+E), voice command or countdown so the phone enumerates fresh in EDL/BROM while you hold the test point.
- **Phone Jig** — front end for the Arduino Mega boot-mode jig: guided wiring checks with a live diagram, every jig command as a button, automatic per-phone profiling over ADB/fastboot, and the **Hardware History** tab (profile database + Google-Sheets-ready workbook export).
- **ADB / Fastboot** — adb, fastboot, download-mode and recovery operations plus a raw command console.
- **Live Coach** — camera + USB watcher that coaches boot-mode entry (vision is advisory, USB VID/PID detection is authoritative).
- **PCB Pad Finder** — finds and numbers gold and white/tinned test pads on motherboard photos; exports a labelled PNG and CSV checklist.
- **Repair DB** and **Tool Licences** — job history and tool-licence/expiry tracking.
- **Logs** — one append-only log across sessions, with search, filters, live tail and integrity check.

## Restyling in Visual Studio Design View
Every window, module page and dialog has a `.Designer.cs` file, so the whole UI can be edited visually:

| What | Open in Design View |
|---|---|
| Shell (sidebar, logo, nav, header, footer) | `Modules/ModuleHostForm.cs` |
| Module pages | `Modules/Views/*View.cs` |
| PCB Pad Finder | `MainForm.cs` |
| Dialogs | `Modules/LicenseEditForm.cs`, `Modules/RepairEditForm.cs`, `Modules/PromptForm.cs`, `HelpForm.cs` |

What you set in the designer is what runs: the old runtime theme pass no longer overrides designer colours. Each module finds its controls by name, so rename a control only together with its module code. A few things are data-driven and created at runtime: the Phone Jig boot-mode buttons (they copy the look of the **OFF (all safe)** button), wiring-step rows, and grid rows.

## Data
Settings, logs and databases stay in `%LOCALAPPDATA%\TestPointTrigger` so data from earlier versions keeps loading. Device workbooks export to `My Drive\MobileSurgery` (Google Drive for desktop) or `Documents\MobileSurgery`.

## Build
Visual Studio 2022+ or `dotnet build -c Release`; .NET Framework 4.8, x64. Output: `MobileSurgery.exe`. Run as Administrator for the USB Hub Trigger.

---
Developer: **HaKDMoDz™** · v3.7.0 · 2026-10-09
