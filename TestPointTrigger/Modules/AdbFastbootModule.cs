// TestPoint Trigger - ADB / Fastboot / Download / Recovery console
// Developer: HaKDMoDz™ · v1.0.0 · 2026-09-26
using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;
using TestPointTrigger.Modules.Views;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// A control panel over adb / fastboot / heimdall. Preset buttons cover the
    /// common operations; the raw command box at the bottom runs ANY command
    /// verbatim and feeds stdin to a live shell/logcat. Tools are auto-located
    /// (set folder, PATH, or common SDK paths). Samsung Odin and Qualcomm
    /// QFIL/EDL flashing are vendor GUIs - this surfaces mode entry + Heimdall.
    /// </summary>
    public class AdbFastbootModule : IModule
    {
        public string Id => "adbfastboot";
        public string Title => "ADB / Fastboot";
        public string Description => "adb, fastboot, download mode and stock-recovery operations in one console.";
        public string Version => "1.0.0";
        public int SortOrder => 15;

        private sealed class Settings { public string ToolsDir = ""; }
        private static readonly string SettingsPath = Path.Combine(AppLog.Dir, "adb-settings.json");

        private IModuleHost _host;
        private readonly CliRunner _runner = new CliRunner();
        private string _adb, _fastboot, _heimdall, _toolsDir;

        private Label _toolStatus;
        private ComboBox _devices;
        private TextBox _console, _command;
        private Button _stop;

        // ─────────────────────────────── View ───────────────────────────────

        public Control CreateView(IModuleHost host)
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
            On(v.btnFbUnlock, () => { if (Confirm("Unlock the bootloader?\nThis WIPES the device.")) Fastboot("flashing unlock"); });
            On(v.btnFbOemUnlock, () => { if (Confirm("oem unlock?\nThis WIPES the device.")) Fastboot("oem unlock"); });
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

        // ─────────────────────────────── Actions ───────────────────────────────

        private void InstallApk()
        {
            using (var d = new OpenFileDialog { Filter = "APK|*.apk|All files|*.*" })
                if (d.ShowDialog(Owner) == DialogResult.OK) Adb("install -r " + Q(d.FileName));
        }

        private void Push()
        {
            using (var d = new OpenFileDialog { Filter = "All files|*.*" })
            {
                if (d.ShowDialog(Owner) != DialogResult.OK) return;
                var remote = Ask("Remote destination path:", "adb push", "/sdcard/");
                if (remote != null) Adb("push " + Q(d.FileName) + " " + remote.Trim());
            }
        }

        private void Pull()
        {
            var remote = Ask("Remote file to pull:", "adb pull", "/sdcard/");
            if (remote == null) return;
            using (var d = new SaveFileDialog { FileName = Path.GetFileName(remote.TrimEnd('/')) })
                if (d.ShowDialog(Owner) == DialogResult.OK) Adb("pull " + remote.Trim() + " " + Q(d.FileName));
        }

        private void Sideload()
        {
            using (var d = new OpenFileDialog { Filter = "OTA / update zip|*.zip|All files|*.*" })
                if (d.ShowDialog(Owner) == DialogResult.OK) Adb("sideload " + Q(d.FileName));
        }

        private void Screencap()
        {
            using (var d = new SaveFileDialog { Filter = "PNG|*.png", FileName = "screen.png" })
            {
                if (d.ShowDialog(Owner) != DialogResult.OK) return;
                // exec-out streams the PNG straight to the local file.
                Adb("exec-out screencap -p > " + Q(d.FileName));
            }
        }

        private void Backup()
        {
            using (var d = new SaveFileDialog { Filter = "Android backup|*.ab", FileName = "backup.ab" })
                if (d.ShowDialog(Owner) == DialogResult.OK) Adb("backup -apk -all -f " + Q(d.FileName));
        }

        private void Restore()
        {
            using (var d = new OpenFileDialog { Filter = "Android backup|*.ab|All files|*.*" })
                if (d.ShowDialog(Owner) == DialogResult.OK) Adb("restore " + Q(d.FileName));
        }

        private void FlashOrBoot(string fixedPartition, bool flash)
        {
            string part = fixedPartition;
            if (part == null)
            {
                part = Ask("Partition to flash (e.g. boot, recovery, vbmeta):", "fastboot flash");
                if (part == null) return;
            }
            using (var d = new OpenFileDialog { Filter = "Image|*.img;*.bin;*.mbn|All files|*.*" })
            {
                if (d.ShowDialog(Owner) != DialogResult.OK) return;
                if (flash) { if (Confirm("Flash '" + part + "'?")) Fastboot("flash " + part.Trim() + " " + Q(d.FileName)); }
                else Fastboot("boot " + Q(d.FileName));
            }
        }

        private void FlashKnown(string part)
        {
            using (var d = new OpenFileDialog { Filter = "Image|*.img;*.bin|All files|*.*" })
                if (d.ShowDialog(Owner) == DialogResult.OK && Confirm("Flash " + part + "?"))
                    Fastboot("flash " + part + " " + Q(d.FileName));
        }

        private void HeimdallFlash()
        {
            var part = Ask("PIT partition name (e.g. RECOVERY, BOOT):", "heimdall flash");
            if (part == null) return;
            using (var d = new OpenFileDialog { Filter = "Image|*.img;*.bin|All files|*.*" })
                if (d.ShowDialog(Owner) == DialogResult.OK)
                    Heimdall("flash --" + part.Trim() + " " + Q(d.FileName));
        }

        // ─────────────────────────── Command plumbing ───────────────────────────

        private void Adb(string args) => RunTool(_adb, "adb", args, injectSerial: true);
        private void Fastboot(string args) => RunTool(_fastboot, "fastboot", args, injectSerial: true);
        private void Heimdall(string args) => RunTool(_heimdall, "heimdall", args, injectSerial: false);

        private void RunTool(string exe, string label, string toolArgs, bool injectSerial)
        {
            if (exe == null) { Append("[" + label + " not found - Set platform-tools folder]"); return; }

            // exec-out redirection needs a shell; run through cmd for those.
            if (toolArgs.Contains(" > "))
            {
                Append("> " + label + " " + toolArgs);
                _runner.Start("cmd.exe", "/c \"" + Quote(exe) + " " + WithSerial(toolArgs, injectSerial) + "\"", _toolsDir);
                AppLog.Info("AdbFastboot", label + " " + toolArgs);
                return;
            }

            var args = WithSerial(toolArgs, injectSerial);
            Append("> " + label + " " + args);
            _runner.Start(exe, args, _toolsDir);
            AppLog.Info("AdbFastboot", label + " " + args);
        }

        private string WithSerial(string args, bool inject)
        {
            var s = SelectedSerial;
            return inject && !string.IsNullOrEmpty(s) ? "-s " + s + " " + args : args;
        }

        private void RunCommand()
        {
            var text = _command.Text.Trim();
            if (text.Length == 0) return;

            // If something is streaming (shell/logcat), send this as stdin.
            if (_runner.IsRunning) { Append("$ " + text); _runner.WriteLine(text); _command.Clear(); return; }

            _command.Clear();
            // First token may name the tool; otherwise assume adb.
            var sp = text.IndexOf(' ');
            var head = (sp < 0 ? text : text.Substring(0, sp)).ToLowerInvariant();
            var rest = sp < 0 ? "" : text.Substring(sp + 1);
            switch (head)
            {
                case "adb": RunTool(_adb, "adb", rest, false); break;
                case "fastboot": RunTool(_fastboot, "fastboot", rest, false); break;
                case "heimdall": RunTool(_heimdall, "heimdall", rest, false); break;
                default: RunTool(_adb, "adb", text, false); break;   // bare command → adb
            }
        }

        private void Query(string exe, string args)
        {
            if (exe == null) { Append("[tool not found - Set platform-tools folder]"); return; }
            Append("> " + Path.GetFileNameWithoutExtension(exe) + " " + args);
            var outText = CliRunner.Capture(exe, args);
            Append(string.IsNullOrWhiteSpace(outText) ? "[no output]" : outText);
        }

        private void RefreshDevices()
        {
            var keep = SelectedSerial;
            var found = new List<string>();
            if (_adb != null)
                foreach (var line in CliRunner.Capture(_adb, "devices").Split('\n').Skip(1))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("*")) continue;
                    var parts = t.Split('\t', ' ');
                    if (parts.Length >= 2) found.Add(parts[0] + "  (" + parts[1].Trim() + ")");
                }
            if (_fastboot != null)
                foreach (var line in CliRunner.Capture(_fastboot, "devices").Split('\n'))
                {
                    var t = line.Trim();
                    if (t.Length == 0) continue;
                    var parts = t.Split('\t', ' ');
                    found.Add(parts[0] + "  (fastboot)");
                }

            _devices.Items.Clear();
            _devices.Items.AddRange(found.Cast<object>().ToArray());
            if (_devices.Items.Count > 0)
            {
                var idx = found.FindIndex(f => keep != null && f.StartsWith(keep));
                _devices.SelectedIndex = idx >= 0 ? idx : 0;
            }
            Append("[" + found.Count + " device(s)]");
        }

        private string SelectedSerial
        {
            get
            {
                var s = _devices.SelectedItem as string;
                if (string.IsNullOrEmpty(s)) return null;
                var i = s.IndexOf("  (");
                return i > 0 ? s.Substring(0, i) : s;
            }
        }

        // ─────────────────────────── Tool resolution ───────────────────────────

        private void ResolveTools()
        {
            _adb = Resolve(_toolsDir, "adb.exe");
            _fastboot = Resolve(_toolsDir, "fastboot.exe");
            _heimdall = Resolve(_toolsDir, "heimdall.exe");
        }

        private static string Resolve(string dir, string exe)
        {
            if (!string.IsNullOrEmpty(dir))
            {
                var p = Path.Combine(dir, exe);
                if (File.Exists(p)) return p;
            }
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                try { var p = Path.Combine(d.Trim(), exe); if (d.Trim().Length > 0 && File.Exists(p)) return p; } catch { }
            foreach (var d in CommonDirs())
                try { var p = Path.Combine(d, exe); if (File.Exists(p)) return p; } catch { }
            return null;
        }

        private static IEnumerable<string> CommonDirs()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(local, "Android", "Sdk", "platform-tools");
            yield return @"C:\platform-tools";
            yield return @"C:\adb";
            yield return @"C:\Program Files\platform-tools";
        }

        private void SetToolsFolder()
        {
            using (var d = new FolderBrowserDialog { Description = "Folder containing adb.exe / fastboot.exe" })
            {
                if (d.ShowDialog(Owner) != DialogResult.OK) return;
                _toolsDir = d.SelectedPath;
                SaveSettings();
                ResolveTools();
                UpdateToolStatus();
                Append("[platform-tools folder set to " + _toolsDir + "]");
            }
        }

        private void UpdateToolStatus()
        {
            _toolStatus.Text = "Tools: " +
                (_adb != null ? "adb " : "adb✗ ") +
                (_fastboot != null ? "fastboot " : "fastboot✗ ") +
                (_heimdall != null ? "heimdall" : "heimdall✗");
            _toolStatus.ForeColor = (_adb != null && _fastboot != null) ? Color.ForestGreen : Color.Firebrick;
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    _toolsDir = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsPath))?.ToolsDir ?? "";
            }
            catch { _toolsDir = ""; }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(AppLog.Dir);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(new Settings { ToolsDir = _toolsDir ?? "" }, Formatting.Indented));
            }
            catch { }
        }

        // ─────────────────────────── Helpers / lifecycle ───────────────────────────

        private IWin32Window Owner => _console?.FindForm();
        private string Ask(string msg, string title, string def = "") => Prompt.Text(Owner, msg, title, def);
        private bool Confirm(string msg) => MessageBox.Show(Owner, msg, "Confirm",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        private static string Q(string path) => "\"" + path + "\"";
        private static string Quote(string s) => s.Contains(" ") ? "\"" + s + "\"" : s;

        private void Append(string line)
        {
            var box = _console;
            if (box == null || box.IsDisposed) return;
            Action a = () =>
            {
                if (box.IsDisposed) return;
                box.AppendText(line + Environment.NewLine);
            };
            try { if (box.IsHandleCreated && box.InvokeRequired) box.BeginInvoke(a); else a(); }
            catch { }
        }

        public void Activate()
        {
            RefreshDevices();
            _host?.SetStatus(_adb != null
                ? "ADB / Fastboot ready. Tools found."
                : "adb not found - click Set platform-tools folder.");
        }

        // Leaving the page stops any live shell/logcat stream.
        public void Deactivate() => _runner.Stop();

        public void Dispose() => _runner.Dispose();
    }
}
