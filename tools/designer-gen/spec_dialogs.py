from gen import *

def ev(evt, handler, typ="System.EventHandler"):
    return "{n}.%s += new %s(this.%s);" % (evt, typ, handler)

def L(name, text, r):
    return Ctl(name, "Label", {"Anchor": enum("AnchorStyles.Left"), "AutoSize": TRUE, "Margin": pad(0, 6, 6, 6),
                               "Text": s(text)}, cell=(0, r))
def T(name, r, w=320, **kw):
    p = {"Anchor": enum("AnchorStyles.Left"), "Size": size(w, 23)}; p.update(kw)
    return Ctl(name, "TextBox", p, cell=(1, r))
def C(name, r, items, w, droplist=False):
    return Ctl(name, "ComboBox", {"DropDownStyle": enum("ComboBoxStyle.DropDownList" if droplist else "ComboBoxStyle.DropDown"),
                                  "FormattingEnabled": TRUE, "Items": items, "Size": size(w, 23)}, cell=(1, r))
def D(name, r, checkbox=True):
    p = {"Format": enum("DateTimePickerFormat.Short"), "Size": size(150, 23)}
    if checkbox: p["ShowCheckBox"] = TRUE
    return Ctl(name, "DateTimePicker", p, cell=(1, r))
def N(name, r, mx, w):
    return Ctl(name, "NumericUpDown", {"Maximum": dec(mx), "Size": size(w, 23)}, cell=(1, r))
def NOTES(name, r, h):
    return Ctl(name, "TextBox", {"Multiline": TRUE, "ScrollBars": enum("ScrollBars.Vertical"), "Size": size(380, h)}, cell=(1, r))

def editor(cls, title, labw, fields, client, path):
    kids = []
    for r, (label, ctl) in enumerate(fields):
        kids.append(L("lbl" + ctl.name[3:], label, r)); kids.append(ctl)
    fields_tlp = Ctl("tlpFields", "TableLayoutPanel", {"AutoScroll": TRUE, "ColumnCount": "2", "Dock": enum("DockStyle.Fill"),
                                                       "Padding": pad(12), "RowCount": str(len(fields))}, kids,
                     styles={"cols": [("Absolute", labw), ("Percent", 100)], "rows": [("AutoSize", None)] * len(fields)})
    cancel = Ctl("btnCancel", "Button", {"DialogResult": enum("DialogResult.Cancel"), "Size": size(90, 27), "Text": s("Cancel"),
                                         "UseVisualStyleBackColor": TRUE})
    save = Ctl("btnSave", "Button", {"Size": size(90, 27), "Text": s("Save"), "UseVisualStyleBackColor": TRUE},
               extra=[ev("Click", "btnSave_Click")])
    buttons = Ctl("flpButtons", "FlowLayoutPanel", {"Dock": enum("DockStyle.Bottom"), "FlowDirection": enum("FlowDirection.RightToLeft"),
                                                    "Padding": pad(10), "Size": size(client[0], 46)}, [cancel, save])
    emit(cls, "TestPointTrigger.Modules", "Form", {
        "AcceptButton": "this.btnSave", "CancelButton": "this.btnCancel", "ClientSize": size(*client),
        "Font": font("Segoe UI", 9), "FormBorderStyle": enum("FormBorderStyle.FixedDialog"), "MaximizeBox": FALSE,
        "MinimizeBox": FALSE, "StartPosition": enum("FormStartPosition.CenterParent"), "Text": s(title)},
        [fields_tlp, buttons], path, modifier="private")

def build(out):
    M = out + r"\Modules" + "\\"
    f = [("Tool *", T("txtTool", 0)), ("Vendor", T("txtVendor", 1)),
         ("Category", C("cboCategory", 2, ["Forensic", "Servicing", "FRP", "Flashing", "Other"], 200, True)),
         ("Licence type", C("cboType", 3, ["Dongle", "Activation", "Credits", "Subscription", "Perpetual"], 200, True)),
         ("Licence key", T("txtKey", 4)), ("Account", T("txtAccount", 5)), ("Hardware/dongle ID", T("txtHardwareId", 6)),
         ("Purchased", D("dtpPurchased", 7)), ("Expires", D("dtpExpires", 8)),
         ("Credits left", N("nudCredits", 9, 1000000000, 100)), ("Cost", T("txtCost", 10, 120)),
         ("Status", C("cboStatus", 11, ["Active", "Expired", "Suspended"], 200, True)),
         ("Vendor URL", T("txtUrl", 12)), ("Notes", NOTES("txtNotes", 13, 80))]
    editor("LicenseEditForm", "Edit tool / licence", 120, f, (540, 640), M + "LicenseEditForm.Designer.cs")

    image = Ctl("flpImage", "FlowLayoutPanel", {"AutoSize": TRUE, "Margin": pad(0), "WrapContents": FALSE}, [
        Ctl("txtImage", "TextBox", {"Size": size(320, 23)}),
        Ctl("btnBrowse", "Button", {"Size": size(32, 23), "Text": s("…"), "UseVisualStyleBackColor": TRUE},
            extra=[ev("Click", "btnBrowse_Click")])], cell=(1, 14))
    f = [("Date", D("dtpDate", 0, checkbox=False)), ("Brand", T("txtBrand", 1)), ("Model *", T("txtModel", 2)),
         ("Chipset", C("cboChipset", 3, ["Qualcomm", "MediaTek", "Exynos", "Unisoc", "Kirin", "Other"], 220)),
         ("IMEI", T("txtImei", 4)), ("Serial", T("txtSerial", 5)), ("Board ID", T("txtBoardId", 6)),
         ("Fault", T("txtFault", 7)),
         ("Procedure", C("cboProcedure", 8, ["EDL test-point", "BROM (MTK)", "EDL 9008", "FRP removal", "Firmware flash",
                                              "Partition format", "Bootloader unlock", "Diag / repair", "Other"], 220)),
         ("Test point", T("txtTestPoint", 9)),
         ("Outcome", C("cboOutcome", 10, ["Success", "Partial", "Failed", "Abandoned"], 220, True)),
         ("Tools", T("txtTools", 11)), ("Time (min)", N("nudTime", 12, 100000, 90)), ("Technician", T("txtTechnician", 13)),
         ("Image", image), ("Notes", NOTES("txtNotes", 15, 90))]
    editor("RepairEditForm", "Edit repair record", 110, f, (540, 660), M + "RepairEditForm.Designer.cs")

    # Help dialog
    emit("HelpForm", "TestPointTrigger", "Form", {
        "AcceptButton": "this.btnOk", "ClientSize": size(460, 444), "FormBorderStyle": enum("FormBorderStyle.FixedDialog"),
        "MaximizeBox": FALSE, "MinimizeBox": FALSE, "StartPosition": enum("FormStartPosition.CenterParent"),
        "Text": s("Mobile Surgery — Help")}, [
        Ctl("txtBlurb", "TextBox", {"BackColor": sysc("Control"), "BorderStyle": enum("BorderStyle.None"),
                                    "Location": pt(16, 16), "Multiline": TRUE, "ReadOnly": TRUE,
                                    "ScrollBars": enum("ScrollBars.Vertical"), "Size": size(428, 330)}),
        Ctl("lnkReadme", "LinkLabel", {"AutoSize": TRUE, "Location": pt(16, 356), "Text": s("View the full README on GitHub")},
            extra=[ev("Click", "lnkReadme_Click")]),
        Ctl("lblFooter", "Label", {"Font": font("Segoe UI", 8), "ForeColor": sysc("GrayText"), "Location": pt(16, 388),
                                   "Size": size(300, 18), "Text": s("Developer: HaKDMoDz™"),
                                   "TextAlign": "System.Drawing.ContentAlignment.MiddleLeft"}),
        Ctl("btnOk", "Button", {"DialogResult": enum("DialogResult.OK"), "Location": pt(368, 404), "Size": size(76, 28),
                                "Text": s("OK"), "UseVisualStyleBackColor": TRUE})],
        out + r"\HelpForm.Designer.cs", modifier="private")

    # Text-input prompt (was built on the fly inside Prompt.Text)
    emit("PromptForm", "TestPointTrigger.Modules", "Form", {
        "AcceptButton": "this.btnOk", "CancelButton": "this.btnCancel", "ClientSize": size(430, 130),
        "Font": font("Segoe UI", 9), "FormBorderStyle": enum("FormBorderStyle.FixedDialog"), "MaximizeBox": FALSE,
        "MinimizeBox": FALSE, "StartPosition": enum("FormStartPosition.CenterParent"), "Text": s("Input")}, [
        Ctl("lblMessage", "Label", {"AutoSize": TRUE, "Location": pt(12, 12), "MaximumSize": size(406, 0), "Text": s("Message")}),
        Ctl("txtValue", "TextBox", {"Location": pt(12, 50), "Size": size(406, 23)}),
        Ctl("btnOk", "Button", {"DialogResult": enum("DialogResult.OK"), "Location": pt(242, 90), "Size": size(80, 27),
                                "Text": s("OK"), "UseVisualStyleBackColor": TRUE}),
        Ctl("btnCancel", "Button", {"DialogResult": enum("DialogResult.Cancel"), "Location": pt(330, 90), "Size": size(80, 27),
                                    "Text": s("Cancel"), "UseVisualStyleBackColor": TRUE})],
        M + "PromptForm.Designer.cs", modifier="private")
