from theme import *

def build(out):
    preview = Ctl("picPreview", "PictureBox", {"BackColor": named("Black"), "Dock": enum("DockStyle.Fill"),
                                               "SizeMode": enum("PictureBoxSizeMode.Zoom"), "TabStop": FALSE}, cell=(0, 0))
    bar = flow("flpBar", [
        cbo("cboKind", ["Local camera", "Phone camera"], Size=size(130, 25)),
        cbo("cboApp", ["IP Webcam", "DroidCam", "RTSP", "Full URL"], Enabled=FALSE, Size=size(110, 25)),
        lbl("lblPhoneIp", "Phone IP:", Padding=pad(6, 6, 0, 0)),
        txt("txtPhoneIp", Enabled=FALSE, Size=size(150, 24), Text=s("192.168.1.")),
        btn("btnStart", "Start watching", Padding=pad(0)), btn("btnStop", "Stop", Padding=pad(0)),
        btn("btnQuickStart", "Quick start", Padding=pad(0)),
    ], cell=(0, 0), AutoSizeMode=enum("AutoSizeMode.GrowAndShrink"))
    log = lst("lstLog", cell=(0, 1), Dock=enum("DockStyle.Fill"), Font=font("Consolas", 9), HorizontalScrollbar=TRUE,
              IntegralHeight=FALSE)
    right = tlp("tlpRight", [("Percent", 100)], [("AutoSize", None), ("Percent", 100)], [bar, log], cell=(1, 0))
    root = tlp("tlpRoot", [("Percent", 60), ("Percent", 40)], [("Percent", 100)], [preview, right], Padding=pad(8))
    emit("LiveCoachView", "TestPointTrigger.Modules.Views", "UserControl", view_root(), [root],
         out + r"\Modules\Views\LiveCoachView.Designer.cs")
