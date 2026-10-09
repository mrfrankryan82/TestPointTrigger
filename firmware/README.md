# Phone Boot Jig firmware

Two builds of the same jig firmware (v2.2.0), for the Phone Jig module in TestPoint Trigger.
Both use the same serial commands at 115200 baud and answer the module's wiring checks
(`ident`, `pins`, `sense`, `selftest`, `uart loop`, `uart listen`).

| Signal | Uno (`PhoneBootController_Uno`) | Mega 2560 (`PhoneBootController_Mega`) |
|---|---|---|
| Test point / Vol+ / Vol- / Power (pull-low only) | D2 / D3 / D4 / D5 | D22 / D23 / D24 / D25 |
| VCC_EN / BTEMP / VBUS from PC | D6 / D7 / D8 | D26 / D27 / D28 |
| TS3USB221 OE# (via 1k/2k divider) | D9 | D30 |
| TS3USB221 S | tie to GND | tie to GND |
| Phone UART TX / RX (via level shifter) | D11 / D12 (SoftwareSerial) | D18 / D19 (Serial1) |
| VCC / VBUS sense (10k/10k dividers) | A0 / A1 | A0 / A1 |
| Busy LED | D13 | D13 |
| UART loop-test jumper | D11 to D12 | D18 to D19 |

Notes:
- No USB Host Shield or ADB in 2.2.0. ADB runs from the PC (ADB / Fastboot module).
  The 2.1.0 Mega sketch included an `adb.h` that is not part of the USB Host Shield 2.0
  library, so it never compiled.
- Uno phone UART: transmitting at 115200 is fine; receiving at 115200 is best effort and
  may drop the odd character in a fast boot log. 57600 and below is solid.
- Both sketches compile (`arduino:avr:uno`, `arduino:avr:mega`) but are not yet bench-tested.

Developer: HaKDMoDz™ · v2.2.0 · 2026-10-09
