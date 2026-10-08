from patch import *

def common(F, nsline, body, colorbreak="        public void Activate()"):
    t = load(F)
    t = replace1(t, nsline, nsline + "\r\nusing TestPointTrigger.Modules.Views;")
    t = between(t, "        public Control CreateView(IModuleHost host)", "        private ", crlf(body))
    # drop the AddCol/Btn builders, now replaced by designer columns/buttons
    i = t.find("        private void AddCol(")
    if i >= 0:
        t = between(t, "        private void AddCol(", colorbreak, "")
    return t

def On():
    return '''        private static void On(Button b, Action a) => b.Click += (s, e) => a();

'''

# ── Logs ──
F = r"Modules\LogModule.cs"
t = common(F, "using System.Windows.Forms;", '''        public Control CreateView(IModuleHost host)
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

''' + On())
save(F, t); print("patched", F)

# ── Licences ──
F = r"Modules\LicenseModule.cs"
t = common(F, "using System.Windows.Forms;", '''        public Control CreateView(IModuleHost host)
        {
            _host = host;

            // Layout and styling live in LicenseView.Designer.cs (open it in Design View).
            var v = new LicenseView();
            _search = v.txtSearch;
            _category = v.cboCategory;
            _status = v.cboStatus;
            _grid = v.grdLicenses;
            _count = v.lblCount;

            _category.SelectedIndex = 0;
            _status.SelectedIndex = 0;
            _search.TextChanged += (s, e) => Refilter();
            _category.SelectedIndexChanged += (s, e) => Refilter();
            _status.SelectedIndexChanged += (s, e) => Refilter();
            On(v.btnAdd, Add);
            On(v.btnEdit, EditSelected);
            On(v.btnDuplicate, DuplicateSelected);
            On(v.btnDelete, DeleteSelected);
            On(v.btnCheckExpiries, CheckExpiries);
            On(v.btnStats, ShowStats);
            On(v.btnExportCsv, ExportCsv);
            On(v.btnImportCsv, ImportCsv);
            On(v.btnOpenPortal, OpenPortal);
            On(v.btnOpenFolder, OpenFolder);

            _grid.AutoGenerateColumns = false;   // columns are defined in the designer
            _grid.DataSource = _view;
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditSelected(); };
            _grid.CellFormatting += ColourExpiry;
            return v;
        }

''' + On())
save(F, t); print("patched", F)

# ── Repair DB ──
F = r"Modules\RepairDbModule.cs"
t = common(F, "using System.Windows.Forms;", '''        public Control CreateView(IModuleHost host)
        {
            _host = host;

            // Layout and styling live in RepairDbView.Designer.cs (open it in Design View).
            var v = new RepairDbView();
            _search = v.txtSearch;
            _outcome = v.cboOutcome;
            _grid = v.grdRepairs;
            _count = v.lblCount;

            _outcome.SelectedIndex = 0;
            _search.TextChanged += (s, e) => Refilter();
            _outcome.SelectedIndexChanged += (s, e) => Refilter();
            On(v.btnAdd, Add);
            On(v.btnEdit, EditSelected);
            On(v.btnDuplicate, DuplicateSelected);
            On(v.btnDelete, DeleteSelected);
            On(v.btnStats, ShowStats);
            On(v.btnExportCsv, ExportCsv);
            On(v.btnImportCsv, ImportCsv);
            On(v.btnOpenImage, OpenImage);
            On(v.btnBackupNow, () => { _store.Save(); Info("Backup written."); });
            On(v.btnOpenFolder, OpenFolder);

            _grid.AutoGenerateColumns = false;   // columns are defined in the designer
            _grid.DataSource = _view;
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditSelected(); };
            return v;
        }

''' + On())
save(F, t); print("patched", F)
