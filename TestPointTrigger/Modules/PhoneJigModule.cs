// TestPoint Trigger - Phone Jig module: Mega jig control, wiring verification, auto device profiling, workbook export
// Developer: HaKDMoDz™ · v1.0.0 · 2026-10-09
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// Front end for the Arduino Mega phone boot-mode jig.
    ///   Wiring  : guided build checklist with live electrical checks and a colour-coded diagram.
    ///   Jig     : every serial command of the sketch as buttons.
    ///   Device  : autonomous per-phone profiling on first ADB/fastboot connection (cached by serial).
    ///   Devices : the profile database and the Google-Sheets-ready workbook export.
    /// </summary>
    public class PhoneJigModule : IModule
    {
        public string Id => "phonejig";
        public string Title => "Phone Jig";
        public string Description => "Mega boot-mode jig: wiring verification, auto device profiling, per-device workbook.";
        public string Version => "1.0.0";
        public int SortOrder => 12;

        private const int Baud = 115200;
        private static readonly string SettingsPath = Path.Combine(AppLog.Dir, "phonejig-settings.json");

        private IModuleHost _host;
        private Control _root;
        private readonly JigLink _link = new JigLink();
        private readonly ProfileStore _store = new ProfileStore();
        private CheckCtx _ctx;

        // top bar / console
        private ComboBox _ports;
        private Label _linkStatus;
        private Button _btnConnect;
        private TextBox _console;
        private List<SerialPortInfo> _portInfo = new List<SerialPortInfo>();
        private HashSet<string> _lastPortNames = new HashSet<string>();
        private System.Windows.Forms.Timer _hotplug, _poll;
        private bool _connecting;

        // wiring tab
        private List<WiringStep> _steps;
        private ListView _stepList;
        private WiringPanel _diagram;
        private TextBox _stepText;
        private Button _btnRun, _btnDrive, _btnOk, _btnFail;
        private bool _wiringBusy;

        // device tab
        private CheckBox _auto;
        private ComboBox _attached;
        private ListView _identity, _modeList;
        private TextBox _procedure, _notes;
        private PictureBox _pic;
        private Label _picInfo;
        private DeviceProfile _current;
        private bool _pollBusy;
        private readonly HashSet<string> _handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // devices tab
        private DataGridView _grid;
        private Label _gridInfo;

        // ───────────────────────────── view ─────────────────────────────

        public Control CreateView(IModuleHost host)
        {
            _host = host;
            _ctx = new CheckCtx { Link = _link, Log = Log };
            _steps = WiringGuide.Build();
            _link.Line += OnJigLine;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150f));

            root.Controls.Add(BuildTopBar(), 0, 0);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(Page("Wiring", BuildWiringTab()));
            tabs.TabPages.Add(Page("Jig", BuildJigTab()));
            tabs.TabPages.Add(Page("Device", BuildDeviceTab()));
            tabs.TabPages.Add(Page("Devices", BuildDevicesTab()));
            tabs.SelectedIndexChanged += (s, e) => { if (tabs.SelectedIndex == 3) RefreshGrid(); };
            root.Controls.Add(tabs, 0, 1);

            _console = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f), BackColor = Color.FromArgb(24, 26, 31), ForeColor = Color.Gainsboro
            };
            root.Controls.Add(_console, 0, 2);

            _root = root;
            return root;
        }

        private static TabPage Page(string title, Control c) { var p = new TabPage(title) { Padding = new Padding(4) }; c.Dock = DockStyle.Fill; p.Controls.Add(c); return p; }

        private Control BuildTopBar()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            _ports = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
            _btnConnect = Btn("Connect", ToggleConnect);
            _linkStatus = new Label { AutoSize = true, Margin = new Padding(8, 7, 0, 0), Text = "Not connected" };
            bar.Controls.AddRange(new Control[]
            {
                new Label { Text = "Jig port:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) }, _ports,
                Btn("Rescan ports", () => RefreshPorts(false)), Btn("Auto-detect Mega", AutoDetectMega), _btnConnect,
                new Label { Text = "115200 8N1", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 7, 0, 0) }, _linkStatus
            });
            return bar;
        }

        private Button Btn(string text, Action a)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(3), Padding = new Padding(4, 2, 4, 2) };
            b.Click += (s, e) => { try { a(); } catch (Exception ex) { Log("[error] " + ex.Message); } };
            return b;
        }

        // ───────────────────────────── logging / threading ─────────────────────────────

        private void UI(Action a)
        {
            var c = _root;
            if (c == null || c.IsDisposed) return;
            try { if (c.InvokeRequired) c.BeginInvoke(a); else a(); } catch { }
        }

        private void Log(string line)
        {
            AppLog.Info("PhoneJig", line);
            UI(() =>
            {
                if (_console == null || _console.IsDisposed) return;
                if (_console.TextLength > 150000) _console.Text = _console.Text.Substring(60000);
                _console.AppendText(DateTime.Now.ToString("HH:mm:ss ") + line + Environment.NewLine);
            });
        }

        private void OnJigLine(string l)
        {
            // Raw lines from the Mega. Keep them out of the file log; it would be mostly UART noise.
            UI(() =>
            {
                if (_console == null || _console.IsDisposed) return;
                if (_console.TextLength > 150000) _console.Text = _console.Text.Substring(60000);
                _console.AppendText("   < " + l + Environment.NewLine);
            });
        }

        private void Cmd(string c)
        {
            if (!_link.IsOpen) { Log("[not connected to the Mega]"); return; }
            Log("> " + c);
            _link.Send(c);
        }

        // ───────────────────────────── connection / Arduino detection ─────────────────────────────

        private void RefreshPorts(bool quiet)
        {
            Task.Run(() =>
            {
                var list = PortScanner.Scan();
                UI(() =>
                {
                    string keep = (_ports.SelectedItem as SerialPortInfo)?.Port;
                    _portInfo = list;
                    _ports.Items.Clear();
                    foreach (var p in list) _ports.Items.Add(p);
                    var pick = list.FirstOrDefault(p => p.Port == keep) ?? list.FirstOrDefault(p => p.LooksArduino) ?? list.FirstOrDefault();
                    if (pick != null) _ports.SelectedItem = pick;
                    if (!quiet)
                    {
                        var ard = list.Where(p => p.LooksArduino).ToList();
                        Log(ard.Count > 0 ? "Arduino-type ports: " + string.Join("; ", ard.Select(p => p.ToString())) : "No Arduino-type USB serial port found. Plug the Mega in (USB-B).");
                    }
                });
            });
        }

        private void ToggleConnect()
        {
            if (_link.IsOpen) { _link.Close(); SetLinkStatus(); Log("Disconnected."); return; }
            var info = _ports.SelectedItem as SerialPortInfo;
            if (info == null) { Log("Pick a port first (or Auto-detect)."); return; }
            ConnectTo(info);
        }

        private void ConnectTo(SerialPortInfo info)
        {
            if (_connecting) return;
            _connecting = true;
            Log("Opening " + info.Port + " (" + info.Kind + ") ...");
            Task.Run(() =>
            {
                bool ok = TryPort(info.Port, out string why);
                _connecting = false;
                UI(() => { SetLinkStatus(); });
                Log(ok ? "Jig firmware found on " + info.Port + ": " + _link.Ident : "Not connected: " + why);
            });
        }

        // Open a port, wait for the Mega's reset-on-open, and ask it to identify itself.
        private bool TryPort(string port, out string why)
        {
            if (!_link.Open(port, Baud, out string err)) { why = err; return false; }
            System.Threading.Thread.Sleep(2300);
            if (_link.Handshake()) { why = null; SaveSettings(port); return true; }
            string reason = "port opened but the sketch did not answer 'ident' (not uploaded, v2.0 firmware, or wrong board)";
            _link.Close();
            why = reason; return false;
        }

        private void AutoDetectMega()
        {
            if (_connecting) return;
            _connecting = true;
            Log("Auto-detect: scanning serial ports for the jig firmware ...");
            Task.Run(() =>
            {
                var list = PortScanner.Scan();
                UI(() => { _portInfo = list; _ports.Items.Clear(); foreach (var p in list) _ports.Items.Add(p); });
                SerialPortInfo found = null;
                foreach (var p in list)
                {
                    Log("  trying " + p);
                    if (TryPort(p.Port, out _)) { found = p; break; }
                }
                _connecting = false;
                UI(() => { if (found != null) _ports.SelectedItem = _ports.Items.Cast<SerialPortInfo>().FirstOrDefault(x => x.Port == found.Port); SetLinkStatus(); });
                Log(found != null ? "Found the jig on " + found.Port + ": " + _link.Ident : "No port answered as the jig. Check the cable and that PhoneBootController_Mega v2.1.0 is uploaded.");
            });
        }

        private void SetLinkStatus()
        {
            if (_link.IsOpen && _link.Ident != null)
            {
                _linkStatus.Text = "Connected " + _link.PortName + " · " + _link.Ident.Replace("IDENT,", "");
                _linkStatus.ForeColor = Color.ForestGreen;
                _btnConnect.Text = "Disconnect";
            }
            else
            {
                _linkStatus.Text = _link.IsOpen ? "Port open, no jig reply" : "Not connected";
                _linkStatus.ForeColor = Color.Firebrick;
                _btnConnect.Text = "Connect";
            }
        }

        private void HotplugTick()
        {
            Task.Run(() =>
            {
                var now = new HashSet<string>(System.IO.Ports.SerialPort.GetPortNames());
                var added = now.Except(_lastPortNames).ToList();
                var gone = _lastPortNames.Except(now).ToList();
                bool first = _lastPortNames.Count == 0 && _portInfo.Count == 0;
                _lastPortNames = now;
                if (added.Count == 0 && gone.Count == 0) return;
                if (_link.IsOpen && gone.Contains(_link.PortName))
                {
                    _link.Close(); UI(SetLinkStatus);
                    Log("The jig port " + gone.First() + " disappeared (cable pulled or Mega reset).");
                    _host?.Notify("Jig serial port disappeared.", ModuleSeverity.Warning);
                }
                if (added.Count > 0 && !first)
                {
                    Log("New serial port: " + string.Join(", ", added) + ". Press Auto-detect Mega.");
                    RefreshPorts(true);
                }
                else if (first) RefreshPorts(true);
            });
        }

        private class Settings { public string LastPort { get; set; } }
        private void SaveSettings(string port)
        {
            try { Directory.CreateDirectory(AppLog.Dir); File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(new Settings { LastPort = port })); } catch { }
        }

        // ───────────────────────────── Wiring tab ─────────────────────────────

        private Control BuildWiringTab()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 700 };

            _diagram = new WiringPanel { Dock = DockStyle.Fill };
            _diagram.SetSteps(_steps);
            _diagram.NodeClicked += id =>
            {
                var s = _steps.FirstOrDefault(x => (x.Nodes ?? "").Split(',').Contains(id));
                if (s != null) foreach (ListViewItem it in _stepList.Items) if (it.Tag == s) { it.Selected = true; it.EnsureVisible(); }
            };
            split.Panel1.Controls.Add(_diagram);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _stepList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            _stepList.Columns.Add("", 26); _stepList.Columns.Add("Step", 300); _stepList.Columns.Add("Test", 52);
            var groups = new Dictionary<string, ListViewGroup>();
            foreach (var s in _steps)
            {
                if (!groups.TryGetValue(s.Group, out var g)) { g = new ListViewGroup(s.Group); groups[s.Group] = g; _stepList.Groups.Add(g); }
                var it = new ListViewItem("o", g) { Tag = s };
                it.SubItems.Add(s.Title); it.SubItems.Add(s.HasCheck ? "auto" : "manual");
                _stepList.Items.Add(it);
            }
            _stepList.SelectedIndexChanged += (s, e) => ShowStep();
            right.Controls.Add(_stepList, 0, 0);

            _stepText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 9f) };
            right.Controls.Add(_stepText, 0, 1);

            var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            _btnRun = Btn("Run check", () => RunSelected());
            _btnDrive = Btn("Drive (set outputs)", DriveSelected);
            _btnOk = Btn("Mark OK", () => SetSelected(StepState.Pass, "Confirmed by operator."));
            _btnFail = Btn("Mark failed", () => SetSelected(StepState.Fail, "Marked failed by operator."));
            btns.Controls.AddRange(new Control[]
            {
                _btnRun, _btnDrive, _btnOk, _btnFail,
                Btn("Run all (guided)", RunAllGuided), Btn("Reset", ResetSteps), Btn("Save report", SaveReport)
            });
            right.Controls.Add(btns, 0, 2);
            split.Panel2.Controls.Add(right);

            if (_stepList.Items.Count > 0) _stepList.Items[0].Selected = true;
            RefreshSteps();
            return split;
        }

        private WiringStep SelectedStep => _stepList.SelectedItems.Count > 0 ? _stepList.SelectedItems[0].Tag as WiringStep : null;

        private void ShowStep()
        {
            var s = SelectedStep;
            if (s == null) { _stepText.Text = ""; _diagram.Highlight(""); return; }
            _diagram.Highlight(s.Nodes);
            var sb = new StringBuilder();
            sb.AppendLine(s.Title).AppendLine();
            sb.AppendLine("WHERE IT GOES").AppendLine(s.Place).AppendLine();
            sb.AppendLine("PARTS: " + s.Parts);
            sb.AppendLine("PINS:  " + s.Pins).AppendLine();
            sb.AppendLine("RESULT: " + s.State.ToString().ToUpperInvariant());
            if (!string.IsNullOrEmpty(s.Detail)) sb.AppendLine(s.Detail);
            _stepText.Text = sb.ToString();
            _btnRun.Enabled = s.HasCheck && !_wiringBusy;
            _btnDrive.Enabled = s.Drive != null;
            _btnOk.Enabled = _btnFail.Enabled = true;
        }

        private void RefreshSteps()
        {
            foreach (ListViewItem it in _stepList.Items)
            {
                var s = (WiringStep)it.Tag;
                it.Text = s.State == StepState.Pass ? "✔" : s.State == StepState.Fail ? "✖" : s.State == StepState.Warn ? "!" : "o";
                it.ForeColor = s.State == StepState.Pass ? Color.ForestGreen : s.State == StepState.Fail ? Color.Firebrick :
                               s.State == StepState.Warn ? Color.DarkOrange : Color.DimGray;
            }
            _diagram.Invalidate();
            ShowStep();
        }

        private void SetStep(WiringStep s, StepState st, string detail)
        {
            s.State = st; s.Detail = detail;
            Log("[" + s.Id + "] " + st + " - " + detail);
            UI(RefreshSteps);
        }

        private void SetSelected(StepState st, string detail)
        {
            var s = SelectedStep; if (s != null) SetStep(s, st, detail);
        }

        private void DriveSelected()
        {
            var s = SelectedStep; if (s?.Drive == null) return;
            foreach (var c in s.Drive) Cmd(c);
            Log("Outputs set. Measure with a meter, then press Mark OK or Mark failed. ('off' releases everything.)");
        }

        private async void RunSelected()
        {
            var s = SelectedStep; if (s == null || _wiringBusy) return;
            if (!s.HasCheck) { Log("This step has no electrical test - use Drive, measure, then Mark OK."); return; }
            await RunStepAsync(s);
        }

        private async Task RunStepAsync(WiringStep s)
        {
            if (!_link.IsOpen) { SetStep(s, StepState.Fail, "Not connected to the Mega."); return; }
            _wiringBusy = true; UI(ShowStep);
            await Task.Run(() =>
            {
                Log("Checking: " + s.Title + " ...");
                KeyValuePair<StepState, string> r;
                try { r = s.Check(_ctx); }
                catch (Exception ex) { r = new KeyValuePair<StepState, string>(StepState.Fail, "Check crashed: " + ex.Message); }
                SetStep(s, r.Key, r.Value);
            });
            _wiringBusy = false; UI(ShowStep);
        }

        // Runs every automatic step in order. Steps that need the operator to prepare something first ask before running.
        private async void RunAllGuided()
        {
            if (_wiringBusy) return;
            if (!_link.IsOpen) { Log("Connect to the Mega first."); return; }
            foreach (var s in _steps)
            {
                if (!s.HasCheck) continue;
                if (s.Id == "mux" || s.Id == "uartloop" || s.Id == "uartphone" || s.Id == "vcc")
                {
                    var r = MessageBox.Show(_root.FindForm(), s.Title + "\n\n" + s.Place + "\n\nYes = run now   No = skip   Cancel = stop",
                        "Prepare: " + s.Id, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Cancel) break;
                    if (r == DialogResult.No) continue;
                }
                await RunStepAsync(s);
            }
            Log("Guided run finished. " + Summary());
        }

        private string Summary() =>
            _steps.Count(s => s.State == StepState.Pass) + " ok, " + _steps.Count(s => s.State == StepState.Warn) + " to check, " +
            _steps.Count(s => s.State == StepState.Fail) + " failed, " + _steps.Count(s => s.State == StepState.Pending) + " not tested.";

        private void ResetSteps()
        {
            foreach (var s in _steps) { s.State = StepState.Pending; s.Detail = ""; }
            _ctx.SelfTest.Clear(); _ctx.SelfTestAt = DateTime.MinValue;
            RefreshSteps();
        }

        private void SaveReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("TestPoint Trigger - jig wiring report").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + AppInfo.DeveloperName);
            sb.AppendLine("Jig: " + (_link.Ident ?? "not connected")).AppendLine(Summary()).AppendLine();
            foreach (var s in _steps)
                sb.AppendLine("[" + s.State.ToString().ToUpperInvariant().PadRight(7) + "] " + s.Group + " / " + s.Title).AppendLine("          " + s.Detail);
            Directory.CreateDirectory(AppLog.Dir);
            string p = Path.Combine(AppLog.Dir, "wiring-report-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt");
            File.WriteAllText(p, sb.ToString(), Encoding.UTF8);
            Log("Report saved: " + p);
            try { Process.Start("explorer.exe", "/select,\"" + p + "\""); } catch { }
        }

        // ───────────────────────────── Jig tab ─────────────────────────────

        private Control BuildJigTab()
        {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };

            FlowLayoutPanel Row(string title)
            {
                var gb = new GroupBox { Text = title, AutoSize = true, Padding = new Padding(6), Width = 900 };
                var f = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
                gb.Controls.Add(f); flow.Controls.Add(gb);
                return f;
            }

            var modes = Row("Boot modes (hover for keys)");
            var tip = new ToolTip();
            foreach (var m in JigModes.All)
            {
                var b = Btn(m.Name, () => Cmd(m.Name));
                tip.SetToolTip(b, m.Keys + " | USB " + m.Usb + " | " + m.Note);
                modes.Controls.Add(b);
            }
            modes.Controls.Add(Btn("OFF (all safe)", () => Cmd("off")));

            var chk = Row("Wiring checks");
            foreach (var c in new[] { "ident", "pins", "sense", "selftest", "status", "help" }) { var cc = c; chk.Controls.Add(Btn(cc, () => Cmd(cc))); }

            var pw = Row("Power and USB");
            foreach (var c in new[] { "usb pc", "usb shield", "usb off", "vcc on", "vcc off", "btemp on", "btemp off" }) { var cc = c; pw.Controls.Add(Btn(cc, () => Cmd(cc))); }

            var keys = Row("Manual pad lines (open-drain)");
            foreach (var k in new[] { "tp", "up", "dn", "pwr" })
            {
                var kk = k;
                keys.Controls.Add(Btn(kk + " hold", () => Cmd("key " + kk + " hold")));
                keys.Controls.Add(Btn(kk + " rel", () => Cmd("key " + kk + " rel")));
            }

            var ua = Row("Phone UART (Serial1)");
            var tb = new TextBox { Width = 220 };
            var trig = new TextBox { Width = 200 };
            ua.Controls.AddRange(new Control[]
            {
                Btn("uart on", () => Cmd("uart on")), Btn("uart off", () => Cmd("uart off")), Btn("baud 115200", () => Cmd("uart baud 115200")),
                Btn("loop test", () => Cmd("uart loop")), Btn("listen 3 s", () => Cmd("uart listen 3000")),
                tb, Btn("send", () => { if (tb.Text.Length > 0) Cmd("uart send " + tb.Text); }),
                new Label { Text = "release lines on:", AutoSize = true, Margin = new Padding(12, 7, 2, 0) }, trig,
                Btn("set trigger", () => { if (trig.Text.Length > 0) Cmd("trigger " + trig.Text); }), Btn("clear", () => Cmd("trigger off"))
            });

            var adb = Row("ADB through the host shield");
            var at = new TextBox { Width = 260, Text = "getprop ro.product.model" };
            adb.Controls.AddRange(new Control[] { at, Btn("adb (via shield)", () => Cmd("adb " + at.Text)) });
            adb.Controls.Add(new Label { Text = "runs 'normal' first so the phone boots and the USB path goes to the shield", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 7, 0, 0) });

            var tune = Row("Tune a mode (RAM only)");
            var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            mode.Items.AddRange(JigModes.All.Select(m => (object)m.Name).ToArray()); mode.SelectedIndex = 0;
            var mask = new TextBox { Width = 50, Text = "0x01" }; var ud = new TextBox { Width = 60, Text = "400" }; var hm = new TextBox { Width = 60, Text = "2500" };
            tune.Controls.AddRange(new Control[]
            {
                mode, new Label { Text = "mask", AutoSize = true, Margin = new Padding(6, 7, 0, 0) }, mask,
                new Label { Text = "usb delay ms", AutoSize = true, Margin = new Padding(6, 7, 0, 0) }, ud,
                new Label { Text = "hold ms", AutoSize = true, Margin = new Padding(6, 7, 0, 0) }, hm,
                Btn("apply", () => Cmd("tune " + mode.Text + " " + mask.Text + " " + ud.Text + " " + hm.Text))
            });

            var raw = Row("Any command");
            var rt = new TextBox { Width = 380 };
            rt.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && rt.Text.Length > 0) { Cmd(rt.Text); rt.Clear(); e.SuppressKeyPress = true; } };
            raw.Controls.AddRange(new Control[] { rt, Btn("send", () => { if (rt.Text.Length > 0) { Cmd(rt.Text); rt.Clear(); } }) });

            return flow;
        }

        // ───────────────────────────── Device tab (autonomous profiling) ─────────────────────────────

        private Control BuildDeviceTab()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            _auto = new CheckBox { Text = "Auto-profile on connect", Checked = true, AutoSize = true, Margin = new Padding(3, 7, 10, 0) };
            _attached = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            bar.Controls.AddRange(new Control[]
            {
                _auto, new Label { Text = "Attached:", AutoSize = true, Margin = new Padding(0, 7, 3, 0) }, _attached,
                Btn("Detect now", () => ProbeSelected(false)), Btn("Rescan (force)", () => ProbeSelected(true)),
                Btn("Find model image", FindImageSelected), Btn("Set image...", SetImage), Btn("Forget device", ForgetCurrent)
            });
            t.Controls.Add(bar, 0, 0);

            var cols = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26f));

            _identity = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            _identity.Columns.Add("Property", 150); _identity.Columns.Add("Value", 330);
            cols.Controls.Add(_identity, 0, 0);

            var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            mid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); mid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _modeList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            _modeList.Columns.Add("Mode", 80); _modeList.Columns.Add("Keys", 110); _modeList.Columns.Add("Software", 120);
            _modeList.DoubleClick += (s, e) => RunModeOnJig();
            mid.Controls.Add(_modeList, 0, 0);
            var mb = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            mb.Controls.Add(Btn("Run on jig", RunModeOnJig));
            mb.Controls.Add(Btn("Run via adb", RunModeViaAdb));
            mid.Controls.Add(mb, 0, 1);
            cols.Controls.Add(mid, 1, 0);

            var imgBox = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            imgBox.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); imgBox.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
            _picInfo = new Label { AutoSize = true, MaximumSize = new Size(260, 0), ForeColor = Color.DimGray, Text = "No image yet" };
            imgBox.Controls.Add(_pic, 0, 0); imgBox.Controls.Add(_picInfo, 0, 1);
            cols.Controls.Add(imgBox, 2, 0);
            t.Controls.Add(cols, 0, 1);

            var lower = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f)); lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
            _procedure = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f) };
            _notes = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
            _notes.Leave += (s, e) => { if (_current != null && _current.Notes != _notes.Text) { _current.Notes = _notes.Text; _store.Upsert(_current); } };
            lower.Controls.Add(_procedure, 0, 0); lower.Controls.Add(_notes, 1, 0);
            t.Controls.Add(lower, 0, 2);
            return t;
        }

        private static string Resolve(string exe)
        {
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                try { var p = Path.Combine(d.Trim(), exe); if (d.Trim().Length > 0 && File.Exists(p)) return p; } catch { }
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var d in new[] { Path.Combine(local, "Android", "Sdk", "platform-tools"), @"C:\platform-tools", @"C:\adb", @"C:\Program Files\platform-tools" })
                try { var p = Path.Combine(d, exe); if (File.Exists(p)) return p; } catch { }
            return null;
        }

        private void PollTick()
        {
            if (_pollBusy || !_auto.Checked) return;
            _pollBusy = true;
            Task.Run(() =>
            {
                try { PollOnce(); }
                catch (Exception ex) { Log("[poll] " + ex.Message); }
                finally { _pollBusy = false; }
            });
        }

        private void PollOnce()
        {
            if (DeviceProbe.Adb == null && DeviceProbe.Fastboot == null) return;
            var adb = DeviceProbe.ListAdb();
            var fb = DeviceProbe.ListFastboot();
            foreach (var a in adb.Where(x => x.Value == "unauthorized"))
                if (_warned.Add(a.Key)) Log("Phone " + a.Key + " is unauthorised: accept the 'Allow USB debugging' prompt on the phone.");
            var present = adb.Where(a => a.Value == "device").Select(a => a.Key).Concat(fb).Distinct().ToList();
            UI(() =>
            {
                string sel = _attached.SelectedItem as string;
                _attached.Items.Clear(); foreach (var s in present) _attached.Items.Add(s);
                if (sel != null && present.Contains(sel)) _attached.SelectedItem = sel; else if (present.Count > 0) _attached.SelectedIndex = 0;
            });
            foreach (var serial in present)
            {
                if (!_handled.Add(serial)) continue;      // already looked at since it was plugged in
                Handle(serial, fb.Contains(serial), false);
            }
            _handled.RemoveWhere(s => !present.Contains(s));
            _warned.RemoveWhere(s => !adb.Any(a => a.Key == s && a.Value == "unauthorized"));
        }

        /// <summary>
        /// The "only when needed" rule. A phone we have a good profile for is only
        /// re-probed when its build fingerprint changed, a previous probe was incomplete,
        /// or the operator forces a rescan.
        /// </summary>
        private void Handle(string serial, bool fastboot, bool force)
        {
            var old = _store.Get(serial);
            string why;
            if (force) why = "manual rescan";
            else if (old == null) why = "first connection";
            else if (old.NeedsRescan) why = "previous probe was incomplete";
            else if (!fastboot)
            {
                string fp = DeviceProbe.QuickFingerprint(serial);
                if (!string.IsNullOrEmpty(fp) && fp == old.BuildFingerprint)
                {
                    old.LastSeen = DateTime.Now; _store.Upsert(old);
                    Log("Known phone " + old.DisplayName + " (" + serial + "): build unchanged, skipping discovery.");
                    ShowProfile(old); return;
                }
                why = "build fingerprint changed";
            }
            else
            {
                if (old != null) { old.LastSeen = DateTime.Now; _store.Upsert(old); Log("Known phone " + old.DisplayName + " in fastboot, skipping discovery."); ShowProfile(old); }
                return;
            }

            Log("Profiling " + serial + " (" + (fastboot ? "fastboot" : "adb") + ", " + why + ") ...");
            DeviceProfile d = fastboot ? DeviceProbe.ProbeFastboot(serial, old, Log) : DeviceProbe.ProbeAdb(serial, old, Log);
            if (!fastboot && d.Props.Count == 0) { d.NeedsRescan = true; Log("Probe returned no properties; will retry next time."); }

            // Same phone, new serial (re-flash, different USB mode)? The IMEI tag recognises it.
            if (!string.IsNullOrEmpty(d.ImeiHash))
            {
                var twin = _store.All().FirstOrDefault(x => x.Key != d.Key && x.ImeiHash == d.ImeiHash);
                if (twin != null)
                {
                    Log("IMEI tag matches an earlier profile (" + twin.Serial + "): same handset, carrying its image and notes over.");
                    if (string.IsNullOrEmpty(d.ImagePath)) { d.ImagePath = twin.ImagePath; d.ImageUrl = twin.ImageUrl; d.ImageSource = twin.ImageSource; d.ImageMatch = twin.ImageMatch; }
                    if (string.IsNullOrEmpty(d.Notes)) d.Notes = twin.Notes;
                }
            }
            _store.Upsert(d);
            Log("Profile stored: " + d.DisplayName + " | " + d.ChipVendor + " | modes: " + string.Join(", ", d.Modes));
            ShowProfile(d);

            if (string.IsNullOrEmpty(d.ImagePath) && !fastboot)
            {
                var t = ImageFetcher.FindAsync(d, Log);
                t.ContinueWith(x => { _store.Upsert(d); ShowProfile(d); });
            }
        }

        private void ProbeSelected(bool force)
        {
            string serial = _attached.SelectedItem as string;
            if (serial == null) { Log("No phone attached over adb/fastboot."); return; }
            Task.Run(() =>
            {
                if (DeviceProbe.Adb == null && DeviceProbe.Fastboot == null) { Log("adb/fastboot not found. Set platform-tools in the ADB / Fastboot module."); return; }
                bool fb = DeviceProbe.ListFastboot().Contains(serial);
                Handle(serial, fb, force);
            });
        }

        private void ShowProfile(DeviceProfile d)
        {
            UI(() =>
            {
                _current = d;
                _identity.BeginUpdate(); _identity.Items.Clear();
                void Add(string k, string v) { if (!string.IsNullOrWhiteSpace(v)) _identity.Items.Add(new ListViewItem(new[] { k, v })); }
                Add("Name", d.DisplayName); Add("Serial", d.Serial); Add("IMEI", d.ImeiStatus == "hashed" ? "read (stored as tag " + d.ImeiHash + ")" : d.ImeiStatus);
                Add("Model", d.Model); Add("Device", d.DeviceCode); Add("Chip family", d.ChipVendor); Add("Platform", d.Platform); Add("Hardware", d.Hardware);
                Add("SoC", string.Join(" ", new[] { d.SocVendor, d.SocModel }.Where(x => !string.IsNullOrWhiteSpace(x))));
                Add("Android", d.AndroidVersion + (string.IsNullOrEmpty(d.Sdk) ? "" : " (SDK " + d.Sdk + ")")); Add("Build", d.BuildId); Add("Patch", d.SecurityPatch);
                Add("Bootloader", d.Bootloader); Add("Baseband", d.Baseband); Add("Boot state", d.BootState); Add("Bootloader lock", d.FlashLocked);
                Add("Display", d.Display + (string.IsNullOrEmpty(d.Density) ? "" : " @" + d.Density + "dpi")); Add("RAM", d.Ram); Add("Storage", d.Storage); Add("Battery", d.Battery);
                Add("USB id", string.IsNullOrEmpty(d.UsbVid) ? "" : d.UsbVid + ":" + d.UsbPid + " " + d.UsbName);
                Add("Properties", d.Props.Count + " getprop, " + d.FastbootVars.Count + " fastboot vars");
                Add("First / last seen", d.FirstSeen.ToString("yyyy-MM-dd HH:mm") + " / " + d.LastSeen.ToString("yyyy-MM-dd HH:mm") + "  (probed " + d.ProbeCount + "x)");
                _identity.EndUpdate();

                _modeList.Items.Clear();
                foreach (var mn in d.Modes) { var m = JigModes.Get(mn); if (m != null) _modeList.Items.Add(new ListViewItem(new[] { m.Name, m.Keys, m.SoftwareCmd })); }
                _procedure.Text = (d.Procedure ?? "").Replace("\n", Environment.NewLine);
                if (_notes.Text != (d.Notes ?? "")) _notes.Text = d.Notes ?? "";
                LoadPicture(d);
            });
        }

        private void LoadPicture(DeviceProfile d)
        {
            try
            {
                var old = _pic.Image; _pic.Image = null; old?.Dispose();
                if (!string.IsNullOrEmpty(d.ImagePath) && File.Exists(d.ImagePath))
                    using (var ms = new MemoryStream(File.ReadAllBytes(d.ImagePath))) _pic.Image = Image.FromStream(ms) is Image im ? new Bitmap(im) : null;
                _picInfo.Text = string.IsNullOrEmpty(d.ImageMatch) ? "No image yet" : d.ImageMatch + "\n" + d.ImageSource;
            }
            catch { _picInfo.Text = "Image could not be loaded"; }
        }

        private void FindImageSelected()
        {
            var d = _current; if (d == null) { Log("Select or detect a phone first."); return; }
            ImageFetcher.FindAsync(d, Log).ContinueWith(x => { _store.Upsert(d); ShowProfile(d); });
        }

        private void SetImage()
        {
            var d = _current; if (d == null) return;
            using (var f = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.gif" })
            {
                if (f.ShowDialog(_root.FindForm()) != DialogResult.OK) return;
                Directory.CreateDirectory(ImageFetcher.Dir);
                string dest = Path.Combine(ImageFetcher.Dir, System.Text.RegularExpressions.Regex.Replace(d.Key, @"[^A-Za-z0-9_\-]", "_") + Path.GetExtension(f.FileName));
                File.Copy(f.FileName, dest, true);
                d.ImagePath = dest; d.ImageUrl = ""; d.ImageSource = "Operator supplied: " + Path.GetFileName(f.FileName); d.ImageMatch = "Set by operator";
                _store.Upsert(d); ShowProfile(d);
            }
        }

        private void ForgetCurrent()
        {
            var d = _current; if (d == null) return;
            if (MessageBox.Show(_root.FindForm(), "Remove " + d.DisplayName + " (" + d.Serial + ") from the database?", "Forget device", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _store.Remove(d.Key); _current = null; _identity.Items.Clear(); _modeList.Items.Clear(); _procedure.Clear(); _handled.Remove(d.Key);
            Log("Forgot " + d.Serial + ". It will be profiled afresh next time it connects.");
        }

        private void RunModeOnJig()
        {
            if (_modeList.SelectedItems.Count == 0) { Log("Pick a mode first."); return; }
            Cmd(_modeList.SelectedItems[0].Text);
        }

        private void RunModeViaAdb()
        {
            if (_modeList.SelectedItems.Count == 0 || DeviceProbe.Adb == null) { Log("Pick a mode (and have adb available)."); return; }
            string sw = _modeList.SelectedItems[0].SubItems[2].Text;
            string serial = _attached.SelectedItem as string;
            if (string.IsNullOrEmpty(sw) || !sw.StartsWith("adb ") || serial == null) { Log("No software route for that mode."); return; }
            Log("> " + sw + " (serial " + serial + ")");
            Task.Run(() => Log(CliRunner.Capture(DeviceProbe.Adb, "-s " + serial + " " + sw.Substring(4), 10000)));
        }

        // ───────────────────────────── Devices tab (database + workbook) ─────────────────────────────

        private Control BuildDevicesTab()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            bar.Controls.AddRange(new Control[]
            {
                Btn("Refresh", RefreshGrid), Btn("Export workbook (.xlsx)", () => ExportWorkbook(false)),
                Btn("Export + open Google Drive", () => ExportWorkbook(true)), Btn("Find images for all", FindAllImages),
                Btn("Export JSON", ExportJson), Btn("Open data folder", () => { try { Process.Start("explorer.exe", AppLog.Dir); } catch { } })
            });
            t.Controls.Add(bar, 0, 0);
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            foreach (var c in new[] { "Device", "Serial", "Chip family", "Modes", "Android", "Image", "Last seen" }) _grid.Columns.Add(c, c);
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var d = _store.Get(Convert.ToString(_grid.Rows[e.RowIndex].Cells[1].Value));
                if (d != null) { ShowProfile(d); Log("Showing " + d.DisplayName + " on the Device tab."); }
            };
            t.Controls.Add(_grid, 0, 1);
            _gridInfo = new Label { AutoSize = true, ForeColor = Color.DimGray };
            t.Controls.Add(_gridInfo, 0, 2);
            return t;
        }

        private void RefreshGrid()
        {
            if (_grid == null) return;
            var all = _store.All();
            _grid.Rows.Clear();
            foreach (var d in all)
                _grid.Rows.Add(d.DisplayName, d.Serial, d.ChipVendor, string.Join(", ", d.Modes), d.AndroidVersion, d.ImageMatch ?? "", d.LastSeen.ToString("yyyy-MM-dd HH:mm"));
            _gridInfo.Text = all.Count + " device(s) · database: " + ProfileStore.PathJson + " · double-click a row to open it";
        }

        // Where the workbook goes: Google Drive for desktop's "My Drive" if present, otherwise Documents.
        private static string WorkbookFolder()
        {
            try
            {
                foreach (var dr in DriveInfo.GetDrives())
                {
                    if (!dr.IsReady) continue;
                    string p = Path.Combine(dr.RootDirectory.FullName, "My Drive");
                    if (Directory.Exists(p)) return Path.Combine(p, "TestPointTrigger");
                }
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                foreach (var n in new[] { "My Drive", "Google Drive" })
                    if (Directory.Exists(Path.Combine(home, n))) return Path.Combine(home, n, "TestPointTrigger");
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TestPointTrigger");
        }

        private void ExportWorkbook(bool openDrive)
        {
            var all = _store.All();
            if (all.Count == 0) { Log("No devices in the database yet."); return; }
            string path = Path.Combine(WorkbookFolder(), "DeviceWorkbook.xlsx");
            Log("Writing workbook (" + all.Count + " worksheet(s)) ...");
            Task.Run(() =>
            {
                try
                {
                    WorkbookExporter.Export(all, path);
                    Log("Workbook saved: " + path);
                    bool drive = path.IndexOf("My Drive", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("Google Drive", StringComparison.OrdinalIgnoreCase) >= 0;
                    Log(drive ? "It is inside Google Drive for desktop: it syncs up, then open it from drive.google.com with Google Sheets."
                              : "To use it in Google Sheets: drive.google.com > New > File upload > open with Google Sheets.");
                    try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
                    if (openDrive) try { Process.Start("https://drive.google.com/drive/my-drive"); } catch { }
                }
                catch (Exception ex) { Log("[workbook error] " + ex.Message); }
            });
        }

        private void FindAllImages()
        {
            var todo = _store.All().Where(d => string.IsNullOrEmpty(d.ImagePath)).ToList();
            if (todo.Count == 0) { Log("Every device already has an image."); return; }
            Task.Run(async () =>
            {
                foreach (var d in todo) { await ImageFetcher.FindAsync(d, Log); _store.Upsert(d); }
                Log("Image lookup finished.");
                UI(RefreshGrid);
            });
        }

        private void ExportJson()
        {
            using (var f = new SaveFileDialog { Filter = "JSON|*.json", FileName = "phone-profiles.json" })
                if (f.ShowDialog(_root.FindForm()) == DialogResult.OK)
                { File.Copy(ProfileStore.PathJson, f.FileName, true); Log("Exported " + f.FileName); }
        }

        // ───────────────────────────── lifecycle ─────────────────────────────

        public void Activate()
        {
            DeviceProbe.Adb = Resolve("adb.exe");
            DeviceProbe.Fastboot = Resolve("fastboot.exe");
            _host?.SetStatus("Phone Jig - " + (_link.IsOpen ? "jig connected" : "jig not connected") + " · adb " + (DeviceProbe.Adb != null ? "found" : "NOT found"));
            if (_hotplug == null)
            {
                _hotplug = new System.Windows.Forms.Timer { Interval = 3000 }; _hotplug.Tick += (s, e) => HotplugTick();
                _poll = new System.Windows.Forms.Timer { Interval = 2500 }; _poll.Tick += (s, e) => PollTick();
            }
            _hotplug.Start(); _poll.Start();
            RefreshPorts(true);
            RefreshGrid();
        }

        // Stop polling when the page is hidden; the serial link stays open so a running mode is not interrupted.
        public void Deactivate() { _hotplug?.Stop(); _poll?.Stop(); }

        public void Dispose()
        {
            try { _hotplug?.Dispose(); _poll?.Dispose(); } catch { }
            _link.Line -= OnJigLine;
            _link.Dispose();
        }
    }
}
