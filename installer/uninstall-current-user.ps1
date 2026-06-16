param(
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$AppName = "CATIA Wing Designer"
$InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\CATIA Wing Designer"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "CATIA Wing Designer.lnk"
$uninstallRegPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CatiaWingDesigner"

if (Test-Path -LiteralPath $startMenuDir) {
    Remove-Item -LiteralPath $startMenuDir -Recurse -Force
}

if (Test-Path -LiteralPath $desktopShortcut) {
    Remove-Item -LiteralPath $desktopShortcut -Force
}

if (Test-Path -LiteralPath $uninstallRegPath) {
    Remove-Item -LiteralPath $uninstallRegPath -Recurse -Force
}

Set-Location $env:TEMP
if (Test-Path -LiteralPath $InstallDir) {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
}

if (-not $Quiet) {
    Write-Host "$AppName has been uninstalled."
}
