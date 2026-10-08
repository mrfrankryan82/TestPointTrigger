// Mobile Surgery - licence editor dialog (layout in LicenseEditForm.Designer.cs)
// Developer: HaKDMoDz™ · v1.1.0 · 2026-10-09
using System;
using System.Globalization;
using System.Windows.Forms;

namespace TestPointTrigger.Modules
{
    /// <summary>Modal add/edit form for a single <see cref="ToolLicense"/>.</summary>
    internal sealed partial class LicenseEditForm : Form
    {
        private readonly ToolLicense _rec;

        /// <summary>Designer-only constructor.</summary>
        public LicenseEditForm() : this(new ToolLicense(), true) { }

        public LicenseEditForm(ToolLicense rec, bool isNew)
        {
            InitializeComponent();
            _rec = rec;
            Text = (isNew ? "Add" : "Edit") + " tool / licence";

            txtTool.Text = rec.Tool ?? "";
            txtVendor.Text = rec.Vendor ?? "";
            cboCategory.Text = rec.Category ?? "";
            cboType.Text = rec.LicenseType ?? "";
            txtKey.Text = rec.LicenseKey ?? "";
            txtAccount.Text = rec.Account ?? "";
            txtHardwareId.Text = rec.HardwareId ?? "";
            SetDate(dtpPurchased, rec.PurchaseDate);
            SetDate(dtpExpires, rec.ExpiryDate);
            nudCredits.Value = Math.Min(nudCredits.Maximum, Math.Max(0, rec.Credits));
            txtCost.Text = rec.Cost.ToString("0.00", CultureInfo.CurrentCulture);
            cboStatus.Text = rec.Status ?? "";
            txtUrl.Text = rec.Url ?? "";
            txtNotes.Text = rec.Notes ?? "";
        }

        // Date pickers show a checkbox: unchecked means "no date" (null).
        private static void SetDate(DateTimePicker p, DateTime? value)
        {
            if (value.HasValue) { p.Checked = true; p.Value = value.Value; }
            else p.Checked = false;
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (Commit()) DialogResult = DialogResult.OK;
        }

        private bool Commit()
        {
            if (string.IsNullOrWhiteSpace(txtTool.Text))
            {
                MessageBox.Show(this, "Tool name is required.", "Licence", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            _rec.Tool = txtTool.Text.Trim();
            _rec.Vendor = txtVendor.Text.Trim();
            _rec.Category = cboCategory.Text.Trim();
            _rec.LicenseType = cboType.Text.Trim();
            _rec.LicenseKey = txtKey.Text.Trim();
            _rec.Account = txtAccount.Text.Trim();
            _rec.HardwareId = txtHardwareId.Text.Trim();
            _rec.PurchaseDate = dtpPurchased.Checked ? dtpPurchased.Value.Date : (DateTime?)null;
            _rec.ExpiryDate = dtpExpires.Checked ? dtpExpires.Value.Date : (DateTime?)null;
            _rec.Credits = (int)nudCredits.Value;
            decimal.TryParse(txtCost.Text.Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out var cost);
            _rec.Cost = cost;
            _rec.Status = string.IsNullOrEmpty(cboStatus.Text) ? "Active" : cboStatus.Text;
            _rec.Url = txtUrl.Text.Trim();
            _rec.Notes = txtNotes.Text.Trim();
            _rec.UpdatedUtc = DateTime.UtcNow;
            return true;
        }
    }
}
