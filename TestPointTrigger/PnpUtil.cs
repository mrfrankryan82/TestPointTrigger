using System;
using System.Diagnostics;
using System.IO;

namespace TestPointTrigger
{
    /// <summary>
    /// Thin wrapper around the built-in pnputil.exe (Windows 10 1803+) for
    /// disabling/enabling a specific device node by its Instance ID
    /// (the same string WMI reports as Win32_PnPEntity.DeviceID).
    ///
    /// Using pnputil instead of raw SetupAPI/CM_* P/Invoke keeps this file
    /// small and avoids fragile interop signatures; the app already runs
    /// elevated (see app.manifest) so no extra UAC prompt happens per call.
    /// </summary>
    internal static class PnpUtil
    {
        private static readonly string PnpUtilPath =
            Path.Combine(Environment.SystemDirectory, "pnputil.exe");

        public static (bool Success, string Output) Disable(string deviceId) =>
            Run($"/disable-device /deviceid \"{deviceId}\"");

        public static (bool Success, string Output) Enable(string deviceId) =>
            Run($"/enable-device /deviceid \"{deviceId}\"");

        private static (bool, string) Run(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PnpUtilPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc == null)
                    {
                        return (false, "Failed to start pnputil.exe (Process.Start returned null).");
                    }

                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    if (!proc.WaitForExit(15000))
                    {
                        try { proc.Kill(); } catch { /* best effort */ }
                        return (false, "pnputil.exe timed out after 15s.");
                    }

                    bool success = proc.ExitCode == 0;
                    string combined = (stdout + stderr).Trim();
                    if (string.IsNullOrEmpty(combined))
                    {
                        combined = success ? "OK." : $"pnputil exited with code {proc.ExitCode}.";
                    }
                    return (success, combined);
                }
            }
            catch (Exception ex)
            {
                return (false, "pnputil invocation failed: " + ex.Message);
            }
        }
    }
}
