# TestPoint Trigger

![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET Framework 4.8](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4.svg)
![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D6.svg)

A small WinForms (.NET Framework 4.8) tool for the classic "test point"
problem when forcing a phone into Qualcomm EDL (BROM) mode: you need three
things happening at once — tweezers bridging the test point to ground,
the battery clip connected, and the USB cable seated — but you only have
two hands.

This app lets you plug the USB cable in ahead of time, but keep Windows
from seeing it as connected, then flip it "live" with a single keypress
(or a countdown) once your tweezers and battery clip are already in
position — so the only thing that has to happen at the last instant is a
key press instead of a physical USB insertion.

Two global hotkeys drive it (they work even without the window focused,
since your hands are full): **Ctrl+Alt+D** disables/arms the selected
hub, **Ctrl+Alt+E** re-enables it (the "go" trigger). A single toggle
button mirrors the same two actions, and its icon (and the app/tray icon)
switch between green (enabled) and red (disabled) so the current state is
visible at a glance — including from the system tray if the window is
minimized. A **Help** button in the app opens a quick reference plus a
link back to this README.

## How it works

It doesn't touch the phone or the cable at all. It disables the specific
USB hub / host controller device node the phone's cable is plugged into
(via the built-in `pnputil /disable-device`), then re-enables it
(`pnputil /enable-device`) on your signal. Re-enabling makes Windows
re-enumerate everything on that hub from scratch — electrically identical
to a fresh plug-in — even though the cable never moved.

## Important safety notes

- **Use a dedicated/spare USB hub for the phone cable.** Disabling a hub
  or host controller disables *everything* downstream of it — if your
  keyboard or mouse shares that same root hub, they'll go dead too until
  you re-enable it. A $5 USB hub plugged into a rarely-used port, with
  only the phone cable in it, is the safe way to do this.
- The app must run **as Administrator** — the manifest is already set to
  request elevation, so Windows will prompt once at launch.
- **"Re-enable All"** and the close-time prompt exist as a safety net —
  if anything goes wrong mid-session, use that button (or Device Manager
  directly) rather than leaving a hub disabled.
- Disabling a device node stops Windows from binding a driver to it and
  from enumerating it, but whether it also drops VBUS/power on that port
  depends on the specific host controller/hub chipset — behavior isn't
  100% uniform across PCs. If your target SoC needs the data lines truly
  floating (not just "undetected by Windows") until the trigger moment,
  a physical inline switch between the port and the phone — the same
  idea as a commercial "deep-flash"/EDL test-point cable, just a
  pushbutton or relay wired into D+/D-/VBUS — is the more reliable
  option. That's a small enough build (and a good fit for something like
  an ESP8266 or just a momentary switch + relay) if the software-only
  approach doesn't behave consistently on your hardware — happy to help
  design that circuit if you want a belt-and-suspenders version.

## Administrator elevation

The app is set up two ways to make sure it always runs elevated (pnputil
needs it):

1. `app.manifest` declares `requireAdministrator`, so Windows normally
   shows the UAC prompt (or blocks the launch) before the app's code ever
   runs at all.
2. As a failsafe, `Program.cs` also checks at startup whether the process
   is actually running as Administrator. If it isn't (e.g. the manifest
   didn't apply for some reason), it relaunches itself with the `runas`
   verb — which pops the UAC prompt — and the non-elevated copy exits. If
   you click "No" on that prompt, it shows a short message explaining
   admin is required instead of continuing to run without it.

## Build

Requires the ".NET desktop development" workload in Visual Studio 2022
(or the .NET Framework 4.8 targeting pack + `dotnet` CLI).

```
cd TestPointTrigger
dotnet build -c Release
```

or just open `TestPointTrigger.sln` in Visual Studio and hit Build/Run (F5).
The output `TestPointTrigger.exe` lands in
`TestPointTrigger\bin\x64\Release\net48\`.

Run the .exe directly (double-click, or right-click → Run as
administrator if UAC doesn't prompt automatically).

## Usage

1. Plug your dedicated USB hub into the PC; plug the phone's USB cable
   into that hub (phone can be powered off / battery disconnected at
   this point).
2. Launch the app (it will prompt for admin elevation).
3. Click **Refresh**, then select that hub from the dropdown. (The list
   shows everything under Device Manager's "Universal Serial Bus
   controllers" node — hubs and host controllers.)
4. Set the countdown seconds — this is just a fallback in case you don't
   hit the hotkey; pick something generous (10-20s) while you're still
   getting the hang of the test-point contact.
5. Click the toggle button (shows a green "Enabled" icon), or press
   **Ctrl+Alt+D** (skips the confirmation dialog, since pressing a hotkey
   is already deliberate). The hub goes dark to Windows, the button/tray
   icon turn red, and the countdown starts.
6. Get your tweezers on the test point and the battery clip connected.
   The USB cable is already seated, so nothing else needs plugging.
7. The instant contact feels solid, press **Ctrl+Alt+E** (works
   globally, no need to click into the window first) or click the toggle
   button again (now showing red "Disabled"). This re-enables the hub,
   and Windows re-enumerates the phone as if it were just plugged in.
8. Check Device Manager / QFIL / your flash tool for the EDL (Qualcomm
   HS-USB QDLoader 9008) or BROM port.

If it doesn't show up, re-arm and try again — test point contact timing
usually takes a few attempts to get consistent regardless of tooling.

## Sound on enable

When the hub is successfully re-enabled (the trigger fires — countdown,
hotkey, or button), the app plays
`C:\Users\User\Downloads\hardware_inserted\hardware_inserted.wav` as an
audible confirmation, so you don't have to be looking at the screen at
the exact moment. If that file isn't there, it's just skipped (logged,
not a crash) — the trigger itself isn't affected either way. Change the
path in `MainForm.cs` (`EnabledSoundPath`) if you keep the wav somewhere else.

## Help button and status icons

The **Help** button (top right) opens a short in-app reference with a
link back to this README on GitHub. The same green/red icon is used in
three places so the state is always obvious: the toggle button, the
window/taskbar icon, and a system tray icon (with a right-click menu for
Show / Disable & Arm / Enable Now / Re-enable All / Exit). Icons live in
`TestPointTrigger\Assets\` and are copied next to the built exe
automatically.

## Developer credit / versioning

The main window and the Help dialog both show a footer:
`HaKDMoDz™ • v<version> • <date>`. The version comes from the csproj's
`<Version>` (bump it there for a release) and the date is a constant in
`AppInfo.cs` — update both together when a meaningful change ships.

## License

MIT — see [LICENSE](LICENSE).
