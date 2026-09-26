// TestPoint Trigger - PCB Pad Finder
// Developer: HaKDMoDz™ · v2.0.0 · 2026-09-23
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using CvRect = OpenCvSharp.Rect;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace TestPointTrigger
{
    public partial class MainForm : Form
    {
        // Read from the assembly so the title/footer always match the csproj <Version>.
        public static readonly string AppVersion = AppInfo.VersionText;
        public const string BuildDate = AppInfo.ReleaseDate;
        public static readonly string Credit = $"Developer: HaKDMoDz™ · v{AppVersion} · {BuildDate}";

        enum Mode { Pan, Edit, Crop }

        // state
        Mat _mat; Bitmap _bmp; string _path;
        List<Pad> _detected = new List<Pad>(), _manual = new List<Pad>(), _pads = new List<Pad>();
        readonly List<PointF> _suppressed = new List<PointF>();
        readonly Stack<(List<Pad> m, List<PointF> s)> _undo = new Stack<(List<Pad>, List<PointF>)>();
        Rectangle? _crop; Rectangle? _cropDrag; Point _dragStart; bool _panning; PointF _panOrigin;
        float _zoom = 1f; PointF _off; Mode _mode = Mode.Edit; int _sel = -1;
        readonly DetectorSettings _ds = new DetectorSettings();
        readonly RenderOptions _ro = new RenderOptions();
        int _detectGen;

        // controls
        readonly CanvasPanel _canvas = new CanvasPanel();
        readonly ToolStripStatusLabel _status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly ListView _list = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill };
        readonly ToolStripButton _bPan = new ToolStripButton("✋ Pan"), _bEdit = new ToolStripButton("✚ Edit pads"), _bCrop = new ToolStripButton("⬚ Crop to board");
        readonly ToolStripComboBox _style = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        readonly NumericUpDown _labelSize = new NumericUpDown { Minimum = 4, Maximum = 60, DecimalPlaces = 1, Increment = 0.5M, Value = 11 };
        readonly NumericUpDown _upscale = new NumericUpDown { Minimum = 1, Maximum = 6, DecimalPlaces = 1, Increment = 0.5M, Value = 3 };
        readonly Timer _debounce = new Timer { Interval = 350 };

        public MainForm()
        {
            Text = $"TestPoint Trigger — PCB Pad Finder  v{AppVersion}";
            Size = new Size(1400, 900); StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f); KeyPreview = true; AllowDrop = true;
            BuildUi();
            SetMode(Mode.Edit);
            _debounce.Tick += (s, e) => { _debounce.Stop(); RunDetect(); };
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => { var f = (string[])e.Data.GetData(DataFormats.FileDrop); if (f?.Length > 0) LoadImage(f[0]); };
            KeyDown += OnKey;
            SetStatus("Open or drop a motherboard photo (Ctrl+O / Ctrl+V). Crop to the board, then fine-tune.");
        }

        // ───────────────────────────── UI ─────────────────────────────
        void BuildUi()
        {
            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(Mi("&Open image…", () => OpenImage(), Keys.Control | Keys.O));
            file.DropDownItems.Add(Mi("&Paste image", () => PasteImage(), Keys.Control | Keys.V));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Mi("Export labelled &PNG…", () => ExportPng(), Keys.Control | Keys.S));
            file.DropDownItems.Add("Export &CSV checklist…", null, (s, e) => ExportCsv());
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("E&xit", null, (s, e) => Close());
            var edit = new ToolStripMenuItem("&Edit");
            edit.DropDownItems.Add(Mi("&Undo", () => Undo(), Keys.Control | Keys.Z));
            edit.DropDownItems.Add("Clear &manual edits", null, (s, e) => { PushUndo(); _manual.Clear(); _suppressed.Clear(); Rebuild(); });
            edit.DropDownItems.Add("Clear c&rop", null, (s, e) => { _crop = null; RunDetect(); });
            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add("&How to use", null, (s, e) => ShowHelp());
            help.DropDownItems.Add("&About", null, (s, e) => MessageBox.Show(this,
                $"TestPoint Trigger — PCB Pad Finder\nVersion {AppVersion} ({BuildDate})\n\nDeveloper: HaKDMoDz™\n\nFinds gold and white/tinned test pads on motherboard photos and labels them for elimination probing.",
                "About", MessageBoxButtons.OK, MessageBoxIcon.Information));
            menu.Items.AddRange(new ToolStripItem[] { file, edit, help });

            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(20, 20) };
            tools.Items.Add(new ToolStripButton("📂 Open", null, (s, e) => OpenImage()));
            tools.Items.Add(new ToolStripButton("🔍 Detect", null, (s, e) => RunDetect()));
            tools.Items.Add(new ToolStripSeparator());
            _bPan.Click += (s, e) => SetMode(Mode.Pan); _bEdit.Click += (s, e) => SetMode(Mode.Edit); _bCrop.Click += (s, e) => SetMode(Mode.Crop);
            tools.Items.AddRange(new ToolStripItem[] { _bPan, _bEdit, _bCrop, new ToolStripSeparator() });
            tools.Items.Add(new ToolStripLabel("Style:"));
            _style.Items.AddRange(new object[] { "Labels only", "Circles + labels" }); _style.SelectedIndex = 0;
            _style.SelectedIndexChanged += (s, e) => { _ro.Style = _style.SelectedIndex == 0 ? MarkStyle.LabelsOnly : MarkStyle.CirclesAndLabels; _canvas.Invalidate(); };
            tools.Items.Add(_style);
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripButton("⤢ Fit", null, (s, e) => FitView()));
            tools.Items.Add(new ToolStripButton("↶ Undo", null, (s, e) => Undo()));
            tools.Items.Add(new ToolStripButton("💾 Export PNG", null, (s, e) => ExportPng()));
            tools.Items.Add(new ToolStripButton("📄 Export CSV", null, (s, e) => ExportCsv()));

            var statusStrip = new StatusStrip();
            statusStrip.Items.Add(_status);
            statusStrip.Items.Add(new ToolStripStatusLabel(Credit) { ForeColor = Color.DimGray });

            // right panel
            var side = new Panel { Dock = DockStyle.Right, Width = 300, Padding = new Padding(8) };
            var tbl = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            AddHeader(tbl, "Detection");
            AddSlider(tbl, "Min pad size", 3, 200, 8, v => _ds.MinAreaFrac = v * 1e-6, v => $"{v * 1e-6:0.0e0}");
            AddSlider(tbl, "Max pad size", 100, 8000, 2800, v => _ds.MaxAreaFrac = v * 1e-6, v => $"{v * 1e-6:0.0e0}");
            AddSlider(tbl, "Roundness (circularity)", 30, 95, 55, v => _ds.CircMin = v / 100.0, v => (v / 100.0).ToString("0.00"));
            AddSlider(tbl, "Shield-metal rejection", 20, 100, 55, v => _ds.IsoMax = v / 100.0, v => (v / 100.0).ToString("0.00"));
            AddSlider(tbl, "Bezel-text rejection", 0, 120, 50, v => _ds.DarkSurroundV = v, v => v.ToString());
            AddSlider(tbl, "Screw-hole rejection", 30, 95, 68, v => _ds.DonutRatio = v / 100.0, v => (v / 100.0).ToString("0.00"));
            var cbRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var cbGold = new CheckBox { Text = "Gold pads", Checked = true, AutoSize = true };
            var cbWhite = new CheckBox { Text = "White/tinned pads", Checked = true, AutoSize = true };
            cbGold.CheckedChanged += (s, e) => { _ds.DetectGold = cbGold.Checked; Queue(); };
            cbWhite.CheckedChanged += (s, e) => { _ds.DetectWhite = cbWhite.Checked; Queue(); };
            cbRow.Controls.AddRange(new Control[] { cbGold, cbWhite }); tbl.Controls.Add(cbRow);

            AddHeader(tbl, "Labels & export");
            tbl.Controls.Add(LabeledRow("Label size (px)", _labelSize));
            tbl.Controls.Add(LabeledRow("Export upscale ×", _upscale));
            _labelSize.ValueChanged += (s, e) => { _ro.LabelPx = (float)_labelSize.Value; _canvas.Invalidate(); };
            var colorBtn = new Button { Text = "Label colour…", AutoSize = true };
            colorBtn.Click += (s, e) => { using (var cd = new ColorDialog { Color = _ro.LabelColor }) if (cd.ShowDialog(this) == DialogResult.OK) { _ro.LabelColor = cd.Color; _canvas.Invalidate(); } };
            var reset = new Button { Text = "Reset defaults", AutoSize = true };
            reset.Click += (s, e) => { if (MessageBox.Show(this, "Reset detection sliders to skill defaults?", "Reset", MessageBoxButtons.YesNo) == DialogResult.Yes) ResetSliders(tbl); };
            var btnRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; btnRow.Controls.AddRange(new Control[] { colorBtn, reset });
            tbl.Controls.Add(btnRow);
            AddHeader(tbl, "Pads (click to locate)");

            _list.Columns.Add("#", 40); _list.Columns.Add("X", 60); _list.Columns.Add("Y", 60); _list.Columns.Add("Source", 90);
            _list.SelectedIndexChanged += (s, e) => { _sel = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1; if (_sel >= 0) CenterOn(_pads[_sel]); _canvas.Invalidate(); };
            var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) }; listHost.Controls.Add(_list);
            side.Controls.Add(listHost); side.Controls.Add(tbl);

            _canvas.Dock = DockStyle.Fill; _canvas.BackColor = Color.FromArgb(24, 24, 28);
            _canvas.Paint += PaintCanvas; _canvas.MouseDown += CanvasDown; _canvas.MouseMove += CanvasMove; _canvas.MouseUp += CanvasUp;
            _canvas.MouseWheel += CanvasWheel; _canvas.Resize += (s, e) => _canvas.Invalidate();

            Controls.Add(_canvas); Controls.Add(side); Controls.Add(tools); Controls.Add(menu); Controls.Add(statusStrip);
            MainMenuStrip = menu;
        }

        static ToolStripMenuItem Mi(string t, Action a, Keys k) => new ToolStripMenuItem(t, null, (s, e) => a()) { ShortcutKeys = k };

        readonly List<(TrackBar tb, int def)> _sliders = new List<(TrackBar, int)>();
        void AddHeader(TableLayoutPanel t, string text) =>
            t.Controls.Add(new Label { Text = text, Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 10, 0, 2) });

        void AddSlider(TableLayoutPanel t, string name, int min, int max, int def, Action<int> apply, Func<int, string> fmt)
        {
            var lbl = new Label { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            var tb = new TrackBar { Minimum = min, Maximum = max, Value = def, TickStyle = TickStyle.None, Width = 270, Height = 28, AutoSize = false };
            tb.ValueChanged += (s, e) => { apply(tb.Value); lbl.Text = $"{name}: {fmt(tb.Value)}"; Queue(); };
            apply(def); lbl.Text = $"{name}: {fmt(def)}";
            t.Controls.Add(lbl); t.Controls.Add(tb); _sliders.Add((tb, def));
        }

        void ResetSliders(TableLayoutPanel t) { foreach (var (tb, def) in _sliders) tb.Value = def; }

        static Control LabeledRow(string text, Control c)
        {
            var p = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            c.Width = 70; p.Controls.Add(new Label { Text = text, AutoSize = true, Width = 150, Margin = new Padding(0, 6, 8, 0) }); p.Controls.Add(c);
            return p;
        }

        void SetMode(Mode m)
        {
            _mode = m; _bPan.Checked = m == Mode.Pan; _bEdit.Checked = m == Mode.Edit; _bCrop.Checked = m == Mode.Crop;
            _canvas.Cursor = m == Mode.Pan ? Cursors.Hand : Cursors.Cross;
            SetStatus(m == Mode.Edit ? "Edit: left-click adds a pad (snaps to centre) · right-click removes · wheel zooms · middle-drag pans"
                    : m == Mode.Crop ? "Crop: drag a rectangle around the board (exclude battery label and bezel)"
                    : "Pan: drag to move · wheel zooms");
        }

        void SetStatus(string s) => _status.Text = s;

        // ─────────────────────────── Image I/O ───────────────────────────
        void OpenImage()
        {
            using (var d = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.webp|All files|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) LoadImage(d.FileName);
        }

        void PasteImage()
        {
            if (!Clipboard.ContainsImage()) { SetStatus("Clipboard has no image."); return; }
            using (var img = Clipboard.GetImage()) using (var ms = new MemoryStream())
            {
                img.Save(ms, ImageFormat.Png);
                LoadBytes(ms.ToArray(), "pasted.png");
            }
        }

        void LoadImage(string path)
        {
            try { LoadBytes(File.ReadAllBytes(path), path); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open image", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void LoadBytes(byte[] data, string path)
        {
            // ImDecode applies EXIF orientation (phone photos); display bitmap is built from the same Mat so coords match.
            var m = Cv2.ImDecode(data, ImreadModes.Color);
            if (m == null || m.Empty()) throw new InvalidDataException("Unsupported or corrupt image.");
            Cv2.ImEncode(".png", m, out byte[] png);
            _mat?.Dispose(); _bmp?.Dispose();
            _mat = m; _bmp = new Bitmap(new MemoryStream(png)); _path = path;
            _detected.Clear(); _manual.Clear(); _suppressed.Clear(); _undo.Clear(); _crop = null; _sel = -1;
            Text = $"TestPoint Trigger — {Path.GetFileName(path)}  v{AppVersion}";
            FitView(); RunDetect();
        }

        // ─────────────────────────── Detection ───────────────────────────
        void Queue() { if (_mat != null) { _debounce.Stop(); _debounce.Start(); } }

        async void RunDetect()
        {
            if (_mat == null) return;
            int gen = ++_detectGen; var ds = _ds.Clone(); var mat = _mat;
            CvRect? roi = _crop.HasValue ? new CvRect(_crop.Value.X, _crop.Value.Y, _crop.Value.Width, _crop.Value.Height) : (CvRect?)null;
            SetStatus("Detecting…"); UseWaitCursor = true;
            try
            {
                var res = await Task.Run(() => PadDetector.Detect(mat, ds, roi));
                if (gen != _detectGen) return;
                _detected = res; Rebuild();
                SetStatus(_pads.Count == 0
                    ? "No pads found even after auto-relaxing. Crop tighter to the board, or click pads manually in Edit mode."
                    : $"{_pads.Count} candidate pads ({_detected.Count} detected, {_manual.Count} manual, {_suppressed.Count} removed).");
            }
            catch (Exception ex) { SetStatus("Detection failed: " + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        void Rebuild()
        {
            var det = _detected.Where(p => !_suppressed.Any(s => Dist(s, p) <= Math.Max(p.R, 3) + 2)).ToList();
            var all = PadDetector.Dedupe(det.Concat(_manual).ToList());
            var rs = all.Select(p => p.R).OrderBy(r => r).ToList();
            double band = rs.Count > 0 ? Math.Max(10, rs[rs.Count / 2] * 3) : 30;
            _pads = PadDetector.Order(all, band);
            _list.BeginUpdate(); _list.Items.Clear();
            for (int i = 0; i < _pads.Count; i++)
                _list.Items.Add(new ListViewItem(new[] { (i + 1).ToString(), ((int)_pads[i].X).ToString(), ((int)_pads[i].Y).ToString(), _pads[i].Manual ? "manual" : $"auto {_pads[i].Circularity:0.00}" }));
            _list.EndUpdate();
            _sel = -1; _canvas.Invalidate();
        }

        static double Dist(PointF a, Pad p) => Math.Sqrt(Math.Pow(a.X - p.X, 2) + Math.Pow(a.Y - p.Y, 2));

        void PushUndo()
        {
            _undo.Push((_manual.Select(p => new Pad { X = p.X, Y = p.Y, R = p.R, Manual = true }).ToList(), new List<PointF>(_suppressed)));
        }

        void Undo()
        {
            if (_undo.Count == 0) { SetStatus("Nothing to undo."); return; }
            var (m, s) = _undo.Pop(); _manual = m; _suppressed.Clear(); _suppressed.AddRange(s); Rebuild();
            SetStatus($"Undone. {_pads.Count} pads.");
        }

        // ─────────────────────────── View math ───────────────────────────
        PointF ToScreen(PointF p) => new PointF(p.X * _zoom + _off.X, p.Y * _zoom + _off.Y);
        PointF ToImage(Point p) => new PointF((p.X - _off.X) / _zoom, (p.Y - _off.Y) / _zoom);

        void FitView()
        {
            if (_bmp == null) return;
            var r = _crop ?? new Rectangle(0, 0, _bmp.Width, _bmp.Height);
            _zoom = Math.Min((_canvas.Width - 20f) / r.Width, (_canvas.Height - 20f) / r.Height);
            _off = new PointF(_canvas.Width / 2f - (r.X + r.Width / 2f) * _zoom, _canvas.Height / 2f - (r.Y + r.Height / 2f) * _zoom);
            _canvas.Invalidate();
        }

        void CenterOn(Pad p)
        {
            _zoom = Math.Max(_zoom, 3f);
            _off = new PointF(_canvas.Width / 2f - (float)p.X * _zoom, _canvas.Height / 2f - (float)p.Y * _zoom);
        }

        void PaintCanvas(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            if (_bmp == null)
            {
                TextRenderer.DrawText(g, "Drop a motherboard photo here\nor press Ctrl+O", new Font("Segoe UI", 16f), _canvas.ClientRectangle, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            g.InterpolationMode = _zoom > 2 ? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor : System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(_bmp, _off.X, _off.Y, _bmp.Width * _zoom, _bmp.Height * _zoom);

            if (_crop.HasValue) DimOutside(g, _crop.Value);
            PadRenderer.Draw(g, _pads, _ro, ToScreen, _zoom, _sel);
            if (_cropDrag.HasValue)
            {
                var a = ToScreen(_cropDrag.Value.Location); var r = _cropDrag.Value;
                using (var pen = new Pen(Color.DeepSkyBlue, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    g.DrawRectangle(pen, a.X, a.Y, r.Width * _zoom, r.Height * _zoom);
            }
        }

        void DimOutside(Graphics g, Rectangle c)
        {
            var a = ToScreen(c.Location); var rc = new RectangleF(a.X, a.Y, c.Width * _zoom, c.Height * _zoom);
            using (var reg = new Region(_canvas.ClientRectangle)) using (var br = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            { reg.Exclude(rc); g.FillRegion(br, reg); }
            using (var pen = new Pen(Color.DeepSkyBlue, 1.5f)) g.DrawRectangle(pen, rc.X, rc.Y, rc.Width, rc.Height);
        }

        // ─────────────────────────── Mouse ───────────────────────────
        void CanvasDown(object sender, MouseEventArgs e)
        {
            _canvas.Focus();
            if (_bmp == null) return;
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && _mode == Mode.Pan))
            { _panning = true; _dragStart = e.Location; _panOrigin = _off; return; }
            var ip = ToImage(e.Location);
            if (_mode == Mode.Crop && e.Button == MouseButtons.Left) { _dragStart = e.Location; _cropDrag = new Rectangle((int)ip.X, (int)ip.Y, 0, 0); return; }
            if (_mode != Mode.Edit) return;

            int hit = HitTest(ip);
            if (e.Button == MouseButtons.Right && hit >= 0)
            {
                PushUndo(); var p = _pads[hit];
                if (p.Manual) _manual.RemoveAll(m => Math.Abs(m.X - p.X) < 0.5 && Math.Abs(m.Y - p.Y) < 0.5);
                else _suppressed.Add(new PointF((float)p.X, (float)p.Y));
                Rebuild(); SetStatus($"Removed pad. {_pads.Count} pads.");
            }
            else if (e.Button == MouseButtons.Left)
            {
                if (hit >= 0) { _sel = hit; _list.Items[hit].Selected = true; _list.EnsureVisible(hit); _canvas.Invalidate(); return; }
                var rs = _pads.Select(p => p.R).OrderBy(r => r).ToList();
                double defR = rs.Count > 0 ? rs[rs.Count / 2] : 6;
                var np = PadDetector.Snap(_mat, ip.X, ip.Y, defR);
                PushUndo();
                _suppressed.RemoveAll(s => Math.Sqrt(Math.Pow(s.X - np.X, 2) + Math.Pow(s.Y - np.Y, 2)) <= np.R + 2);
                _manual.Add(np); Rebuild(); SetStatus($"Added pad. {_pads.Count} pads.");
            }
        }

        int HitTest(PointF ip)
        {
            int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < _pads.Count; i++)
            {
                double d = Math.Sqrt(Math.Pow(_pads[i].X - ip.X, 2) + Math.Pow(_pads[i].Y - ip.Y, 2));
                if (d <= _pads[i].R + 4 / _zoom + 2 && d < bd) { bd = d; best = i; }
            }
            return best;
        }

        void CanvasMove(object sender, MouseEventArgs e)
        {
            if (_panning) { _off = new PointF(_panOrigin.X + e.X - _dragStart.X, _panOrigin.Y + e.Y - _dragStart.Y); _canvas.Invalidate(); return; }
            if (_cropDrag.HasValue)
            {
                var a = ToImage(_dragStart); var b = ToImage(e.Location);
                _cropDrag = Rectangle.FromLTRB((int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y), (int)Math.Max(a.X, b.X), (int)Math.Max(a.Y, b.Y));
                _canvas.Invalidate();
            }
        }

        void CanvasUp(object sender, MouseEventArgs e)
        {
            _panning = false;
            if (_cropDrag.HasValue)
            {
                var r = Rectangle.Intersect(_cropDrag.Value, new Rectangle(0, 0, _bmp.Width, _bmp.Height)); _cropDrag = null;
                if (r.Width > 20 && r.Height > 20) { _crop = r; SetMode(Mode.Edit); RunDetect(); }
                _canvas.Invalidate();
            }
        }

        void CanvasWheel(object sender, MouseEventArgs e)
        {
            if (_bmp == null) return;
            var ip = ToImage(e.Location);
            _zoom = Math.Max(0.05f, Math.Min(40f, _zoom * (e.Delta > 0 ? 1.2f : 1 / 1.2f)));
            _off = new PointF(e.X - ip.X * _zoom, e.Y - ip.Y * _zoom);
            _canvas.Invalidate();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Control) return;
            if (e.KeyCode == Keys.E) SetMode(Mode.Edit);
            else if (e.KeyCode == Keys.P || e.KeyCode == Keys.Space) SetMode(Mode.Pan);
            else if (e.KeyCode == Keys.C) SetMode(Mode.Crop);
            else if (e.KeyCode == Keys.F) FitView();
            else if (e.KeyCode == Keys.D) RunDetect();
            else if (e.KeyCode == Keys.Delete && _sel >= 0)
            {
                var p = _pads[_sel]; PushUndo();
                if (p.Manual) _manual.Remove(p); else _suppressed.Add(new PointF((float)p.X, (float)p.Y));
                Rebuild();
            }
        }

        // ─────────────────────────── Export ───────────────────────────
        string BaseName => _path == null ? "board" : Path.GetFileNameWithoutExtension(_path);

        void ExportPng()
        {
            if (_bmp == null) return;
            using (var d = new SaveFileDialog { Filter = "PNG image|*.png", FileName = BaseName + "_pads.png" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var crop = _crop ?? new Rectangle(0, 0, _bmp.Width, _bmp.Height);
                float up = (float)_upscale.Value;
                long px = (long)(crop.Width * up) * (long)(crop.Height * up);
                if (px > 180_000_000) { MessageBox.Show(this, "Export too large — lower the upscale or crop to the board.", "Export"); return; }
                using (var bmp = PadRenderer.Export(_bmp, crop, _pads, _ro, up, $"{Credit} · {_pads.Count} pads"))
                    bmp.Save(d.FileName, ImageFormat.Png);
                SetStatus($"Saved {d.FileName}");
            }
        }

        void ExportCsv()
        {
            if (_pads.Count == 0) return;
            using (var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = BaseName + "_pads.csv" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var sb = new StringBuilder("id,x,y,radius,source,circularity,probed,notes\r\n");
                for (int i = 0; i < _pads.Count; i++)
                {
                    var p = _pads[i];
                    sb.Append($"{i + 1},{(int)Math.Round(p.X)},{(int)Math.Round(p.Y)},{p.R:0.0},{(p.Manual ? "manual" : "auto")},{(p.Manual ? "" : p.Circularity.ToString("0.00"))},,\r\n");
                }
                sb.Append($"# {Credit}\r\n");
                File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
                SetStatus($"Saved {d.FileName}");
            }
        }

        void ShowHelp() => MessageBox.Show(this,
            "1. Open, drop or paste a top-down motherboard photo.\n" +
            "2. Crop (C): drag around the board to exclude the battery label and bezel.\n" +
            "3. Tune sliders if needed — missing pads: lower Min size / Roundness; false hits: raise them.\n" +
            "4. Edit (E): left-click a missed pad to add it (snaps to its centre); right-click or Delete removes.\n" +
            "   Removed auto-pads stay removed when you re-detect. Ctrl+Z undoes.\n" +
            "5. Choose Labels only or Circles + labels, then Export PNG (upscaled) and CSV checklist.\n\n" +
            "Keys: E edit · P/Space pan · C crop · F fit · D detect · wheel zoom · middle-drag pan.\n\n" +
            "Candidates only — confirm the real test point by probing.", "How to use");
    }

    class CanvasPanel : Panel
    {
        public CanvasPanel() { DoubleBuffered = true; ResizeRedraw = true; SetStyle(ControlStyles.Selectable, true); TabStop = true; }
    }
}
