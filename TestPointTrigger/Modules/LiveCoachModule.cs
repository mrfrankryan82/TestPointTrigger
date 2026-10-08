// TestPoint Trigger - Live Coach module
// Developer: HaKDMoDz™ · v1.2.0 · 2026-09-26
using System;
using System.Drawing;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using TestPointTrigger.Modules.Views;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// Watches the bench through a camera and coaches boot-mode entry.
    /// Vision is advisory; BootModeWatcher (USB VID/PID) is authoritative.
    /// </summary>
    public class LiveCoachModule : IModule
    {
        public string Id => "livecoach";
        public string Title => "Live Coach";
        public string Description => "Camera + USB watcher coaching for EDL/BROM entry";
        public string Version => "1.2.0";
        public int SortOrder => 10;

        private IModuleHost _host;
        private VideoCapture _cam;
        private VisionCoach _vision;
        private BootModeWatcher _usb;
        private PictureBox _preview;
        private ListBox _log;
        private System.Threading.Thread _grabThread;
        private Bitmap _latest;
        private readonly object _frameLock = new object();
        private ComboBox _kind;
        private ComboBox _app;
        private TextBox _host_ip;
        private volatile bool _stopping;
        private readonly System.Collections.Generic.List<string> _pending =
            new System.Collections.Generic.List<string>();

        public Control CreateView(IModuleHost host)
        {
            _host = host;

            // Layout and styling live in LiveCoachView.Designer.cs (open it in Design View).
            var v = new LiveCoachView();
            _preview = v.picPreview;
            _kind = v.cboKind;
            _app = v.cboApp;
            _host_ip = v.txtPhoneIp;
            _log = v.lstLog;

            _kind.SelectedIndex = 0;
            _app.SelectedIndex = 0;
            _kind.SelectedIndexChanged += (s, e) =>
            {
                var phone = _kind.SelectedIndex == 1;
                _app.Enabled = phone; _host_ip.Enabled = phone;
            };

            v.btnStart.Click += (s, e) => StartWatching();
            v.btnStop.Click += (s, e) => { Log("Stop clicked."); StopWatching(); Log("Stopped."); };
            v.btnQuickStart.Click += (s, e) => ShowQuickStart();

            _log.HandleCreated += (s, e) =>
            {
                lock (_pending)
                {
                    // Pending lines are already oldest-first; append in order.
                    foreach (var line in _pending) _log.Items.Add(line);
                    _pending.Clear();
                    while (_log.Items.Count > 200) _log.Items.RemoveAt(0);
                    // Startup buffer begins with the quick-start card: open at its top.
                    _log.TopIndex = 0;
                }
            };

            ShowQuickStart();
            return v;
        }

        /// <summary>
        /// Reminder card written once per session, the first time Live Coach
        /// opens. Buffered until the log exists, so it is there before any
        /// camera is started.
        /// </summary>
        private static readonly string[] QuickStartLines =
            {
                "──────────── LIVE COACH · QUICK START ────────────",
                "NEEDS: Ollama running (tray icon) with the minicpm-v model,",
                "       and a camera pointed straight down at the bench.",
                "",
                "USB WEBCAM",
                "  1. Plug it in, choose \"Local camera\", press Start watching.",
                "",
                "PHONE AS CAMERA  (phone + PC on the SAME Wi-Fi)",
                "  1. Install IP Webcam (Android, port 8080) or DroidCam (port 4747).",
                "  2. Open the app, start its server, note the IP it shows",
                "     e.g. 192.168.1.23",
                "  3. Choose \"Phone camera\", pick the app, type just the IP",
                "     (no http://, no port — added for you).",
                "  4. RTSP apps: enter rtsp://ip:port/path.",
                "     \"Full URL\": paste the complete stream address.",
                "  5. Press Start watching. \"Connecting…\" then a picture = working.",
                "",
                "READING THE LOG",
                "  eyes: = vision model's guess every ~1.5 s. Advisory only.",
                "  usb : = real USB detection of EDL/BROM. This is the one to trust.",
                "",
                "WITH USB HUB TRIGGER",
                "  Arm the hub on that page, come back here — its hotkeys, voice",
                "  and countdown keep working while you watch for the device.",
                "",
                "IF IT FAILS",
                "  \"vision unavailable\" → start Ollama; `ollama list` should show minicpm-v.",
                "  Phone won't connect  → same Wi-Fi? app server on? open the URL in a browser.",
                "  Leaving this page stops the camera; press Start again when you return.",
                "──────────────────────────────────────────────────"
            };

        private void ShowQuickStart()
        {
            var lines = QuickStartLines;
            // Show the card from its first line, not the auto-scrolled bottom.
            int start = _log != null && _log.IsHandleCreated ? _log.Items.Count : 0;
            foreach (var l in lines) Log(l, stamp: false);
            if (_log != null && _log.IsHandleCreated && !_log.InvokeRequired)
                _log.TopIndex = Math.Min(start, Math.Max(0, _log.Items.Count - 1));
        }

        /// <summary>
        /// Removes every copy of the quick-start card once a stream is up,
        /// leaving real log lines (earlier usb/eyes events) untouched. Card
        /// lines are unstamped, so they never collide with timestamped ones.
        /// </summary>
        private void ClearQuickStart()
        {
            var card = new System.Collections.Generic.HashSet<string>(QuickStartLines);
            lock (_pending) _pending.RemoveAll(card.Contains);
            if (_log == null || _log.IsDisposed || !_log.IsHandleCreated) return;

            _log.BeginUpdate();
            try
            {
                for (int i = _log.Items.Count - 1; i >= 0; i--)
                    if (_log.Items[i] is string s && card.Contains(s)) _log.Items.RemoveAt(i);
            }
            finally { _log.EndUpdate(); }
        }

        public void Activate()
        {
            _host?.SetStatus("Live Coach ready. Vision is advisory; USB detection confirms entry.");
        }

        public void Deactivate()
        {
            StopWatching();
        }

        private void StartWatching()
        {
            if (_cam != null) return;
            _stopping = false;

            var src = new CameraSource();
            if (_kind != null && _kind.SelectedIndex == 1)
            {
                src.Kind = CameraSource.SourceKind.NetworkStream;
                src.Url = CameraSource.BuildPhoneUrl(_host_ip.Text, _app.SelectedItem as string);
                if (string.IsNullOrWhiteSpace(src.Url)) { Log("Enter the phone's IP address first."); return; }
                Log("Connecting to " + src.Url + " ...");
            }

            _cam = src.Open();
            if (_cam == null)
            {
                Log(src.Kind == CameraSource.SourceKind.LocalDevice
                    ? "Local camera could not be opened."
                    : "Could not reach the phone stream. Check the app is running and the IP is right.");
                return;
            }

            // Connected: the setup card has done its job. Clear it so the
            // log is just this session's coaching and USB events.
            ClearQuickStart();
            Log("Connected: " + src + ". (Quick start button brings the setup notes back.)");

            // Capture runs on its own thread. Read() can block longer than the
            // frame interval, and on the UI thread that starves painting and
            // mouse input, so the whole window goes unresponsive.
            _grabThread = new System.Threading.Thread(GrabLoop)
            {
                IsBackground = true,
                Name = "LiveCoach capture"
            };
            _grabThread.Start();

            _vision = new VisionCoach();
            _vision.Observation += (s, text) => Log("eyes: " + text);
            _vision.Start(SnapshotForVision, 1500);

            _usb = new BootModeWatcher();
            _usb.ModeChanged += OnModeChanged;
            _usb.Start();

            Log("Watching. Short the test point, then trigger the hub.");
        }

        private void StopWatching()
        {
            // Mark stopped FIRST so any in-flight GrabFrame bails out rather
            // than blocking on a dead network read and freezing the UI.
            _stopping = true;
            _grabThread = null;  // background, IsBackground=true; exits on _stopping
            _vision?.Dispose(); _vision = null;
            if (_usb != null) { _usb.ModeChanged -= OnModeChanged; _usb.Dispose(); _usb = null; }

            // Disposing the capture can block for seconds while an in-flight
            // read drains, which freezes the UI and makes Stop look dead.
            // Hand ownership to a background thread and return immediately.
            var cam = _cam; _cam = null;
            if (cam != null)
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { cam.Dispose(); } catch { }
                });

            lock (_frameLock) { _latest?.Dispose(); _latest = null; }
        }

        /// <summary>
        /// Background capture loop. Owns the blocking Read() call so the UI
        /// thread stays free, and hands finished bitmaps to the preview via
        /// BeginInvoke.
        /// </summary>
        private void GrabLoop()
        {
            while (!_stopping)
            {
                var cam = _cam;
                if (cam == null) break;

                try
                {
                    using (var mat = new Mat())
                    {
                        if (!cam.Read(mat) || mat.Empty())
                        {
                            System.Threading.Thread.Sleep(30);
                            continue;
                        }

                        var bmp = BitmapConverter.ToBitmap(mat);

                        lock (_frameLock)
                        {
                            _latest?.Dispose();
                            _latest = (Bitmap)bmp.Clone();
                        }

                        ShowFrame(bmp);
                    }
                }
                catch
                {
                    // Transient capture failures are not fatal; back off briefly.
                    System.Threading.Thread.Sleep(100);
                }

                System.Threading.Thread.Sleep(30);
            }
        }

        /// <summary>Marshals a finished frame onto the UI thread for display.</summary>
        private void ShowFrame(Bitmap bmp)
        {
            var pb = _preview;
            if (pb == null || pb.IsDisposed || !pb.IsHandleCreated) { bmp.Dispose(); return; }

            try
            {
                pb.BeginInvoke((Action)(() =>
                {
                    if (pb.IsDisposed) { bmp.Dispose(); return; }
                    var old = pb.Image;
                    pb.Image = bmp;
                    old?.Dispose();
                }));
            }
            catch (ObjectDisposedException) { bmp.Dispose(); }
            catch (InvalidOperationException) { bmp.Dispose(); }
        }

        /// <summary>Hands the vision thread a private copy of the newest frame.</summary>
        private Bitmap SnapshotForVision()
        {
            lock (_frameLock)
            {
                return _latest == null ? null : (Bitmap)_latest.Clone();
            }
        }

        private void OnModeChanged(object sender, BootModeEventArgs e)
        {
            var line = e.Describe();
            if (_log != null && _log.IsHandleCreated)
                _log.BeginInvoke((Action)(() => Log("usb : " + line)));

            if (!e.Appeared) return;
            var sev = !e.Signature.IsGoal ? ModuleSeverity.Warning
                    : e.DriverBound ? ModuleSeverity.Success
                    : ModuleSeverity.Warning;
            _host?.Notify(line, sev);
        }

        private void Log(string text, bool stamp = true)
        {
            // Mirror real events to the single app-wide log file (skip the
            // decorative, unstamped quick-start card).
            if (stamp)
                AppLog.Append("LiveCoach",
                    text.StartsWith("eyes:") ? "EYES" : text.StartsWith("usb :") ? "USB" : "INFO", text);

            if (_log == null || _log.IsDisposed) return;
            var line = stamp ? DateTime.Now.ToString("HH:mm:ss") + "  " + text : text;

            // The handle does not exist until the view is actually shown.
            // Queue anything logged before that and flush it on HandleCreated,
            // otherwise startup messages disappear without trace.
            if (!_log.IsHandleCreated)
            {
                lock (_pending) _pending.Add(line);
                return;
            }

            Action add = () =>
            {
                if (_log.IsDisposed) return;
                _log.Items.Add(line);
                while (_log.Items.Count > 200) _log.Items.RemoveAt(0);
                // Keep the newest line in view.
                _log.TopIndex = Math.Max(0, _log.Items.Count - 1);
            };
            try
            {
                if (_log.InvokeRequired) _log.BeginInvoke(add); else add();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        public void Dispose() { StopWatching(); }
    }
}
