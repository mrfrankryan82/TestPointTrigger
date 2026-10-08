from patch import *
F = r"Modules\UsbTriggerModule.cs"
t = load(F)
t = replace1(t, "using System.Windows.Forms;", "using System.Windows.Forms;\r\nusing TestPointTrigger.Modules.Views;")
t = between(t, "        public Control CreateView(IModuleHost host)", "        public void Activate()", crlf('''        public Control CreateView(IModuleHost host)
        {
            _host = host;
            LoadImages();

            // Layout and styling live in UsbTriggerView.Designer.cs (open it in Design View).
            var v = new UsbTriggerView();
            v.flpAdminBanner.Visible = !_elevated;   // pnputil cannot touch device nodes without admin
            v.btnRestartAdmin.Click += (s, e) => RelaunchElevated();

            _devices = v.cboDevices;
            _refresh = v.btnRefresh;
            _seconds = v.nudSeconds;
            _timeLeft = v.lblTimeLeft;
            _toggle = v.btnToggle;
            _reenableAll = v.btnReenableAll;
            _voiceOn = v.chkVoice;
            _phrases = v.txtPhrases;
            _confidence = v.nudConfidence;
            _log = v.txtLog;
            _ui = v;

            _refresh.Click += (s, e) => RefreshDevices();
            _toggle.Click += async (s, e) => await OnToggleAsync();
            _reenableAll.Click += async (s, e) => await ReenableAllAsync();
            _voiceOn.CheckedChanged += (s, e) => ToggleVoice();
            _phrases.Leave += (s, e) => { if (_voiceOn.Checked) ToggleVoice(); };      // re-load new phrases
            _confidence.ValueChanged += (s, e) => { if (_voice != null) _voice.MinConfidence = (float)_confidence.Value / 100f; };

            _countdown.Tick += async (s, e) =>
            {
                _secondsLeft--;
                _timeLeft.Text = _secondsLeft > 0 ? _secondsLeft + "s" : "GO";
                if (_secondsLeft <= 0) await TriggerAsync();
            };

            RegisterHotkeys();
            if (!_elevated) Log("Not elevated: use 'Restart as Administrator' before arming.");
            UpdateUi();
            return v;
        }

'''))
save(F, t); print("patched", F)
