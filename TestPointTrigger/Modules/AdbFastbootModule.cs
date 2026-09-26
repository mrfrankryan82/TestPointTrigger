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

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // tool/device bar
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));       // tabs of buttons
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));       // console
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // command bar

            // --- tool + device bar ---
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            _toolStatus = new Label { AutoSize = true, Margin = new Padding(0, 6, 12, 0) };
            _devices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
            bar.Controls.AddRange(new Control[]
            {
                _toolStatus,
                Btn("Set platform-tools…", SetToolsFolder),
                Btn("Refresh devices", RefreshDevices),
                new Label { Text = "Device:", AutoSize = true, Margin = new Padding(8, 6, 4, 0) }, _devices,
                Btn("adb devices", () => Query(_adb, "devices -l")),
                Btn("fastboot devices", () => Query(_fastboot, "devices"))
            });

            // --- button tabs ---
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(AdbTab());
            tabs.TabPages.Add(FastbootTab());
            tabs.TabPages.Add(DownloadTab());

            // --- console ---
            _console = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                WordWrap = false, BackColor = Color.FromArgb(24, 24, 28), ForeColor = Color.Gainsboro,
                Font = new Font("Consolas", 9.5f)
            };

            // --- command bar ---
            var cmdBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, AutoSize = true };
            cmdBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 3; i++) cmdBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _command = new TextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f) };
            _command.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunCommand(); } };
            var run = Btn("Run / Send", RunCommand);
            _stop = Btn("Stop", () => _runner.Stop());
            var clear = Btn("Clear", () => _console.Clear());
            cmdBar.Controls.Add(_command, 0, 0);
            cmdBar.Controls.Add(run, 1, 0);
            cmdBar.Controls.Add(_stop, 2, 0);
            cmdBar.Controls.Add(clear, 3, 0);

            root.Controls.Add(bar, 0, 0);
            root.Controls.Add(tabs, 0, 1);
            root.Controls.Add(_console, 0, 2);
            root.Controls.Add(cmdBar, 0, 3);

            _runner.Output += Append;
            _runner.Exited += code => Append("[exit " + code + "]");

            UpdateToolStatus();
            return root;
        }

        private TabPage AdbTab()
        {
            var p = FlowPage("ADB");
            p.Controls.AddRange(new Control[]
            {
                Btn("Reboot", () => Adb("reboot")),
                Btn("→ Recovery", () => Adb("reboot recovery")),
                Btn("→ Bootloader", () => Adb("reboot bootloader")),
                Btn("→ Download", () => Adb("reboot download")),
                Btn("→ EDL (9008)", () => Adb("reboot edl")),
                Btn("Install APK…", InstallApk),
                Btn("Uninstall pkg…", () => { var pkg = Ask("Package name to uninstall:", "Uninstall"); if (pkg != null) Adb("uninstall " + pkg.Trim()); }),
                Btn("Push file…", Push),
                Btn("Pull file…", Pull),
                Btn("Sideload OTA…", Sideload),
                Btn("Logcat", () => Adb("logcat")),
                Btn("Screencap…", Screencap),
                Btn("Interactive shell", () => Adb("shell")),
                Btn("getprop", () => Adb("shell getprop")),
                Btn("List packages", () => Adb("shell pm list packages")),
                Btn("Battery", () => Adb("shell dumpsys battery")),
                Btn("Screen size", () => Adb("shell wm size")),
                Btn("Wi-Fi: tcpip 5555", () => Adb("tcpip 5555")),
                Btn("Wi-Fi: connect…", () => { var ip = Ask("Device IP[:port] to connect:", "adb connect"); if (ip != null) Adb("connect " + ip.Trim()); }),
                Btn("Backup…", Backup),
                Btn("Restore…", Restore),
                Btn("Kill server", () => Query(_adb, "kill-server")),
                Btn("Start server", () => Query(_adb, "start-server"))
            });
            return Wrap(p);
        }

        private TabPage FastbootTab()
        {
            var p = FlowPage("Fastboot");
            p.Controls.AddRange(new Control[]
            {
                Btn("getvar all", () => Fastboot("getvar all")),
                Btn("Unlock", () => { if (Confirm("Unlock the bootloader?\nThis WIPES the device.")) Fastboot("flashing unlock"); }),
                Btn("OEM unlock", () => { if (Confirm("oem unlock?\nThis WIPES the device.")) Fastboot("oem unlock"); }),
                Btn("Lock", () => { if (Confirm("Re-lock the bootloader?")) Fastboot("flashing lock"); }),
                Btn("Reboot", () => Fastboot("reboot")),
                Btn("→ Bootloader", () => Fastboot("reboot bootloader")),
                Btn("→ Recovery", () => Fastboot("reboot recovery")),
                Btn("→ fastbootd", () => Fastboot("reboot fastboot")),
                Btn("Boot img (temp)…", () => FlashOrBoot("boot", false)),
                Btn("Flash partition…", () => FlashOrBoot(null, true)),
                Btn("Flash boot…", () => FlashKnown("boot")),
                Btn("Flash recovery…", () => FlashKnown("recovery")),
                Btn("Erase partition…", () => { var part = Ask("Partition to ERASE:", "fastboot erase"); if (part != null && Confirm("Erase '" + part + "'?")) Fastboot("erase " + part.Trim()); }),
                Btn("Format partition…", () => { var part = Ask("Partition to FORMAT:", "fastboot format"); if (part != null && Confirm("Format '" + part + "'?")) Fastboot("format " + part.Trim()); }),
                Btn("Set active a", () => Fastboot("--set-active=a")),
                Btn("Set active b", () => Fastboot("--set-active=b")),
                Btn("Wipe (-w)", () => { if (Confirm("fastboot -w wipes userdata + cache. Continue?")) Fastboot("-w"); }),
                Btn("Continue", () => Fastboot("continue"))
            });
            return Wrap(p);
        }

        private TabPage DownloadTab()
        {
            var p = FlowPage("Download / Recovery");
            p.Controls.AddRange(new Control[]
            {
                Btn("Enter Download (Samsung)", () => Adb("reboot download")),
                Btn("Enter EDL (adb)", () => Adb("reboot edl")),
                Btn("Enter EDL (fastboot)", () => Fastboot("oem edl")),
                Btn("Heimdall: detect", () => Heimdall("detect")),
                Btn("Heimdall: print-pit", () => Heimdall("print-pit")),
                Btn("Heimdall: flash partition…", HeimdallFlash),
                Btn("Recovery: reboot", () => Adb("reboot recovery")),
                Btn("Recovery: sideload OTA…", Sideload),
                new Label {
                    Text = "Samsung Odin and Qualcomm QFIL/EDL flashing are vendor GUIs.\r\n" +
                           "Use these to ENTER the mode; use Heimdall (open Odin) or QFIL to flash.",
                    AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(4, 10, 4, 4)
                }
            });
            return Wrap(p);
        }

        private TabPage Wrap(FlowLayoutPanel p)
        {
            var t = new TabPage((string)p.Tag);
            p.Dock = DockStyle.Fill;
            t.Controls.Add(p);
            return t;
        }

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

        private FlowLayoutPanel FlowPage(string title)
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true, Padding = new Padding(4) };
            p.Tag = title;
            return p;
        }

        private Button Btn(string text, Action a)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(3), Padding = new Padding(4, 2, 4, 2) };
            b.Click += (s, e) => a();
            return b;
        }

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
