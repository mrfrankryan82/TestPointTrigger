// Mobile Surgery - Log viewer / query module
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
using TestPointTrigger.Modules.Views;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// Reviews the single append-only log (AppLog). Reads across sessions,
    /// filters by source/level/text, tails the live file, verifies integrity,
    /// and saves/loads/archives without ever destroying history.
    /// </summary>
    public class LogModule : IModule
    {
        public string Id => "logs";
        public string Title => "Logs";
        public string Description => "Review, search and verify the single append-only log across all sessions.";
        public string Version => "1.0.0";
        public int SortOrder => 90;

        private const int MaxLines = 20000;   // cap the view for very long logs

        private IModuleHost _host;
        private string _path = AppLog.FilePath;   // file currently shown (live or an archive)
        private bool _liveActive;
        private Action<LogEntry> _onEntry;

        private readonly List<LogEntry> _all = new List<LogEntry>();
        private readonly BindingList<LogEntry> _viewList = new BindingList<LogEntry>();

        private DataGridView _grid;
        private TextBox _search;
        private ComboBox _source, _level;
        private CheckBox _tail;
        private Label _summary;

        public Control CreateView(IModuleHost host)
        {
            _host = host;

            // Layout and styling live in LogView.Designer.cs (open it in Design View).
            var v = new LogView();
            _search = v.txtSearch;
            _source = v.cboSource;
            _level = v.cboLevel;
            _tail = v.chkLive;
            _grid = v.grdLog;
            _summary = v.lblSummary;

            _search.TextChanged += (s, e) => ApplyFilter();
            _source.SelectedIndexChanged += (s, e) => ApplyFilter();
            _level.SelectedIndexChanged += (s, e) => ApplyFilter();
            _tail.CheckedChanged += (s, e) => { if (_tail.Checked) EnsureLive(); else DetachLive(); };
            On(v.btnRefresh, Reload);
            On(v.btnVerify, Verify);
            On(v.btnSaveView, SaveView);
            On(v.btnLoadFile, LoadFile);
            On(v.btnOpenFolder, OpenFolder);
            On(v.btnArchiveClear, ArchiveClear);

            _grid.AutoGenerateColumns = false;   // columns are defined in the designer
            _grid.DataSource = _viewList;
            return v;
        }

        private static void On(Button b, Action a) => b.Click += (s, e) => a();

        public void Activate()
        {
            Reload();
            if (_tail.Checked) EnsureLive();
            _host?.SetStatus("Logs: " + _path);
        }

        // Detach the tail when hidden so a background module costs nothing;
        // the file keeps recording via AppLog regardless.
        public void Deactivate() => DetachLive();

        // ─────────────────────────── Load / filter ───────────────────────────

        private void Reload()
        {
            _all.Clear();
            try
            {
                if (File.Exists(_path))
                {
                    // Share ReadWrite: the live file may be appended to while we read.
                    using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            var e = AppLog.Parse(line);
                            if (e != null) _all.Add(e);
                        }
                    }
                    if (_all.Count > MaxLines) _all.RemoveRange(0, _all.Count - MaxLines);
                }
            }
            catch (Exception ex)
            {
                _host?.SetStatus("Could not read log: " + ex.Message);
            }

            RebuildFilterLists();
            ApplyFilter();
        }

        private void RebuildFilterLists()
        {
            FillCombo(_source, _all.Select(e => e.Source));
            FillCombo(_level, _all.Select(e => e.Level));
        }

        private static void FillCombo(ComboBox c, IEnumerable<string> values)
        {
            var keep = c.SelectedItem as string;
            var items = new List<string> { "(all)" };
            items.AddRange(values.Where(v => !string.IsNullOrEmpty(v))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
            c.Items.Clear();
            c.Items.AddRange(items.Cast<object>().ToArray());
            var idx = keep != null ? items.FindIndex(v => string.Equals(v, keep, StringComparison.OrdinalIgnoreCase)) : 0;
            c.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private bool Passes(LogEntry e)
        {
            if (_source.SelectedIndex > 0 &&
                !string.Equals(e.Source, (string)_source.SelectedItem, StringComparison.OrdinalIgnoreCase)) return false;
            if (_level.SelectedIndex > 0 &&
                !string.Equals(e.Level, (string)_level.SelectedItem, StringComparison.OrdinalIgnoreCase)) return false;
            var term = _search.Text.Trim();
            if (term.Length > 0 && (e.Raw ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private void ApplyFilter()
        {
            _viewList.RaiseListChangedEvents = false;
            _viewList.Clear();
            foreach (var e in _all.Where(Passes)) _viewList.Add(e);
            _viewList.RaiseListChangedEvents = true;
            _viewList.ResetBindings();
            ScrollEnd();
            UpdateSummary();
        }

        private void ScrollEnd()
        {
            if (_grid.Rows.Count > 0)
                try { _grid.FirstDisplayedScrollingRowIndex = _grid.Rows.Count - 1; } catch { }
        }

        private void UpdateSummary()
        {
            string size = "-";
            try { if (File.Exists(_path)) size = (new FileInfo(_path).Length / 1024.0).ToString("0.0") + " KB"; }
            catch { }
            _summary.Text = _viewList.Count + " of " + _all.Count + " lines \u00b7 " + size + " \u00b7 " + _path;
        }

        // ─────────────────────────── Live tail ───────────────────────────

        private void EnsureLive()
        {
            if (_liveActive) return;
            if (!string.Equals(_path, AppLog.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                _tail.Checked = false;   // can't tail an archived file
                return;
            }
            _onEntry = OnLiveEntry;
            AppLog.Entry += _onEntry;
            _liveActive = true;
        }

        private void DetachLive()
        {
            if (_onEntry != null) { AppLog.Entry -= _onEntry; _onEntry = null; }
            _liveActive = false;
        }

        private void OnLiveEntry(LogEntry e)
        {
            var g = _grid;
            if (g == null || g.IsDisposed || !g.IsHandleCreated) return;
            try
            {
                g.BeginInvoke((Action)(() =>
                {
                    _all.Add(e);
                    if (_all.Count > MaxLines) _all.RemoveAt(0);
                    if (Passes(e)) { _viewList.Add(e); ScrollEnd(); }
                    UpdateSummary();
                }));
            }
            catch { }
        }

        // ─────────────────────────── Verify ───────────────────────────

        private void Verify()
        {
            if (!File.Exists(_path)) { Msg("Log file does not exist:\n" + _path); return; }

            long bytes = 0; int lines = 0, parsed = 0, malformed = 0, regressions = 0;
            DateTime first = DateTime.MinValue, last = DateTime.MinValue, prev = DateTime.MinValue;
            try
            {
                bytes = new FileInfo(_path).Length;
                using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        lines++;
                        var e = AppLog.Parse(line);
                        if (e != null && e.Time != default && e.Source.Length > 0)
                        {
                            parsed++;
                            if (first == DateTime.MinValue) first = e.Time;
                            last = e.Time;
                            if (prev != DateTime.MinValue && e.Time < prev) regressions++;
                            prev = e.Time;
                        }
                        else malformed++;
                    }
                }
            }
            catch (Exception ex) { Msg("Verify failed to read the file:\n" + ex.Message); return; }

            var sb = new StringBuilder();
            sb.AppendLine("File:     " + _path);
            sb.AppendLine("Size:     " + (bytes / 1024.0).ToString("0.0") + " KB");
            sb.AppendLine("Lines:    " + lines);
            sb.AppendLine("Parsed:   " + parsed);
            sb.AppendLine("Malformed:" + malformed + (malformed == 0 ? "  (clean)" : "  <-- check these"));
            sb.AppendLine("Range:    " + (first == DateTime.MinValue ? "-" :
                          first.ToString("yyyy-MM-dd HH:mm:ss") + "  to  " + last.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine("Time order:" + (regressions == 0
                          ? " monotonic (OK)"
                          : " " + regressions + " out-of-order line(s) - clock change or edit"));
            sb.AppendLine();
            sb.AppendLine(malformed == 0 && regressions == 0
                ? "Integrity check passed."
                : "Integrity check found anomalies - see above.");

            AppLog.Info("Logs", "Verify: " + lines + " lines, " + parsed + " parsed, " +
                        malformed + " malformed, " + regressions + " out-of-order.");
            Msg(sb.ToString(), "Verify log");
        }

        // ─────────────────────────── Save / load / archive ───────────────────────────

        private void SaveView()
        {
            if (_viewList.Count == 0) { _host?.SetStatus("Nothing in the current view to save."); return; }
            using (var d = new SaveFileDialog { Filter = "Text log|*.txt|CSV|*.csv", FileName = "log-view.txt" })
            {
                if (d.ShowDialog(_grid.FindForm()) != DialogResult.OK) return;
                try
                {
                    var sb = new StringBuilder();
                    bool csv = d.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
                    if (csv)
                    {
                        sb.AppendLine("Time,Source,Level,Message");
                        foreach (var e in _viewList)
                            sb.AppendLine(string.Join(",", new[]
                            {
                                e.Time.ToString("yyyy-MM-dd HH:mm:ss"), Csv(e.Source), Csv(e.Level), Csv(e.Message)
                            }));
                    }
                    else
                    {
                        foreach (var e in _viewList) sb.AppendLine(e.Raw);
                    }
                    File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
                    _host?.SetStatus("Saved " + _viewList.Count + " lines to " + d.FileName);
                }
                catch (Exception ex) { _host?.SetStatus("Save failed: " + ex.Message); }
            }
        }

        private static string Csv(string s)
        {
            s = s ?? "";
            return s.Contains(",") || s.Contains("\"") ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        private void LoadFile()
        {
            using (var d = new OpenFileDialog { Filter = "Log files|*.log;*.txt|All files|*.*" })
            {
                try { d.InitialDirectory = AppLog.Dir; } catch { }
                if (d.ShowDialog(_grid.FindForm()) != DialogResult.OK) return;
                DetachLive();
                _tail.Checked = false;
                _path = d.FileName;
                Reload();
                _host?.SetStatus("Viewing " + _path + " (live tail off for archived files).");
            }
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(AppLog.Dir);
                Process.Start(new ProcessStartInfo(AppLog.Dir) { UseShellExecute = true });
            }
            catch { }
        }

        private void ArchiveClear()
        {
            if (!string.Equals(_path, AppLog.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                _host?.SetStatus("Archive & clear only applies to the live log. Load the live log first.");
                return;
            }
            if (MessageBox.Show(_grid.FindForm(),
                    "Copy the current log to a timestamped archive, then start a fresh empty log?\n\n" +
                    "Nothing is deleted - the archive is kept in the logs folder.",
                    "Archive & clear", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            try
            {
                var archive = Path.Combine(AppLog.Dir, "archive-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                if (File.Exists(AppLog.FilePath)) File.Copy(AppLog.FilePath, archive, true);
                File.WriteAllText(AppLog.FilePath, string.Empty, Encoding.UTF8);
                AppLog.Info("Logs", "Log archived to " + Path.GetFileName(archive) + " and cleared.");
                Reload();
                _host?.SetStatus("Archived to " + archive);
            }
            catch (Exception ex) { _host?.SetStatus("Archive failed: " + ex.Message); }
        }

        private void Msg(string text, string title = "Logs") =>
            MessageBox.Show(_grid.FindForm(), text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

        public void Dispose() => DetachLive();
    }
}
