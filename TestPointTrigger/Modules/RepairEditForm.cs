// Mobile Surgery - repair record editor dialog (layout in RepairEditForm.Designer.cs)
// Developer: HaKDMoDz™ · v1.1.0 · 2026-10-09
using System;
using System.Windows.Forms;

namespace TestPointTrigger.Modules
{
    /// <summary>Modal add/edit form for a single <see cref="RepairRecord"/>.</summary>
    internal sealed partial class RepairEditForm : Form
    {
        private readonly RepairRecord _rec;

        /// <summary>Designer-only constructor.</summary>
        public RepairEditForm() : this(new RepairRecord(), true) { }

        public RepairEditForm(RepairRecord rec, bool isNew)
        {
            InitializeComponent();
            _rec = rec;
            Text = (isNew ? "Add" : "Edit") + " repair record";

            dtpDate.Value = rec.Date == default ? DateTime.Today : rec.Date;
            txtBrand.Text = rec.Brand ?? "";
            txtModel.Text = rec.Model ?? "";
            cboChipset.Text = rec.Chipset ?? "";
            txtImei.Text = rec.Imei ?? "";
            txtSerial.Text = rec.Serial ?? "";
            txtBoardId.Text = rec.BoardId ?? "";
            txtFault.Text = rec.Fault ?? "";
            cboProcedure.Text = rec.Procedure ?? "";
            txtTestPoint.Text = rec.TestPoint ?? "";
            cboOutcome.Text = rec.Outcome ?? "";
            txtTools.Text = rec.Tools ?? "";
            nudTime.Value = Math.Min(nudTime.Maximum, Math.Max(0, rec.TimeMinutes));
            txtTechnician.Text = string.IsNullOrEmpty(rec.Technician) ? "HaKDMoDz" : rec.Technician;
            txtImage.Text = rec.ImagePath ?? "";
            txtNotes.Text = rec.Notes ?? "";
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using (var d = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) txtImage.Text = d.FileName;
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (Commit()) DialogResult = DialogResult.OK;
        }

        private bool Commit()
        {
            if (string.IsNullOrWhiteSpace(txtModel.Text))
            {
                MessageBox.Show(this, "Model is required.", "Repair record", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            _rec.Date = dtpDate.Value.Date;
            _rec.Brand = txtBrand.Text.Trim();
            _rec.Model = txtModel.Text.Trim();
            _rec.Chipset = cboChipset.Text.Trim();
            _rec.Imei = txtImei.Text.Trim();
            _rec.Serial = txtSerial.Text.Trim();
            _rec.BoardId = txtBoardId.Text.Trim();
            _rec.Fault = txtFault.Text.Trim();
            _rec.Procedure = cboProcedure.Text.Trim();
            _rec.TestPoint = txtTestPoint.Text.Trim();
            _rec.Outcome = string.IsNullOrEmpty(cboOutcome.Text) ? "Success" : cboOutcome.Text;
            _rec.Tools = txtTools.Text.Trim();
            _rec.TimeMinutes = (int)nudTime.Value;
            _rec.Technician = txtTechnician.Text.Trim();
            _rec.ImagePath = txtImage.Text.Trim();
            _rec.Notes = txtNotes.Text.Trim();
            _rec.UpdatedUtc = DateTime.UtcNow;
            return true;
        }
    }
}
