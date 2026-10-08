from theme import *

def ev(evt, handler, typ="System.EventHandler"):
    return "{n}.%s += new %s(this.%s);" % (evt, typ, handler)

def mi(name, text, click, shortcut=None):
    p = {"Text": s(text)}
    if shortcut: p["ShortcutKeys"] = shortcut
    return Ctl(name, "ToolStripMenuItem", p, extra=[ev("Click", click)])

def tsb(name, text, click):
    return Ctl(name, "ToolStripButton", {"DisplayStyle": "System.Windows.Forms.ToolStripItemDisplayStyle.Text",
                                         "Text": s(text)}, extra=[ev("Click", click)])

def sep(name, typ="ToolStripSeparator"): return Ctl(name, typ)

def build(out):
    menu = Ctl("menuMain", "MenuStrip", {"ForeColor": INK}, items=[
        Ctl("mnuFile", "ToolStripMenuItem", {"Text": s("&File")}, ddi=[
            mi("mnuOpen", "&Open image…", "mnuOpen_Click", keys("Control", "O")),
            mi("mnuPaste", "&Paste image", "mnuPaste_Click", keys("Control", "V")),
            sep("mnuSep1"),
            mi("mnuExportPng", "Export labelled &PNG…", "mnuExportPng_Click", keys("Control", "S")),
            mi("mnuExportCsv", "Export &CSV checklist…", "mnuExportCsv_Click"),
            sep("mnuSep2"),
            mi("mnuExit", "E&xit", "mnuExit_Click")]),
        Ctl("mnuEdit", "ToolStripMenuItem", {"Text": s("&Edit")}, ddi=[
            mi("mnuUndo", "&Undo", "mnuUndo_Click", keys("Control", "Z")),
            mi("mnuClearManual", "Clear &manual edits", "mnuClearManual_Click"),
            mi("mnuClearCrop", "Clear c&rop", "mnuClearCrop_Click"),
            mi("mnuClearBox", "Clear &box detections", "mnuClearBox_Click")]),
        Ctl("mnuHelp", "ToolStripMenuItem", {"Text": s("&Help")}, ddi=[
            mi("mnuHowTo", "&How to use", "mnuHowTo_Click"),
            mi("mnuAbout", "&About", "mnuAbout_Click")])])

    style = Ctl("tscStyle", "ToolStripComboBox", {"DropDownStyle": enum("ComboBoxStyle.DropDownList"),
                                                  "Items": ["Labels only", "Circles + labels"], "Size": size(150, 28)},
                extra=[ev("SelectedIndexChanged", "tscStyle_SelectedIndexChanged")])
    tools = Ctl("toolMain", "ToolStrip", {"ForeColor": INK, "GripStyle": enum("ToolStripGripStyle.Hidden"),
                                          "ImageScalingSize": size(20, 20)}, items=[
        tsb("tsbOpen", "📂 Open", "mnuOpen_Click"), tsb("tsbDetect", "🔍 Detect", "tsbDetect_Click"), sep("tss1"),
        tsb("tsbPan", "✋ Pan", "tsbPan_Click"), tsb("tsbEdit", "✚ Edit pads", "tsbEdit_Click"),
        tsb("tsbCrop", "⬚ Crop to board", "tsbCrop_Click"), tsb("tsbBox", "▧ Box detect", "tsbBox_Click"), sep("tss2"),
        Ctl("tslStyle", "ToolStripLabel", {"Text": s("Style:")}), style, sep("tss3"),
        tsb("tsbFit", "⤢ Fit", "tsbFit_Click"), tsb("tsbUndo", "↶ Undo", "mnuUndo_Click"),
        tsb("tsbExportPng", "💾 Export PNG", "mnuExportPng_Click"), tsb("tsbExportCsv", "📄 Export CSV", "mnuExportCsv_Click")])

    status = Ctl("statusMain", "StatusStrip", {"ForeColor": INK}, items=[
        Ctl("lblStatus", "ToolStripStatusLabel", {"Spring": TRUE, "Text": s("Ready"),
                                                  "TextAlign": "System.Drawing.ContentAlignment.MiddleLeft"}),
        Ctl("lblCredit", "ToolStripStatusLabel", {"ForeColor": named("DimGray"), "Text": s("Developer: HaKDMoDz™")})])

    hdr = lambda n, t, r: lbl(n, t, cell=(0, r), Font=font("Segoe UI", 9, "Bold"), Margin=pad(0, 10, 0, 2))
    def slider(n, lbltext, mn, mx, val, r):
        l = lbl("lbl" + n, lbltext, cell=(0, r), Margin=pad(0, 4, 0, 0))
        tb = Ctl("tb" + n, "TrackBar", {"AutoSize": FALSE, "Maximum": str(mx), "Minimum": str(mn), "Size": size(270, 28),
                                        "TickStyle": enum("TickStyle.None"), "Value": str(val)}, cell=(0, r + 1),
                 extra=[ev("ValueChanged", "Slider_ValueChanged")])
        return [l, tb]
    def labeled(n, text, ctl, r):
        return flow("flp" + n, [lbl("lbl" + n, text, AutoSize=TRUE, Margin=pad(0, 6, 8, 0), MinimumSize=size(150, 0)), ctl],
                    cell=(0, r), WrapContents=FALSE)
    kids = [hdr("lblHdrDetection", "Detection", 0)]
    kids += slider("MinRadius", "Min pad radius (px)", 2, 40, 4, 1)
    kids += slider("MaxRadius", "Max pad radius (px)", 10, 200, 45, 3)
    kids += slider("Roundness", "Core roundness", 15, 80, 35, 5)
    kids.append(flow("flpPadKinds", [
        chk("chkGold", "Gold pads", Checked=TRUE, CheckState=enum("CheckState.Checked")),
        chk("chkWhite", "White/tinned pads", Checked=TRUE, CheckState=enum("CheckState.Checked"))], cell=(0, 7)))
    kids[-1].kids[0].extra.append(ev("CheckedChanged", "chkPadKind_CheckedChanged"))
    kids[-1].kids[1].extra.append(ev("CheckedChanged", "chkPadKind_CheckedChanged"))
    kids.append(hdr("lblHdrLabels", "Labels & export", 8))
    nl = nud("nudLabelSize", 4, 60, 11, Size=size(70, 24), DecimalPlaces="1", Increment=dec(0.5))
    nl.extra.append(ev("ValueChanged", "nudLabelSize_ValueChanged"))
    kids.append(labeled("LabelSize", "Label size (px)", nl, 9))
    kids.append(labeled("Upscale", "Export upscale ×", nud("nudUpscale", 1, 6, 3, Size=size(70, 24), DecimalPlaces="1",
                                                          Increment=dec(0.5)), 10))
    b1 = btn("btnLabelColour", "Label colour…", Padding=pad(0)); b1.extra.append(ev("Click", "btnLabelColour_Click"))
    b2 = btn("btnResetDefaults", "Reset defaults", Padding=pad(0)); b2.extra.append(ev("Click", "btnResetDefaults_Click"))
    kids.append(flow("flpLabelButtons", [b1, b2], cell=(0, 11)))
    kids.append(hdr("lblHdrPads", "Pads (click to locate)", 12))
    settings = tlp("tlpSettings", [("Percent", 100)], [("AutoSize", None)] * 13, kids,
                   AutoSize=TRUE, Dock=enum("DockStyle.Top"))
    pads = lv("lvPads", [("chPadNo", "#", 40), ("chPadX", "X", 60), ("chPadY", "Y", 60), ("chPadSource", "Source", 90)])
    pads.extra.append(ev("SelectedIndexChanged", "lvPads_SelectedIndexChanged"))
    listhost = panel("pnlListHost", [pads], Dock=enum("DockStyle.Fill"), Padding=pad(0, 4, 0, 0))
    side = panel("pnlSide", [listhost, settings], Dock=enum("DockStyle.Right"), Padding=pad(8), Size=size(300, 780))
    canvas = Ctl("canvas", "TestPointTrigger.CanvasPanel", {"BackColor": col(24, 24, 28), "Dock": enum("DockStyle.Fill")},
                 extra=[ev("Paint", "PaintCanvas", "System.Windows.Forms.PaintEventHandler"),
                        ev("MouseDown", "CanvasDown", "System.Windows.Forms.MouseEventHandler"),
                        ev("MouseMove", "CanvasMove", "System.Windows.Forms.MouseEventHandler"),
                        ev("MouseUp", "CanvasUp", "System.Windows.Forms.MouseEventHandler"),
                        ev("Resize", "canvas_Resize")])
    emit("MainForm", "TestPointTrigger", "Form", {
        "AllowDrop": TRUE, "ClientSize": size(1384, 861), "Font": font("Segoe UI", 9), "KeyPreview": TRUE,
        "MainMenuStrip": "this.menuMain", "StartPosition": enum("FormStartPosition.CenterScreen"),
        "Text": s("Mobile Surgery — PCB Pad Finder")},
        [canvas, side, tools, menu, status], out + r"\MainForm.Designer.cs",
        root_extra=[ev("DragDrop", "MainForm_DragDrop", "System.Windows.Forms.DragEventHandler"),
                    ev("DragEnter", "MainForm_DragEnter", "System.Windows.Forms.DragEventHandler"),
                    ev("KeyDown", "OnKey", "System.Windows.Forms.KeyEventHandler")])
