param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\CatiaWingDesigner"),
    [switch]$NoDesktopShortcut,
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$AppName = "CATIA Wing Designer"
$ExeName = "CatiaWingDesigner.App.exe"
$SourceDir = $PSScriptRoot
$SourceExe = Join-Path $SourceDir $ExeName
$VersionFile = Join-Path $SourceDir "package-version.txt"
$Version = if (Test-Path -LiteralPath $VersionFile) {
    (Get-Content -LiteralPath $VersionFile -Raw).Trim()
} else {
    "0.1.0"
}

function Assert-DotNetFramework48 {
    $releasePath = "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"
    $release = Get-ItemPropertyValue -Path $releasePath -Name Release -ErrorAction SilentlyContinue
    if ($null -eq $release -or [int]$release -lt 528040) {
        throw ".NET Framework 4.8 or later was not detected. Install .NET Framework 4.8 Runtime first."
    }
}

function Get-CatiaLocalServerCommand {
    $clsidKey = Get-Item -LiteralPath "Registry::HKEY_CLASSES_ROOT\CATIA.Application\CLSID" -ErrorAction SilentlyContinue
    if ($null -eq $clsidKey) {
        return $null
    }

    $clsid = [string]$clsidKey.GetValue("")
    if ([string]::IsNullOrWhiteSpace($clsid)) {
        return $null
    }

    $serverKey = Get-Item -LiteralPath "Registry::HKEY_CLASSES_ROOT\CLSID\$clsid\LocalServer32" -ErrorAction SilentlyContinue
    if ($null -eq $serverKey) {
        return $null
    }

    return [string]$serverKey.GetValue("")
}

function Assert-CatiaV5R20Com {
    $catiaType = [type]::GetTypeFromProgID("CATIA.Application")
    if ($null -eq $catiaType) {
        throw "CATIA.Application COM registration was not detected. Install CATIA V5 R20 and run V5RegServer first."
    }

    $serverCommand = Get-CatiaLocalServerCommand
    if ([string]::IsNullOrWhiteSpace($serverCommand)) {
        Write-Warning "CATIA.Application ProgID was detected, but LocalServer32 was not found."
        return
    }

    if ($serverCommand -notmatch "V5R20" -and $serverCommand -notmatch "R20") {
        Write-Warning "CATIA COM launch command does not explicitly contain V5R20/R20. Confirm that the target machine uses CATIA V5 R20. Command: $serverCommand"
    }
}

function New-Shortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $shortcutDir = Split-Path -Parent $ShortcutPath
    New-Item -ItemType Directory -Force -Path $shortcutDir | Out-Null

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = "CATIA V5 R20 wing surface designer"
    $shortcut.IconLocation = "$TargetPath,0"
    $shortcut.Save()
}

function Register-UninstallEntry {
    param(
        [Parameter(Mandatory = $true)][string]$TargetDir,
        [Parameter(Mandatory = $true)][string]$TargetExe,
        [Parameter(Mandatory = $true)][string]$DisplayVersion
    )

    $uninstallScript = Join-Path $TargetDir "uninstall-current-user.ps1"
    $uninstallCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`""
    $estimatedSizeKb = [int]((Get-ChildItem -LiteralPath $TargetDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1KB)
    $regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CatiaWingDesigner"

    New-Item -Path $regPath -Force | Out-Null
    New-ItemProperty -Path $regPath -Name "DisplayName" -Value $AppName -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $regPath -Name "DisplayVersion" -Value $DisplayVersion -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $regPath -Name "Publisher" -Value "TUTLIU" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $regPath -Name "InstallLocation" -Value $TargetDir -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $regPath -Name "DisplayIcon" -Value $TargetExe -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $regPath -Name "UninstallString" -Value $uninstallCommand -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $regPath -Name "EstimatedSize" -Value $estimatedSizeKb -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $regPath -Name "NoModify" -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $regPath -Name "NoRepair" -Value 1 -PropertyType DWord -Force | Out-Null
}

Assert-DotNetFramework48
Assert-CatiaV5R20Com

if ($ValidateOnly) {
    Write-Host "Installer prerequisite check passed."
    exit 0
}

if (-not (Test-Path -LiteralPath $SourceExe)) {
    throw "Installer payload is incomplete: $ExeName was not found."
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Get-ChildItem -LiteralPath $SourceDir -File |
    Where-Object { $_.Name -ne "install-current-user.ps1" } |
    Copy-Item -Destination $InstallDir -Force

$TargetExe = Join-Path $InstallDir $ExeName
if (-not (Test-Path -LiteralPath $TargetExe)) {
    throw "Installation failed: $TargetExe was not created."
}

$startMenuShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\CATIA Wing Designer\CATIA Wing Designer.lnk"
New-Shortcut -ShortcutPath $startMenuShortcut -TargetPath $TargetExe -WorkingDirectory $InstallDir

if (-not $NoDesktopShortcut) {
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "CATIA Wing Designer.lnk"
    New-Shortcut -ShortcutPath $desktopShortcut -TargetPath $TargetExe -WorkingDirectory $InstallDir
}

Register-UninstallEntry -TargetDir $InstallDir -TargetExe $TargetExe -DisplayVersion $Version

Write-Host "Installation completed: $InstallDir"
Write-Host "Before generating CATIA geometry, open CATIA V5 R20 manually and run CATIA and this app with the same privilege level."
