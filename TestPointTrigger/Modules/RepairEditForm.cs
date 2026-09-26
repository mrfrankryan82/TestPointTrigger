// TestPoint Trigger - repair record editor dialog
// Developer: HaKDMoDz™ · v1.0.0 · 2026-09-26
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TestPointTrigger.Modules
{
    /// <summary>Modal add/edit form for a single <see cref="RepairRecord"/>.</summary>
    internal sealed class RepairEditForm : Form
    {
        private readonly RepairRecord _rec;
        private DateTimePicker _date;
        private TextBox _brand, _model, _imei, _serial, _board, _fault, _testpoint, _tools, _image, _notes, _tech;
        private ComboBox _chipset, _procedure, _outcome;
        private NumericUpDown _time;

        public RepairEditForm(RepairRecord rec, bool isNew)
        {
            _rec = rec;
            Text = (isNew ? "Add" : "Edit") + " repair record";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(540, 660);

            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 0,
                Padding = new Padding(12), AutoScroll = true, GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _date = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 150,
                                         Value = rec.Date == default ? DateTime.Today : rec.Date };
            _brand = Tb(rec.Brand);
            _model = Tb(rec.Model);
            _chipset = Cb(rec.Chipset, "Qualcomm", "MediaTek", "Exynos", "Unisoc", "Kirin", "Other");
            _imei = Tb(rec.Imei);
            _serial = Tb(rec.Serial);
            _board = Tb(rec.BoardId);
            _fault = Tb(rec.Fault);
            _procedure = Cb(rec.Procedure, "EDL test-point", "BROM (MTK)", "EDL 9008", "FRP removal",
                            "Firmware flash", "Partition format", "Bootloader unlock", "Diag / repair", "Other");
            _testpoint = Tb(rec.TestPoint);
            _outcome = Cb(rec.Outcome, "Success", "Partial", "Failed", "Abandoned");
            _outcome.DropDownStyle = ComboBoxStyle.DropDownList;
            _tools = Tb(rec.Tools);
            _time = new NumericUpDown { Minimum = 0, Maximum = 100000, Width = 90,
                                        Value = Math.Max(0, rec.TimeMinutes) };
            _tech = Tb(string.IsNullOrEmpty(rec.Technician) ? "HaKDMoDz" : rec.Technician);

            _image = Tb(rec.ImagePath);
            _image.Width = 320;
            var browse = new Button { Text = "…", Width = 32 };
            browse.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*" })
                    if (d.ShowDialog(this) == DialogResult.OK) _image.Text = d.FileName;
            };
            var imageRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            imageRow.Controls.AddRange(new Control[] { _image, browse });

            _notes = new TextBox { Text = rec.Notes, Multiline = true, Height = 90,
                                   Width = 380, ScrollBars = ScrollBars.Vertical };

            void Row(string label, Control c)
            {
                int r = t.RowCount;
                t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left,
                                           Margin = new Padding(0, 6, 6, 6) }, 0, r);
                t.Controls.Add(c, 1, r);
                t.RowCount = r + 1;
            }

            Row("Date", _date);
            Row("Brand", _brand);
            Row("Model *", _model);
            Row("Chipset", _chipset);
            Row("IMEI", _imei);
            Row("Serial", _serial);
            Row("Board ID", _board);
            Row("Fault", _fault);
            Row("Procedure", _procedure);
            Row("Test point", _testpoint);
            Row("Outcome", _outcome);
            Row("Tools", _tools);
            Row("Time (min)", _time);
            Row("Technician", _tech);
            Row("Image", imageRow);
            Row("Notes", _notes);

            var ok = new Button { Text = "Save", Width = 90 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
            ok.Click += (s, e) => { if (Commit()) DialogResult = DialogResult.OK; };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft,
                                                Height = 46, Padding = new Padding(10) };
            buttons.Controls.AddRange(new Control[] { cancel, ok });

            Controls.Add(t);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static TextBox Tb(string v) =>
            new TextBox { Text = v ?? "", Width = 320, Anchor = AnchorStyles.Left };

        private static ComboBox Cb(string v, params string[] items)
        {
            var c = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDown };
            c.Items.AddRange(items);
            c.Text = v ?? "";
            return c;
        }

        private bool Commit()
        {
            if (string.IsNullOrWhiteSpace(_model.Text))
            {
                MessageBox.Show(this, "Model is required.", "Repair record",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            _rec.Date = _date.Value.Date;
            _rec.Brand = _brand.Text.Trim();
            _rec.Model = _model.Text.Trim();
            _rec.Chipset = _chipset.Text.Trim();
            _rec.Imei = _imei.Text.Trim();
            _rec.Serial = _serial.Text.Trim();
            _rec.BoardId = _board.Text.Trim();
            _rec.Fault = _fault.Text.Trim();
            _rec.Procedure = _procedure.Text.Trim();
            _rec.TestPoint = _testpoint.Text.Trim();
            _rec.Outcome = string.IsNullOrEmpty(_outcome.Text) ? "Success" : _outcome.Text;
            _rec.Tools = _tools.Text.Trim();
            _rec.TimeMinutes = (int)_time.Value;
            _rec.Technician = _tech.Text.Trim();
            _rec.ImagePath = _image.Text.Trim();
            _rec.Notes = _notes.Text.Trim();
            _rec.UpdatedUtc = DateTime.UtcNow;
            return true;
        }
    }
}
