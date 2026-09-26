// TestPoint Trigger - licence editor dialog
// Developer: HaKDMoDz™ · v1.0.0 · 2026-09-26
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace TestPointTrigger.Modules
{
    /// <summary>Modal add/edit form for a single <see cref="ToolLicense"/>.</summary>
    internal sealed class LicenseEditForm : Form
    {
        private readonly ToolLicense _rec;
        private TextBox _tool, _vendor, _key, _account, _hw, _cost, _url, _notes;
        private ComboBox _category, _type, _status;
        private DateTimePicker _purchase, _expiry;
        private NumericUpDown _credits;

        public LicenseEditForm(ToolLicense rec, bool isNew)
        {
            _rec = rec;
            Text = (isNew ? "Add" : "Edit") + " tool / licence";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(540, 640);

            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 0,
                Padding = new Padding(12), AutoScroll = true, GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _tool = Tb(rec.Tool);
            _vendor = Tb(rec.Vendor);
            _category = Cb(rec.Category, "Forensic", "Servicing", "FRP", "Flashing", "Other");
            _category.DropDownStyle = ComboBoxStyle.DropDownList;
            _type = Cb(rec.LicenseType, "Dongle", "Activation", "Credits", "Subscription", "Perpetual");
            _type.DropDownStyle = ComboBoxStyle.DropDownList;
            _key = Tb(rec.LicenseKey);
            _account = Tb(rec.Account);
            _hw = Tb(rec.HardwareId);
            _purchase = DatePicker(rec.PurchaseDate);
            _expiry = DatePicker(rec.ExpiryDate);
            _credits = new NumericUpDown { Minimum = 0, Maximum = 1000000000, Width = 100, Value = Math.Max(0, rec.Credits) };
            _cost = Tb(rec.Cost.ToString("0.00", CultureInfo.CurrentCulture));
            _cost.Width = 120;
            _status = Cb(rec.Status, "Active", "Expired", "Suspended");
            _status.DropDownStyle = ComboBoxStyle.DropDownList;
            _url = Tb(rec.Url);
            _notes = new TextBox { Text = rec.Notes, Multiline = true, Height = 80, Width = 380, ScrollBars = ScrollBars.Vertical };

            void Row(string label, Control c)
            {
                int r = t.RowCount;
                t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 6) }, 0, r);
                t.Controls.Add(c, 1, r);
                t.RowCount = r + 1;
            }

            Row("Tool *", _tool);
            Row("Vendor", _vendor);
            Row("Category", _category);
            Row("Licence type", _type);
            Row("Licence key", _key);
            Row("Account", _account);
            Row("Hardware/dongle ID", _hw);
            Row("Purchased", _purchase);
            Row("Expires", _expiry);
            Row("Credits left", _credits);
            Row("Cost", _cost);
            Row("Status", _status);
            Row("Vendor URL", _url);
            Row("Notes", _notes);

            var ok = new Button { Text = "Save", Width = 90 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
            ok.Click += (s, e) => { if (Commit()) DialogResult = DialogResult.OK; };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(10) };
            buttons.Controls.AddRange(new Control[] { cancel, ok });

            Controls.Add(t);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static TextBox Tb(string v) => new TextBox { Text = v ?? "", Width = 320, Anchor = AnchorStyles.Left };

        private static ComboBox Cb(string v, params string[] items)
        {
            var c = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDown };
            c.Items.AddRange(items);
            c.Text = v ?? "";
            return c;
        }

        // DateTimePicker with a checkbox: unchecked means "no date" (null).
        private static DateTimePicker DatePicker(DateTime? value)
        {
            var p = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 150, ShowCheckBox = true };
            if (value.HasValue) { p.Checked = true; p.Value = value.Value; }
            else p.Checked = false;
            return p;
        }

        private bool Commit()
        {
            if (string.IsNullOrWhiteSpace(_tool.Text))
            {
                MessageBox.Show(this, "Tool name is required.", "Licence", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            _rec.Tool = _tool.Text.Trim();
            _rec.Vendor = _vendor.Text.Trim();
            _rec.Category = _category.Text.Trim();
            _rec.LicenseType = _type.Text.Trim();
            _rec.LicenseKey = _key.Text.Trim();
            _rec.Account = _account.Text.Trim();
            _rec.HardwareId = _hw.Text.Trim();
            _rec.PurchaseDate = _purchase.Checked ? _purchase.Value.Date : (DateTime?)null;
            _rec.ExpiryDate = _expiry.Checked ? _expiry.Value.Date : (DateTime?)null;
            _rec.Credits = (int)_credits.Value;
            decimal.TryParse(_cost.Text.Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out var cost);
            _rec.Cost = cost;
            _rec.Status = string.IsNullOrEmpty(_status.Text) ? "Active" : _status.Text;
            _rec.Url = _url.Text.Trim();
            _rec.Notes = _notes.Text.Trim();
            _rec.UpdatedUtc = DateTime.UtcNow;
            return true;
        }
    }
}
