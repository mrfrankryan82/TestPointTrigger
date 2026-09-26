// TestPoint Trigger - console process runner (adb / fastboot / heimdall)
// Developer: HaKDMoDz™ · v1.0.0 · 2026-09-26
using System;
using System.Diagnostics;
using System.Text;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// Runs a console executable (adb/fastboot/heimdall) streaming stdout and
    /// stderr live, and accepting stdin so an interactive shell or a logcat
    /// stream can be driven and stopped. One runner holds at most one live
    /// process; Start() cancels any previous one.
    ///
    /// Output and Exited fire on background threads - callers must marshal to
    /// the UI thread before touching controls.
    /// </summary>
    internal sealed class CliRunner : IDisposable
    {
        private readonly object _gate = new object();
        private Process _proc;

        public event Action<string> Output;   // one line of stdout/stderr
        public event Action<int> Exited;       // process exit code

        public bool IsRunning
        {
            get { lock (_gate) return _proc != null && !SafeHasExited(_proc); }
        }

        public bool Start(string exe, string args, string workDir = null)
        {
            Stop();
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (!string.IsNullOrEmpty(workDir)) psi.WorkingDirectory = workDir;

            try
            {
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (s, e) => { if (e.Data != null) Output?.Invoke(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) Output?.Invoke(e.Data); };
                p.Exited += (s, e) => { int code = SafeExit(p); Exited?.Invoke(code); };
                lock (_gate) _proc = p;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                return true;
            }
            catch (Exception ex)
            {
                Output?.Invoke("[failed to start: " + ex.Message + "]");
                return false;
            }
        }

        /// <summary>Send a line to the live process's stdin (e.g. interactive shell).</summary>
        public void WriteLine(string text)
        {
            try
            {
                lock (_gate)
                    if (_proc != null && !SafeHasExited(_proc)) _proc.StandardInput.WriteLine(text);
            }
            catch { }
        }

        public void Stop()
        {
            Process p;
            lock (_gate) { p = _proc; _proc = null; }
            if (p == null) return;
            try { if (!SafeHasExited(p)) p.Kill(); } catch { }
            try { p.Dispose(); } catch { }
        }

        /// <summary>
        /// Run a short command to completion and return its combined output.
        /// For quick queries (devices, getvar) where streaming isn't needed.
        /// </summary>
        public static string Capture(string exe, string args, int timeoutMs = 10000)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe, Arguments = args, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return "[could not start " + exe + "]";
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return (o + e).Trim() + "\n[timed out]"; }
                    return (o + e).Trim();
                }
            }
            catch (Exception ex) { return "[error: " + ex.Message + "]"; }
        }

        private static bool SafeHasExited(Process p) { try { return p.HasExited; } catch { return true; } }
        private static int SafeExit(Process p) { try { return p.ExitCode; } catch { return -1; } }

        public void Dispose() => Stop();
    }

    /// <summary>Tiny text-input dialog (WinForms has no built-in InputBox).</summary>
    internal static class Prompt
    {
        public static string Text(System.Windows.Forms.IWin32Window owner, string message, string title, string def = "")
        {
            using (var f = new System.Windows.Forms.Form
            {
                Text = title,
                StartPosition = System.Windows.Forms.FormStartPosition.CenterParent,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false,
                ClientSize = new System.Drawing.Size(430, 130),
                Font = new System.Drawing.Font("Segoe UI", 9f)
            })
            {
                var lbl = new System.Windows.Forms.Label { Text = message, AutoSize = true, Location = new System.Drawing.Point(12, 12), MaximumSize = new System.Drawing.Size(406, 0) };
                var tb = new System.Windows.Forms.TextBox { Text = def, Location = new System.Drawing.Point(12, 50), Width = 406 };
                var ok = new System.Windows.Forms.Button { Text = "OK", DialogResult = System.Windows.Forms.DialogResult.OK, Location = new System.Drawing.Point(242, 90), Width = 80 };
                var cancel = new System.Windows.Forms.Button { Text = "Cancel", DialogResult = System.Windows.Forms.DialogResult.Cancel, Location = new System.Drawing.Point(330, 90), Width = 80 };
                f.Controls.AddRange(new System.Windows.Forms.Control[] { lbl, tb, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK ? tb.Text : null;
            }
        }
    }
}
