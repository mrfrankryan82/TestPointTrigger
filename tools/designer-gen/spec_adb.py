from theme import *

ADB = [("btnAdbReboot", "Reboot"), ("btnAdbRecovery", "→ Recovery"), ("btnAdbBootloader", "→ Bootloader"),
       ("btnAdbDownload", "→ Download"), ("btnAdbEdl", "→ EDL (9008)"), ("btnAdbInstall", "Install APK…"),
       ("btnAdbUninstall", "Uninstall pkg…"), ("btnAdbPush", "Push file…"), ("btnAdbPull", "Pull file…"),
       ("btnAdbSideload", "Sideload OTA…"), ("btnAdbLogcat", "Logcat"), ("btnAdbScreencap", "Screencap…"),
       ("btnAdbShell", "Interactive shell"), ("btnAdbGetprop", "getprop"), ("btnAdbPackages", "List packages"),
       ("btnAdbBattery", "Battery"), ("btnAdbScreenSize", "Screen size"), ("btnAdbTcpip", "Wi-Fi: tcpip 5555"),
       ("btnAdbConnect", "Wi-Fi: connect…"), ("btnAdbBackup", "Backup…"), ("btnAdbRestore", "Restore…"),
       ("btnAdbKillServer", "Kill server"), ("btnAdbStartServer", "Start server")]
FB = [("btnFbGetvar", "getvar all"), ("btnFbUnlock", "Unlock"), ("btnFbOemUnlock", "OEM unlock"), ("btnFbLock", "Lock"),
      ("btnFbReboot", "Reboot"), ("btnFbBootloader", "→ Bootloader"), ("btnFbRecovery", "→ Recovery"),
      ("btnFbFastbootd", "→ fastbootd"), ("btnFbBootImg", "Boot img (temp)…"), ("btnFbFlashPartition", "Flash partition…"),
      ("btnFbFlashBoot", "Flash boot…"), ("btnFbFlashRecovery", "Flash recovery…"), ("btnFbErase", "Erase partition…"),
      ("btnFbFormat", "Format partition…"), ("btnFbActiveA", "Set active a"), ("btnFbActiveB", "Set active b"),
      ("btnFbWipe", "Wipe (-w)"), ("btnFbContinue", "Continue")]
DL = [("btnDlSamsung", "Enter Download (Samsung)"), ("btnDlEdlAdb", "Enter EDL (adb)"), ("btnDlEdlFastboot", "Enter EDL (fastboot)"),
      ("btnHeimdallDetect", "Heimdall: detect"), ("btnHeimdallPit", "Heimdall: print-pit"),
      ("btnHeimdallFlash", "Heimdall: flash partition…"), ("btnRecReboot", "Recovery: reboot"),
      ("btnRecSideload", "Recovery: sideload OTA…")]

def build(out):
    def page(name, title, flpname, kids):
        f = flow(flpname, kids, AutoSize=FALSE, AutoScroll=TRUE, Padding=pad(4))
        return tabpage(name, title, [f], Padding=pad(3))
    bar = flow("flpToolBar", [
        lbl("lblToolStatus", "Tools:", Margin=pad(0, 6, 12, 0)),
        btn("btnSetTools", "Set platform-tools…"), btn("btnRefreshDevices", "Refresh devices"),
        lbl("lblDevice", "Device:", Margin=pad(8, 6, 4, 0)), cbo("cboDevices", Size=size(260, 25)),
        btn("btnAdbDevices", "adb devices"), btn("btnFastbootDevices", "fastboot devices")], cell=(0, 0))
    note = lbl("lblVendorNote", "Samsung Odin and Qualcomm QFIL/EDL flashing are vendor GUIs.\r\n"
               "Use these to ENTER the mode; use Heimdall (open Odin) or QFIL to flash.", fore=DIMGRAY, Margin=pad(4, 10, 4, 4))
    t = tabs("tabActions", [
        page("tabAdb", "ADB", "flpAdb", [btn(n, x) for n, x in ADB]),
        page("tabFastboot", "Fastboot", "flpFastboot", [btn(n, x) for n, x in FB]),
        page("tabDownload", "Download / Recovery", "flpDownload", [btn(n, x) for n, x in DL] + [note])], cell=(0, 1))
    console = txt("txtConsole", cell=(0, 2), readonly=True, fore=named("Gainsboro"), Dock=enum("DockStyle.Fill"),
                  Font=font("Consolas", 9.5), Multiline=TRUE, ScrollBars=enum("ScrollBars.Both"), WordWrap=FALSE)
    cmd = tlp("tlpCommandBar", [("Percent", 100), ("AutoSize", None), ("AutoSize", None), ("AutoSize", None)],
              [("AutoSize", None)], [
                  txt("txtCommand", cell=(0, 0), Dock=enum("DockStyle.Fill"), Font=font("Consolas", 9.5)),
                  btn("btnRun", "Run / Send", cell=(1, 0)), btn("btnStop", "Stop", cell=(2, 0)),
                  btn("btnClear", "Clear", cell=(3, 0))], cell=(0, 3), AutoSize=TRUE)
    root = tlp("tlpRoot", [("Percent", 100)], [("AutoSize", None), ("Percent", 42), ("Percent", 58), ("AutoSize", None)],
               [bar, t, console, cmd], Padding=pad(8))
    emit("AdbFastbootView", "TestPointTrigger.Modules.Views", "UserControl", view_root(), [root],
         out + r"\Modules\Views\AdbFastbootView.Designer.cs")
