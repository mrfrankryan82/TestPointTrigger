// TestPoint Trigger - USB Hub Trigger module
// Developer: HaKDMoDz™ · v1.0.0 · 2026-09-26
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Media;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// The original v1 TestPoint Trigger as a module: disable a USB hub or
    /// controller, hold the test point, then re-enable it on a global hotkey
    /// (or a fallback countdown) so the phone enumerates fresh in EDL/BROM.
    ///
    /// Deliberate exception to the IModule "stop on Deactivate" rule: the
    /// hotkeys and countdown stay live while another module is showing, so
    /// you can arm here and watch Live Coach for the boot-mode device.
    /// Everything is torn down (and disabled hubs offered back) in Dispose.
    /// </summary>
    public class UsbTriggerModule : IModule
    {
        public string Id => "usbtrigger";
        public string Title => "USB Hub Trigger";
        public string Description => "Disable a USB hub, then re-enable it on a hotkey or countdown to time test-point entry";
        public string Version => "1.0.0";
        public int SortOrder => 5;

        private const string EnabledSoundPath =
            @"C:\Users\User\Downloads\hardware_inserted\hardware_inserted.wav";

        private const int HK_ENABLE = 0xB001;
        private const int HK_DISABLE = 0xB002;
        private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002;
        private const uint VK_D = 0x44, VK_E = 0x45;

        private IModuleHost _host;
        private HotkeyWindow _hotkeys;
        private readonly Timer _countdown = new Timer { Interval = 1000 };
        private readonly HashSet<string> _disabledBySession =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _armedId, _armedName;
        private int _secondsLeft;
        private bool _busy, _enumerated, _disposed;
        private readonly bool _elevated = IsElevated();
        private SoundPlayer _sound;
        private Image _imgEnabled, _imgDisabled;

        private ComboBox _devices;
        private Button _refresh, _toggle, _reenableAll;
        private NumericUpDown _seconds;
        private Label _timeLeft;
        private TextBox _log;

        private VoiceTrigger _voice;
        private CheckBox _voiceOn;
        private TextBox _phrases;
        private NumericUpDown _confidence;
        private Control _ui;   // any live control, used to marshal recogniser events

        private bool IsArmed => _armedId != null;

        // ───────────────────────────── View ─────────────────────────────

        public Control CreateView(IModuleHost host)
        {
            _host = host;
            LoadImages();

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(12) };
            for (int i = 0; i < 7; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // Elevation banner - pnputil cannot touch device nodes without admin.
            var banner = Flow();
            banner.BackColor = Color.FromArgb(255, 244, 206);
            banner.Padding = new Padding(6);
            banner.Visible = !_elevated;
            var bRestart = new Button { Text = "Restart as Administrator", AutoSize = true };
            bRestart.Click += (s, e) => RelaunchElevated();
            banner.Controls.Add(new Label
            {
                Text = "Not running as Administrator — disabling/enabling hubs needs admin rights.",
                AutoSize = true, Margin = new Padding(0, 8, 8, 0)
            });
            banner.Controls.Add(bRestart);

            var lblDev = new Label { Text = "USB hub / controller to arm:", AutoSize = true, Margin = new Padding(0, 8, 0, 2) };

            var devRow = Flow();
            _devices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 520 };
            _refresh = new Button { Text = "Refresh", AutoSize = true };
            _refresh.Click += (s, e) => RefreshDevices();
            devRow.Controls.AddRange(new Control[] { _devices, _refresh });

            var cdRow = Flow();
            _seconds = new NumericUpDown { Minimum = 3, Maximum = 120, Value = 10, Width = 60 };
            _timeLeft = new Label
            {
                Text = "--", AutoSize = true, MinimumSize = new Size(90, 0),
                Font = new Font("Segoe UI", 20f, FontStyle.Bold), Margin = new Padding(8, 0, 0, 0)
            };
            cdRow.Controls.AddRange(new Control[]
            {
                new Label { Text = "Countdown (seconds, fallback if you don't hit the hotkey):", AutoSize = true, Margin = new Padding(0, 6, 4, 0) },
                _seconds,
                new Label { Text = "Time left:", AutoSize = true, Margin = new Padding(24, 6, 0, 0) },
                _timeLeft
            });

            var actRow = Flow();
            _toggle = new Button
            {
                Size = new Size(380, 52), FlatStyle = FlatStyle.Flat,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            _toggle.Click += async (s, e) => await OnToggleAsync();
            _reenableAll = new Button { Text = "Re-enable All", Size = new Size(180, 52) };
            _reenableAll.Click += async (s, e) => await ReenableAllAsync();
            actRow.Controls.AddRange(new Control[] { _toggle, _reenableAll });

            // Voice trigger: offline, only acts while armed.
            var voiceRow = Flow();
            _voiceOn = new CheckBox { Text = "Voice trigger", AutoSize = true, Margin = new Padding(0, 6, 12, 0) };
            _phrases = new TextBox { Text = "go, trigger, enable now", Width = 220 };
            _confidence = new NumericUpDown { Minimum = 40, Maximum = 99, Value = 70, Width = 55 };
            _voiceOn.CheckedChanged += (s, e) => ToggleVoice();
            _phrases.Leave += (s, e) => { if (_voiceOn.Checked) ToggleVoice(); };      // re-load new phrases
            _confidence.ValueChanged += (s, e) => { if (_voice != null) _voice.MinConfidence = (float)_confidence.Value / 100f; };
            voiceRow.Controls.AddRange(new Control[]
            {
                _voiceOn,
                new Label { Text = "Say any of:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) },
                _phrases,
                new Label { Text = "Min confidence %:", AutoSize = true, Margin = new Padding(12, 6, 4, 0) },
                _confidence
            });

            var hint = new Label
            {
                Text = "Global hotkeys (work from any module, even unfocused): Ctrl+Alt+D disable/arm, Ctrl+Alt+E enable now.\r\n" +
                       "If your keyboard or a USB microphone shares the hub you disable, that trigger dies with it — the countdown is your fallback.",
                AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 0, 6)
            };

            _log = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10f),
                BackColor = SystemColors.Window
            };

            root.Controls.Add(banner, 0, 0);
            root.Controls.Add(lblDev, 0, 1);
            root.Controls.Add(devRow, 0, 2);
            root.Controls.Add(cdRow, 0, 3);
            root.Controls.Add(actRow, 0, 4);
            root.Controls.Add(voiceRow, 0, 5);
            root.Controls.Add(hint, 0, 6);
            root.Controls.Add(_log, 0, 7);
            _ui = root;

            _countdown.Tick += async (s, e) =>
            {
                _secondsLeft--;
                _timeLeft.Text = _secondsLeft > 0 ? _secondsLeft + "s" : "GO";
                if (_secondsLeft <= 0) await TriggerAsync();
            };

            RegisterHotkeys();
            if (!_elevated) Log("Not elevated: use 'Restart as Administrator' before arming.");
            UpdateUi();
            return root;
        }

        private static FlowLayoutPanel Flow() => new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true, Margin = new Padding(0, 2, 0, 2)
        };

        public void Activate()
        {
            _host?.SetStatus(IsArmed
                ? "ARMED: " + _armedName + " is disabled. Ctrl+Alt+E to trigger."
                : "USB Hub Trigger ready. Use a dedicated hub — not the one your keyboard/mouse is on.");
            if (!_enumerated) { _enumerated = true; RefreshDevices(); }
        }

        // Intentionally empty: stays armed across modules (see class summary).
        public void Deactivate() { }

        // ─────────────────────────── Devices ───────────────────────────

        private async void RefreshDevices()
        {
            _refresh.Enabled = false;
            try
            {
                var found = await Task.Run(() =>
                {
                    var list = new List<UsbDeviceInfo>();
                    // PNPClass 'USB' = everything under Device Manager's
                    // "Universal Serial Bus controllers": hubs, root hubs, host controllers.
                    using (var q = new ManagementObjectSearcher(
                        "SELECT Name, DeviceID, Status FROM Win32_PnPEntity WHERE PNPClass = 'USB'"))
                    {
                        foreach (ManagementObject mo in q.Get())
                            using (mo)
                            {
                                var id = mo["DeviceID"]?.ToString();
                                if (string.IsNullOrEmpty(id)) continue;
                                list.Add(new UsbDeviceInfo
                                {
                                    Name = mo["Name"]?.ToString() ?? "(unnamed device)",
                                    DeviceId = id,
                                    Status = mo["Status"]?.ToString() ?? ""
                                });
                            }
                    }
                    return list.OrderBy(d => d.Name).ToList();
                });

                if (_disposed) return;
                var keep = (_devices.SelectedItem as UsbDeviceInfo)?.DeviceId;
                _devices.Items.Clear();
                foreach (var d in found) _devices.Items.Add(d);
                var idx = found.FindIndex(d => string.Equals(d.DeviceId, keep, StringComparison.OrdinalIgnoreCase));
                if (_devices.Items.Count > 0) _devices.SelectedIndex = idx >= 0 ? idx : 0;
                Log($"Found {found.Count} USB hub/controller device(s).");
            }
            catch (Exception ex)
            {
                Log("Error enumerating devices: " + ex.Message);
            }
            finally
            {
                if (!_disposed) _refresh.Enabled = true;
            }
        }

        // ─────────────────────────── Arm / trigger ───────────────────────────

        private async Task OnToggleAsync()
        {
            if (IsArmed) { await TriggerAsync(); return; }

            if (!(_devices.SelectedItem is UsbDeviceInfo dev))
            {
                MessageBox.Show(_toggle.FindForm(), "Select a USB hub/controller first.", "USB Hub Trigger");
                return;
            }

            var ok = MessageBox.Show(_toggle.FindForm(),
                "This will disable:\n\n" + dev.Name + "\n" + dev.DeviceId + "\n\n" +
                "EVERYTHING on this hub/controller (keyboard, mouse, webcam if they share it) will stop " +
                "until it is re-enabled.\n\nUse a dedicated hub with only the phone cable on it.\n\nContinue?",
                "Confirm disable", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (ok == DialogResult.Yes) await ArmAsync(dev);
        }

        private async Task ArmAsync(UsbDeviceInfo dev)
        {
            if (_busy || IsArmed) return;
            _busy = true; UpdateUi();
            try
            {
                Log("Disabling '" + dev.Name + "' ...");
                var (ok, output) = await Task.Run(() => PnpUtil.Disable(dev.DeviceId));
                Log(output);
                if (!ok)
                {
                    Log("Disable FAILED — see output above.");
                    _host?.Notify("USB hub disable failed — see the USB Hub Trigger log.", ModuleSeverity.Error);
                    return;
                }

                _disabledBySession.Add(dev.DeviceId);
                _armedId = dev.DeviceId;
                _armedName = dev.Name;
                _secondsLeft = (int)_seconds.Value;
                _timeLeft.Text = _secondsLeft + "s";
                _countdown.Start();

                Log("ARMED: '" + dev.Name + "' disabled. Tweezers on the test point, battery connected, cable seated — " +
                    "press Ctrl+Alt+E the instant contact is solid, or let the countdown finish.");
                _host?.Notify("ARMED: " + dev.Name + " disabled. Ctrl+Alt+E to trigger.", ModuleSeverity.Warning);
            }
            finally { _busy = false; UpdateUi(); }
        }

        private async Task TriggerAsync()
        {
            if (!IsArmed || _busy) return;
            _busy = true;
            _countdown.Stop();
            var id = _armedId; var name = _armedName;
            _timeLeft.Text = "GO";
            UpdateUi();
            try
            {
                var (ok, output) = await Task.Run(() => PnpUtil.Enable(id));
                Log(output);
                if (ok)
                {
                    _disabledBySession.Remove(id);
                    Log("TRIGGERED: '" + name + "' re-enabled — the phone should enumerate fresh now. Check your flash tool.");
                    PlaySound();
                    _host?.Notify("TRIGGERED: " + name + " re-enabled.", ModuleSeverity.Info);
                }
                else
                {
                    Log("Re-enable FAILED! Use 'Re-enable All' or Device Manager now.");
                    _host?.Notify("Hub re-enable failed — use Re-enable All.", ModuleSeverity.Error);
                }
            }
            finally
            {
                _armedId = null; _armedName = null;
                _timeLeft.Text = "--";
                _busy = false; UpdateUi();
            }
        }

        private async Task ReenableAllAsync()
        {
            if (_busy) { Log("Busy — try again in a moment."); return; }
            _busy = true;
            _countdown.Stop();
            UpdateUi();
            try
            {
                var ids = _disabledBySession.ToList();

                // Recovery after a crash: nothing tracked this session, but the
                // selected device is visibly not OK - offer to enable that one.
                if (ids.Count == 0 && _devices.SelectedItem is UsbDeviceInfo sel &&
                    !string.Equals(sel.Status, "OK", StringComparison.OrdinalIgnoreCase))
                    ids.Add(sel.DeviceId);

                if (ids.Count == 0) { Log("Nothing to re-enable."); return; }

                foreach (var id in ids)
                {
                    var (ok, output) = await Task.Run(() => PnpUtil.Enable(id));
                    Log(output);
                    if (ok) _disabledBySession.Remove(id);
                }
                Log("Re-enable All complete.");
            }
            finally
            {
                _armedId = null; _armedName = null;
                _timeLeft.Text = "--";
                _busy = false; UpdateUi();
                RefreshDevices();
            }
        }

        // ─────────────────────────── Hotkeys ───────────────────────────

        private void RegisterHotkeys()
        {
            _hotkeys = new HotkeyWindow();
            _hotkeys.Pressed += OnHotkey;
            bool e = _hotkeys.Register(HK_ENABLE, MOD_CONTROL | MOD_ALT, VK_E);
            bool d = _hotkeys.Register(HK_DISABLE, MOD_CONTROL | MOD_ALT, VK_D);
            if (!e) Log("Warning: Ctrl+Alt+E is taken by another app — use the on-screen button.");
            if (!d) Log("Warning: Ctrl+Alt+D is taken by another app — use the on-screen button.");
            if (e && d) Log("Global hotkeys registered — Ctrl+Alt+D disables/arms, Ctrl+Alt+E re-enables.");
        }

        private async void OnHotkey(int id)
        {
            Log(id == HK_ENABLE ? "Hotkey received: Ctrl+Alt+E" : "Hotkey received: Ctrl+Alt+D");
            if (id == HK_ENABLE)
            {
                if (!IsArmed) { Log("Ctrl+Alt+E pressed, but nothing is armed."); return; }
                await TriggerAsync();
            }
            else if (id == HK_DISABLE)
            {
                if (IsArmed) { Log("Already armed — Ctrl+Alt+E to trigger first."); return; }
                if (!_elevated) { Log("Ctrl+Alt+D ignored: not running as Administrator."); return; }
                if (!(_devices?.SelectedItem is UsbDeviceInfo dev)) { Log("Ctrl+Alt+D pressed, but no hub is selected."); return; }
                // No confirm dialog: the hotkey is already deliberate and your hands are full.
                await ArmAsync(dev);
            }
        }

        // ─────────────────────────── Voice ───────────────────────────

        private void ToggleVoice()
        {
            _voice?.Dispose();
            _voice = null;
            if (!_voiceOn.Checked) { Log("Voice trigger off."); return; }

            _voice = new VoiceTrigger { MinConfidence = (float)_confidence.Value / 100f };
            _voice.Info += text => OnUi(() => Log(text));
            _voice.Heard += (phrase, conf) => OnUi(async () => await OnVoiceHeardAsync(phrase, conf));

            if (!_voice.Start(_phrases.Text.Split(',', ';')))
            {
                _voice.Dispose(); _voice = null;
                _voiceOn.Checked = false;   // re-enters ToggleVoice once, logs "off"
            }
        }

        private async System.Threading.Tasks.Task OnVoiceHeardAsync(string phrase, float confidence)
        {
            if (!IsArmed)
            {
                Log($"Voice: heard \"{phrase}\" ({confidence:P0}) but nothing is armed — ignored.");
                return;
            }
            Log($"Voice: \"{phrase}\" ({confidence:P0}) — triggering.");
            await TriggerAsync();
        }

        /// <summary>Recogniser events arrive on a worker thread; hop to the UI thread.</summary>
        private void OnUi(Action a)
        {
            var c = _ui;
            if (c == null || c.IsDisposed || !c.IsHandleCreated) return;
            try { c.BeginInvoke(a); } catch (ObjectDisposedException) { } catch (InvalidOperationException) { }
        }

        /// <summary>Message-only window that receives WM_HOTKEY for the whole app.</summary>
        private sealed class HotkeyWindow : NativeWindow, IDisposable
        {
            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

            private const int WM_HOTKEY = 0x0312;
            private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
            private readonly List<int> _ids = new List<int>();

            public event Action<int> Pressed;

            public HotkeyWindow() { CreateHandle(new CreateParams { Parent = HWND_MESSAGE }); }

            public bool Register(int id, uint mods, uint vk)
            {
                if (!RegisterHotKey(Handle, id, mods, vk)) return false;
                _ids.Add(id);
                return true;
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY) Pressed?.Invoke(m.WParam.ToInt32());
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                foreach (var id in _ids) UnregisterHotKey(Handle, id);
                _ids.Clear();
                if (Handle != IntPtr.Zero) DestroyHandle();
            }
        }

        // ─────────────────────────── Helpers ───────────────────────────

        private void UpdateUi()
        {
            if (_toggle == null || _toggle.IsDisposed) return;
            if (IsArmed)
            {
                _toggle.Text = "DISABLED — click to Enable Now";
                _toggle.BackColor = Color.FromArgb(205, 60, 50);
                _toggle.Image = _imgDisabled;
            }
            else
            {
                _toggle.Text = "Enabled — click to Disable && Arm";
                _toggle.BackColor = Color.FromArgb(40, 150, 85);
                _toggle.Image = _imgEnabled;
            }
            _toggle.ForeColor = Color.White;
            _toggle.Enabled = _elevated && !_busy;
            _reenableAll.Enabled = _elevated && !_busy;
            _devices.Enabled = !IsArmed && !_busy;
            _seconds.Enabled = !IsArmed;
        }

        private void LoadImages()
        {
            try
            {
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
                using (var on = Image.FromFile(Path.Combine(dir, "status-enabled.png")))
                    _imgEnabled = new Bitmap(on, 32, 32);
                using (var off = Image.FromFile(Path.Combine(dir, "status-disabled.png")))
                    _imgDisabled = new Bitmap(off, 32, 32);
            }
            catch { /* icons are cosmetic; the button colour carries the state */ }
        }

        private void PlaySound()
        {
            try
            {
                if (File.Exists(EnabledSoundPath))
                {
                    if (_sound == null) _sound = new SoundPlayer(EnabledSoundPath);
                    _sound.Play();
                }
                else SystemSounds.Asterisk.Play();
            }
            catch { SystemSounds.Asterisk.Play(); }
        }

        private static bool IsElevated()
        {
            using (var id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void RelaunchElevated()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (Win32Exception) { Log("Elevation was cancelled."); return; }
            Application.Exit();
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            // Mirror to the single app-wide log file.
            AppLog.Append("UsbTrigger",
                message.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0 || message.StartsWith("Warning") ? "WARN"
                : message.StartsWith("Voice") ? "VOICE" : "INFO", message);

            if (_log == null || _log.IsDisposed) return;
            var line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message.Trim() + Environment.NewLine;
            Action add = () => { if (!_log.IsDisposed) _log.AppendText(line); };
            if (_log.IsHandleCreated && _log.InvokeRequired) _log.BeginInvoke(add); else add();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _countdown.Stop();
            _countdown.Dispose();
            _voice?.Dispose(); _voice = null;
            _hotkeys?.Dispose(); _hotkeys = null;

            // Never leave a hub switched off because the app was closed.
            if (_disabledBySession.Count > 0 &&
                MessageBox.Show(_disabledBySession.Count + " USB device(s) are still disabled by this app.\n\nRe-enable them before exiting?",
                    "TestPoint Trigger", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                foreach (var id in _disabledBySession.ToList()) PnpUtil.Enable(id);
            }

            _sound?.Dispose();
            _imgEnabled?.Dispose();
            _imgDisabled?.Dispose();
        }
    }
}
