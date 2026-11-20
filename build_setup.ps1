$ErrorActionPreference = "Stop"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

Write-Host "0. Compiling UMA Connect.exe and Uninstall.exe..."
if (Test-Path $csc) {
    if (Test-Path "UMAConnect.cs") {
        & $csc /target:winexe /out:"UMA Connect.exe" /win32icon:app_icon.ico UMAConnect.cs
        if ($LASTEXITCODE -ne 0) { throw "Failed to compile UMAConnect.cs" }
        Write-Host "  UMA Connect.exe compiled successfully!"
    }
    if (Test-Path "Uninstall.cs") {
        & $csc /target:winexe /out:"Uninstall.exe" /win32icon:app_icon.ico Uninstall.cs
        if ($LASTEXITCODE -ne 0) { throw "Failed to compile Uninstall.cs" }
        Write-Host "  Uninstall.exe compiled successfully!"
    }
}

Write-Host "1. Updating app_payload.zip..."
if (Test-Path "app_payload.zip") {
    Remove-Item -Force "app_payload.zip"
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open("app_payload.zip", [System.IO.Compression.ZipArchiveMode]::Create)
$files = @(
    "index.html",
    "UMA Connect.exe",
    "Uninstall.exe",
    "app_icon.ico",
    "app_icon.png",
    "favicon.ico",
    "favicon.png",
    "apple-touch-icon.png",
    "manifest.json"
)
foreach ($f in $files) {
    if (Test-Path $f) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Resolve-Path $f), $f, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        Write-Host "  Added $f"
    }
}
$zip.Dispose()
Write-Host "app_payload.zip updated successfully!"

Write-Host "2. Compiling UMA_Connect_Setup.exe..."
if (Test-Path $csc) {
    & $csc /target:winexe /out:UMA_Connect_Setup.exe /win32icon:app_icon.ico /resource:app_payload.zip /resource:app_icon.ico /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll Installer.cs
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile Installer.cs" }
    Write-Host "UMA_Connect_Setup.exe compiled successfully!"
}

Write-Host "3. Building & Signing Production APKs (v1 + v2 Signed, 4-Byte Aligned)..."
python build_apk.py
if ($LASTEXITCODE -ne 0) { throw "Failed to build and sign APKs!" }

Write-Host "4. Packaging UMA_Connect_Share_Package.zip..."
if (Test-Path "UMA_Connect_Share_Package.zip") {
    Remove-Item -Force "UMA_Connect_Share_Package.zip"
}
$shareZip = [System.IO.Compression.ZipFile]::Open("UMA_Connect_Share_Package.zip", [System.IO.Compression.ZipArchiveMode]::Create)
$shareFiles = @(
    "UMA_Connect_Setup.exe",
    "UMA Connect.exe",
    "Run_UMA_Connect.bat",
    "UMA Connect.apk",
    "index.html",
    "HOW_TO_USE.txt",
    "app_icon.ico",
    "favicon.ico",
    "favicon.png",
    "apple-touch-icon.png",
    "manifest.json"
)
foreach ($sf in $shareFiles) {
    if (Test-Path $sf) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($shareZip, (Resolve-Path $sf), $sf, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        Write-Host "  Added $sf to share package"
    }
}
$shareZip.Dispose()
Write-Host "UMA_Connect_Share_Package.zip built successfully!"
