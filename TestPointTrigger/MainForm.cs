// Mobile Surgery - PCB Pad Finder
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

        enum Mode { Pan, Edit, Crop, Box }

        // state
        Mat _mat; Bitmap _bmp; string _path;
        List<Pad> _detected = new List<Pad>(), _manual = new List<Pad>(), _pads = new List<Pad>();
        List<Pad> _boxDetected = new List<Pad>();
        readonly List<PointF> _suppressed = new List<PointF>();
        readonly Stack<(List<Pad> m, List<PointF> s)> _undo = new Stack<(List<Pad>, List<PointF>)>();
        Rectangle? _crop; Rectangle? _cropDrag; Rectangle? _boxDrag; Point _dragStart; bool _panning; PointF _panOrigin;
        float _zoom = 1f; PointF _off; Mode _mode = Mode.Edit; int _sel = -1;
        readonly DetectorSettings _ds = new DetectorSettings();
        readonly RenderOptions _ro = new RenderOptions();
        int _detectGen;

        // Controls are declared in MainForm.Designer.cs.
        readonly Timer _debounce = new Timer { Interval = 350 };

        public MainForm()
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
            $"Mobile Surgery — PCB Pad Finder\nVersion {AppVersion} ({BuildDate})\n\nDeveloper: HaKDMoDz™\n\nFinds gold and white/tinned test pads on motherboard photos and labels them for elimination probing.",
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

        void SetMode(Mode m)
        {
            // Entering Box mode clears the full-board auto-detections so you build
            // your set region by region instead of starting from the flood.
            if (m == Mode.Box && _detected.Count > 0) { _detected.Clear(); Rebuild(); }

            _mode = m; tsbPan.Checked = m == Mode.Pan; tsbEdit.Checked = m == Mode.Edit; tsbCrop.Checked = m == Mode.Crop; tsbBox.Checked = m == Mode.Box;
            canvas.Cursor = m == Mode.Pan ? Cursors.Hand : Cursors.Cross;
            SetStatus(m == Mode.Edit ? "Edit: left-click adds a pad (snaps to centre) · right-click removes · wheel zooms · middle-drag pans"
                    : m == Mode.Crop ? "Crop: drag a rectangle around the board (exclude battery label and bezel)"
                    : m == Mode.Box ? "Box detect: drag a rectangle to detect pads only inside it. Each box adds to your set; Edit (E) to curate."
                    : "Pan: drag to move · wheel zooms");
        }

        void SetStatus(string s) => lblStatus.Text = s;

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
            _detected.Clear(); _manual.Clear(); _boxDetected.Clear(); _suppressed.Clear(); _undo.Clear(); _crop = null; _sel = -1;
            Text = $"Mobile Surgery — {Path.GetFileName(path)}  v{AppVersion}";
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

        // Detect only inside a dragged box and ADD the results to the running set.
        async void RunBoxDetect(Rectangle box)
        {
            if (_mat == null) return;
            var ds = _ds.Clone(); var mat = _mat;
            var roi = new CvRect(box.X, box.Y, box.Width, box.Height);
            SetStatus("Detecting in box…"); UseWaitCursor = true;
            try
            {
                var res = await Task.Run(() => PadDetector.Detect(mat, ds, roi));
                _boxDetected = PadDetector.Dedupe(_boxDetected.Concat(res).ToList());
                Rebuild();
                SetStatus($"Box: +{res.Count} pads · {_pads.Count} total. Drag another box, or Edit (E) to curate.");
            }
            catch (Exception ex) { SetStatus("Box detect failed: " + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        void Rebuild()
        {
            var det = _detected.Concat(_boxDetected).Where(p => !_suppressed.Any(s => Dist(s, p) <= Math.Max(p.R, 3) + 2)).ToList();
            var all = PadDetector.Dedupe(det.Concat(_manual).ToList());
            var rs = all.Select(p => p.R).OrderBy(r => r).ToList();
            double band = rs.Count > 0 ? Math.Max(10, rs[rs.Count / 2] * 3) : 30;
            _pads = PadDetector.Order(all, band);
            lvPads.BeginUpdate(); lvPads.Items.Clear();
            for (int i = 0; i < _pads.Count; i++)
                lvPads.Items.Add(new ListViewItem(new[] { (i + 1).ToString(), ((int)_pads[i].X).ToString(), ((int)_pads[i].Y).ToString(), _pads[i].Manual ? "manual" : $"auto {_pads[i].Circularity:0.00}" }));
            lvPads.EndUpdate();
            _sel = -1; canvas.Invalidate();
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
            _zoom = Math.Min((canvas.Width - 20f) / r.Width, (canvas.Height - 20f) / r.Height);
            _off = new PointF(canvas.Width / 2f - (r.X + r.Width / 2f) * _zoom, canvas.Height / 2f - (r.Y + r.Height / 2f) * _zoom);
            canvas.Invalidate();
        }

        void CenterOn(Pad p)
        {
            _zoom = Math.Max(_zoom, 3f);
            _off = new PointF(canvas.Width / 2f - (float)p.X * _zoom, canvas.Height / 2f - (float)p.Y * _zoom);
        }

        void PaintCanvas(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            if (_bmp == null)
            {
                TextRenderer.DrawText(g, "Drop a motherboard photo here\nor press Ctrl+O", new Font("Segoe UI", 16f), canvas.ClientRectangle, Color.Gray,
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
            if (_boxDrag.HasValue)
            {
                var a = ToScreen(_boxDrag.Value.Location); var r = _boxDrag.Value;
                using (var pen = new Pen(Color.LimeGreen, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    g.DrawRectangle(pen, a.X, a.Y, r.Width * _zoom, r.Height * _zoom);
            }
        }

        void DimOutside(Graphics g, Rectangle c)
        {
            var a = ToScreen(c.Location); var rc = new RectangleF(a.X, a.Y, c.Width * _zoom, c.Height * _zoom);
            using (var reg = new Region(canvas.ClientRectangle)) using (var br = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            { reg.Exclude(rc); g.FillRegion(br, reg); }
            using (var pen = new Pen(Color.DeepSkyBlue, 1.5f)) g.DrawRectangle(pen, rc.X, rc.Y, rc.Width, rc.Height);
        }

        // ─────────────────────────── Mouse ───────────────────────────
        void CanvasDown(object sender, MouseEventArgs e)
        {
            canvas.Focus();
            if (_bmp == null) return;
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && _mode == Mode.Pan))
            { _panning = true; _dragStart = e.Location; _panOrigin = _off; return; }
            var ip = ToImage(e.Location);
            if (_mode == Mode.Crop && e.Button == MouseButtons.Left) { _dragStart = e.Location; _cropDrag = new Rectangle((int)ip.X, (int)ip.Y, 0, 0); return; }
            if (_mode == Mode.Box && e.Button == MouseButtons.Left) { _dragStart = e.Location; _boxDrag = new Rectangle((int)ip.X, (int)ip.Y, 0, 0); return; }
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
                if (hit >= 0) { _sel = hit; lvPads.Items[hit].Selected = true; lvPads.EnsureVisible(hit); canvas.Invalidate(); return; }
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
            if (_panning) { _off = new PointF(_panOrigin.X + e.X - _dragStart.X, _panOrigin.Y + e.Y - _dragStart.Y); canvas.Invalidate(); return; }
            if (_cropDrag.HasValue)
            {
                var a = ToImage(_dragStart); var b = ToImage(e.Location);
                _cropDrag = Rectangle.FromLTRB((int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y), (int)Math.Max(a.X, b.X), (int)Math.Max(a.Y, b.Y));
                canvas.Invalidate();
            }
            else if (_boxDrag.HasValue)
            {
                var a = ToImage(_dragStart); var b = ToImage(e.Location);
                _boxDrag = Rectangle.FromLTRB((int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y), (int)Math.Max(a.X, b.X), (int)Math.Max(a.Y, b.Y));
                canvas.Invalidate();
            }
        }

        void CanvasUp(object sender, MouseEventArgs e)
        {
            _panning = false;
            if (_cropDrag.HasValue)
            {
                var r = Rectangle.Intersect(_cropDrag.Value, new Rectangle(0, 0, _bmp.Width, _bmp.Height)); _cropDrag = null;
                if (r.Width > 20 && r.Height > 20) { _crop = r; SetMode(Mode.Edit); RunDetect(); }
                canvas.Invalidate();
            }
            else if (_boxDrag.HasValue)
            {
                var r = Rectangle.Intersect(_boxDrag.Value, new Rectangle(0, 0, _bmp.Width, _bmp.Height)); _boxDrag = null;
                if (r.Width > 16 && r.Height > 16) RunBoxDetect(r);
                canvas.Invalidate();
            }
        }

        void CanvasWheel(object sender, MouseEventArgs e)
        {
            if (_bmp == null) return;
            var ip = ToImage(e.Location);
            _zoom = Math.Max(0.05f, Math.Min(40f, _zoom * (e.Delta > 0 ? 1.2f : 1 / 1.2f)));
            _off = new PointF(e.X - ip.X * _zoom, e.Y - ip.Y * _zoom);
            canvas.Invalidate();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Control) return;
            if (e.KeyCode == Keys.E) SetMode(Mode.Edit);
            else if (e.KeyCode == Keys.P || e.KeyCode == Keys.Space) SetMode(Mode.Pan);
            else if (e.KeyCode == Keys.C) SetMode(Mode.Crop);
            else if (e.KeyCode == Keys.B) SetMode(Mode.Box);
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
                float up = (float)nudUpscale.Value;
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
            "2. Box detect (B): drag a rectangle over one area to detect only its pads. Each box adds to your set —\n" +
            "   sweep the board region by region to keep counts small and relevant. (Whole-board Detect is on 🔍 / D.)\n" +
            "3. Crop (C): drag around the board to limit whole-board detection and exclude the bezel.\n" +
            "4. Tune: Min/Max pad radius sets the size of blob to accept; Core roundness rejects trace-like shapes.\n" +
            "5. Edit (E): left-click a missed pad to add it (snaps to its centre); right-click or Delete removes.\n" +
            "   Removed pads stay removed when you re-detect. Ctrl+Z undoes.\n" +
            "6. Choose Labels only or Circles + labels, then Export PNG (upscaled) and CSV checklist.\n\n" +
            "Keys: E edit · B box detect · P/Space pan · C crop · F fit · D detect · wheel zoom · middle-drag pan.\n\n" +
            "Candidates only — confirm the real test point by probing.", "How to use");
    }

    public class CanvasPanel : Panel
    {
        public CanvasPanel() { DoubleBuffered = true; ResizeRedraw = true; SetStyle(ControlStyles.Selectable, true); TabStop = true; }
    }
}
