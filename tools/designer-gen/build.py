import sys, importlib
OUT = r"C:\Users\User\source\repos\TestPointTrigger\TestPointTrigger"
specs = sys.argv[1:] or ["phonejig"]
for name in specs:
    importlib.import_module("spec_" + name).build(OUT)
