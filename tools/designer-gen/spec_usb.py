from theme import *

def build(out):
    def row(name, kids, cell, **kw):
        return flow(name, kids, cell=cell, AutoSizeMode=enum("AutoSizeMode.GrowAndShrink"), Margin=pad(0, 2, 0, 2), **kw)
    brown = col(74, 48, 0)
    restart = Ctl("btnRestartAdmin", "Button", {
        "AutoSize": TRUE, "BackColor": col(150, 82, 0), "FlatStyle": enum("FlatStyle.Flat"),
        "Font": font("Segoe UI Semibold", 9.5, "Bold"), "ForeColor": named("White"), "Padding": pad(6, 2, 6, 2),
        "Text": s("Restart as Administrator"), "UseVisualStyleBackColor": FALSE},
        extra=["{n}.FlatAppearance.BorderColor = " + col(110, 58, 0) + ";",
               "{n}.FlatAppearance.MouseOverBackColor = " + col(175, 98, 0) + ";"])
    banner = row("flpAdminBanner", [
        lbl("lblAdminWarning", "Not running as Administrator — disabling/enabling hubs needs admin rights.",
            fore=brown, Margin=pad(0, 8, 8, 0)), restart], (0, 0),
        BackColor=col(255, 244, 206), ForeColor=brown, Padding=pad(6))
    devrow = row("flpDeviceRow", [cbo("cboDevices", Size=size(520, 25)), btn("btnRefresh", "Refresh")], (0, 2))
    cdrow = row("flpCountdownRow", [
        lbl("lblCountdown", "Countdown (seconds, fallback if you don't hit the hotkey):", Margin=pad(0, 6, 4, 0)),
        nud("nudSeconds", 3, 120, 10, Size=size(60, 24)),
        lbl("lblTimeLeftCaption", "Time left:", Margin=pad(24, 6, 0, 0)),
        lbl("lblTimeLeft", "--", Font=font("Segoe UI", 20, "Bold"), Margin=pad(8, 0, 0, 0), MinimumSize=size(90, 0)),
    ], (0, 3))
    toggle = Ctl("btnToggle", "Button", {
        "BackColor": col(40, 150, 85), "FlatStyle": enum("FlatStyle.Flat"), "Font": font("Segoe UI", 10, "Bold"),
        "ForeColor": named("White"), "Size": size(380, 52), "Text": s("Enabled — click to Disable && Arm"),
        "TextImageRelation": enum("TextImageRelation.ImageBeforeText"), "UseVisualStyleBackColor": FALSE})
    reall = btn("btnReenableAll", "Re-enable All", AutoSize=FALSE, Size=size(180, 52), Padding=pad(0))
    actrow = row("flpActionRow", [toggle, reall], (0, 4))
    voicerow = row("flpVoiceRow", [
        chk("chkVoice", "Voice trigger", Margin=pad(0, 6, 12, 0)),
        lbl("lblSayAny", "Say any of:", Margin=pad(0, 6, 4, 0)),
        txt("txtPhrases", Size=size(220, 24), Text=s("go, trigger, enable now")),
        lbl("lblMinConfidence", "Min confidence %:", Margin=pad(12, 6, 4, 0)),
        nud("nudConfidence", 40, 99, 70, Size=size(55, 24)),
    ], (0, 5))
    hint = lbl("lblHotkeyHint",
               "Global hotkeys (work from any module, even unfocused): Ctrl+Alt+D disable/arm, Ctrl+Alt+E enable now.\r\n"
               "If your keyboard or a USB microphone shares the hub you disable, that trigger dies with it — the countdown is your fallback.",
               cell=(0, 6), fore=DIMGRAY, Margin=pad(0, 6, 0, 6))
    log = txt("txtLog", cell=(0, 7), readonly=True, Dock=enum("DockStyle.Fill"), Font=font("Consolas", 10),
              Multiline=TRUE, ScrollBars=enum("ScrollBars.Vertical"))
    root = tlp("tlpRoot", [("Percent", 100)], [("AutoSize", None)] * 7 + [("Percent", 100)],
               [banner, lbl("lblDevice", "USB hub / controller to arm:", cell=(0, 1), Margin=pad(0, 8, 0, 2)),
                devrow, cdrow, actrow, voicerow, hint, log], Padding=pad(12))
    emit("UsbTriggerView", "TestPointTrigger.Modules.Views", "UserControl", view_root(), [root],
         out + r"\Modules\Views\UsbTriggerView.Designer.cs")
