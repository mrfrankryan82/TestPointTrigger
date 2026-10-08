from theme import *

def ev(evt, handler, typ="System.EventHandler"):
    return "{n}.%s += new %s(this.%s);" % (evt, typ, handler)

def build(out):
    logo_title = lbl("lblLogoTitle", "MOBILE SURGERY", fore=TEXT, BackColor=named("Transparent"),
                     Font=font("Segoe UI", 12.5, "Bold"), Location=pt(82, 16))
    logo_sub = lbl("lblLogoSubtitle", "Bench Suite", fore=MUTED, BackColor=named("Transparent"),
                   Font=font("Segoe UI", 9), Location=pt(84, 42))
    logo = Ctl("cardLogo", "TestPointTrigger.Card", {"BackColor": BG, "Dock": enum("DockStyle.Top"), "Fill": PANEL,
                                                     "Radius": "16", "Size": size(256, 82), "Stroke": BORDER},
               [logo_title, logo_sub], extra=[ev("Paint", "cardLogo_Paint", "System.Windows.Forms.PaintEventHandler")])
    nav = Ctl("lstNav", "ListBox", {"BackColor": SIDEBAR, "BorderStyle": enum("BorderStyle.None"),
                                    "Dock": enum("DockStyle.Fill"), "DrawMode": enum("DrawMode.OwnerDrawFixed"),
                                    "Font": font("Segoe UI", 11), "ForeColor": MUTED, "FormattingEnabled": TRUE,
                                    "IntegralHeight": FALSE, "ItemHeight": "46"},
              extra=[ev("DrawItem", "DrawNavItem", "System.Windows.Forms.DrawItemEventHandler"),
                     ev("SelectedIndexChanged", "lstNav_SelectedIndexChanged"),
                     ev("MouseLeave", "lstNav_MouseLeave"),
                     ev("MouseMove", "lstNav_MouseMove", "System.Windows.Forms.MouseEventHandler")])
    foot = Ctl("lblSideFoot", "Label", {"Dock": enum("DockStyle.Bottom"), "Font": font("Segoe UI", 8.5),
                                        "ForeColor": MUTED2, "Padding": pad(4, 8, 4, 0), "Size": size(256, 96),
                                        "Text": s("Modular bench toolkit\r\nCamera coaching · USB trigger · ADB/Fastboot ·\r\n"
                                                  "pad finder · job database — one shell.")})
    sidebar = panel("pnlSidebar", [nav, foot, logo], BackColor=SIDEBAR, Dock=enum("DockStyle.Left"),
                    Padding=pad(16), Size=size(288, 881))

    hero = Ctl("heroCard", "TestPointTrigger.HeroCard", {"BackColor": BG, "Dock": enum("DockStyle.Top"),
                                                         "Padding": pad(0, 0, 0, 12), "Size": size(1068, 120)})
    viewhost = panel("pnlViewHost", [], Dock=enum("DockStyle.Fill"))
    status = Ctl("lblStatus", "Label", {"Dock": enum("DockStyle.Fill"), "Font": font("Segoe UI", 9), "ForeColor": MUTED,
                                        "Text": s("Ready"), "TextAlign": "System.Drawing.ContentAlignment.MiddleLeft"})
    ver = Ctl("lblVersion", "Label", {"Dock": enum("DockStyle.Right"), "Font": font("Segoe UI", 8.5), "ForeColor": MUTED2,
                                      "Size": size(260, 34), "Text": s("Developer: HaKDMoDz™"),
                                      "TextAlign": "System.Drawing.ContentAlignment.MiddleRight"})
    footer = panel("pnlFooter", [status, ver], Dock=enum("DockStyle.Bottom"), Size=size(1068, 34))
    main = panel("pnlMain", [viewhost, hero, footer], Dock=enum("DockStyle.Fill"), Padding=pad(24, 18, 24, 0))

    emit("ModuleHostForm", "TestPointTrigger.Modules", "Form", {
        "BackColor": BG, "ClientSize": size(1404, 881), "Font": UI_FONT, "ForeColor": TEXT,
        "StartPosition": enum("FormStartPosition.CenterScreen"), "Text": s("Mobile Surgery — Bench Suite")},
        [main, sidebar], out + r"\Modules\ModuleHostForm.Designer.cs",
        root_extra=[ev("FormClosing", "ModuleHostForm_FormClosing", "System.Windows.Forms.FormClosingEventHandler")])
