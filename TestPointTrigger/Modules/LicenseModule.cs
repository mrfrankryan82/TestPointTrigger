// TestPoint Trigger - forensic tools & licences module
// Developer: HaKDMoDz™ · v1.0.0 · 2026-09-26
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// Inventory of forensic/servicing tools and their licences. Tracks expiry
    /// (rows tint amber within 30 days, red once expired), credits and cost,
    /// with search, stats, CSV in/out and a "check expiries" report.
    /// </summary>
    public class LicenseModule : IModule
    {
        public string Id => "licenses";
        public string Title => "Tool Licences";
        public string Description => "Track forensic/servicing tools, licences, credits, expiry and cost.";
        public string Version => "1.0.0";
        public int SortOrder => 40;

        private const int ExpirySoonDays = 30;

        private IModuleHost _host;
        private readonly LicenseStore _store = new LicenseStore();
        private readonly BindingList<ToolLicense> _view = new BindingList<ToolLicense>();
        private DataGridView _grid;
        private TextBox _search;
        private ComboBox _category, _status;
        private Label _count;
        private bool _loaded;

        public Control CreateView(IModuleHost host)
        {
            _host = host;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            _search = new TextBox { Width = 180 };
            _search.TextChanged += (s, e) => Refilter();
            _category = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            _category.Items.AddRange(new object[] { "All categories", "Forensic", "Servicing", "FRP", "Flashing", "Other" });
            _category.SelectedIndex = 0; _category.SelectedIndexChanged += (s, e) => Refilter();
            _status = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            _status.Items.AddRange(new object[] { "All statuses", "Active", "Expired", "Suspended" });
            _status.SelectedIndex = 0; _status.SelectedIndexChanged += (s, e) => Refilter();

            bar.Controls.AddRange(new Control[]
            {
                new Label { Text = "Search:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) }, _search, _category, _status,
                Btn("Add", Add), Btn("Edit", EditSelected), Btn("Duplicate", DuplicateSelected), Btn("Delete", DeleteSelected),
                Btn("Check expiries", CheckExpiries), Btn("Stats", ShowStats),
                Btn("Export CSV", ExportCsv), Btn("Import CSV", ImportCsv),
                Btn("Open portal", OpenPortal), Btn("Open folder", OpenFolder)
            });

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false, AutoGenerateColumns = false, AllowUserToResizeRows = false
            };
            AddCol("Tool", "Tool", 130, null);
            AddCol("Vendor", "Vendor", 100, null);
            AddCol("Category", "Category", 80, null);
            AddCol("LicenseType", "Type", 90, null);
            AddCol("Status", "Status", 70, null);
            AddCol("Credits", "Credits", 60, null);
            AddCol("ExpiryDate", "Expires", 90, "yyyy-MM-dd");
            AddCol("HardwareId", "Dongle/HW ID", 120, null);
            AddCol("Notes", "Notes", 180, null);
            _grid.DataSource = _view;
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditSelected(); };
            _grid.CellFormatting += ColourExpiry;

            _count = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(2, 4, 0, 0) };

            root.Controls.Add(bar, 0, 0);
            root.Controls.Add(_grid, 0, 1);
            root.Controls.Add(_count, 0, 2);
            return root;
        }

        private void ColourExpiry(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (_grid.Columns[e.ColumnIndex].DataPropertyName != "ExpiryDate") return;
            if (!(_grid.Rows[e.RowIndex].DataBoundItem is ToolLicense rec)) return;
            var days = rec.DaysToExpiry;
            if (days == null) return;
            if (days < 0) { e.CellStyle.BackColor = Color.MistyRose; e.CellStyle.ForeColor = Color.DarkRed; }
            else if (days <= ExpirySoonDays) { e.CellStyle.BackColor = Color.LightGoldenrodYellow; e.CellStyle.ForeColor = Color.Sienna; }
        }

        private void AddCol(string prop, string header, int w, string format)
        {
            var col = new DataGridViewTextBoxColumn
            {
                DataPropertyName = prop, HeaderText = header, Width = w,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };
            if (format != null) { col.DefaultCellStyle.Format = format; col.DefaultCellStyle.NullValue = ""; }
            _grid.Columns.Add(col);
        }

        private Button Btn(string text, Action a)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(3, 2, 0, 2) };
            b.Click += (s, e) => a();
            return b;
        }

        public void Activate()
        {
            if (!_loaded) { _store.Load(); _loaded = true; }
            Refilter();
            var soon = _store.Items.Count(x => x.DaysToExpiry is double d && d >= 0 && d <= ExpirySoonDays);
            var gone = _store.Items.Count(x => x.DaysToExpiry is double d && d < 0);
            _host?.SetStatus("Tool Licences: " + _store.Items.Count + " items \u00b7 " +
                             gone + " expired, " + soon + " expiring soon.");
        }

        public void Deactivate() { }

        private void Refilter()
        {
            IEnumerable<ToolLicense> q = _store.Items;
            var term = _search.Text.Trim();
            if (term.Length > 0)
                q = q.Where(x => (x.Tool + " " + x.Vendor + " " + x.Category + " " + x.LicenseType + " " +
                                  x.Account + " " + x.HardwareId + " " + x.Notes)
                                 .IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            if (_category.SelectedIndex > 0)
                q = q.Where(x => string.Equals(x.Category, (string)_category.SelectedItem, StringComparison.OrdinalIgnoreCase));
            if (_status.SelectedIndex > 0)
                q = q.Where(x => string.Equals(x.Status, (string)_status.SelectedItem, StringComparison.OrdinalIgnoreCase));

            _view.RaiseListChangedEvents = false;
            _view.Clear();
            foreach (var x in q.OrderBy(x => x.Tool, StringComparer.OrdinalIgnoreCase)) _view.Add(x);
            _view.RaiseListChangedEvents = true;
            _view.ResetBindings();
            _count.Text = _view.Count + " shown \u00b7 " + _store.Items.Count + " total";
        }

        private ToolLicense Selected => _grid.CurrentRow?.DataBoundItem as ToolLicense;

        private void Add()
        {
            var rec = new ToolLicense();
            using (var f = new LicenseEditForm(rec, true))
                if (f.ShowDialog(_grid.FindForm()) == DialogResult.OK)
                {
                    _store.Items.Add(rec); _store.Save(); Refilter();
                    AppLog.Info("Licenses", "Added " + rec.Tool + " [" + rec.Category + "/" + rec.LicenseType + "]");
                }
        }

        private void EditSelected()
        {
            var rec = Selected;
            if (rec == null) { Info("Select a licence first."); return; }
            using (var f = new LicenseEditForm(rec, false))
                if (f.ShowDialog(_grid.FindForm()) == DialogResult.OK)
                {
                    _store.Save(); Refilter();
                    AppLog.Info("Licenses", "Edited " + rec.Tool);
                }
        }

        private void DuplicateSelected()
        {
            var s = Selected;
            if (s == null) { Info("Select a licence to duplicate."); return; }
            var copy = new ToolLicense
            {
                Tool = s.Tool, Vendor = s.Vendor, Category = s.Category, LicenseType = s.LicenseType,
                Account = s.Account, Url = s.Url, Status = "Active"
            };
            using (var f = new LicenseEditForm(copy, true))
                if (f.ShowDialog(_grid.FindForm()) == DialogResult.OK)
                { _store.Items.Add(copy); _store.Save(); Refilter(); }
        }

        private void DeleteSelected()
        {
            var rec = Selected;
            if (rec == null) { Info("Select a licence to delete."); return; }
            if (MessageBox.Show(_grid.FindForm(), "Delete this licence?\n\n" + rec.Tool + "  (" + rec.Vendor + ")",
                    "Tool Licences", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _store.Items.Remove(rec); _store.Save(); Refilter();
            AppLog.Warn("Licenses", "Deleted " + rec.Tool);
        }

        private void CheckExpiries()
        {
            var expired = _store.Items.Where(x => x.DaysToExpiry is double d && d < 0)
                                      .OrderBy(x => x.ExpiryDate).ToList();
            var soon = _store.Items.Where(x => x.DaysToExpiry is double d && d >= 0 && d <= ExpirySoonDays)
                                   .OrderBy(x => x.ExpiryDate).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("EXPIRED (" + expired.Count + "):");
            if (expired.Count == 0) sb.AppendLine("  none");
            foreach (var x in expired)
                sb.AppendLine("  " + x.Tool + "  -  " + x.ExpiryDate.Value.ToString("yyyy-MM-dd") +
                              "  (" + (-(int)x.DaysToExpiry.Value) + " days ago)");
            sb.AppendLine();
            sb.AppendLine("EXPIRING within " + ExpirySoonDays + " days (" + soon.Count + "):");
            if (soon.Count == 0) sb.AppendLine("  none");
            foreach (var x in soon)
                sb.AppendLine("  " + x.Tool + "  -  " + x.ExpiryDate.Value.ToString("yyyy-MM-dd") +
                              "  (in " + (int)x.DaysToExpiry.Value + " days)");

            AppLog.Info("Licenses", "Expiry check: " + expired.Count + " expired, " + soon.Count + " expiring soon.");
            MessageBox.Show(_grid.FindForm(), sb.ToString(), "Licence expiries",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowStats()
        {
            var r = _store.Items;
            if (r.Count == 0) { Info("No licences yet."); return; }
            var sb = new StringBuilder();
            sb.AppendLine("Total tools/licences: " + r.Count);
            sb.AppendLine("Total spend: " + r.Sum(x => x.Cost).ToString("0.00"));
            sb.AppendLine("Total credits on hand: " + r.Sum(x => x.Credits));
            sb.AppendLine("Expired: " + r.Count(x => x.DaysToExpiry is double d && d < 0) +
                          " \u00b7 expiring \u226430d: " + r.Count(x => x.DaysToExpiry is double d && d >= 0 && d <= ExpirySoonDays));
            sb.AppendLine();
            sb.AppendLine("By category:");
            foreach (var g in r.GroupBy(x => string.IsNullOrEmpty(x.Category) ? "(blank)" : x.Category).OrderByDescending(g => g.Count()))
                sb.AppendLine("  " + g.Key.PadRight(10) + " " + g.Count());
            sb.AppendLine();
            sb.AppendLine("By licence type:");
            foreach (var g in r.GroupBy(x => string.IsNullOrEmpty(x.LicenseType) ? "(blank)" : x.LicenseType).OrderByDescending(g => g.Count()))
                sb.AppendLine("  " + g.Key.PadRight(12) + " " + g.Count());
            MessageBox.Show(_grid.FindForm(), sb.ToString(), "Licence statistics",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExportCsv()
        {
            if (_store.Items.Count == 0) { Info("Nothing to export."); return; }
            using (var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "licenses.csv" })
            {
                if (d.ShowDialog(_grid.FindForm()) != DialogResult.OK) return;
                var sb = new StringBuilder();
                sb.AppendLine("Tool,Vendor,Category,LicenseType,LicenseKey,Account,HardwareId,PurchaseDate,ExpiryDate,Credits,Cost,Status,Url,Notes");
                foreach (var x in _store.Items.OrderBy(x => x.Tool))
                    sb.AppendLine(string.Join(",", new[]
                    {
                        C(x.Tool), C(x.Vendor), C(x.Category), C(x.LicenseType), C(x.LicenseKey), C(x.Account), C(x.HardwareId),
                        x.PurchaseDate?.ToString("yyyy-MM-dd") ?? "", x.ExpiryDate?.ToString("yyyy-MM-dd") ?? "",
                        x.Credits.ToString(), x.Cost.ToString("0.00"), C(x.Status), C(x.Url), C(x.Notes)
                    }));
                File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
                Info("Exported " + _store.Items.Count + " licences.");
                AppLog.Info("Licenses", "Exported " + _store.Items.Count + " licences.");
            }
        }

        private static string C(string s)
        {
            s = s ?? "";
            return s.Contains(",") || s.Contains("\"") || s.Contains("\n") ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        private void ImportCsv()
        {
            using (var d = new OpenFileDialog { Filter = "CSV|*.csv|All files|*.*" })
            {
                if (d.ShowDialog(_grid.FindForm()) != DialogResult.OK) return;
                int added = 0;
                try
                {
                    var lines = File.ReadAllLines(d.FileName);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var f = ParseCsvLine(lines[i]);
                        if (f.Count < 1 || string.IsNullOrWhiteSpace(Get(f, 0))) continue;
                        var rec = new ToolLicense
                        {
                            Tool = Get(f, 0), Vendor = Get(f, 1), Category = Def(Get(f, 2), "Servicing"),
                            LicenseType = Def(Get(f, 3), "Activation"), LicenseKey = Get(f, 4), Account = Get(f, 5),
                            HardwareId = Get(f, 6), Status = Def(Get(f, 11), "Active"), Url = Get(f, 12), Notes = Get(f, 13)
                        };
                        rec.PurchaseDate = ParseDate(Get(f, 7));
                        rec.ExpiryDate = ParseDate(Get(f, 8));
                        int.TryParse(Get(f, 9), out var cr); rec.Credits = cr;
                        decimal.TryParse(Get(f, 10), out var cost); rec.Cost = cost;
                        _store.Items.Add(rec); added++;
                    }
                    _store.Save(); Refilter();
                    Info("Imported " + added + " licence(s).");
                    AppLog.Info("Licenses", "Imported " + added + " licences from " + Path.GetFileName(d.FileName));
                }
                catch (Exception ex) { Info("Import failed: " + ex.Message); }
            }
        }

        private static DateTime? ParseDate(string s) =>
            DateTime.TryParse(s, out var d) ? d.Date : (DateTime?)null;
        private static string Def(string s, string fallback) => string.IsNullOrWhiteSpace(s) ? fallback : s;
        private static string Get(List<string> f, int i) => i < f.Count ? f[i] : "";

        private static List<string> ParseCsvLine(string line)
        {
            var outp = new List<string>();
            if (line == null) return outp;
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') q = true;
                    else if (c == ',') { outp.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }
            outp.Add(sb.ToString());
            return outp;
        }

        private void OpenPortal()
        {
            var rec = Selected;
            if (rec == null || string.IsNullOrWhiteSpace(rec.Url)) { Info("No vendor URL on the selected licence."); return; }
            var url = rec.Url.Trim();
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Info("Could not open URL: " + ex.Message); }
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(LicenseStore.Dir);
                Process.Start(new ProcessStartInfo(LicenseStore.Dir) { UseShellExecute = true });
            }
            catch { }
        }

        private void Info(string m) => _host?.SetStatus(m);

        public void Dispose() { }
    }
}
