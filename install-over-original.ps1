# Replaces TestPoint Trigger v1 with v2.0.0 in place, preserving v1 in git history.
# Run from the folder containing this script (the extracted zip).
$ErrorActionPreference = 'Stop'
$repo = 'C:\Users\User\source\repos\TestPointTrigger'
if (-not (Test-Path $repo)) { New-Item -ItemType Directory $repo | Out-Null }
if ((Resolve-Path $PSScriptRoot).Path -like "$repo*") { throw 'Extract the zip somewhere else (e.g. Downloads) first.' }
Set-Location $repo
if (-not (Test-Path .git)) { git init | Out-Null }
if ((git status --porcelain) -or (git rev-parse --verify HEAD 2>$null)) {
    git add -A; git commit -m "Snapshot v1 USB trigger before v2 rewrite" 2>$null | Out-Null
    git tag -f v1-usb-trigger | Out-Null
}
Get-ChildItem $repo -Force | Where-Object { $_.Name -ne '.git' } | Remove-Item -Recurse -Force
Copy-Item "$PSScriptRoot\*" $repo -Recurse -Force -Exclude 'install-over-original.ps1'
git add -A; git commit -m "v2.0.0: rewrite as PCB Pad Finder (HaKDMoDz)"
git tag -f v2.0.0
if (git remote) { git push -u origin HEAD --tags --force-with-lease } else {
    Write-Host "No remote set. Create it with:  gh repo create TestPointTrigger --public --source . --push" }
