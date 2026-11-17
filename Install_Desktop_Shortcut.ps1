param(
    [switch]$Silent
)

$ErrorActionPreference = "Stop"
$appDir = $PSScriptRoot
$targetExe = Join-Path $appDir "UMA Connect.exe"
$iconPath = Join-Path $appDir "app_icon.ico"

if (-not (Test-Path $targetExe)) {
    throw "UMA Connect.exe was not found in $appDir"
}

$wshShell = New-Object -ComObject WScript.Shell

$desktopPath = [Environment]::GetFolderPath("Desktop")
$desktopShortcut = Join-Path $desktopPath "UMA Connect.lnk"
$sc = $wshShell.CreateShortcut($desktopShortcut)
$sc.TargetPath = $targetExe
$sc.WorkingDirectory = $appDir
$sc.IconLocation = "$iconPath,0"
$sc.Description = "UMA Connect - Enterprise Operations Portal"
$sc.Save()

$startMenuDir = Join-Path ([Environment]::GetFolderPath("Programs")) "UMA Connect"
if (-not (Test-Path $startMenuDir)) {
    New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
}
$startShortcut = Join-Path $startMenuDir "UMA Connect.lnk"
$sc2 = $wshShell.CreateShortcut($startShortcut)
$sc2.TargetPath = $targetExe
$sc2.WorkingDirectory = $appDir
$sc2.IconLocation = "$iconPath,0"
$sc2.Description = "UMA Connect - Enterprise Operations Portal"
$sc2.Save()

if (-not $Silent) {
    Write-Host "Created Desktop shortcut: $desktopShortcut"
    Write-Host "Created Start Menu shortcut: $startShortcut"
}

