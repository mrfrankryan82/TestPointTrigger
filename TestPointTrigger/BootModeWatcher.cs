using System;
using System.Collections.Generic;
using System.Management;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TestPointTrigger
{
    /// <summary>
    /// Polls Win32_PnPEntity for known boot-mode VID/PID signatures.
    /// This is the AUTHORITATIVE confirmation that a device entered
    /// EDL/BROM/Download mode. Vision coaching is advisory only.
    /// </summary>
    public class BootModeWatcher : IDisposable
    {
        public class ModeSignature
        {
            public string Vid;
            public string Pid;
            public string Label;
            public bool IsGoal;   // false = a "close but wrong" state
        }

        private static readonly List<ModeSignature> Signatures = new List<ModeSignature>
        {
            new ModeSignature { Vid="05C6", Pid="9008", Label="Qualcomm EDL 9008",   IsGoal=true  },
            new ModeSignature { Vid="05C6", Pid="900E", Label="Qualcomm Diag 900E",  IsGoal=false },
            new ModeSignature { Vid="0E8D", Pid="0003", Label="MediaTek BROM",       IsGoal=true  },
            new ModeSignature { Vid="0E8D", Pid="2000", Label="MediaTek Preloader",  IsGoal=false },
            new ModeSignature { Vid="1782", Pid="4D00", Label="Unisoc Diag",         IsGoal=true  },
            new ModeSignature { Vid="04E8", Pid="685D", Label="Samsung Download",    IsGoal=true  }
        };

        /// <summary>Fired when a boot-mode signature appears or disappears.</summary>
        public event EventHandler<BootModeEventArgs> ModeChanged;

        private readonly Timer _timer;
        private readonly HashSet<string> _seen = new HashSet<string>();

        public BootModeWatcher(int pollMs = 250)
        {
            _timer = new Timer { Interval = pollMs };
            _timer.Tick += (s, e) => Poll();
        }

        public void Start() { _seen.Clear(); _timer.Start(); }
        public void Stop()  { _timer.Stop(); }

        private void Poll()
        {
            var current = new HashSet<string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, Name, Status FROM Win32_PnPEntity"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        var id = mo["DeviceID"] as string;
                        if (string.IsNullOrEmpty(id)) continue;
                        if (id.IndexOf("USB\\", StringComparison.OrdinalIgnoreCase) < 0) continue;

                        var m = Regex.Match(id, @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})");
                        if (!m.Success) continue;

                        var vid = m.Groups[1].Value.ToUpperInvariant();
                        var pid = m.Groups[2].Value.ToUpperInvariant();
                        var key = vid + ":" + pid;

                        var sig = Signatures.Find(x => x.Vid == vid && x.Pid == pid);
                        if (sig == null) continue;

                        current.Add(key);
                        if (_seen.Add(key))
                        {
                            var status = mo["Status"] as string;
                            var driverOk = string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase);
                            ModeChanged?.Invoke(this, new BootModeEventArgs
                            {
                                Signature = sig,
                                Appeared = true,
                                DriverBound = driverOk
                            });
                        }
                    }
                }
            }
            catch { /* WMI hiccups during hub cycling are expected; ignore */ }

            _seen.RemoveWhere(k =>
            {
                if (current.Contains(k)) return false;
                var parts = k.Split(':');
                var sig = Signatures.Find(x => x.Vid == parts[0] && x.Pid == parts[1]);
                if (sig != null)
                    ModeChanged?.Invoke(this, new BootModeEventArgs { Signature = sig, Appeared = false });
                return true;
            });
        }

        public void Dispose() { _timer?.Dispose(); }
    }

    public class BootModeEventArgs : EventArgs
    {
        public BootModeWatcher.ModeSignature Signature { get; set; }
        public bool Appeared { get; set; }
        public bool DriverBound { get; set; }

        public string Describe()
        {
            if (!Appeared) return Signature.Label + " disconnected";
            if (!Signature.IsGoal)
                return "Dropped to " + Signature.Label + " - unplug, hold the short longer, retry";
            return DriverBound
                ? Signature.Label + " CONFIRMED - driver bound, safe to launch tool"
                : Signature.Label + " detected but DRIVER NOT BOUND - bind before launching tool";
        }
    }
}
