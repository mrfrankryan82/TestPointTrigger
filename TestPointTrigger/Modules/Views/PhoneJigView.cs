// Mobile Surgery - Phone Jig view (layout lives in PhoneJigView.Designer.cs; behaviour in PhoneJigModule)
// Developer: HaKDMoDz™ · v3.7.0 · 2026-10-09
using System.Windows.Forms;

namespace TestPointTrigger.Modules.Views
{
    /// <summary>
    /// Design-time surface for the Phone Jig module. Open in Design View to restyle
    /// anything; PhoneJigModule finds the controls by name and wires their behaviour.
    /// Boot-mode buttons, wiring steps and device rows are data-driven and added at runtime.
    /// </summary>
    public partial class PhoneJigView : UserControl
    {
        public PhoneJigView()
        {
            InitializeComponent();
        }
    }
}
