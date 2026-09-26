// TestPoint Trigger - Completed repairs database module
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
    /// Database of completed boards/phones. Records live in a single JSON file
    /// (RepairStore) with rolling backups; this module is the CRUD + search +
    /// stats + import/export front end.
    /// </summary>
    public class RepairDbModule : IModule
    {
        public string Id => "repairdb";
        public string Title => "Repair DB";
        public string Description => "Database of completed boards/phones — add, search, stats, export.";
        public string Version => "1.0.0";
        public int SortOrder => 30;

        private IModuleHost _host;
        private readonly RepairStore _store = new RepairStore();
        private readonly BindingList<RepairRecord> _view = new BindingList<RepairRecord>();
        private DataGridView _grid;
        private TextBox _search;
        private ComboBox _outcome;
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
            _search = new TextBox { Width = 200 };
            _search.TextChanged += (s, e) => Refilter();
            _outcome = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            _outcome.Items.AddRange(new object[] { "All outcomes", "Success", "Partial", "Failed", "Abandoned" });
            _outcome.SelectedIndex = 0;
            _outcome.SelectedIndexChanged += (s, e) => Refilter();
            bar.Controls.AddRange(new Control[]
            {
                new Label { Text = "Search:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) }, _search, _outcome,
                Btn("Add", Add), Btn("Edit", EditSelected), Btn("Duplicate", DuplicateSelected),
                Btn("Delete", DeleteSelected), Btn("Stats", ShowStats),
                Btn("Export CSV", ExportCsv), Btn("Import CSV", ImportCsv),
                Btn("Open image", OpenImage), Btn("Backup now", () => { _store.Save(); Info("Backup written."); }),
                Btn("Open folder", OpenFolder)
            });

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false, AutoGenerateColumns = false, AllowUserToResizeRows = false
            };
            AddCol("Date", "Date", 90);
            AddCol("Brand", "Brand", 90);
            AddCol("Model", "Model", 140);
            AddCol("Chipset", "Chipset", 90);
            AddCol("Procedure", "Procedure", 150);
            AddCol("Outcome", "Outcome", 80);
            AddCol("TestPoint", "Test point", 130);
            AddCol("TimeMinutes", "Min", 50);
            AddCol("Notes", "Notes", 220);
            _grid.DataSource = _view;
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditSelected(); };

            _count = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(2, 4, 0, 0) };

            root.Controls.Add(bar, 0, 0);
            root.Controls.Add(_grid, 0, 1);
            root.Controls.Add(_count, 0, 2);
            return root;
        }

        private void AddCol(string prop, string header, int w)
        {
            var col = new DataGridViewTextBoxColumn
            {
                DataPropertyName = prop, HeaderText = header, Width = w,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };
            if (prop == "Date") col.DefaultCellStyle.Format = "yyyy-MM-dd";
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
            _host?.SetStatus("Repair DB: " + _store.Records.Count + " records \u00b7 " + RepairStore.FilePath);
        }

        public void Deactivate() { }

        private void Refilter()
        {
            IEnumerable<RepairRecord> q = _store.Records;

            var term = _search.Text.Trim();
            if (term.Length > 0)
                q = q.Where(r => (r.Brand + " " + r.Model + " " + r.Chipset + " " + r.Fault + " " +
                                  r.Procedure + " " + r.TestPoint + " " + r.Imei + " " + r.Serial + " " +
                                  r.BoardId + " " + r.Notes).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

            if (_outcome.SelectedIndex > 0)
                q = q.Where(r => string.Equals(r.Outcome, (string)_outcome.SelectedItem, StringComparison.OrdinalIgnoreCase));

            _view.RaiseListChangedEvents = false;
            _view.Clear();
            foreach (var r in q.OrderByDescending(r => r.Date).ThenByDescending(r => r.UpdatedUtc)) _view.Add(r);
            _view.RaiseListChangedEvents = true;
            _view.ResetBindings();

            _count.Text = _view.Count + " shown \u00b7 " + _store.Records.Count + " total";
        }

        private RepairRecord Selected => _grid.CurrentRow?.DataBoundItem as RepairRecord;

        private void Add()
        {
            var rec = new RepairRecord();
            using (var f = new RepairEditForm(rec, true))
                if (f.ShowDialog(_grid.FindForm()) == DialogResult.OK)
                {
                    _store.Records.Add(rec);
                    _store.Save();
                    Refilter();
                    AppLog.Info("RepairDb", "Added " + rec.Brand + " " + rec.Model +
                                " [" + rec.Outcome + "] " + rec.Procedure);
                }
        }

        private void EditSelected()
        {
            var rec = Selected;
            if (rec == null) { Info("Select a record first."); return; }
            using (var f = new RepairEditForm(rec, false))
                if (f.ShowDialog(_grid.FindForm()) == DialogResult.OK)
                {
                    _store.Save();
                    Refilter();
                    AppLog.Info("RepairDb", "Edited " + rec.Brand + " " + rec.Model);
                }
        }

        private void DuplicateSelected()
        {
            var s = Selected;
            if (s == null) { Info("Select a record to duplicate."); return; }
            var copy = new RepairRecord
            {
                Date = DateTime.Today, Brand = s.Brand, Model = s.Model, Chipset = s.Chipset,
                BoardId = s.BoardId, Fault = s.Fault, Procedure = s.Procedure, TestPoint = s.TestPoint,
                Tools = s.Tools, Technician = s.Technician, Outcome = "Success"
            };
            using (var f = new RepairEditForm(copy, true))
                if (f.ShowDialog(_grid.FindForm()) == DialogResult.OK)
                {
                    _store.Records.Add(copy);
                    _store.Save();
                    Refilter();
                }
        }

        private void DeleteSelected()
        {
            var rec = Selected;
            if (rec == null) { Info("Select a record to delete."); return; }
            if (MessageBox.Show(_grid.FindForm(),
                    "Delete this record?\n\n" + rec.Date.ToString("d") + "  " + rec.Brand + " " + rec.Model,
                    "Repair DB", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _store.Records.Remove(rec);
            _store.Save();
            Refilter();
            AppLog.Warn("RepairDb", "Deleted " + rec.Brand + " " + rec.Model);
        }

        private void ShowStats()
        {
            var r = _store.Records;
            if (r.Count == 0) { Info("No records yet."); return; }

            int total = r.Count;
            int ok = r.Count(x => (x.Outcome ?? "").Equals("Success", StringComparison.OrdinalIgnoreCase));

            var sb = new StringBuilder();
            sb.AppendLine("Total jobs: " + total);
            sb.AppendLine("Success rate: " + (ok * 100.0 / total).ToString("0.0") + "%  (" + ok + "/" + total + ")");
            sb.AppendLine();
            sb.AppendLine("By outcome:");
            foreach (var g in r.GroupBy(x => string.IsNullOrEmpty(x.Outcome) ? "(blank)" : x.Outcome)
                               .OrderByDescending(g => g.Count()))
                sb.AppendLine("  " + g.Key.PadRight(10) + " " + g.Count());
            sb.AppendLine();
            sb.AppendLine("Top models:");
            foreach (var g in r.Where(x => !string.IsNullOrWhiteSpace(x.Model))
                               .GroupBy(x => (x.Brand + " " + x.Model).Trim())
                               .OrderByDescending(g => g.Count()).Take(8))
                sb.AppendLine("  " + g.Count().ToString().PadLeft(3) + "  " + g.Key);
            sb.AppendLine();
            sb.AppendLine("Top chipsets:");
            foreach (var g in r.Where(x => !string.IsNullOrWhiteSpace(x.Chipset))
                               .GroupBy(x => x.Chipset).OrderByDescending(g => g.Count()))
                sb.AppendLine("  " + g.Count().ToString().PadLeft(3) + "  " + g.Key);
            sb.AppendLine();
            sb.AppendLine("Top procedures:");
            foreach (var g in r.Where(x => !string.IsNullOrWhiteSpace(x.Procedure))
                               .GroupBy(x => x.Procedure).OrderByDescending(g => g.Count()).Take(8))
                sb.AppendLine("  " + g.Count().ToString().PadLeft(3) + "  " + g.Key);
            var timed = r.Where(x => x.TimeMinutes > 0).Select(x => (double)x.TimeMinutes).ToList();
            sb.AppendLine();
            sb.AppendLine("Avg time (where recorded): " +
                (timed.Count > 0 ? timed.Average().ToString("0") : "-") + " min");

            MessageBox.Show(_grid.FindForm(), sb.ToString(), "Repair statistics",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExportCsv()
        {
            if (_store.Records.Count == 0) { Info("Nothing to export."); return; }
            using (var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "repairs.csv" })
            {
                if (d.ShowDialog(_grid.FindForm()) != DialogResult.OK) return;
                var sb = new StringBuilder();
                sb.AppendLine("Date,Brand,Model,Chipset,IMEI,Serial,BoardId,Fault,Procedure,TestPoint,Outcome,Tools,TimeMinutes,Technician,ImagePath,Notes");
                foreach (var x in _store.Records.OrderBy(x => x.Date))
                    sb.AppendLine(string.Join(",", new[]
                    {
                        x.Date.ToString("yyyy-MM-dd"), C(x.Brand), C(x.Model), C(x.Chipset), C(x.Imei), C(x.Serial),
                        C(x.BoardId), C(x.Fault), C(x.Procedure), C(x.TestPoint), C(x.Outcome), C(x.Tools),
                        x.TimeMinutes.ToString(), C(x.Technician), C(x.ImagePath), C(x.Notes)
                    }));
                File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
                Info("Exported " + _store.Records.Count + " records.");
                AppLog.Info("RepairDb", "Exported " + _store.Records.Count + " records to " + Path.GetFileName(d.FileName));
            }
        }

        private static string C(string s)
        {
            s = s ?? "";
            return s.Contains(",") || s.Contains("\"") || s.Contains("\n")
                ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
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
                    for (int i = 1; i < lines.Length; i++)   // skip header row
                    {
                        var f = ParseCsvLine(lines[i]);
                        if (f.Count < 3 || string.IsNullOrWhiteSpace(Get(f, 2))) continue;   // needs a model
                        DateTime.TryParse(Get(f, 0), out var date);
                        var rec = new RepairRecord
                        {
                            Date = date == default ? DateTime.Today : date,
                            Brand = Get(f, 1), Model = Get(f, 2), Chipset = Get(f, 3),
                            Imei = Get(f, 4), Serial = Get(f, 5), BoardId = Get(f, 6),
                            Fault = Get(f, 7), Procedure = Get(f, 8), TestPoint = Get(f, 9),
                            Outcome = string.IsNullOrEmpty(Get(f, 10)) ? "Success" : Get(f, 10),
                            Tools = Get(f, 11), Technician = Get(f, 13), ImagePath = Get(f, 14), Notes = Get(f, 15)
                        };
                        int.TryParse(Get(f, 12), out var min);
                        rec.TimeMinutes = min;
                        _store.Records.Add(rec);
                        added++;
                    }
                    _store.Save();
                    Refilter();
                    Info("Imported " + added + " record(s).");
                    AppLog.Info("RepairDb", "Imported " + added + " records from " + Path.GetFileName(d.FileName));
                }
                catch (Exception ex) { Info("Import failed: " + ex.Message); }
            }
        }

        private static string Get(List<string> f, int i) => i < f.Count ? f[i] : "";

        /// <summary>Minimal RFC-4180-ish CSV line parser (handles quoted commas and "" escapes).</summary>
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
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else q = false;
                    }
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

        private void OpenImage()
        {
            var rec = Selected;
            if (rec == null || string.IsNullOrWhiteSpace(rec.ImagePath)) { Info("No image on the selected record."); return; }
            if (!File.Exists(rec.ImagePath)) { Info("Image file not found: " + rec.ImagePath); return; }
            try { Process.Start(new ProcessStartInfo(rec.ImagePath) { UseShellExecute = true }); }
            catch (Exception ex) { Info("Could not open image: " + ex.Message); }
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(RepairStore.Dir);
                Process.Start(new ProcessStartInfo(RepairStore.Dir) { UseShellExecute = true });
            }
            catch { }
        }

        private void Info(string m) => _host?.SetStatus(m);

        public void Dispose() { }
    }
}
