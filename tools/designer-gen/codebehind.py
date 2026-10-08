import os
ROOT = r"C:\Users\User\source\repos\TestPointTrigger\TestPointTrigger\Modules\Views" + "\\"
VIEWS = {
    "UsbTriggerView": ("UsbTriggerModule", "USB Hub Trigger",
                       "The arm/enable button colours and text are state-driven and set by the module at runtime."),
    "AdbFastbootView": ("AdbFastbootModule", "ADB / Fastboot", None),
    "LiveCoachView": ("LiveCoachModule", "Live Coach", None),
    "LogView": ("LogModule", "Logs", "Grid columns are defined here; rows are data-bound by the module."),
    "LicenseView": ("LicenseModule", "Tool Licences",
                    "Grid columns are defined here; rows are data-bound by the module (expiry tinting is applied in code)."),
    "RepairDbView": ("RepairDbModule", "Repair DB", "Grid columns are defined here; rows are data-bound by the module."),
}
T = '''// Mobile Surgery - {title} view (layout lives in {cls}.Designer.cs; behaviour in {mod})
// Developer: HaKDMoDz™ · v3.7.0 · 2026-10-09
using System.Windows.Forms;

namespace TestPointTrigger.Modules.Views
{{
    /// <summary>
    /// Design-time surface for the {title} module. Open in Design View to restyle
    /// anything; {mod} finds the controls by name and wires their behaviour.{note}
    /// </summary>
    public partial class {cls} : UserControl
    {{
        public {cls}()
        {{
            InitializeComponent();
        }}
    }}
}}
'''
for cls, (mod, title, note) in VIEWS.items():
    p = ROOT + cls + ".cs"
    if os.path.exists(p): continue
    with open(p, "w", encoding="utf-8-sig", newline="\r\n") as f:
        f.write(T.format(cls=cls, mod=mod, title=title, note=("\n    /// " + note) if note else ""))
    print("wrote", p)
