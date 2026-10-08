from patch import *
F = r"Modules\PhoneJigModule.cs"
t = load(F)

t = replace1(t, "///   Devices : the profile database and the Google-Sheets-ready workbook export.",
                "///   Hardware History : the profile database and the Google-Sheets-ready workbook export.")
t = replace1(t, "using Newtonsoft.Json;", "using Newtonsoft.Json;\r\nusing TestPointTrigger.Modules.Views;")
t = replace1(t, "        // devices tab\r\n", "        // hardware history tab\r\n")
t = replace1(t, "        private Label _gridInfo;\r\n", "        private Label _gridInfo;\r\n        private PhoneJigView _view;\r\n        private ToolTip _tips;\r\n")

t = between(t, "        public Control CreateView(IModuleHost host)", "        // ───────────────────────────── logging / threading", crlf('''        public Control CreateView(IModuleHost host)
        {
            _host = host;
            _ctx = new CheckCtx { Link = _link, Log = Log };
            _steps = WiringGuide.Build();
            _link.Line += OnJigLine;

            // Layout and styling live in PhoneJigView.Designer.cs (open it in Design View).
            var v = _view = new PhoneJigView();
            _root = v;

            _ports = v.cboPorts;
            _linkStatus = v.lblLinkStatus;
            _btnConnect = v.btnConnect;
            _console = v.txtConsole;
            On(v.btnRescanPorts, () => RefreshPorts(false));
            On(v.btnAutoDetect, AutoDetectMega);
            On(v.btnConnect, ToggleConnect);
            v.tabMain.SelectedIndexChanged += (s, e) => { if (v.tabMain.SelectedTab == v.tabHardwareHistory) RefreshGrid(); };

            WireWiringTab(v);
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

'''))

# Wiring tab
t = between(t, "        private Control BuildWiringTab()", "        private WiringStep SelectedStep", crlf('''        private void WireWiringTab(PhoneJigView v)
        {
            _diagram = v.pnlDiagram;
            _stepList = v.lvSteps;
            _stepText = v.txtStepText;
            _btnRun = v.btnRunCheck;
            _btnDrive = v.btnDrive;
            _btnOk = v.btnMarkOk;
            _btnFail = v.btnMarkFailed;

            _diagram.SetSteps(_steps);
            _diagram.NodeClicked += id =>
            {
                var s = _steps.FirstOrDefault(x => (x.Nodes ?? "").Split(',').Contains(id));
                if (s != null) foreach (ListViewItem it in _stepList.Items) if (it.Tag == s) { it.Selected = true; it.EnsureVisible(); }
            };

            // Steps are data (WiringGuide), so the rows are added here; columns are in the designer.
            var groups = new Dictionary<string, ListViewGroup>();
            foreach (var s in _steps)
            {
                if (!groups.TryGetValue(s.Group, out var g)) { g = new ListViewGroup(s.Group); groups[s.Group] = g; _stepList.Groups.Add(g); }
                var it = new ListViewItem("o", g) { Tag = s };
                it.SubItems.Add(s.Title); it.SubItems.Add(s.HasCheck ? "auto" : "manual");
                _stepList.Items.Add(it);
            }
            _stepList.SelectedIndexChanged += (s, e) => ShowStep();

            On(_btnRun, () => RunSelected());
            On(_btnDrive, DriveSelected);
            On(_btnOk, () => SetSelected(StepState.Pass, "Confirmed by operator."));
            On(_btnFail, () => SetSelected(StepState.Fail, "Marked failed by operator."));
            On(v.btnRunAll, RunAllGuided);
            On(v.btnResetSteps, ResetSteps);
            On(v.btnSaveReport, SaveReport);

            if (_stepList.Items.Count > 0) _stepList.Items[0].Selected = true;
            RefreshSteps();
        }

'''))

# Jig tab
t = between(t, "        private Control BuildJigTab()", "        // ───────────────────────────── Device tab", crlf('''        private void WireJigTab(PhoneJigView v)
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
            On(v.btnAdbViaShield, () => Cmd("adb " + v.txtAdbCmd.Text));

            v.cboTuneMode.Items.AddRange(JigModes.All.Select(m => (object)m.Name).ToArray());
            if (v.cboTuneMode.Items.Count > 0) v.cboTuneMode.SelectedIndex = 0;
            On(v.btnTuneApply, () => Cmd("tune " + v.cboTuneMode.Text + " " + v.txtMask.Text + " " + v.txtUsbDelay.Text + " " + v.txtHold.Text));

            var rt = v.txtRawCmd;
            rt.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && rt.Text.Length > 0) { Cmd(rt.Text); rt.Clear(); e.SuppressKeyPress = true; } };
            On(v.btnRawSend, () => { if (rt.Text.Length > 0) { Cmd(rt.Text); rt.Clear(); } });
        }

'''))

# Device tab
t = between(t, "        private Control BuildDeviceTab()", "        private static string Resolve(string exe)", crlf('''        private void WireDeviceTab(PhoneJigView v)
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

'''))

# Hardware History tab
t = replace1(t, "        // ───────────────────────────── Devices tab (database + workbook)", "        // ───────────────────────────── Hardware History tab (database + workbook)")
t = between(t, "        private Control BuildDevicesTab()", "        private void RefreshGrid()", crlf('''        private void WireHistoryTab(PhoneJigView v)
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

'''))

t = replace1(t, "            try { _hotplug?.Dispose(); _poll?.Dispose(); } catch { }",
                "            try { _hotplug?.Dispose(); _poll?.Dispose(); _tips?.Dispose(); } catch { }")
save(F, t)
print("patched", F)
