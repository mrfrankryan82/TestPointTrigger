from patch import *
F = "TestPointTrigger.csproj"
t = load(F)
items = [("MainForm.cs", "Form"), ("HelpForm.cs", "Form"), (r"Modules\ModuleHostForm.cs", "Form"),
         (r"Modules\LicenseEditForm.cs", "Form"), (r"Modules\RepairEditForm.cs", "Form"), (r"Modules\PromptForm.cs", "Form")]
for v in ["PhoneJigView", "UsbTriggerView", "AdbFastbootView", "LiveCoachView", "LogView", "LicenseView", "RepairDbView"]:
    items.append((r"Modules\Views\%s.cs" % v, "UserControl"))
lines = ["  <ItemGroup>", "    <!-- Designer support: each form/view opens in Design View; its .Designer.cs nests under it. -->"]
for path, sub in items:
    lines.append('    <Compile Update="%s">' % path)
    lines.append("      <SubType>%s</SubType>" % sub)
    lines.append("    </Compile>")
    d = path[:-3] + ".Designer.cs"
    lines.append('    <Compile Update="%s">' % d)
    lines.append("      <DependentUpon>%s</DependentUpon>" % path.split("\\")[-1])
    lines.append("    </Compile>")
lines.append("  </ItemGroup>")
block = "\r\n".join(lines) + "\r\n"
if "Designer support:" not in t:
    t = replace1(t, "</Project>", block + "</Project>")
save(F, t); print("csproj updated", len(items), "forms/views")
