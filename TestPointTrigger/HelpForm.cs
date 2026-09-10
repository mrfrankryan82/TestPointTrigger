using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TestPointTrigger
{
    /// <summary>
    /// Simple built-in help dialog: a short blurb on what the app does and
    /// how to use it, plus a link out to the full README on GitHub. Built
    /// entirely in code (no separate .Designer.cs) since it's a one-off
    /// static dialog.
    /// </summary>
    public class HelpForm : Form
    {
        private const string Blurb =
            "TestPoint Trigger\r\n\r\n" +
            "Lets you plug a phone's USB cable in ahead of time, but keep Windows from " +
            "seeing it as connected, then flip it \"live\" with one keypress once your " +
            "tweezers are on the EDL/BROM test point and the battery clip is connected — " +
            "so the only thing left to do at the last instant is press a key instead of " +
            "physically plugging the cable in.\r\n\r\n" +
            "How it works: it disables the specific USB hub/host controller the phone's " +
            "cable is plugged into (via pnputil), then re-enables it on your signal. " +
            "Re-enabling makes Windows re-enumerate everything on that hub from scratch — " +
            "electrically identical to a fresh plug-in.\r\n\r\n" +
            "Quick controls:\r\n" +
            "  • Refresh — reload the list of USB hubs/controllers\r\n" +
            "  • Toggle button / Ctrl+Alt+D — disable & arm the selected hub\r\n" +
            "  • Toggle button / Ctrl+Alt+E — enable now (the trigger)\r\n" +
            "  • Re-enable All — safety net if anything is stuck disabled\r\n" +
            "  • Tray icon — green = enabled, red = disabled, at a glance\r\n\r\n" +
            "Safety: point this at a dedicated/spare USB hub, not the port sharing your " +
            "keyboard or mouse — disabling a hub disables everything downstream of it.\r\n\r\n" +
            "Run as Administrator (pnputil needs it).";

        private readonly string _repoUrl;

        public HelpForm(string repoUrl)
        {
            _repoUrl = repoUrl;

            Text = "TestPoint Trigger — Help";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 444);

            var blurbBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(16, 16),
                Size = new Size(428, 330),
                Text = Blurb,
            };

            var link = new LinkLabel
            {
                Text = "View the full README on GitHub",
                Location = new Point(16, 356),
                AutoSize = true,
            };
            link.Click += (s, e) => OpenRepo();

            var footerLabel = new Label
            {
                Text = AppInfo.FooterText,
                ForeColor = SystemColors.GrayText,
                Font = new Font("Segoe UI", 8F),
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(16, 388),
                Size = new Size(300, 18),
            };

            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(368, 404),
                Size = new Size(76, 28),
            };

            Controls.Add(blurbBox);
            Controls.Add(link);
            Controls.Add(footerLabel);
            Controls.Add(okButton);
            AcceptButton = okButton;
        }

        private void OpenRepo()
        {
            try
            {
                Process.Start(new ProcessStartInfo(_repoUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Couldn't open a browser automatically. The README lives here:\n\n{_repoUrl}\n\n({ex.Message})",
                    "TestPoint Trigger — Help",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
    }
}
