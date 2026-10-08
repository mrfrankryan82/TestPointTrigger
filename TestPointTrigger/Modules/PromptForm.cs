// Mobile Surgery - text-input dialog (layout in PromptForm.Designer.cs)
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
