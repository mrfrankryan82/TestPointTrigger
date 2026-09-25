// TestPoint Trigger - Modular Bench Suite
// Developer: HaKDMoDz™ · v3.0.0 · 2026-09-26
using System;
using System.Windows.Forms;
using TestPointTrigger.Modules;

namespace TestPointTrigger
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var host = new ModuleHostForm();

            // Register modules here. Adding a feature to the suite means
            // writing an IModule and adding one line below - nothing in the
            // shell or in any other module needs to change.
            host.Register(new LiveCoachModule());
            host.Register(new PadFinderModule());

            Application.Run(host);
        }
    }
}
