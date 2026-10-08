// Mobile Surgery - USB Hub Trigger view (layout lives in UsbTriggerView.Designer.cs; behaviour in UsbTriggerModule)
// Developer: HaKDMoDz™ · v3.7.0 · 2026-10-09
using System.Windows.Forms;

namespace TestPointTrigger.Modules.Views
{
    /// <summary>
    /// Design-time surface for the USB Hub Trigger module. Open in Design View to restyle
    /// anything; UsbTriggerModule finds the controls by name and wires their behaviour.
    /// The arm/enable button colours and text are state-driven and set by the module at runtime.
    /// </summary>
    public partial class UsbTriggerView : UserControl
    {
        public UsbTriggerView()
        {
            InitializeComponent();
        }
    }
}
