from theme import *

def build(out):
    M7 = lambda l=0, r=0: pad(l, 7, r, 0)
    def cmd(name, text, command):
        b = btn(name, text); b.props.append(("Tag", s(command))); return b
    def tb(name, w, text=None):
        t = txt(name, Size=size(w, 24))
        if text: t.props.append(("Text", s(text)))
        return t
    def grp(name, title, inner):
        g = group(name, title, [inner], AutoSize=TRUE, Padding=pad(6), Size=size(900, 60))
        return g
    def gflow(name, kids):
        return flow(name, kids)

    topbar = flow("flpTopBar", [
        lbl("lblJigPort", "Jig port:", Margin=pad(0, 7, 4, 0)),
        cbo("cboPorts", Size=size(330, 25)),
        btn("btnRescanPorts", "Rescan ports"),
        btn("btnAutoDetect", "Auto-detect Mega"),
        btn("btnConnect", "Connect"),
        lbl("lblBaud", "115200 8N1", fore=DIMGRAY, Margin=pad(8, 7, 0, 0)),
        lbl("lblLinkStatus", "Not connected", Margin=pad(8, 7, 0, 0)),
    ], cell=(0, 0))

    # ── Wiring tab ──
    steps = lv("lvSteps", [("chStepState", "", 26), ("chStepTitle", "Step", 300), ("chStepTest", "Test", 52)],
               cell=(0, 0), MultiSelect=FALSE)
    steptext = txt("txtStepText", cell=(0, 1), readonly=True, Dock=enum("DockStyle.Fill"), Font=font("Segoe UI", 9),
                   Multiline=TRUE, ScrollBars=enum("ScrollBars.Vertical"))
    wbtns = flow("flpWiringButtons", [
        btn("btnRunCheck", "Run check"), btn("btnDrive", "Drive (set outputs)"),
        btn("btnMarkOk", "Mark OK"), btn("btnMarkFailed", "Mark failed"),
        btn("btnRunAll", "Run all (guided)"), btn("btnResetSteps", "Reset"), btn("btnSaveReport", "Save report"),
    ], cell=(0, 2))
    right = tlp("tlpWiringRight", [("Percent", 100)], [("Percent", 42), ("Percent", 58), ("AutoSize", None)],
                [steps, steptext, wbtns])
    right.slot = "Panel2"
    diagram = Ctl("pnlDiagram", "TestPointTrigger.Modules.WiringPanel",
                  {"Dock": enum("DockStyle.Fill"), "AutoScroll": TRUE, "BackColor": named("White")}, slot="Panel1")
    split = Ctl("splWiring", "SplitContainer",
                {"BackColor": BG, "Dock": enum("DockStyle.Fill"), "ForeColor": TEXT, "Size": size(984, 470),
                 "SplitterDistance": "700"}, [diagram, right])
    tabWiring = tabpage("tabWiring", "Wiring", [split])

    # ── Jig tab ──
    g1 = grp("grpBootModes", "Boot modes (hover for keys)",
             gflow("flpBootModes", [cmd("btnModeOff", "OFF (all safe)", "off")]))
    g2 = grp("grpChecks", "Wiring checks", gflow("flpChecks", [
        cmd("btnCmdIdent", "ident", "ident"), cmd("btnCmdPins", "pins", "pins"), cmd("btnCmdSense", "sense", "sense"),
        cmd("btnCmdSelftest", "selftest", "selftest"), cmd("btnCmdStatus", "status", "status"), cmd("btnCmdHelp", "help", "help")]))
    g3 = grp("grpPower", "Power and USB", gflow("flpPower", [
        cmd("btnUsbPc", "usb pc", "usb pc"), cmd("btnUsbShield", "usb shield", "usb shield"), cmd("btnUsbOff", "usb off", "usb off"),
        cmd("btnVccOn", "vcc on", "vcc on"), cmd("btnVccOff", "vcc off", "vcc off"),
        cmd("btnBtempOn", "btemp on", "btemp on"), cmd("btnBtempOff", "btemp off", "btemp off")]))
    keys_ = []
    for k, nm in (("tp", "Tp"), ("up", "Up"), ("dn", "Dn"), ("pwr", "Pwr")):
        keys_.append(cmd("btnKey%sHold" % nm, k + " hold", "key %s hold" % k))
        keys_.append(cmd("btnKey%sRel" % nm, k + " rel", "key %s rel" % k))
    g4 = grp("grpPads", "Manual pad lines (open-drain)", gflow("flpPads", keys_))
    g5 = grp("grpUart", "Phone UART (Serial1)", gflow("flpUart", [
        cmd("btnUartOn", "uart on", "uart on"), cmd("btnUartOff", "uart off", "uart off"),
        cmd("btnUartBaud", "baud 115200", "uart baud 115200"), cmd("btnUartLoop", "loop test", "uart loop"),
        cmd("btnUartListen", "listen 3 s", "uart listen 3000"),
        tb("txtUartSend", 220), btn("btnUartSend", "send"),
        lbl("lblTrigger", "release lines on:", Margin=pad(12, 7, 2, 0)), tb("txtTrigger", 200),
        btn("btnSetTrigger", "set trigger"), cmd("btnClearTrigger", "clear", "trigger off")]))
    g6 = grp("grpAdbShield", "ADB through the host shield", gflow("flpAdbShield", [
        tb("txtAdbCmd", 260, "getprop ro.product.model"), btn("btnAdbViaShield", "adb (via shield)"),
        lbl("lblAdbHint", "runs 'normal' first so the phone boots and the USB path goes to the shield",
            fore=DIMGRAY, Margin=pad(8, 7, 0, 0))]))
    g7 = grp("grpTune", "Tune a mode (RAM only)", gflow("flpTune", [
        cbo("cboTuneMode", Size=size(110, 25)),
        lbl("lblMask", "mask", Margin=pad(6, 7, 0, 0)), tb("txtMask", 50, "0x01"),
        lbl("lblUsbDelay", "usb delay ms", Margin=pad(6, 7, 0, 0)), tb("txtUsbDelay", 60, "400"),
        lbl("lblHold", "hold ms", Margin=pad(6, 7, 0, 0)), tb("txtHold", 60, "2500"),
        btn("btnTuneApply", "apply")]))
    g8 = grp("grpRaw", "Any command", gflow("flpRaw", [tb("txtRawCmd", 380), btn("btnRawSend", "send")]))
    jigflow = flow("flpJig", [g1, g2, g3, g4, g5, g6, g7, g8], AutoSize=FALSE, AutoScroll=TRUE,
                   FlowDirection=enum("FlowDirection.TopDown"), WrapContents=FALSE)
    tabJig = tabpage("tabJig", "Jig", [jigflow])

    # ── Device tab ──
    devbar = flow("flpDeviceBar", [
        chk("chkAutoProfile", "Auto-profile on connect", Checked=TRUE, CheckState=enum("CheckState.Checked"),
            Margin=pad(3, 7, 10, 0)),
        lbl("lblAttached", "Attached:", Margin=pad(0, 7, 3, 0)),
        cbo("cboAttached", Size=size(200, 25)),
        btn("btnDetectNow", "Detect now"), btn("btnRescanForce", "Rescan (force)"),
        btn("btnFindImage", "Find model image"), btn("btnSetImage", "Set image..."), btn("btnForgetDevice", "Forget device"),
    ], cell=(0, 0))
    ident = lv("lvIdentity", [("chPropName", "Property", 150), ("chPropValue", "Value", 330)], cell=(0, 0),
               GridLines=TRUE, HideSelection=TRUE)
    modes = lv("lvModes", [("chModeName", "Mode", 80), ("chModeKeys", "Keys", 110), ("chModeSoftware", "Software", 120)],
               cell=(0, 0), MultiSelect=FALSE)
    modebtns = flow("flpModeButtons", [btn("btnRunOnJig", "Run on jig"), btn("btnRunViaAdb", "Run via adb")], cell=(0, 1))
    tModes = tlp("tlpModes", [("Percent", 100)], [("Percent", 100), ("AutoSize", None)], [modes, modebtns], cell=(1, 0))
    pic = Ctl("picDevice", "PictureBox", {"BackColor": named("White"), "BorderStyle": enum("BorderStyle.FixedSingle"),
                                          "Dock": enum("DockStyle.Fill"), "SizeMode": enum("PictureBoxSizeMode.Zoom"),
                                          "TabStop": FALSE}, cell=(0, 0))
    picinfo = lbl("lblPicInfo", "No image yet", cell=(0, 1), fore=DIMGRAY, MaximumSize=size(260, 0))
    tImg = tlp("tlpImage", [("Percent", 100)], [("Percent", 100), ("AutoSize", None)], [pic, picinfo], cell=(2, 0))
    cols = tlp("tlpDeviceCols", [("Percent", 46), ("Percent", 28), ("Percent", 26)], [("Percent", 100)],
               [ident, tModes, tImg], cell=(0, 1))
    proc = txt("txtProcedure", cell=(0, 0), readonly=True, Dock=enum("DockStyle.Fill"), Font=font("Consolas", 9),
               Multiline=TRUE, ScrollBars=enum("ScrollBars.Vertical"))
    notes = txt("txtNotes", cell=(1, 0), Dock=enum("DockStyle.Fill"), Multiline=TRUE, ScrollBars=enum("ScrollBars.Vertical"))
    lower = tlp("tlpDeviceLower", [("Percent", 70), ("Percent", 30)], [("Percent", 100)], [proc, notes], cell=(0, 2))
    tDev = tlp("tlpDevice", [("Percent", 100)], [("AutoSize", None), ("Percent", 62), ("Percent", 38)],
               [devbar, cols, lower])
    tabDevice = tabpage("tabDevice", "Device", [tDev])

    # ── Hardware History tab (was "Devices") ──
    hbar = flow("flpHistoryBar", [
        btn("btnRefreshHistory", "Refresh"), btn("btnExportWorkbook", "Export workbook (.xlsx)"),
        btn("btnExportDrive", "Export + open Google Drive"), btn("btnFindAllImages", "Find images for all"),
        btn("btnExportJson", "Export JSON"), btn("btnOpenDataFolder", "Open data folder"),
    ], cell=(0, 0))
    gl, gp = grid_locals("historyGrid")
    gp.update({"AllowUserToAddRows": FALSE, "AutoSizeColumnsMode": enum("DataGridViewAutoSizeColumnsMode.Fill"),
               "Dock": enum("DockStyle.Fill"), "MultiSelect": FALSE, "ReadOnly": TRUE,
               "SelectionMode": enum("DataGridViewSelectionMode.FullRowSelect")})
    grid = Ctl("grdHistory", "DataGridView", gp, cell=(0, 1),
               columns=[gcol("colHistDevice", "Device"), gcol("colHistSerial", "Serial"), gcol("colHistChip", "Chip family"),
                        gcol("colHistModes", "Modes"), gcol("colHistAndroid", "Android"), gcol("colHistImage", "Image"),
                        gcol("colHistLastSeen", "Last seen")],
               extra=["{n}.RowTemplate.Height = 30;"])
    hinfo = lbl("lblHistoryInfo", "", cell=(0, 2), fore=DIMGRAY)
    tHist = tlp("tlpHistory", [("Percent", 100)], [("AutoSize", None), ("Percent", 100), ("AutoSize", None)],
                [hbar, grid, hinfo])
    tabHistory = tabpage("tabHardwareHistory", "Hardware History", [tHist])

    tabMain = tabs("tabMain", [tabWiring, tabJig, tabDevice, tabHistory], cell=(0, 1))
    console = txt("txtConsole", cell=(0, 2), readonly=True, fore=named("Gainsboro"), Dock=enum("DockStyle.Fill"),
                  Font=font("Consolas", 9), Multiline=TRUE, ScrollBars=enum("ScrollBars.Vertical"))
    root = tlp("tlpRoot", [("Percent", 100)], [("AutoSize", None), ("Percent", 100), ("Absolute", 150)],
               [topbar, tabMain, console], Padding=pad(6))

    emit("PhoneJigView", "TestPointTrigger.Modules.Views", "UserControl", view_root(), [root],
         out + r"\Modules\Views\PhoneJigView.Designer.cs", locals_=gl)
