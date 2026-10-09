// Mobile Surgery - Phone Jig module: Uno/Mega jig control, wiring verification, auto device profiling, workbook export
// Developer: HaKDMoDz™ · v1.1.0 · 2026-10-09
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
using TestPointTrigger.Modules.Views;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// Front end for the Arduino (Uno or Mega 2560) phone boot-mode jig.
    ///   Wiring  : guided build checklist with live electrical checks and a colour-coded diagram.
    ///   Jig     : every serial command of the sketch as buttons.
    ///   Device  : autonomous per-phone profiling on first ADB/fastboot connection (cached by serial).
    ///   Hardware History : the profile database and the Google-Sheets-ready workbook export.
    /// </summary>
    public class PhoneJigModule : IModule
    {
        public string Id => "phonejig";
        public string Title => "Phone Jig";
        public string Description => "Uno / Mega boot-mode jig: wiring verification, auto device profiling, per-device workbook.";
        public string Version => "1.1.0";
        public int SortOrder => 12;

        private const int Baud = 115200;
        private static readonly string SettingsPath = Path.Combine(AppLog.Dir, "phonejig-settings.json");

        private IModuleHost _host;
        private Control _root;
        private readonly JigLink _link = new JigLink();
        private readonly ProfileStore _store = new ProfileStore();
        private CheckCtx _ctx;
        private BoardPins _pins = BoardPins.Mega;   // which board the wiring guide describes
        private ComboBox _board;

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

        // hardware history tab
        private DataGridView _grid;
        private Label _gridInfo;
        private PhoneJigView _view;
        private ToolTip _tips;

        // ───────────────────────────── view ─────────────────────────────

        public Control CreateView(IModuleHost host)
        {
            _host = host;
            _pins = BoardPins.For(LoadSettings().Board == "Uno" ? JigBoard.Uno : JigBoard.Mega);
            _ctx = new CheckCtx { Link = _link, Log = Log, Pins = _pins };
            _steps = WiringGuide.Build(_pins);
            _link.Line += OnJigLine;

            // Layout and styling live in PhoneJigView.Designer.cs (open it in Design View).
            var v = _view = new PhoneJigView();
            _root = v;

            _ports = v.cboPorts;
            _linkStatus = v.lblLinkStatus;
            _btnConnect = v.btnConnect;
            _console = v.txtConsole;
            On(v.btnRescanPorts, () => RefreshPorts(false));
            On(v.btnAutoDetect, AutoDetectJig);
            On(v.btnConnect, ToggleConnect);
            v.tabMain.SelectedIndexChanged += (s, e) => { if (v.tabMain.SelectedTab == v.tabHardwareHistory) RefreshGrid(); };

            WireWiringTab(v);

            // Board choice (items are data, the combo itself is in the designer).
            _board = v.cboBoard;
            _board.Items.AddRange(new object[] { BoardPins.Uno.Name, BoardPins.Mega.Name });
            _board.SelectedIndex = _pins.Board == JigBoard.Uno ? 0 : 1;
            _board.SelectedIndexChanged += (s, e) => SetBoard(_board.SelectedIndex == 0 ? JigBoard.Uno : JigBoard.Mega);
            WireJigTab(v);
            WireDeviceTab(v);
            WireHistoryTab(v);
            return v;
        }

        /// <summary>Runs a button action, logging instead of crashing on failure.</summary>
        private void On(Button b, Action a)
        {
            b.Click += (s, e) => { try { a(); } catch (Exception ex) { Log("[error] " + ex.Message); } };
        }

        /// <summary>
        /// Runtime buttons (one per boot mode) copy the look of a designer button,
        /// so restyling that button in Design View restyles them too.
        /// </summary>
        private static Button CloneButton(Button like, string text)
        {
            var b = new Button
            {
                Text = text, AutoSize = like.AutoSize, Margin = like.Margin, Padding = like.Padding, Font = like.Font,
                FlatStyle = like.FlatStyle, BackColor = like.BackColor, ForeColor = like.ForeColor,
                UseVisualStyleBackColor = like.UseVisualStyleBackColor
            };
            b.FlatAppearance.BorderColor = like.FlatAppearance.BorderColor;
            b.FlatAppearance.BorderSize = like.FlatAppearance.BorderSize;
            b.FlatAppearance.MouseOverBackColor = like.FlatAppearance.MouseOverBackColor;
            return b;
        }

        private static IEnumerable<Button> ButtonsIn(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is Button b) yield return b;
                foreach (var inner in ButtonsIn(c)) yield return inner;
            }
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
            // Raw lines from the jig. Keep them out of the file log; it would be mostly UART noise.
            UI(() =>
            {
                if (_console == null || _console.IsDisposed) return;
                if (_console.TextLength > 150000) _console.Text = _console.Text.Substring(60000);
                _console.AppendText("   < " + l + Environment.NewLine);
            });
        }

        private void Cmd(string c)
        {
            if (!_link.IsOpen) { Log("[not connected to the jig]"); return; }
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
                        Log(ard.Count > 0 ? "Arduino-type ports: " + string.Join("; ", ard.Select(p => p.ToString())) : "No Arduino-type USB serial port found. Plug the Uno or Mega in with a data USB cable.");
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
                UI(() => { SetLinkStatus(); if (ok) FollowFirmwareBoard(); });
                Log(ok ? "Jig firmware found on " + info.Port + ": " + _link.Ident : "Not connected: " + why);
            });
        }

        // Open a port, wait for the board's reset-on-open, and ask it to identify itself.
        private bool TryPort(string port, out string why)
        {
            if (!_link.Open(port, Baud, out string err)) { why = err; return false; }
            System.Threading.Thread.Sleep(2300);
            if (_link.Handshake()) { why = null; SaveSettings(port, null); return true; }
            string reason = "port opened but the sketch did not answer 'ident' (not uploaded, v2.0 firmware, or wrong board)";
            _link.Close();
            why = reason; return false;
        }

        private void AutoDetectJig()
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
                UI(() => { if (found != null) { _ports.SelectedItem = _ports.Items.Cast<SerialPortInfo>().FirstOrDefault(x => x.Port == found.Port); FollowFirmwareBoard(); } SetLinkStatus(); });
                Log(found != null ? "Found the jig on " + found.Port + ": " + _link.Ident : "No port answered as the jig. Check the cable and that PhoneBootController_Uno or _Mega v2.2.0 (repo firmware/ folder) is uploaded.");
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
                    Log("The jig port " + gone.First() + " disappeared (cable pulled or board reset).");
                    _host?.Notify("Jig serial port disappeared.", ModuleSeverity.Warning);
                }
                if (added.Count > 0 && !first)
                {
                    Log("New serial port: " + string.Join(", ", added) + ". Press Auto-detect jig.");
                    RefreshPorts(true);
                }
                else if (first) RefreshPorts(true);
            });
        }

        private class Settings { public string LastPort { get; set; } public string Board { get; set; } }

        private Settings LoadSettings()
        {
            try { if (File.Exists(SettingsPath)) return JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings(); }
            catch { }
            return new Settings();
        }

        // Pass null to keep the stored value for that field.
        private void SaveSettings(string port, string board)
        {
            try
            {
                var cur = LoadSettings();
                if (port != null) cur.LastPort = port;
                if (board != null) cur.Board = board;
                Directory.CreateDirectory(AppLog.Dir);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(cur));
            }
            catch { }
        }

        // ───────────────────────────── board (Uno / Mega) ─────────────────────────────

        /// <summary>When the firmware says which board it is, make the wiring guide match.</summary>
        private void FollowFirmwareBoard()
        {
            var b = _link.Board;
            if (b == null || b == _pins.Board) return;
            Log("Firmware reports the " + BoardPins.For(b.Value).Name + " build - switching the wiring guide to it.");
            _board.SelectedIndex = b == JigBoard.Uno ? 0 : 1;   // fires SetBoard
        }

        private void SetBoard(JigBoard b)
        {
            if (_pins.Board == b) return;
            if (_wiringBusy) { Log("Wait for the running check to finish before changing board."); _board.SelectedIndex = _pins.Board == JigBoard.Uno ? 0 : 1; return; }
            _pins = BoardPins.For(b);
            _ctx.Pins = _pins;
            _ctx.SelfTest.Clear(); _ctx.SelfTestAt = DateTime.MinValue;
            _steps = WiringGuide.Build(_pins);
            _diagram.SetBoard(_pins);
            _diagram.SetSteps(_steps);
            PopulateStepList();
            SaveSettings(null, b.ToString());
            Log("Wiring guide set to " + _pins.Name + ".");
        }

        // ───────────────────────────── Wiring tab ─────────────────────────────

        private void WireWiringTab(PhoneJigView v)
        {
            _diagram = v.pnlDiagram;
            _stepList = v.lvSteps;
            _stepText = v.txtStepText;
            _btnRun = v.btnRunCheck;
            _btnDrive = v.btnDrive;
            _btnOk = v.btnMarkOk;
            _btnFail = v.btnMarkFailed;

            _diagram.SetBoard(_pins);
            _diagram.SetSteps(_steps);
            _diagram.NodeClicked += id =>
            {
                var s = _steps.FirstOrDefault(x => (x.Nodes ?? "").Split(',').Contains(id));
                if (s != null) foreach (ListViewItem it in _stepList.Items) if (it.Tag == s) { it.Selected = true; it.EnsureVisible(); }
            };

            _stepList.SelectedIndexChanged += (s, e) => ShowStep();

            On(_btnRun, () => RunSelected());
            On(_btnDrive, DriveSelected);
            On(_btnOk, () => SetSelected(StepState.Pass, "Confirmed by operator."));
            On(_btnFail, () => SetSelected(StepState.Fail, "Marked failed by operator."));
            On(v.btnRunAll, RunAllGuided);
            On(v.btnResetSteps, ResetSteps);
            On(v.btnSaveReport, SaveReport);

            PopulateStepList();
        }

        // Steps are data (WiringGuide, per board), so the rows are added here; columns are in the designer.
        private void PopulateStepList()
        {
            _stepList.BeginUpdate();
            _stepList.Items.Clear(); _stepList.Groups.Clear();
            var groups = new Dictionary<string, ListViewGroup>();
            foreach (var s in _steps)
            {
                if (!groups.TryGetValue(s.Group, out var g)) { g = new ListViewGroup(s.Group); groups[s.Group] = g; _stepList.Groups.Add(g); }
                var it = new ListViewItem("o", g) { Tag = s };
                it.SubItems.Add(s.Title); it.SubItems.Add(s.HasCheck ? "auto" : "manual");
                _stepList.Items.Add(it);
            }
            _stepList.EndUpdate();
            if (_stepList.Items.Count > 0) _stepList.Items[0].Selected = true;
            RefreshSteps();
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
            if (!_link.IsOpen) { SetStep(s, StepState.Fail, "Not connected to the jig."); return; }
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
            if (!_link.IsOpen) { Log("Connect to the jig first."); return; }
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
            sb.AppendLine("Mobile Surgery - jig wiring report").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + AppInfo.DeveloperName);
            sb.AppendLine("Board: " + _pins.Name + "   Jig: " + (_link.Ident ?? "not connected")).AppendLine(Summary()).AppendLine();
            foreach (var s in _steps)
                sb.AppendLine("[" + s.State.ToString().ToUpperInvariant().PadRight(7) + "] " + s.Group + " / " + s.Title).AppendLine("          " + s.Detail);
            Directory.CreateDirectory(AppLog.Dir);
            string p = Path.Combine(AppLog.Dir, "wiring-report-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt");
            File.WriteAllText(p, sb.ToString(), Encoding.UTF8);
            Log("Report saved: " + p);
            try { Process.Start("explorer.exe", "/select,\"" + p + "\""); } catch { }
        }

        // ───────────────────────────── Jig tab ─────────────────────────────

        private void WireJigTab(PhoneJigView v)
        {
            // Designer buttons whose Tag holds a jig command simply send it.
            foreach (var b in ButtonsIn(v.flpJig).ToList())
                if (b.Tag is string c && c.Length > 0) { var cc = c; On(b, () => Cmd(cc)); }

            // Boot modes come from JigModes, one button each, placed before "OFF (all safe)".
            _tips = new ToolTip();
            int i = 0;
            foreach (var m in JigModes.All)
            {
                var mm = m;
                var b = CloneButton(v.btnModeOff, m.Name);
                On(b, () => Cmd(mm.Name));
                _tips.SetToolTip(b, m.Keys + " | USB " + m.Usb + " | " + m.Note);
                v.flpBootModes.Controls.Add(b);
                v.flpBootModes.Controls.SetChildIndex(b, i++);
            }

            On(v.btnUartSend, () => { if (v.txtUartSend.Text.Length > 0) Cmd("uart send " + v.txtUartSend.Text); });
            On(v.btnSetTrigger, () => { if (v.txtTrigger.Text.Length > 0) Cmd("trigger " + v.txtTrigger.Text); });
            On(v.btnOpenAdb, () => _host?.Navigate("adbfastboot"));

            v.cboTuneMode.Items.AddRange(JigModes.All.Select(m => (object)m.Name).ToArray());
            if (v.cboTuneMode.Items.Count > 0) v.cboTuneMode.SelectedIndex = 0;
            On(v.btnTuneApply, () => Cmd("tune " + v.cboTuneMode.Text + " " + v.txtMask.Text + " " + v.txtUsbDelay.Text + " " + v.txtHold.Text));

            var rt = v.txtRawCmd;
            rt.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && rt.Text.Length > 0) { Cmd(rt.Text); rt.Clear(); e.SuppressKeyPress = true; } };
            On(v.btnRawSend, () => { if (rt.Text.Length > 0) { Cmd(rt.Text); rt.Clear(); } });
        }

        // ───────────────────────────── Device tab (autonomous profiling) ─────────────────────────────

        private void WireDeviceTab(PhoneJigView v)
        {
            _auto = v.chkAutoProfile;
            _attached = v.cboAttached;
            _identity = v.lvIdentity;
            _modeList = v.lvModes;
            _procedure = v.txtProcedure;
            _notes = v.txtNotes;
            _pic = v.picDevice;
            _picInfo = v.lblPicInfo;

            On(v.btnDetectNow, () => ProbeSelected(false));
            On(v.btnRescanForce, () => ProbeSelected(true));
            On(v.btnFindImage, FindImageSelected);
            On(v.btnSetImage, SetImage);
            On(v.btnForgetDevice, ForgetCurrent);
            On(v.btnRunOnJig, RunModeOnJig);
            On(v.btnRunViaAdb, RunModeViaAdb);
            _modeList.DoubleClick += (s, e) => RunModeOnJig();
            _notes.Leave += (s, e) => { if (_current != null && _current.Notes != _notes.Text) { _current.Notes = _notes.Text; _store.Upsert(_current); } };
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

        // ───────────────────────────── Hardware History tab (database + workbook) ─────────────────────────────

        private void WireHistoryTab(PhoneJigView v)
        {
            _grid = v.grdHistory;
            _gridInfo = v.lblHistoryInfo;
            On(v.btnRefreshHistory, RefreshGrid);
            On(v.btnExportWorkbook, () => ExportWorkbook(false));
            On(v.btnExportDrive, () => ExportWorkbook(true));
            On(v.btnFindAllImages, FindAllImages);
            On(v.btnExportJson, ExportJson);
            On(v.btnOpenDataFolder, () => { try { Process.Start("explorer.exe", AppLog.Dir); } catch { } });
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var d = _store.Get(Convert.ToString(_grid.Rows[e.RowIndex].Cells[1].Value));
                if (d != null) { ShowProfile(d); Log("Showing " + d.DisplayName + " on the Device tab."); }
            };
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
                    if (Directory.Exists(p)) return Path.Combine(p, "MobileSurgery");
                }
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                foreach (var n in new[] { "My Drive", "Google Drive" })
                    if (Directory.Exists(Path.Combine(home, n))) return Path.Combine(home, n, "MobileSurgery");
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MobileSurgery");
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
            try { _hotplug?.Dispose(); _poll?.Dispose(); _tips?.Dispose(); } catch { }
            _link.Line -= OnJigLine;
            _link.Dispose();
        }
    }
}
