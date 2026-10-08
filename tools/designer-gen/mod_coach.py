from patch import *
F = r"Modules\LiveCoachModule.cs"
t = load(F)
t = replace1(t, "using OpenCvSharp.Extensions;", "using OpenCvSharp.Extensions;\nusing TestPointTrigger.Modules.Views;")
t = between(t, "            var root = new TableLayoutPanel", "        /// <summary>\n        /// Reminder card", ('''            // Layout and styling live in LiveCoachView.Designer.cs (open it in Design View).
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

'''))
save(F, t); print("patched", F)
