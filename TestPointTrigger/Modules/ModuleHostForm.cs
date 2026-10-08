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
    public class ModuleHostForm : Form, IModuleHost
    {
        private readonly List<IModule> _modules = new List<IModule>();
        private readonly Dictionary<string, Control> _views =
            new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

        private IModule _active;
        private readonly ListBox _nav;
        private readonly Panel _viewHost;
        private readonly HeroCard _hero;
        private readonly Label _status;
        private int _hoverIndex = -1;

        public ModuleHostForm()
        {
            Text = "TestPoint Trigger — Bench Suite  v" + MainForm.AppVersion;
            Size = new Size(1420, 920);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            DoubleBuffered = true;

            // ── sidebar ──
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 288, BackColor = Theme.Sidebar, Padding = new Padding(16) };

            var logo = BuildLogoCard();

            _nav = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Sidebar,
                ForeColor = Theme.Muted,
                IntegralHeight = false,
                ItemHeight = 46,
                DrawMode = DrawMode.OwnerDrawFixed,
                Font = Theme.NavFont
            };
            _nav.DrawItem += DrawNavItem;
            _nav.SelectedIndexChanged += (s, e) =>
            {
                if (_nav.SelectedIndex >= 0 && _nav.SelectedIndex < _modules.Count)
                    Show(_modules[_nav.SelectedIndex]);
            };
            _nav.MouseMove += (s, e) =>
            {
                int i = _nav.IndexFromPoint(e.Location);
                if (i != _hoverIndex) { _hoverIndex = i; _nav.Invalidate(); }
            };
            _nav.MouseLeave += (s, e) => { _hoverIndex = -1; _nav.Invalidate(); };

            var sideFoot = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 96,
                ForeColor = Theme.Muted2,
                Font = new Font("Segoe UI", 8.5f),
                Text = "Modular bench toolkit\r\nCamera coaching · USB trigger · ADB/Fastboot ·\r\npad finder · job database — one shell.\r\n\r\n" + MainForm.Credit,
                Padding = new Padding(4, 8, 4, 0)
            };

            sidebar.Controls.Add(_nav);
            sidebar.Controls.Add(sideFoot);
            sidebar.Controls.Add(logo);

            // ── main ──
            var main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(24, 18, 24, 0) };

            _hero = new HeroCard();
            _viewHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = Theme.Bg };
            _status = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9f) };
            var ver = new Label { Dock = DockStyle.Right, AutoSize = false, Width = 260, ForeColor = Theme.Muted2, TextAlign = ContentAlignment.MiddleRight, Text = MainForm.Credit, Font = new Font("Segoe UI", 8.5f) };
            footer.Controls.Add(_status);
            footer.Controls.Add(ver);

            main.Controls.Add(_viewHost);
            main.Controls.Add(_hero);
            main.Controls.Add(footer);

            Controls.Add(main);
            Controls.Add(sidebar);

            FormClosing += (s, e) =>
            {
                _active?.Deactivate();
                foreach (var m in _modules) m.Dispose();
            };
        }

        private Control BuildLogoCard()
        {
            var card = new Card { Dock = DockStyle.Top, Height = 82, Radius = 16, Fill = Theme.Panel };
            card.Paint += (s, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                int cx = 48, cy = card.Height / 2;
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
                using (var tf = new Font("Segoe UI", 13.5f, FontStyle.Bold))
                using (var b = new SolidBrush(Theme.Text))
                    g.DrawString("TESTPOINT", tf, b, 84, 18);
                using (var sf = new Font("Segoe UI", 9f))
                using (var b = new SolidBrush(Theme.Muted))
                    g.DrawString("Bench Suite", sf, b, 86, 42);
            };
            return card;
        }

        private void DrawNavItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _modules.Count) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var m = _modules[e.Index];
            bool selected = e.Index == _nav.SelectedIndex;
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
            _nav.Items.Clear();
            foreach (var m in _modules) _nav.Items.Add(m.Title);
            if (keep != null)
            {
                var i = _modules.FindIndex(m => string.Equals(m.Id, keep, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) _nav.SelectedIndex = i;
            }
            else if (_nav.Items.Count > 0) _nav.SelectedIndex = 0;
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
                    Theme.Apply(view);           // darken the module content to match the shell
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

            _viewHost.SuspendLayout();
            _viewHost.Controls.Clear();
            _viewHost.Controls.Add(view);
            _viewHost.ResumeLayout();

            _hero.Set(module.Title, module.Description);
            _active = module;
            SetStatus(module.Description);
            module.Activate();
        }

        // ───────────────────────── IModuleHost ─────────────────────────

        public void SetStatus(string text)
        {
            if (_status == null) return;
            Action set = () => _status.Text = text ?? string.Empty;
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
            Action go = () => _nav.SelectedIndex = i;
            if (InvokeRequired) BeginInvoke(go); else go();
            return true;
        }
    }
}
