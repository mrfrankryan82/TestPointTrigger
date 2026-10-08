import re
from patch import *
F = "MainForm.cs"
t = load(F)
nl = "\r\n" if "\r\n" in t else "\n"
fix = lambda x: x.replace("\r\n", "\n").replace("\n", nl)

t = between(t, "        // controls" + nl, "        public MainForm()", fix('''        // Controls are declared in MainForm.Designer.cs.
        readonly Timer _debounce = new Timer { Interval = 350 };

'''))

t = between(t, "        public MainForm()", "        void SetMode(Mode m)", fix('''        public MainForm()
        {
            // Layout, menus, toolbar and styling live in MainForm.Designer.cs (open it in Design View).
            InitializeComponent();
            Text = $"Mobile Surgery — PCB Pad Finder  v{AppVersion}";
            lblCredit.Text = Credit;
            canvas.MouseWheel += CanvasWheel;   // MouseWheel isn't listed in the designer's Events grid
            tscStyle.SelectedIndex = 0;
            InitSliders();
            SetMode(Mode.Edit);
            _debounce.Tick += (s, e) => { _debounce.Stop(); RunDetect(); };
            SetStatus("Open or drop a motherboard photo (Ctrl+O / Ctrl+V). Crop to the board, then fine-tune.");
        }

        // ─────────────────────── Designer event handlers ───────────────────────
        void mnuOpen_Click(object sender, EventArgs e) => OpenImage();
        void mnuPaste_Click(object sender, EventArgs e) => PasteImage();
        void mnuExportPng_Click(object sender, EventArgs e) => ExportPng();
        void mnuExportCsv_Click(object sender, EventArgs e) => ExportCsv();
        void mnuExit_Click(object sender, EventArgs e) => Close();
        void mnuUndo_Click(object sender, EventArgs e) => Undo();
        void mnuClearManual_Click(object sender, EventArgs e) { PushUndo(); _manual.Clear(); _suppressed.Clear(); Rebuild(); }
        void mnuClearCrop_Click(object sender, EventArgs e) { _crop = null; RunDetect(); }
        void mnuClearBox_Click(object sender, EventArgs e) { _boxDetected.Clear(); Rebuild(); SetStatus("Cleared box detections."); }
        void mnuHowTo_Click(object sender, EventArgs e) => ShowHelp();
        void mnuAbout_Click(object sender, EventArgs e) => MessageBox.Show(this,
            $"Mobile Surgery — PCB Pad Finder\\nVersion {AppVersion} ({BuildDate})\\n\\nDeveloper: HaKDMoDz™\\n\\nFinds gold and white/tinned test pads on motherboard photos and labels them for elimination probing.",
            "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        void tsbDetect_Click(object sender, EventArgs e) => RunDetect();
        void tsbPan_Click(object sender, EventArgs e) => SetMode(Mode.Pan);
        void tsbEdit_Click(object sender, EventArgs e) => SetMode(Mode.Edit);
        void tsbCrop_Click(object sender, EventArgs e) => SetMode(Mode.Crop);
        void tsbBox_Click(object sender, EventArgs e) => SetMode(Mode.Box);
        void tsbFit_Click(object sender, EventArgs e) => FitView();
        void tscStyle_SelectedIndexChanged(object sender, EventArgs e)
        {
            _ro.Style = tscStyle.SelectedIndex == 0 ? MarkStyle.LabelsOnly : MarkStyle.CirclesAndLabels;
            canvas.Invalidate();
        }
        void chkPadKind_CheckedChanged(object sender, EventArgs e) { _ds.DetectGold = chkGold.Checked; _ds.DetectWhite = chkWhite.Checked; Queue(); }
        void nudLabelSize_ValueChanged(object sender, EventArgs e) { _ro.LabelPx = (float)nudLabelSize.Value; canvas.Invalidate(); }
        void btnLabelColour_Click(object sender, EventArgs e)
        {
            using (var cd = new ColorDialog { Color = _ro.LabelColor })
                if (cd.ShowDialog(this) == DialogResult.OK) { _ro.LabelColor = cd.Color; canvas.Invalidate(); }
        }
        void btnResetDefaults_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Reset detection sliders to skill defaults?", "Reset", MessageBoxButtons.YesNo) == DialogResult.Yes) ResetSliders();
        }
        void lvPads_SelectedIndexChanged(object sender, EventArgs e)
        {
            _sel = lvPads.SelectedIndices.Count > 0 ? lvPads.SelectedIndices[0] : -1;
            if (_sel >= 0) CenterOn(_pads[_sel]);
            canvas.Invalidate();
        }
        void canvas_Resize(object sender, EventArgs e) => canvas.Invalidate();
        void MainForm_DragEnter(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }
        void MainForm_DragDrop(object sender, DragEventArgs e) { var f = (string[])e.Data.GetData(DataFormats.FileDrop); if (f?.Length > 0) LoadImage(f[0]); }

        // ─────────────────────── Detection sliders ───────────────────────
        // Each TrackBar's Value in the designer is its default (what "Reset defaults" returns to).
        sealed class Slider { public TrackBar Bar; public Label Caption; public string Name; public int Default; public Action<int> Apply; public Func<int, string> Format; }
        readonly List<Slider> _sliders = new List<Slider>();

        void InitSliders()
        {
            _sliders.Add(new Slider { Bar = tbMinRadius, Caption = lblMinRadius, Name = "Min pad radius (px)", Apply = v => _ds.MinRadiusPx = v, Format = v => v + " px" });
            _sliders.Add(new Slider { Bar = tbMaxRadius, Caption = lblMaxRadius, Name = "Max pad radius (px)", Apply = v => _ds.MaxRadiusPx = v, Format = v => v + " px" });
            _sliders.Add(new Slider { Bar = tbRoundness, Caption = lblRoundness, Name = "Core roundness", Apply = v => _ds.CircMin = v / 100.0, Format = v => (v / 100.0).ToString("0.00") });
            foreach (var s in _sliders) { s.Default = s.Bar.Value; ApplySlider(s); }
        }

        void ApplySlider(Slider s) { s.Apply(s.Bar.Value); s.Caption.Text = $"{s.Name}: {s.Format(s.Bar.Value)}"; }

        void Slider_ValueChanged(object sender, EventArgs e)
        {
            var s = _sliders.Find(x => x.Bar == sender);
            if (s == null) return;
            ApplySlider(s); Queue();
        }

        void ResetSliders() { foreach (var s in _sliders) s.Bar.Value = s.Default; }

'''))

for old, new in [("_canvas", "canvas"), ("_status", "lblStatus"), ("_list", "lvPads"), ("_bPan", "tsbPan"),
                 ("_bEdit", "tsbEdit"), ("_bCrop", "tsbCrop"), ("_bBox", "tsbBox"), ("_style", "tscStyle"),
                 ("_labelSize", "nudLabelSize"), ("_upscale", "nudUpscale")]:
    t = re.sub(r"\b%s\b" % re.escape(old), new, t)
save(F, t); print("patched", F)
