from patch import *

# HelpForm: constructor now fills the designer controls
F = "HelpForm.cs"
t = load(F)
nl = "\r\n" if "\r\n" in t else "\n"
fix = lambda x: x.replace("\r\n", "\n").replace("\n", nl)
t = t.replace("TestPoint Trigger", "Mobile Surgery")
t = replace1(t, "    public class HelpForm : Form", "    public partial class HelpForm : Form")
t = t.replace("    /// static dialog.", "    /// static dialog. Layout lives in HelpForm.Designer.cs.")
t = t.replace("Built" + nl + "    /// entirely in code (no separate .Designer.cs) since it's a one-off" + nl, "")
t = between(t, "        public HelpForm(string repoUrl)", "        private void OpenRepo()", fix('''        /// <summary>Designer-only constructor.</summary>
        public HelpForm() : this("https://github.com/mrfrankryan82/TestPointTrigger") { }

        public HelpForm(string repoUrl)
        {
            InitializeComponent();
            _repoUrl = repoUrl;
            txtBlurb.Text = Blurb;
            lblFooter.Text = AppInfo.FooterText;
        }

        private void lnkReadme_Click(object sender, EventArgs e) => OpenRepo();

'''))
save(F, t); print("patched", F)

# Prompt: uses the designer PromptForm instead of building a form in code
F = r"Modules\CliRunner.cs"
t = load(F)
nl = "\r\n" if "\r\n" in t else "\n"
fix = lambda x: x.replace("\r\n", "\n").replace("\n", nl)
t = between(t, "        public static string Text(System.Windows.Forms.IWin32Window owner", "    }" + nl + "}", fix('''        public static string Text(System.Windows.Forms.IWin32Window owner, string message, string title, string def = "")
        {
            using (var f = new PromptForm(message, title, def))
                return f.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK ? f.Value : null;
        }
'''))
save(F, t); print("patched", F)

with open(ROOT + r"Modules\PromptForm.cs", "w", encoding="utf-8-sig", newline="\r\n") as f:
    f.write('''// Mobile Surgery - text-input dialog (layout in PromptForm.Designer.cs)
// Developer: HaKDMoDz™ · v1.0.0 · 2026-10-09
using System.Windows.Forms;

namespace TestPointTrigger.Modules
{
    /// <summary>Tiny text-input dialog (WinForms has no built-in InputBox). Used by Prompt.Text.</summary>
    internal sealed partial class PromptForm : Form
    {
        /// <summary>Designer-only constructor.</summary>
        public PromptForm() : this("Message", "Input", "") { }

        public PromptForm(string message, string title, string def)
        {
            InitializeComponent();
            Text = title;
            lblMessage.Text = message;
            txtValue.Text = def ?? "";
        }

        public string Value => txtValue.Text;
    }
}
''')
print("wrote PromptForm.cs")
