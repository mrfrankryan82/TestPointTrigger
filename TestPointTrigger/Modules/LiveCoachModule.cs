// TestPoint Trigger - Live Coach module
// Developer: HaKDMoDz™ · v1.0.0 · 2026-09-26
using System;
using System.Drawing;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;

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
        public string Version => "1.0.0";
        public int SortOrder => 10;

        private IModuleHost _host;
        private VideoCapture _cam;
        private VisionCoach _vision;
        private BootModeWatcher _usb;
        private PictureBox _preview;
        private ListBox _log;
        private Timer _frameTimer;
        private Bitmap _latest;
        private readonly object _frameLock = new object();
        private ComboBox _kind;
        private ComboBox _app;
        private TextBox _host_ip;

        public Control CreateView(IModuleHost host)
        {
            _host = host;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(8)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));

            _preview = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };

            _kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
            _kind.Items.AddRange(new object[] { "Local camera", "Phone camera" });
            _kind.SelectedIndex = 0;

            _app = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Enabled = false };
            _app.Items.AddRange(new object[] { "IP Webcam", "DroidCam", "RTSP", "Full URL" });
            _app.SelectedIndex = 0;

            _host_ip = new TextBox { Width = 150, Enabled = false, Text = "192.168.1." };
            var lblIp = new Label { Text = "Phone IP:", AutoSize = true, Padding = new Padding(6, 6, 0, 0) };

            _kind.SelectedIndexChanged += (s, e) =>
            {
                var phone = _kind.SelectedIndex == 1;
                _app.Enabled = phone; _host_ip.Enabled = phone;
            };

            var bStart = new Button { Text = "Start watching", AutoSize = true };
            var bStop = new Button { Text = "Stop", AutoSize = true };
            bStart.Click += (s, e) => StartWatching();
            bStop.Click += (s, e) => StopWatching();
            bar.Controls.AddRange(new Control[] { _kind, _app, lblIp, _host_ip, bStart, bStop });

            _log = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };

            right.Controls.Add(bar, 0, 0);
            right.Controls.Add(_log, 0, 1);
            root.Controls.Add(_preview, 0, 0);
            root.Controls.Add(right, 1, 0);

            return root;
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

            _frameTimer = new Timer { Interval = 60 };
            _frameTimer.Tick += (s, e) => GrabFrame();
            _frameTimer.Start();

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
            _frameTimer?.Stop(); _frameTimer?.Dispose(); _frameTimer = null;
            _vision?.Dispose(); _vision = null;
            if (_usb != null) { _usb.ModeChanged -= OnModeChanged; _usb.Dispose(); _usb = null; }
            _cam?.Dispose(); _cam = null;
            lock (_frameLock) { _latest?.Dispose(); _latest = null; }
        }

        private void GrabFrame()
        {
            if (_cam == null) return;
            try
            {
                using (var mat = new Mat())
                {
                    if (!_cam.Read(mat) || mat.Empty()) return;
                    var bmp = BitmapConverter.ToBitmap(mat);
                    lock (_frameLock)
                    {
                        _latest?.Dispose();
                        _latest = (Bitmap)bmp.Clone();
                    }
                    var old = _preview.Image;
                    _preview.Image = bmp;
                    old?.Dispose();
                }
            }
            catch { /* transient capture failures are not fatal */ }
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

        private void Log(string text)
        {
            if (_log == null) return;
            Action add = () =>
            {
                _log.Items.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + text);
                while (_log.Items.Count > 200) _log.Items.RemoveAt(_log.Items.Count - 1);
            };
            if (_log.InvokeRequired) _log.BeginInvoke(add); else add();
        }

        public void Dispose() { StopWatching(); }
    }
}
