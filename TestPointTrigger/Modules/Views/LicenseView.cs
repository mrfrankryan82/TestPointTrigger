// Mobile Surgery - Tool Licences view (layout lives in LicenseView.Designer.cs; behaviour in LicenseModule)
// Developer: HaKDMoDz™ · v3.7.0 · 2026-10-09
using System.Windows.Forms;

namespace TestPointTrigger.Modules.Views
{
    /// <summary>
    /// Design-time surface for the Tool Licences module. Open in Design View to restyle
    /// anything; LicenseModule finds the controls by name and wires their behaviour.
    /// Grid columns are defined here; rows are data-bound by the module (expiry tinting is applied in code).
    /// </summary>
    public partial class LicenseView : UserControl
    {
        public LicenseView()
        {
            InitializeComponent();
        }
    }
}
