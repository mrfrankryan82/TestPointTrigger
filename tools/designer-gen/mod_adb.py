from patch import *
F = r"Modules\AdbFastbootModule.cs"
t = load(F)
t = replace1(t, "using Newtonsoft.Json;", "using Newtonsoft.Json;\r\nusing TestPointTrigger.Modules.Views;")
t = between(t, "        public Control CreateView(IModuleHost host)", "        // ─────────────────────────────── Actions", crlf('''        public Control CreateView(IModuleHost host)
        {
            _host = host;
            LoadSettings();
            ResolveTools();

            // Layout and styling live in AdbFastbootView.Designer.cs (open it in Design View).
            var v = new AdbFastbootView();
            _toolStatus = v.lblToolStatus;
            _devices = v.cboDevices;
            _console = v.txtConsole;
            _command = v.txtCommand;
            _stop = v.btnStop;

            On(v.btnSetTools, SetToolsFolder);
            On(v.btnRefreshDevices, RefreshDevices);
            On(v.btnAdbDevices, () => Query(_adb, "devices -l"));
            On(v.btnFastbootDevices, () => Query(_fastboot, "devices"));

            // ADB tab
            On(v.btnAdbReboot, () => Adb("reboot"));
            On(v.btnAdbRecovery, () => Adb("reboot recovery"));
            On(v.btnAdbBootloader, () => Adb("reboot bootloader"));
            On(v.btnAdbDownload, () => Adb("reboot download"));
            On(v.btnAdbEdl, () => Adb("reboot edl"));
            On(v.btnAdbInstall, InstallApk);
            On(v.btnAdbUninstall, () => { var pkg = Ask("Package name to uninstall:", "Uninstall"); if (pkg != null) Adb("uninstall " + pkg.Trim()); });
            On(v.btnAdbPush, Push);
            On(v.btnAdbPull, Pull);
            On(v.btnAdbSideload, Sideload);
            On(v.btnAdbLogcat, () => Adb("logcat"));
            On(v.btnAdbScreencap, Screencap);
            On(v.btnAdbShell, () => Adb("shell"));
            On(v.btnAdbGetprop, () => Adb("shell getprop"));
            On(v.btnAdbPackages, () => Adb("shell pm list packages"));
            On(v.btnAdbBattery, () => Adb("shell dumpsys battery"));
            On(v.btnAdbScreenSize, () => Adb("shell wm size"));
            On(v.btnAdbTcpip, () => Adb("tcpip 5555"));
            On(v.btnAdbConnect, () => { var ip = Ask("Device IP[:port] to connect:", "adb connect"); if (ip != null) Adb("connect " + ip.Trim()); });
            On(v.btnAdbBackup, Backup);
            On(v.btnAdbRestore, Restore);
            On(v.btnAdbKillServer, () => Query(_adb, "kill-server"));
            On(v.btnAdbStartServer, () => Query(_adb, "start-server"));

            // Fastboot tab
            On(v.btnFbGetvar, () => Fastboot("getvar all"));
            On(v.btnFbUnlock, () => { if (Confirm("Unlock the bootloader?\\nThis WIPES the device.")) Fastboot("flashing unlock"); });
            On(v.btnFbOemUnlock, () => { if (Confirm("oem unlock?\\nThis WIPES the device.")) Fastboot("oem unlock"); });
            On(v.btnFbLock, () => { if (Confirm("Re-lock the bootloader?")) Fastboot("flashing lock"); });
            On(v.btnFbReboot, () => Fastboot("reboot"));
            On(v.btnFbBootloader, () => Fastboot("reboot bootloader"));
            On(v.btnFbRecovery, () => Fastboot("reboot recovery"));
            On(v.btnFbFastbootd, () => Fastboot("reboot fastboot"));
            On(v.btnFbBootImg, () => FlashOrBoot("boot", false));
            On(v.btnFbFlashPartition, () => FlashOrBoot(null, true));
            On(v.btnFbFlashBoot, () => FlashKnown("boot"));
            On(v.btnFbFlashRecovery, () => FlashKnown("recovery"));
            On(v.btnFbErase, () => { var part = Ask("Partition to ERASE:", "fastboot erase"); if (part != null && Confirm("Erase '" + part + "'?")) Fastboot("erase " + part.Trim()); });
            On(v.btnFbFormat, () => { var part = Ask("Partition to FORMAT:", "fastboot format"); if (part != null && Confirm("Format '" + part + "'?")) Fastboot("format " + part.Trim()); });
            On(v.btnFbActiveA, () => Fastboot("--set-active=a"));
            On(v.btnFbActiveB, () => Fastboot("--set-active=b"));
            On(v.btnFbWipe, () => { if (Confirm("fastboot -w wipes userdata + cache. Continue?")) Fastboot("-w"); });
            On(v.btnFbContinue, () => Fastboot("continue"));

            // Download / Recovery tab
            On(v.btnDlSamsung, () => Adb("reboot download"));
            On(v.btnDlEdlAdb, () => Adb("reboot edl"));
            On(v.btnDlEdlFastboot, () => Fastboot("oem edl"));
            On(v.btnHeimdallDetect, () => Heimdall("detect"));
            On(v.btnHeimdallPit, () => Heimdall("print-pit"));
            On(v.btnHeimdallFlash, HeimdallFlash);
            On(v.btnRecReboot, () => Adb("reboot recovery"));
            On(v.btnRecSideload, Sideload);

            // Command bar
            _command.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunCommand(); } };
            On(v.btnRun, RunCommand);
            On(_stop, () => _runner.Stop());
            On(v.btnClear, () => _console.Clear());

            _runner.Output += Append;
            _runner.Exited += code => Append("[exit " + code + "]");

            UpdateToolStatus();
            return v;
        }

        private static void On(Button b, Action a) => b.Click += (s, e) => a();

'''))
t = between(t, "        private FlowLayoutPanel FlowPage(string title)", "        private void Append(string line)", "")
save(F, t); print("patched", F)
