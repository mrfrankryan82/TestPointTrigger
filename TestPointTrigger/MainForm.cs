using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TestPointTrigger
{
    public partial class MainForm : Form
    {
        internal const string RepoUrl = "https://github.com/mrfrankryan82/TestPointTrigger";

        // Played once the hub is successfully re-enabled (the "go" trigger),
        // so you get an audible confirmation without having to look at the
        // screen while your hands are on the test point.
        private const string EnabledSoundPath = @"C:\Users\User\Downloads\hardware_inserted\hardware_inserted.wav";
        private SoundPlayer _enabledSound;

        // ---- Global hotkeys (work even when this window isn't focused,
        // e.g. while both hands are busy holding tweezers + a battery clip) ----
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID_ENABLE = 0xB001;
        private const int HOTKEY_ID_DISABLE = 0xB002;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const uint VK_E = 0x45;
        private const uint VK_D = 0x44;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly Timer _countdownTimer = new Timer { Interval = 1000 };
        private int _secondsRemaining;
        private string _armedDeviceId;
        private string _armedDeviceName;

        // Devices this app has disabled and not yet re-enabled. Tracked so
        // "Re-enable All" and the close-time safety check never leave a
        // hub/controller stuck off.
        private readonly HashSet<string> _disabledBySession = new HashSet<string>();

        // Status icons (enabled = green, disabled = red) — used for the
        // window/taskbar icon, the tray "status notifier" icon, and the
        // toggle button image. Loaded from Assets\ next to the exe.
        private static readonly string AssetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
        private Icon _iconEnabled;
        private Icon _iconDisabled;
        private Image _imgEnabled;
        private Image _imgDisabled;

        public MainForm()
        {
            InitializeComponent();
            _countdownTimer.Tick += CountdownTimer_Tick;
            LoadStatusIcons();
            footerLabel.Text = AppInfo.FooterText;
        }

        private void LoadStatusIcons()
        {
            try
            {
                _iconEnabled = new Icon(Path.Combine(AssetsDir, "status-enabled.ico"));
                _iconDisabled = new Icon(Path.Combine(AssetsDir, "status-disabled.ico"));
                _imgEnabled = Image.FromFile(Path.Combine(AssetsDir, "status-enabled.png"));
                _imgDisabled = Image.FromFile(Path.Combine(AssetsDir, "status-disabled.png"));
            }
            catch (Exception ex)
            {
                // Missing/corrupt Assets folder shouldn't stop the app from
                // running — just fall back to no custom icon.
                Log("Could not load status icons from Assets\\: " + ex.Message);
            }
        }

        private void PlayEnabledSound()
        {
            try
            {
                _enabledSound ??= new SoundPlayer(EnabledSoundPath);
                _enabledSound.Play(); // async — doesn't block the UI thread
            }
            catch (Exception ex)
            {
                // A missing/locked wav file shouldn't stop the trigger from
                // having already happened — just log it and move on.
                Log($"Could not play enabled sound ({EnabledSoundPath}): {ex.Message}");
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            bool enableOk = RegisterHotKey(Handle, HOTKEY_ID_ENABLE, MOD_CONTROL | MOD_ALT, VK_E);
            bool disableOk = RegisterHotKey(Handle, HOTKEY_ID_DISABLE, MOD_CONTROL | MOD_ALT, VK_D);

            if (!enableOk)
            {
                Log("Warning: could not register global hotkey Ctrl+Alt+E (another app may already be using it). " +
                    "The on-screen 'Enable Now' button still works.");
            }
            if (!disableOk)
            {
                Log("Warning: could not register global hotkey Ctrl+Alt+D (another app may already be using it). " +
                    "The on-screen 'Disable & Arm' button still works.");
            }
            if (enableOk && disableOk)
            {
                Log("Global hotkeys registered — Ctrl+Alt+D disables/arms the selected device, Ctrl+Alt+E re-enables it.");
            }

            UpdateStatusUI();
            RefreshDevices();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnregisterHotKey(Handle, HOTKEY_ID_ENABLE);
            UnregisterHotKey(Handle, HOTKEY_ID_DISABLE);

            if (_disabledBySession.Count > 0)
            {
                var result = MessageBox.Show(
                    $"{_disabledBySession.Count} USB device(s) are still disabled by this app.\n\n" +
                    "Re-enable them before exiting?",
                    "TestPoint Trigger",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    ReenableAll();
                }
            }

            trayIcon.Visible = false;
            _enabledSound?.Dispose();
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_ID_ENABLE)
                {
                    TriggerNow();
                }
                else if (id == HOTKEY_ID_DISABLE)
                {
                    ArmFromHotkey();
                }
            }
            base.WndProc(ref m);
        }

        // ---- Status icon / toggle button / tray sync ----

        private bool IsArmed => _armedDeviceId != null;

        private void UpdateStatusUI()
        {
            if (IsArmed)
            {
                toggleButton.Text = "Disabled — click to Enable Now";
                toggleButton.Image = _imgDisabled;
                if (_iconDisabled != null) Icon = _iconDisabled;
                trayIcon.Icon = _iconDisabled ?? Icon;
                trayIcon.Text = Truncate($"TestPoint Trigger — disabled ({_armedDeviceName})", 63);
                trayToggleItem.Text = "Enable Now";
            }
            else
            {
                toggleButton.Text = "Enabled — click to Disable && Arm";
                toggleButton.Image = _imgEnabled;
                if (_iconEnabled != null) Icon = _iconEnabled;
                trayIcon.Icon = _iconEnabled ?? Icon;
                trayIcon.Text = "TestPoint Trigger — enabled";
                trayToggleItem.Text = "Disable && Arm";
            }
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        // ---- Tray icon actions ----

        private void TrayShowItem_Click(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void TrayExitItem_Click(object sender, EventArgs e) => Close();

        // ---- Help ----

        private void HelpButton_Click(object sender, EventArgs e)
        {
            using var help = new HelpForm(RepoUrl);
            help.ShowDialog(this);
        }

        // ---- Device enumeration ----

        private void RefreshDevices()
        {
            devicesCombo.Items.Clear();

            try
            {
                // PNPClass == "USB" covers both USB-enumerated hubs/root hubs
                // AND PCI-enumerated host controllers - i.e. exactly the set
                // shown under Device Manager's "Universal Serial Bus
                // controllers" node.
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, DeviceID, PNPClass, Status FROM Win32_PnPEntity WHERE PNPClass = 'USB'");

                foreach (ManagementObject mo in searcher.Get())
                {
                    string name = mo["Name"]?.ToString() ?? "(unnamed device)";
                    string id = mo["DeviceID"]?.ToString();
                    string status = mo["Status"]?.ToString() ?? "";

                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    devicesCombo.Items.Add(new UsbDeviceInfo { Name = name, DeviceId = id, Status = status });
                }

                Log($"Found {devicesCombo.Items.Count} USB hub/controller device(s).");
            }
            catch (Exception ex)
            {
                Log("Error enumerating devices (run as Administrator?): " + ex.Message);
            }

            if (devicesCombo.Items.Count > 0)
            {
                devicesCombo.SelectedIndex = 0;
            }
        }

        private void RefreshButton_Click(object sender, EventArgs e) => RefreshDevices();

        // ---- Toggle (Arm / Trigger in one button) ----

        private void ToggleButton_Click(object sender, EventArgs e)
        {
            if (IsArmed)
            {
                TriggerNow();
                return;
            }

            if (devicesCombo.SelectedItem is not UsbDeviceInfo dev)
            {
                MessageBox.Show("Select a USB hub/controller first.", "TestPoint Trigger");
                return;
            }

            var confirm = MessageBox.Show(
                "This will disable:\n\n" +
                $"{dev.Name}\n{dev.DeviceId}\n\n" +
                "ALL devices on this hub/controller (keyboard, mouse, webcam, etc. if they share it) " +
                "will stop responding until it is re-enabled.\n\n" +
                "Strongly recommended: point this at a dedicated/spare USB hub with only the target " +
                "phone cable plugged into it, not the port sharing your keyboard/mouse.\n\nContinue?",
                "Confirm disable",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            ArmDevice(dev);
        }

        // Ctrl+Alt+D — same as the toggle button's "arm" path, but skips the
        // confirmation dialog: a hotkey press is already a deliberate
        // action, and a modal dialog would just block while your hands are full.
        private void ArmFromHotkey()
        {
            if (IsArmed)
            {
                Log("Already armed — trigger (Ctrl+Alt+E) or re-enable it first.");
                return;
            }

            if (devicesCombo.SelectedItem is not UsbDeviceInfo dev)
            {
                Log("Ctrl+Alt+D pressed, but no USB hub/controller is selected.");
                return;
            }

            ArmDevice(dev);
        }

        private void ArmDevice(UsbDeviceInfo dev)
        {
            var (ok, output) = PnpUtil.Disable(dev.DeviceId);
            Log(output);

            if (!ok)
            {
                MessageBox.Show("Failed to disable the device. See the log for details.", "TestPoint Trigger",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _disabledBySession.Add(dev.DeviceId);
            _armedDeviceId = dev.DeviceId;
            _armedDeviceName = dev.Name;
            _secondsRemaining = (int)secondsUpDown.Value;
            UpdateCountdownLabel();
            _countdownTimer.Start();

            devicesCombo.Enabled = false;
            UpdateStatusUI();

            Log($"ARMED: '{dev.Name}' disabled. Position tweezers on the test point, hold the battery " +
                "clip connected, and get the USB cable seated. Press Ctrl+Alt+E (or click the toggle) " +
                "the instant contact is solid — or let the countdown finish on its own.");
        }

        // ---- Countdown / trigger ----

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            _secondsRemaining--;
            UpdateCountdownLabel();

            if (_secondsRemaining <= 0)
            {
                TriggerNow();
            }
        }

        private void UpdateCountdownLabel()
        {
            countdownLabel.Text = _secondsRemaining > 0 ? $"{_secondsRemaining}s" : "GO";
        }

        private void TriggerNow()
        {
            if (_armedDeviceId == null)
            {
                return;
            }

            _countdownTimer.Stop();

            var (ok, output) = PnpUtil.Enable(_armedDeviceId);
            Log(output);

            if (ok)
            {
                _disabledBySession.Remove(_armedDeviceId);
                Log($"TRIGGERED: '{_armedDeviceName}' re-enabled — Windows should now enumerate it fresh, " +
                    "same as a brand-new plug-in. Check Device Manager / QFIL / your flash tool now.");
                PlayEnabledSound();
            }
            else
            {
                Log("Failed to re-enable the device! Use 'Re-enable All' below or Device Manager immediately.");
            }

            countdownLabel.Text = "--";
            devicesCombo.Enabled = true;
            _armedDeviceId = null;
            UpdateStatusUI();
        }

        // ---- Panic / cleanup ----

        private void PanicButton_Click(object sender, EventArgs e) => ReenableAll();

        private void ReenableAll()
        {
            foreach (var id in _disabledBySession.ToList())
            {
                var (ok, output) = PnpUtil.Enable(id);
                Log(output);
                if (ok)
                {
                    _disabledBySession.Remove(id);
                }
            }

            _countdownTimer.Stop();
            devicesCombo.Enabled = true;
            countdownLabel.Text = "--";
            _armedDeviceId = null;
            UpdateStatusUI();

            Log("Re-enable All complete.");
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message.Trim()}{Environment.NewLine}");
        }
    }
}
