from theme import *

def b2(name, text):   # these modules used tighter buttons
    return btn(name, text, Margin=pad(3, 2, 0, 2), Padding=pad(0))

def fmt_local(name, fmt, null_empty=False):
    loc = ["System.Windows.Forms.DataGridViewCellStyle %s = new System.Windows.Forms.DataGridViewCellStyle();" % name,
           "%s.Format = %s;" % (name, s(fmt))]
    if null_empty: loc.append('%s.NullValue = "";' % name)
    return loc

def grid(name, prefix, columns, extra_locals, cell, **kw):
    gl, gp = grid_locals(prefix)
    gp.update({"AllowUserToAddRows": FALSE, "AllowUserToDeleteRows": FALSE, "AllowUserToResizeRows": FALSE,
               "Dock": enum("DockStyle.Fill"), "ReadOnly": TRUE,
               "SelectionMode": enum("DataGridViewSelectionMode.FullRowSelect")})
    gp.update(kw)
    return Ctl(name, "DataGridView", gp, cell=cell, columns=columns, extra=["{n}.RowTemplate.Height = 30;"]), extra_locals + gl

def page(cls, bar_kids, g, locs, path, count_name):
    bar = flow("flpBar", bar_kids, cell=(0, 0))
    count = lbl(count_name, "", cell=(0, 2), fore=DIMGRAY, Margin=pad(2, 4, 0, 0))
    root = tlp("tlpRoot", [("Percent", 100)], [("AutoSize", None), ("Percent", 100), ("AutoSize", None)],
               [bar, g, count], Padding=pad(8))
    emit(cls, "TestPointTrigger.Modules.Views", "UserControl", view_root(), [root], path, locals_=locs)

def build(out):
    V = out + r"\Modules\Views" + "\\"
    # ── Logs ──
    g, locs = grid("grdLog", "logGrid", [
        gcol("colTime", "Time", "Time", 140, fmt_local="logTimeFormat"),
        gcol("colSource", "Source", "Source", 90), gcol("colLevel", "Level", "Level", 70),
        gcol("colMessage", "Message", "Message", fill=True)],
        fmt_local("logTimeFormat", "yyyy-MM-dd HH:mm:ss"), (0, 1), Font=font("Consolas", 9))
    page("LogView", [
        lbl("lblSearch", "Search:", Margin=pad(0, 6, 4, 0)), txt("txtSearch", Size=size(200, 24)),
        lbl("lblSource", "Source:", Margin=pad(8, 6, 4, 0)), cbo("cboSource", Size=size(120, 25)),
        lbl("lblLevel", "Level:", Margin=pad(8, 6, 4, 0)), cbo("cboLevel", Size=size(100, 25)),
        chk("chkLive", "Live", Checked=TRUE, CheckState=enum("CheckState.Checked"), Margin=pad(8, 6, 8, 0)),
        b2("btnRefresh", "Refresh"), b2("btnVerify", "Verify"), b2("btnSaveView", "Save view…"),
        b2("btnLoadFile", "Load file…"), b2("btnOpenFolder", "Open folder"), b2("btnArchiveClear", "Archive & clear"),
    ], g, locs, V + "LogView.Designer.cs", "lblSummary")

    # ── Tool licences ──
    g, locs = grid("grdLicenses", "licGrid", [
        gcol("colTool", "Tool", "Tool", 130), gcol("colVendor", "Vendor", "Vendor", 100),
        gcol("colCategory", "Category", "Category", 80), gcol("colType", "Type", "LicenseType", 90),
        gcol("colStatus", "Status", "Status", 70), gcol("colCredits", "Credits", "Credits", 60),
        gcol("colExpires", "Expires", "ExpiryDate", 90, fmt_local="licExpiryFormat"),
        gcol("colHardwareId", "Dongle/HW ID", "HardwareId", 120), gcol("colNotes", "Notes", "Notes", 180)],
        fmt_local("licExpiryFormat", "yyyy-MM-dd", null_empty=True), (0, 1), MultiSelect=FALSE)
    page("LicenseView", [
        lbl("lblSearch", "Search:", Margin=pad(0, 6, 4, 0)), txt("txtSearch", Size=size(180, 24)),
        cbo("cboCategory", ["All categories", "Forensic", "Servicing", "FRP", "Flashing", "Other"], Size=size(120, 25)),
        cbo("cboStatus", ["All statuses", "Active", "Expired", "Suspended"], Size=size(110, 25)),
        b2("btnAdd", "Add"), b2("btnEdit", "Edit"), b2("btnDuplicate", "Duplicate"), b2("btnDelete", "Delete"),
        b2("btnCheckExpiries", "Check expiries"), b2("btnStats", "Stats"),
        b2("btnExportCsv", "Export CSV"), b2("btnImportCsv", "Import CSV"),
        b2("btnOpenPortal", "Open portal"), b2("btnOpenFolder", "Open folder"),
    ], g, locs, V + "LicenseView.Designer.cs", "lblCount")

    # ── Repair DB ──
    g, locs = grid("grdRepairs", "repGrid", [
        gcol("colDate", "Date", "Date", 90, fmt_local="repDateFormat"), gcol("colBrand", "Brand", "Brand", 90),
        gcol("colModel", "Model", "Model", 140), gcol("colChipset", "Chipset", "Chipset", 90),
        gcol("colProcedure", "Procedure", "Procedure", 150), gcol("colOutcome", "Outcome", "Outcome", 80),
        gcol("colTestPoint", "Test point", "TestPoint", 130), gcol("colMinutes", "Min", "TimeMinutes", 50),
        gcol("colNotes", "Notes", "Notes", 220)],
        fmt_local("repDateFormat", "yyyy-MM-dd"), (0, 1), MultiSelect=FALSE)
    page("RepairDbView", [
        lbl("lblSearch", "Search:", Margin=pad(0, 6, 4, 0)), txt("txtSearch", Size=size(200, 24)),
        cbo("cboOutcome", ["All outcomes", "Success", "Partial", "Failed", "Abandoned"], Size=size(120, 25)),
        b2("btnAdd", "Add"), b2("btnEdit", "Edit"), b2("btnDuplicate", "Duplicate"), b2("btnDelete", "Delete"),
        b2("btnStats", "Stats"), b2("btnExportCsv", "Export CSV"), b2("btnImportCsv", "Import CSV"),
        b2("btnOpenImage", "Open image"), b2("btnBackupNow", "Backup now"), b2("btnOpenFolder", "Open folder"),
    ], g, locs, V + "RepairDbView.Designer.cs", "lblCount")
