"""Rename the app's user-visible name to Mobile Surgery and bump to v3.7.0.
Keeps the C# namespace and the LocalAppData data folder (TestPointTrigger) so existing data still loads."""
import os, re
REPO = r"C:\Users\User\source\repos\TestPointTrigger"
APP = os.path.join(REPO, "TestPointTrigger")

def rw(path, fn):
    with open(path, "r", encoding="utf-8-sig", newline="") as f: t = f.read()
    n = fn(t)
    if n != t:
        bom = open(path, "rb").read(3) == b"\xef\xbb\xbf"
        with open(path, "w", encoding="utf-8-sig" if bom else "utf-8", newline="") as f: f.write(n)
        print("renamed in", os.path.relpath(path, REPO))

def cs(t):
    t = t.replace("TestPoint Trigger", "Mobile Surgery")
    t = t.replace('"TestPointTrigger/3.6 (bench tool;', '"MobileSurgery/3.7 (bench tool;')
    # Workbook export folder is user-facing output (regenerated from the DB on every export)
    t = t.replace('return Path.Combine(p, "TestPointTrigger");', 'return Path.Combine(p, "MobileSurgery");')
    t = t.replace('return Path.Combine(home, n, "TestPointTrigger");', 'return Path.Combine(home, n, "MobileSurgery");')
    t = t.replace('Environment.SpecialFolder.MyDocuments), "TestPointTrigger");', 'Environment.SpecialFolder.MyDocuments), "MobileSurgery");')
    t = re.sub(r"(// Developer: HaKDMoDz™ · v)3\.\d+\.\d+( · )\d{4}-\d{2}-\d{2}", r"\g<1>3.7.0\g<2>2026-10-09", t) if "Program.cs" in t[:0] else t
    return t

for root, dirs, files in os.walk(APP):
    dirs[:] = [d for d in dirs if d not in ("bin", "obj")]
    for fn in files:
        if fn.endswith(".cs"):
            rw(os.path.join(root, fn), cs)

def proj(t):
    t = t.replace("<AssemblyName>TestPointTrigger</AssemblyName>", "<AssemblyName>MobileSurgery</AssemblyName>")
    t = t.replace("<AssemblyTitle>TestPoint Trigger - Modular Bench Suite</AssemblyTitle>", "<AssemblyTitle>Mobile Surgery - Modular Bench Suite</AssemblyTitle>")
    t = t.replace("<Product>TestPoint Trigger</Product>", "<Product>Mobile Surgery</Product>")
    t = t.replace("<Version>3.6.0</Version>", "<Version>3.7.0</Version>")
    t = t.replace("<FileVersion>3.6.0.0</FileVersion>", "<FileVersion>3.7.0.0</FileVersion>")
    t = t.replace("<AssemblyVersion>3.6.0.0</AssemblyVersion>", "<AssemblyVersion>3.7.0.0</AssemblyVersion>")
    return t
rw(os.path.join(APP, "TestPointTrigger.csproj"), proj)
