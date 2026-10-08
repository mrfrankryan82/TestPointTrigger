// TestPoint Trigger - Modular host shell (NAXUS-themed)
// Developer: HaKDMoDz™ · v3.1.0 · 2026-09-26
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// The application shell: a themed sidebar (logo + nav), a glowing per-module
    /// header card, the module content area and a footer. Knows nothing about
    /// what any module does.
    /// </summary>
    public partial class ModuleHostForm : Form, IModuleHost
    {
        private readonly List<IModule> _modules = new List<IModule>();
        private readonly Dictionary<string, Control> _views =
            new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

        private IModule _active;
        private int _hoverIndex = -1;

        public ModuleHostForm()
        {
            // Sidebar, logo, navigation, header card and footer live in
            // ModuleHostForm.Designer.cs (open it in Design View to restyle).
            InitializeComponent();
            DoubleBuffered = true;
            Text = "Mobile Surgery — Bench Suite  v" + MainForm.AppVersion;
            lblSideFoot.Text += "\r\n\r\n" + MainForm.Credit;
            lblVersion.Text = MainForm.Credit;
        }

        private void ModuleHostForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _active?.Deactivate();
            foreach (var m in _modules) m.Dispose();
        }

        private void lstNav_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstNav.SelectedIndex >= 0 && lstNav.SelectedIndex < _modules.Count)
                Show(_modules[lstNav.SelectedIndex]);
        }

        private void lstNav_MouseMove(object sender, MouseEventArgs e)
        {
            int i = lstNav.IndexFromPoint(e.Location);
            if (i != _hoverIndex) { _hoverIndex = i; lstNav.Invalidate(); }
        }

        private void lstNav_MouseLeave(object sender, EventArgs e) { _hoverIndex = -1; lstNav.Invalidate(); }

        /// <summary>Draws the logo mark; the name and subtitle are labels on the card.</summary>
        private void cardLogo_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = 48, cy = cardLogo.Height / 2;
            using (var glow = new SolidBrush(Color.FromArgb(40, Theme.Cyan)))
                g.FillEllipse(glow, cx - 24, cy - 24, 48, 48);
            using (var pen = new Pen(Theme.CyanDim, 1.4f))
                g.DrawEllipse(pen, cx - 20, cy - 20, 40, 40);
            using (var b = new SolidBrush(Theme.CyanBright))
                g.FillEllipse(b, cx - 6, cy - 6, 12, 12);
            using (var pen = new Pen(Theme.CyanBright, 1.4f))
            {
                g.DrawLine(pen, cx, cy - 20, cx, cy - 13);
                g.DrawLine(pen, cx, cy + 13, cx, cy + 20);
                g.DrawLine(pen, cx - 20, cy, cx - 13, cy);
                g.DrawLine(pen, cx + 13, cy, cx + 20, cy);
            }
        }

        private void DrawNavItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _modules.Count) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var m = _modules[e.Index];
            bool selected = e.Index == lstNav.SelectedIndex;
            bool hover = e.Index == _hoverIndex;

            using (var bg = new SolidBrush(Theme.Sidebar)) g.FillRectangle(bg, e.Bounds);

            var r = new Rectangle(e.Bounds.Left + 2, e.Bounds.Top + 3, e.Bounds.Width - 6, e.Bounds.Height - 6);
            if (selected)
            {
                Theme.FillRound(g, r, 11, Theme.Panel2, Theme.Border);
                using (var bar = new SolidBrush(Theme.Cyan))
                    g.FillRectangle(bar, r.Left + 1, r.Top + 8, 3, r.Height - 16);
            }
            else if (hover)
            {
                Theme.FillRound(g, r, 11, Theme.C2("#0D1420"), Color.Empty);
            }

            var fg = selected || hover ? Theme.Text : Theme.Muted;
            var iconColor = selected ? Theme.Cyan : (hover ? Theme.CyanDim : Theme.Muted);

            using (var icf = Theme.Icons)
            using (var ib = new SolidBrush(iconColor))
                g.DrawString(Theme.Glyph(m.Id), icf, ib, r.Left + 16, r.Top + (r.Height - 20) / 2f);

            using (var tf = Theme.NavFont)
            using (var b = new SolidBrush(fg))
                g.DrawString(m.Title, tf, b, r.Left + 46, r.Top + (r.Height - 20) / 2f);
        }

        /// <summary>Register a module. Call before showing the form.</summary>
        public void Register(IModule module)
        {
            if (module == null) return;
            if (_modules.Any(m => string.Equals(m.Id, module.Id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Duplicate module id: " + module.Id);

            _modules.Add(module);
            _modules.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            RebuildNav();
        }

        private void RebuildNav()
        {
            var keep = _active?.Id;
            lstNav.Items.Clear();
            foreach (var m in _modules) lstNav.Items.Add(m.Title);
            if (keep != null)
            {
                var i = _modules.FindIndex(m => string.Equals(m.Id, keep, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) lstNav.SelectedIndex = i;
            }
            else if (lstNav.Items.Count > 0) lstNav.SelectedIndex = 0;
        }

        private void Show(IModule module)
        {
            if (module == null || ReferenceEquals(module, _active)) return;
            _active?.Deactivate();

            Control view;
            if (!_views.TryGetValue(module.Id, out view))
            {
                try
                {
                    view = module.CreateView(this);
                    view.Dock = DockStyle.Fill;
                    _views[module.Id] = view;
                }
                catch (Exception ex)
                {
                    view = new Label
                    {
                        Dock = DockStyle.Fill,
                        Text = module.Title + " failed to load:\r\n\r\n" + ex.Message,
                        TextAlign = ContentAlignment.MiddleCenter,
                        ForeColor = Theme.Red,
                        BackColor = Theme.Bg
                    };
                    _views[module.Id] = view;
                }
            }

            pnlViewHost.SuspendLayout();
            pnlViewHost.Controls.Clear();
            pnlViewHost.Controls.Add(view);
            pnlViewHost.ResumeLayout();

            heroCard.Set(module.Title, module.Description);
            _active = module;
            SetStatus(module.Description);
            module.Activate();
        }

        // ───────────────────────── IModuleHost ─────────────────────────

        public void SetStatus(string text)
        {
            if (lblStatus == null) return;
            Action set = () => lblStatus.Text = text ?? string.Empty;
            if (InvokeRequired) BeginInvoke(set); else set();
        }

        public void Notify(string message, ModuleSeverity severity)
        {
            AppLog.Append("Host", severity.ToString().ToUpperInvariant(), message);
            SetStatus(message);
            if (severity == ModuleSeverity.Error || severity == ModuleSeverity.Warning)
                System.Media.SystemSounds.Exclamation.Play();
            else if (severity == ModuleSeverity.Success)
                System.Media.SystemSounds.Asterisk.Play();
        }

        public bool Navigate(string moduleId)
        {
            var i = _modules.FindIndex(m => string.Equals(m.Id, moduleId, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return false;
            Action go = () => lstNav.SelectedIndex = i;
            if (InvokeRequired) BeginInvoke(go); else go();
            return true;
        }
    }
}
