import re
from patch import *
F = r"Modules\ModuleHostForm.cs"
t = load(F)
nl = "\r\n" if "\r\n" in t else "\n"
fix = lambda x: x.replace("\r\n", "\n").replace("\n", nl)

t = between(t, "    public class ModuleHostForm : Form, IModuleHost", "        private void DrawNavItem(", fix('''    public partial class ModuleHostForm : Form, IModuleHost
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
            lblSideFoot.Text += "\\r\\n\\r\\n" + MainForm.Credit;
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

'''))

for old, new in [("_nav", "lstNav"), ("_viewHost", "pnlViewHost"), ("_hero", "heroCard"), ("_status", "lblStatus")]:
    t = re.sub(r"\b%s\b" % re.escape(old), new, t)

t = replace1(t, "                    Theme.Apply(view);           // darken the module content to match the shell" + nl, "")
save(F, t); print("patched", F)
