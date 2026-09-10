namespace TestPointTrigger
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.Label titleLabel;
        private System.Windows.Forms.Button helpButton;
        private System.Windows.Forms.Label deviceLabel;
        private System.Windows.Forms.ComboBox devicesCombo;
        private System.Windows.Forms.Button refreshButton;
        private System.Windows.Forms.Label secondsLabel;
        private System.Windows.Forms.NumericUpDown secondsUpDown;
        private System.Windows.Forms.Button toggleButton;
        private System.Windows.Forms.Button panicButton;
        private System.Windows.Forms.Label countdownCaption;
        private System.Windows.Forms.Label countdownLabel;
        private System.Windows.Forms.Label hotkeyInfoLabel;
        private System.Windows.Forms.TextBox logBox;
        private System.Windows.Forms.Label footerLabel;

        // System tray "status notifier" — shows the same enabled/disabled
        // icon as the toggle button so the current state is visible even
        // when the window is minimized or behind other windows.
        private System.Windows.Forms.NotifyIcon trayIcon;
        private System.Windows.Forms.ContextMenuStrip trayMenu;
        private System.Windows.Forms.ToolStripMenuItem trayShowItem;
        private System.Windows.Forms.ToolStripMenuItem trayToggleItem;
        private System.Windows.Forms.ToolStripMenuItem trayReenableAllItem;
        private System.Windows.Forms.ToolStripSeparator traySeparator;
        private System.Windows.Forms.ToolStripMenuItem trayExitItem;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.titleLabel = new System.Windows.Forms.Label();
            this.helpButton = new System.Windows.Forms.Button();
            this.deviceLabel = new System.Windows.Forms.Label();
            this.devicesCombo = new System.Windows.Forms.ComboBox();
            this.refreshButton = new System.Windows.Forms.Button();
            this.secondsLabel = new System.Windows.Forms.Label();
            this.secondsUpDown = new System.Windows.Forms.NumericUpDown();
            this.toggleButton = new System.Windows.Forms.Button();
            this.panicButton = new System.Windows.Forms.Button();
            this.countdownCaption = new System.Windows.Forms.Label();
            this.countdownLabel = new System.Windows.Forms.Label();
            this.hotkeyInfoLabel = new System.Windows.Forms.Label();
            this.logBox = new System.Windows.Forms.TextBox();
            this.footerLabel = new System.Windows.Forms.Label();
            this.trayMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.trayShowItem = new System.Windows.Forms.ToolStripMenuItem();
            this.trayToggleItem = new System.Windows.Forms.ToolStripMenuItem();
            this.trayReenableAllItem = new System.Windows.Forms.ToolStripMenuItem();
            this.traySeparator = new System.Windows.Forms.ToolStripSeparator();
            this.trayExitItem = new System.Windows.Forms.ToolStripMenuItem();
            this.trayIcon = new System.Windows.Forms.NotifyIcon(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.secondsUpDown)).BeginInit();
            this.trayMenu.SuspendLayout();
            this.SuspendLayout();

            // titleLabel
            this.titleLabel.AutoSize = true;
            this.titleLabel.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.titleLabel.Location = new System.Drawing.Point(16, 12);
            this.titleLabel.Text = "TestPoint Trigger";

            // helpButton
            this.helpButton.Location = new System.Drawing.Point(524, 10);
            this.helpButton.Size = new System.Drawing.Size(40, 28);
            this.helpButton.Text = "Help";
            this.helpButton.UseVisualStyleBackColor = true;
            this.helpButton.Click += new System.EventHandler(this.HelpButton_Click);

            // deviceLabel
            this.deviceLabel.AutoSize = true;
            this.deviceLabel.Location = new System.Drawing.Point(16, 48);
            this.deviceLabel.Text = "USB hub / controller to arm:";

            // devicesCombo
            this.devicesCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.devicesCombo.Location = new System.Drawing.Point(16, 68);
            this.devicesCombo.Size = new System.Drawing.Size(460, 23);

            // refreshButton
            this.refreshButton.Location = new System.Drawing.Point(484, 67);
            this.refreshButton.Size = new System.Drawing.Size(80, 25);
            this.refreshButton.Text = "Refresh";
            this.refreshButton.UseVisualStyleBackColor = true;
            this.refreshButton.Click += new System.EventHandler(this.RefreshButton_Click);

            // secondsLabel
            this.secondsLabel.AutoSize = true;
            this.secondsLabel.Location = new System.Drawing.Point(16, 106);
            this.secondsLabel.Text = "Countdown (seconds, fallback if you don't hit the hotkey):";

            // secondsUpDown
            this.secondsUpDown.Location = new System.Drawing.Point(16, 126);
            this.secondsUpDown.Size = new System.Drawing.Size(60, 23);
            this.secondsUpDown.Minimum = 2;
            this.secondsUpDown.Maximum = 120;
            this.secondsUpDown.Value = 10;

            // toggleButton — single on/off style toggle: its icon and text
            // always reflect the CURRENT status (green/enabled or
            // red/disabled), and clicking it flips to the other state.
            this.toggleButton.Location = new System.Drawing.Point(16, 162);
            this.toggleButton.Size = new System.Drawing.Size(308, 34);
            this.toggleButton.Text = "Enabled — click to Disable && Arm";
            this.toggleButton.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.toggleButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.toggleButton.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.toggleButton.UseVisualStyleBackColor = true;
            this.toggleButton.Click += new System.EventHandler(this.ToggleButton_Click);

            // panicButton
            this.panicButton.Location = new System.Drawing.Point(332, 162);
            this.panicButton.Size = new System.Drawing.Size(150, 34);
            this.panicButton.Text = "Re-enable All";
            this.panicButton.UseVisualStyleBackColor = true;
            this.panicButton.Click += new System.EventHandler(this.PanicButton_Click);

            // countdownCaption
            this.countdownCaption.AutoSize = true;
            this.countdownCaption.Location = new System.Drawing.Point(500, 130);
            this.countdownCaption.Text = "Time left:";

            // countdownLabel
            this.countdownLabel.AutoSize = true;
            this.countdownLabel.Font = new System.Drawing.Font("Segoe UI", 24F, System.Drawing.FontStyle.Bold);
            this.countdownLabel.Location = new System.Drawing.Point(500, 148);
            this.countdownLabel.Size = new System.Drawing.Size(60, 45);
            this.countdownLabel.Text = "--";

            // hotkeyInfoLabel
            this.hotkeyInfoLabel.AutoSize = true;
            this.hotkeyInfoLabel.ForeColor = System.Drawing.Color.DimGray;
            this.hotkeyInfoLabel.Location = new System.Drawing.Point(16, 206);
            this.hotkeyInfoLabel.Text = "Global hotkeys (work without this window focused): Ctrl+Alt+D disable/arm, Ctrl+Alt+E enable now.";

            // logBox
            this.logBox.Location = new System.Drawing.Point(16, 232);
            this.logBox.Size = new System.Drawing.Size(548, 220);
            this.logBox.Multiline = true;
            this.logBox.ReadOnly = true;
            this.logBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.logBox.Font = new System.Drawing.Font("Consolas", 9F);

            // footerLabel — developer/version credit, filled in at runtime
            // from the assembly version (see MainForm.cs UpdateFooter()).
            this.footerLabel.AutoSize = false;
            this.footerLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.footerLabel.ForeColor = System.Drawing.Color.Gray;
            this.footerLabel.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.footerLabel.Location = new System.Drawing.Point(16, 460);
            this.footerLabel.Size = new System.Drawing.Size(548, 18);
            this.footerLabel.Text = "HaKDMoDz™";

            // trayMenu
            this.trayMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.trayShowItem,
                this.trayToggleItem,
                this.trayReenableAllItem,
                this.traySeparator,
                this.trayExitItem});

            // trayShowItem
            this.trayShowItem.Text = "Show TestPoint Trigger";
            this.trayShowItem.Click += new System.EventHandler(this.TrayShowItem_Click);

            // trayToggleItem
            this.trayToggleItem.Text = "Disable && Arm";
            this.trayToggleItem.Click += new System.EventHandler(this.ToggleButton_Click);

            // trayReenableAllItem
            this.trayReenableAllItem.Text = "Re-enable All";
            this.trayReenableAllItem.Click += new System.EventHandler(this.PanicButton_Click);

            // trayExitItem
            this.trayExitItem.Text = "Exit";
            this.trayExitItem.Click += new System.EventHandler(this.TrayExitItem_Click);

            // trayIcon
            this.trayIcon.ContextMenuStrip = this.trayMenu;
            this.trayIcon.Text = "TestPoint Trigger — enabled";
            this.trayIcon.Visible = true;
            this.trayIcon.DoubleClick += new System.EventHandler(this.TrayShowItem_Click);

            // MainForm
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(580, 494);
            this.Controls.Add(this.titleLabel);
            this.Controls.Add(this.helpButton);
            this.Controls.Add(this.deviceLabel);
            this.Controls.Add(this.devicesCombo);
            this.Controls.Add(this.refreshButton);
            this.Controls.Add(this.secondsLabel);
            this.Controls.Add(this.secondsUpDown);
            this.Controls.Add(this.toggleButton);
            this.Controls.Add(this.panicButton);
            this.Controls.Add(this.countdownCaption);
            this.Controls.Add(this.countdownLabel);
            this.Controls.Add(this.hotkeyInfoLabel);
            this.Controls.Add(this.logBox);
            this.Controls.Add(this.footerLabel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "TestPoint Trigger — run as Administrator";
            ((System.ComponentModel.ISupportInitialize)(this.secondsUpDown)).EndInit();
            this.trayMenu.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
