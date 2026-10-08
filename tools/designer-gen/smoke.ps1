$ErrorActionPreference = 'Stop'
$bin = 'C:\Users\User\source\repos\TestPointTrigger\TestPointTrigger\bin\Debug\net48'
Set-Location $bin
[Environment]::CurrentDirectory = $bin
Add-Type -AssemblyName System.Windows.Forms
$asm = [Reflection.Assembly]::LoadFrom("$bin\MobileSurgery.exe")
[AppDomain]::CurrentDomain.add_AssemblyResolve({ param($s, $e)
    $p = Join-Path $bin (($e.Name -split ',')[0] + '.dll'); if (Test-Path $p) { [Reflection.Assembly]::LoadFrom($p) } })
$ok = 0; $fail = 0
function T($name, [scriptblock]$b) {
    try { & $b; Write-Output ("PASS  " + $name); $script:ok++ }
    catch { Write-Output ("FAIL  " + $name + " :: " + $_.Exception.GetBaseException().Message); $script:fail++ }
}
# Host form (registers nav, creates first module view)
$host_ = [Activator]::CreateInstance($asm.GetType('TestPointTrigger.Modules.ModuleHostForm'))
T 'ModuleHostForm' { $null = $host_.Handle; if ($host_.Text -notlike 'Mobile Surgery*') { throw "title: $($host_.Text)" } }
foreach ($m in 'UsbTriggerModule','PhoneJigModule','AdbFastbootModule','LiveCoachModule','PadFinderModule','RepairDbModule','LicenseModule','LogModule') {
    T $m {
        $mod = [Activator]::CreateInstance($asm.GetType("TestPointTrigger.Modules.$m"))
        $view = $mod.CreateView($host_)
        $view.Dock = 'Fill'; $host_.Controls.Add($view); $null = $view.Handle
        $script:count = 0
        function Walk($c) { $script:count++; foreach ($k in $c.Controls) { Walk $k } }
        Walk $view
        if ($m -eq 'PhoneJigModule') {
            $tabs = $view.Controls[0].Controls | ? { $_ -is [Windows.Forms.TabControl] }
            $names = ($tabs.TabPages | % Text) -join ', '
            Write-Output ("      tabs: " + $names)
            if ($names -notmatch 'Hardware History') { throw 'Hardware History tab missing' }
        }
        Write-Output ("      " + $script:count + " controls")
        $host_.Controls.Remove($view)
        if ($m -ne 'UsbTriggerModule') { $mod.Dispose() }
    }
}
foreach ($f in 'TestPointTrigger.Modules.LicenseEditForm','TestPointTrigger.Modules.RepairEditForm','TestPointTrigger.Modules.PromptForm','TestPointTrigger.HelpForm','TestPointTrigger.MainForm') {
    T $f { $x = [Activator]::CreateInstance($asm.GetType($f), $true); $null = $x.Handle; $x.Dispose() }
}
Write-Output "RESULT: $ok passed, $fail failed"
