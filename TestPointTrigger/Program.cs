using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace TestPointTrigger
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // app.manifest already declares requireAdministrator, so Windows
            // normally elevates (or blocks) this exe before Main() ever
            // runs. This check is a belt-and-suspenders failsafe for cases
            // where that manifest doesn't apply — e.g. the exe gets copied/
            // renamed in a way that drops the embedded manifest, or it's
            // launched through a host that ignores it.
            if (!IsRunningAsAdministrator())
            {
                if (!RelaunchElevated())
                {
                    MessageBox.Show(
                        "TestPoint Trigger needs to run as Administrator — pnputil (used to " +
                        "disable/enable USB devices) requires it.\n\n" +
                        "Restart the app and accept the UAC prompt to continue.",
                        "Administrator rights required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return; // This (non-elevated) instance always exits here.
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static bool IsRunningAsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// Relaunches this exe with the "runas" verb, which makes Windows
        /// pop the UAC elevation prompt. Returns true once the elevated
        /// copy has been successfully started; false if the user clicked
        /// "No" on the prompt or elevation otherwise failed to launch.
        /// </summary>
        private static bool RelaunchElevated()
        {
            try
            {
                string currentExe = Process.GetCurrentProcess().MainModule?.FileName
                    ?? Application.ExecutablePath;

                var psi = new ProcessStartInfo(currentExe)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppContext.BaseDirectory,
                };

                Process.Start(psi);
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // ERROR_CANCELLED — the user clicked "No" on the UAC prompt.
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
