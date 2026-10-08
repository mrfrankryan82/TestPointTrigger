// Mobile Surgery - shared visual theme (NAXUS-style dark/cyan)
// Developer: HaKDMoDz™ · v1.0.0 · 2026-09-26
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TestPointTrigger
{
    /// <summary>
    /// Central palette + helpers so every module shares one look: near-black
    /// navy background, cyan primary, amber secondary, rounded bordered cards.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Bg = C("#080B11");
        public static readonly Color Sidebar = C("#090D14");
        public static readonly Color Panel = C("#0D1521");
        public static readonly Color Panel2 = C("#101A28");
        public static readonly Color Field = C("#0B1220");
        public static readonly Color Console = C("#070B11");
        public static readonly Color Border = C("#1B3A4B");
        public static readonly Color BorderSoft = C("#15242F");
        public static readonly Color Cyan = C("#35C4E0");
        public static readonly Color CyanBright = C("#5FDCF2");
        public static readonly Color CyanDim = C("#2A8BA3");
        public static readonly Color Amber = C("#F5972B");
        public static readonly Color Red = C("#EF4757");
        public static readonly Color Green = C("#2FB56C");
        public static readonly Color Text = C("#EAF2F8");
        public static readonly Color Muted = C("#8695A6");
        public static readonly Color Muted2 = C("#5D6B7A");
        public static readonly Color Ink = C("#101822");     // dark text for light surfaces (tool strips)

        public static Font Base => new Font("Segoe UI", 9.5f);
        public static Font Title => Rounded(21f, FontStyle.Bold);
        public static Font Pill => new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
        public static Font NavFont => new Font("Segoe UI", 11f);
        public static Font Icons => new Font("Segoe MDL2 Assets", 12f);

        // Prefer a rounded face if the machine has one; fall back gracefully.
        public static Font Rounded(float size, FontStyle style)
        {
            foreach (var name in new[] { "Baloo 2", "Quicksand", "Segoe UI Variable Display", "Segoe UI Semibold" })
            {
                try { using (var f = new Font(name, size, style)) if (f.Name == name) return new Font(name, size, style); }
                catch { }
            }
            return new Font("Segoe UI", size, style);
        }

        private static Color C(string hex)
        {
            hex = hex.TrimStart('#');
            return Color.FromArgb(
                Convert.ToInt32(hex.Substring(0, 2), 16),
                Convert.ToInt32(hex.Substring(2, 2), 16),
                Convert.ToInt32(hex.Substring(4, 2), 16));
        }

        /// <summary>Public hex-&gt;Color for the odd one-off colour in painters.</summary>
        public static Color C2(string hex) => C(hex);

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill, Color border)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border != Color.Empty)
                    using (var pen = new Pen(border, 1)) g.DrawPath(pen, path);
            }
        }

        /// <summary>
        /// Recursively darken a module's view. Intentionally-coloured controls
        /// (a green Arm button, a red danger button, coloured labels) are left
        /// alone by only restyling controls still on their default colours.
        /// </summary>
        public static void Apply(Control root)
        {
            if (root == null) return;
            Walk(root, true);
        }

        private static void Walk(Control c, bool isRoot)
        {
            switch (c)
            {
                case DataGridView g: StyleGrid(g); break;

                case Button b:
                    if (IsDefault(b.BackColor, SystemColors.Control))
                    {
                        b.FlatStyle = FlatStyle.Flat;
                        b.FlatAppearance.BorderColor = Border;
                        b.FlatAppearance.BorderSize = 1;
                        b.FlatAppearance.MouseOverBackColor = Panel2;
                        b.BackColor = Field;
                        b.ForeColor = CyanBright;
                    }
                    break;

                case TextBox tx:
                    tx.BorderStyle = BorderStyle.FixedSingle;
                    tx.BackColor = tx.ReadOnly ? Console : Field;
                    if (IsDefault(tx.ForeColor, SystemColors.WindowText)) tx.ForeColor = Text;
                    break;

                case ListBox lb:
                    lb.BorderStyle = BorderStyle.None;
                    lb.BackColor = Console;
                    if (IsDefault(lb.ForeColor, SystemColors.WindowText)) lb.ForeColor = Text;
                    break;

                case ComboBox cb:
                    cb.FlatStyle = FlatStyle.Flat;
                    cb.BackColor = Field;
                    if (IsDefault(cb.ForeColor, SystemColors.WindowText)) cb.ForeColor = Text;
                    break;

                case NumericUpDown nud:
                    nud.BorderStyle = BorderStyle.FixedSingle;
                    nud.BackColor = Field;
                    nud.ForeColor = Text;
                    break;

                case CheckBox _:
                case RadioButton _:
                    if (IsDefault(c.ForeColor, SystemColors.ControlText)) c.ForeColor = Text;
                    break;

                case Label _:   // also covers LinkLabel
                    if (IsDefault(c.ForeColor, SystemColors.ControlText)) c.ForeColor = Muted;
                    break;

                case Panel _:   // also covers TableLayoutPanel, FlowLayoutPanel, TabPage
                case GroupBox _:
                case SplitContainer _:
                case TabControl _:
                    if (IsDefault(c.BackColor, SystemColors.Control)) c.BackColor = Bg;
                    if (IsDefault(c.ForeColor, SystemColors.ControlText)) c.ForeColor = Text;
                    break;

                case ToolStrip ts:   // also MenuStrip / StatusStrip
                    // Strips keep their light system background, but would otherwise inherit the
                    // near-white Text colour from the dark panel around them and become unreadable.
                    ts.ForeColor = Ink;
                    foreach (ToolStripItem it in ts.Items)
                        if (IsDefault(it.ForeColor, SystemColors.ControlText) || it.ForeColor.ToArgb() == Text.ToArgb()) it.ForeColor = Ink;
                    break;

                default:
                    if (isRoot && IsDefault(c.BackColor, SystemColors.Control)) c.BackColor = Bg;
                    break;
            }

            foreach (Control child in c.Controls) Walk(child, false);
        }

        public static void StyleGrid(DataGridView g)
        {
            g.EnableHeadersVisualStyles = false;
            g.BackgroundColor = Panel;
            g.BorderStyle = BorderStyle.None;
            g.GridColor = BorderSoft;
            g.RowHeadersVisible = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersDefaultCellStyle.BackColor = Panel2;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Panel2;
            g.ColumnHeadersHeight = 38;
            g.DefaultCellStyle.BackColor = Panel;
            g.DefaultCellStyle.ForeColor = Text;
            g.DefaultCellStyle.SelectionBackColor = C("#123041");
            g.DefaultCellStyle.SelectionForeColor = CyanBright;
            g.AlternatingRowsDefaultCellStyle.BackColor = C("#0B131E");
            g.RowTemplate.Height = 30;
        }

        private static bool IsDefault(Color c, Color sys)
            => c == sys || c == Color.Empty || c.ToArgb() == sys.ToArgb();

        // MDL2 icon glyphs per module id (Segoe MDL2 Assets).
        private static readonly Dictionary<string, string> Glyphs =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "usbtrigger", "\uE945" },   // lightning
                { "adbfastboot", "\uE756" },  // command prompt
                { "livecoach", "\uE714" },    // video
                { "padfinder", "\uE71E" },    // zoom
                { "repairdb", "\uE8F1" },     // library
                { "licenses", "\uE8D7" },     // permissions
                { "logs", "\uE7C3" },         // page
            };

        public static string Glyph(string moduleId)
            => moduleId != null && Glyphs.TryGetValue(moduleId, out var g) ? g : "\uE700";
    }

    /// <summary>Rounded, bordered dark panel used as a content card.</summary>
    public class Card : Panel
    {
        public int Radius { get; set; } = 16;
        public Color Fill { get; set; } = Theme.Panel;
        public Color Stroke { get; set; } = Theme.Border;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(e.Graphics, r, Radius, Fill, Stroke);
        }
    }

    /// <summary>The glowing page-header card (pill + big title + subtitle).</summary>
    public class HeroCard : Panel
    {
        public string Pill { get; set; } = "BENCH MODULE";
        public string TitleText { get; set; } = "";
        public string Subtitle { get; set; } = "";

        public HeroCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Height = 120;
            Dock = DockStyle.Top;
            Padding = new Padding(0, 0, 0, 12);
        }

        public void Set(string title, string subtitle)
        {
            TitleText = title ?? ""; Subtitle = subtitle ?? ""; Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 13);

            using (var path = Theme.Round(r, 18))
            {
                using (var lg = new LinearGradientBrush(r, Theme.C2("#0F1C28"), Theme.C2("#0B1017"), 115f))
                    g.FillPath(lg, path);

                // cyan glow, top-right
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (var gp = new GraphicsPath())
                {
                    var glow = new Rectangle(r.Right - 360, r.Top - 220, 480, 480);
                    gp.AddEllipse(glow);
                    using (var pgb = new PathGradientBrush(gp))
                    {
                        pgb.CenterColor = Color.FromArgb(70, Theme.Cyan);
                        pgb.SurroundColors = new[] { Color.FromArgb(0, Theme.Cyan) };
                        g.FillPath(pgb, gp);
                    }
                }
                g.Clip = clip;

                using (var pen = new Pen(Theme.Border, 1)) g.DrawPath(pen, path);
            }

            // pill
            using (var pf = Theme.Pill)
            {
                var ps = g.MeasureString(Pill, pf);
                var pr = new Rectangle(28, 20, (int)ps.Width + 22, (int)ps.Height + 10);
                using (var pp = Theme.Round(pr, 8))
                using (var pen = new Pen(Theme.CyanDim, 1))
                    g.DrawPath(pen, pp);
                using (var b = new SolidBrush(Theme.CyanBright))
                    g.DrawString(Pill, pf, b, pr.X + 11, pr.Y + 5);
            }

            using (var tf = Theme.Title)
            using (var b = new SolidBrush(Theme.Text))
                g.DrawString(TitleText, tf, b, 26, 46);

            if (!string.IsNullOrEmpty(Subtitle))
                using (var sf = new Font("Segoe UI", 10f))
                using (var b = new SolidBrush(Theme.Muted))
                    g.DrawString(Subtitle, sf, b, new RectangleF(28, 86, Width - 60, 24));
        }
    }
}
