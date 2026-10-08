# Theme palette + control factories. Values mirror TestPointTrigger.Theme so
# Design View shows exactly what runs.
from gen import *

BG = col(8, 11, 17); SIDEBAR = col(9, 13, 20); PANEL = col(13, 21, 33); PANEL2 = col(16, 26, 40)
FIELD = col(11, 18, 32); CONSOLE = col(7, 11, 17); BORDER = col(27, 58, 75); BORDERSOFT = col(21, 36, 47)
CYAN = col(53, 196, 224); CYANBRIGHT = col(95, 220, 242); CYANDIM = col(42, 139, 163)
RED = col(239, 71, 87); TEXT = col(234, 242, 248); MUTED = col(134, 149, 166); MUTED2 = col(93, 107, 122)
INK = col(16, 24, 34)
DIMGRAY = named("DimGray")
UI_FONT = font("Segoe UI", 9.5)

def _p(base, kw):
    d = dict(base)
    for k, v in kw.items(): d[k] = v
    return d

BTN_EXTRA = ["{n}.FlatAppearance.BorderColor = " + BORDER + ";",
             "{n}.FlatAppearance.MouseOverBackColor = " + PANEL2 + ";"]

def btn(name, text, cell=None, **kw):
    """Dark themed button (AutoSize). kw overrides any property."""
    props = _p({"AutoSize": TRUE, "BackColor": FIELD, "FlatStyle": enum("FlatStyle.Flat"),
                "ForeColor": CYANBRIGHT, "Margin": pad(3), "Padding": pad(4, 2, 4, 2),
                "Text": s(text), "UseVisualStyleBackColor": FALSE}, kw)
    return Ctl(name, "Button", props, cell=cell, extra=list(BTN_EXTRA))

def lbl(name, text, cell=None, fore=MUTED, **kw):
    return Ctl(name, "Label", _p({"AutoSize": TRUE, "ForeColor": fore, "Text": s(text)}, kw), cell=cell)

def txt(name, cell=None, readonly=False, fore=TEXT, **kw):
    base = {"BackColor": CONSOLE if readonly else FIELD, "BorderStyle": enum("BorderStyle.FixedSingle"), "ForeColor": fore}
    if readonly: base["ReadOnly"] = TRUE
    return Ctl(name, "TextBox", _p(base, kw), cell=cell)

def cbo(name, items=None, cell=None, **kw):
    props = _p({"BackColor": FIELD, "DropDownStyle": enum("ComboBoxStyle.DropDownList"),
                "FlatStyle": enum("FlatStyle.Flat"), "ForeColor": TEXT, "FormattingEnabled": TRUE}, kw)
    if items: props["Items"] = items
    return Ctl(name, "ComboBox", props, cell=cell)

def nud(name, mn, mx, val, cell=None, **kw):
    props = {"BackColor": FIELD, "BorderStyle": enum("BorderStyle.FixedSingle"), "ForeColor": TEXT}
    props.update(kw)
    props["Maximum"] = dec(mx); props["Minimum"] = dec(mn); props["Value"] = dec(val)
    return Ctl(name, "NumericUpDown", props, cell=cell)

def chk(name, text, cell=None, **kw):
    return Ctl(name, "CheckBox", _p({"AutoSize": TRUE, "ForeColor": TEXT, "Text": s(text),
                                     "UseVisualStyleBackColor": FALSE}, kw), cell=cell)

def lst(name, cell=None, **kw):
    return Ctl(name, "ListBox", _p({"BackColor": CONSOLE, "BorderStyle": enum("BorderStyle.None"),
                                    "ForeColor": TEXT, "FormattingEnabled": TRUE}, kw), cell=cell)

def flow(name, kids, cell=None, **kw):
    return Ctl(name, "FlowLayoutPanel", _p({"AutoSize": TRUE, "BackColor": BG, "Dock": enum("DockStyle.Fill"),
                                            "ForeColor": TEXT}, kw), kids, cell=cell)

def tlp(name, cols, rows, kids, cell=None, **kw):
    props = _p({"BackColor": BG, "ColumnCount": str(len(cols)), "Dock": enum("DockStyle.Fill"),
                "ForeColor": TEXT, "RowCount": str(len(rows))}, kw)
    return Ctl(name, "TableLayoutPanel", props, kids, cell=cell, styles={"cols": cols, "rows": rows})

def panel(name, kids, cell=None, **kw):
    return Ctl(name, "Panel", _p({"BackColor": BG, "ForeColor": TEXT}, kw), kids, cell=cell)

def tabpage(name, text, kids, **kw):
    return Ctl(name, "TabPage", _p({"BackColor": BG, "ForeColor": TEXT, "Padding": pad(4), "Text": s(text),
                                    "UseVisualStyleBackColor": FALSE}, kw), kids)

def tabs(name, pages, cell=None, **kw):
    return Ctl(name, "TabControl", _p({"Dock": enum("DockStyle.Fill"), "SelectedIndex": "0"}, kw), pages, cell=cell)

def group(name, text, kids, **kw):
    return Ctl(name, "GroupBox", _p({"BackColor": BG, "ForeColor": TEXT, "Text": s(text)}, kw), kids)

def lv(name, columns, cell=None, **kw):
    return Ctl(name, "ListView", _p({"Dock": enum("DockStyle.Fill"), "FullRowSelect": TRUE, "HideSelection": FALSE,
                                     "UseCompatibleStateImageBehavior": FALSE, "View": enum("View.Details")}, kw),
               columns=[Ctl(n, "ColumnHeader", {"Text": s(t), "Width": str(wd)}) for n, t, wd in columns], cell=cell)

_grid_n = [0]
def grid_locals(prefix):
    """Cell-style locals that reproduce Theme.StyleGrid. Returns (locals, props)."""
    h, d, a = prefix + "Header", prefix + "Cell", prefix + "Alt"
    loc = [
        "System.Windows.Forms.DataGridViewCellStyle %s = new System.Windows.Forms.DataGridViewCellStyle();" % a,
        "System.Windows.Forms.DataGridViewCellStyle %s = new System.Windows.Forms.DataGridViewCellStyle();" % h,
        "System.Windows.Forms.DataGridViewCellStyle %s = new System.Windows.Forms.DataGridViewCellStyle();" % d,
    ]
    sets = [
        "%s.BackColor = %s;" % (a, col(11, 19, 30)),
        "%s.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;" % h,
        "%s.BackColor = %s;" % (h, PANEL2),
        "%s.Font = %s;" % (h, font("Segoe UI Semibold", 8.5, "Bold")),
        "%s.ForeColor = %s;" % (h, MUTED),
        "%s.SelectionBackColor = %s;" % (h, PANEL2),
        "%s.SelectionForeColor = %s;" % (h, MUTED),
        "%s.WrapMode = System.Windows.Forms.DataGridViewTriState.True;" % h,
        "%s.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;" % d,
        "%s.BackColor = %s;" % (d, PANEL),
        "%s.ForeColor = %s;" % (d, TEXT),
        "%s.SelectionBackColor = %s;" % (d, col(18, 48, 65)),
        "%s.SelectionForeColor = %s;" % (d, CYANBRIGHT),
        "%s.WrapMode = System.Windows.Forms.DataGridViewTriState.False;" % d,
    ]
    props = {"AlternatingRowsDefaultCellStyle": a, "BackgroundColor": PANEL, "BorderStyle": enum("BorderStyle.None"),
             "CellBorderStyle": enum("DataGridViewCellBorderStyle.SingleHorizontal"),
             "ColumnHeadersBorderStyle": enum("DataGridViewHeaderBorderStyle.None"),
             "ColumnHeadersDefaultCellStyle": h, "ColumnHeadersHeight": "38",
             "ColumnHeadersHeightSizeMode": enum("DataGridViewColumnHeadersHeightSizeMode.DisableResizing"),
             "DefaultCellStyle": d, "EnableHeadersVisualStyles": FALSE, "GridColor": BORDERSOFT,
             "RowHeadersVisible": FALSE}
    return loc + sets, props

def gcol(name, header, prop=None, width=None, fill=False, fmt_local=None):
    p = {"HeaderText": s(header)}
    if prop: p["DataPropertyName"] = s(prop)
    if fill: p["AutoSizeMode"] = enum("DataGridViewAutoSizeColumnMode.Fill")
    elif width: p["AutoSizeMode"] = enum("DataGridViewAutoSizeColumnMode.None"); p["Width"] = str(width)
    if fmt_local: p["DefaultCellStyle"] = fmt_local
    p["ReadOnly"] = TRUE
    return Ctl(name, "DataGridViewTextBoxColumn", p)

def view_root(**kw):
    return _p({"BackColor": BG, "Font": UI_FONT, "ForeColor": TEXT, "Size": size(1000, 680)}, kw)
