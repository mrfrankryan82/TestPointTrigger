// TestPoint Trigger - Modular host shell
// Developer: HaKDMoDz™ · v3.0.0 · 2026-09-26
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// The application shell. Owns a list of modules, a navigation strip
    /// and a content area. Knows nothing about what any module does.
    /// </summary>
    public class ModuleHostForm : Form, IModuleHost
    {
        private readonly List<IModule> _modules = new List<IModule>();
        private readonly Dictionary<string, Control> _views =
            new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

        private IModule _active;
        private readonly ListBox _nav = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            ItemHeight = 28,
            BorderStyle = BorderStyle.None
        };
        private readonly Panel _content = new Panel { Dock = DockStyle.Fill };
        private readonly ToolStripStatusLabel _status =
            new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        public ModuleHostForm()
        {
            Text = $"TestPoint Trigger — Bench Suite  v{MainForm.AppVersion}";
            Size = new Size(1400, 900);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterDistance = 220
            };

            _nav.SelectedIndexChanged += (s, e) =>
            {
                if (_nav.SelectedIndex >= 0 && _nav.SelectedIndex < _modules.Count)
                    Show(_modules[_nav.SelectedIndex]);
            };
            _nav.DrawMode = DrawMode.OwnerDrawFixed;
            _nav.DrawItem += DrawNavItem;

            split.Panel1.Controls.Add(_nav);
            split.Panel2.Controls.Add(_content);

            var strip = new StatusStrip();
            strip.Items.Add(_status);
            strip.Items.Add(new ToolStripStatusLabel(MainForm.Credit) { ForeColor = Color.DimGray });

            Controls.Add(split);
            Controls.Add(strip);

            FormClosing += (s, e) =>
            {
                _active?.Deactivate();
                foreach (var m in _modules) m.Dispose();
            };
        }

        private void DrawNavItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _modules.Count) return;
            e.DrawBackground();
            var m = _modules[e.Index];
            using (var brush = new SolidBrush(e.ForeColor))
                e.Graphics.DrawString(m.Title, e.Font, brush, e.Bounds.Left + 8, e.Bounds.Top + 5);
            e.DrawFocusRectangle();
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
                var i = _modules.FindIndex(m =>
                    string.Equals(m.Id, keep, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) _nav.SelectedIndex = i;
            }
            else if (_nav.Items.Count > 0) _nav.SelectedIndex = 0;
        }

        /// <summary>
        /// Switch to a module. Views are built lazily on first show and then
        /// cached, so navigating away and back is cheap. The outgoing module
        /// is always deactivated first so nothing keeps a camera or timer
        /// running in the background.
        /// </summary>
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
                        ForeColor = Color.Firebrick
                    };
                    _views[module.Id] = view;
                }
            }

            _content.SuspendLayout();
            _content.Controls.Clear();
            _content.Controls.Add(view);
            _content.ResumeLayout();

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
            SetStatus(message);
            if (severity == ModuleSeverity.Error || severity == ModuleSeverity.Warning)
                System.Media.SystemSounds.Exclamation.Play();
            else if (severity == ModuleSeverity.Success)
                System.Media.SystemSounds.Asterisk.Play();
        }

        public bool Navigate(string moduleId)
        {
            var i = _modules.FindIndex(m =>
                string.Equals(m.Id, moduleId, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return false;
            Action go = () => _nav.SelectedIndex = i;
            if (InvokeRequired) BeginInvoke(go); else go();
            return true;
        }
    }
}
