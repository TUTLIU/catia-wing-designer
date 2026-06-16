param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [switch]$Restore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$ProjectPath = Join-Path $RootDir "CatiaWingDesigner.App\CatiaWingDesigner.App.csproj"
$NuGetConfig = Join-Path $RootDir "NuGet.Config"
$ArtifactsDir = Join-Path $RootDir "artifacts"
$PublishDir = Join-Path $ArtifactsDir "publish\app"
$InstallerOutDir = Join-Path $ArtifactsDir "installer"
$SetupFileName = "CatiaWingDesigner-$Version-Setup.exe"
$SetupOutPath = Join-Path $InstallerOutDir $SetupFileName
$IExpressExe = Join-Path $env:SystemRoot "System32\iexpress.exe"
$IExpressWorkDir = Join-Path $env:TEMP "CatiaWingDesignerInstaller-$Version"
$PayloadDir = Join-Path $IExpressWorkDir "payload"
$SedPath = Join-Path $IExpressWorkDir "CatiaWingDesigner.sed"
$TempSetupPath = Join-Path $IExpressWorkDir $SetupFileName

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $parentFull = Resolve-FullPath $Parent
    $childFull = Resolve-FullPath $Child
    if (-not $childFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean an unexpected directory: $childFull"
    }
}

function Find-MSBuild {
    $knownPath = "D:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"
    if (Test-Path -LiteralPath $knownPath) {
        return $knownPath
    }

    $whereOutput = & where.exe MSBuild.exe 2>$null
    if ($LASTEXITCODE -eq 0 -and $whereOutput) {
        return ($whereOutput | Select-Object -First 1)
    }

    throw "MSBuild.exe was not found. Install VS2022 Build Tools or update the MSBuild path in this script."
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $FilePath $($Arguments -join ' ')"
    }
}

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "Project file was not found: $ProjectPath"
}

if (-not (Test-Path -LiteralPath $IExpressExe)) {
    throw "Windows IExpress was not found: $IExpressExe"
}

Assert-ChildPath -Parent $RootDir -Child $ArtifactsDir
if (Test-Path -LiteralPath $ArtifactsDir) {
    Remove-Item -LiteralPath $ArtifactsDir -Recurse -Force
}

if (Test-Path -LiteralPath $IExpressWorkDir) {
    Remove-Item -LiteralPath $IExpressWorkDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $PublishDir, $InstallerOutDir, $PayloadDir | Out-Null

$msbuild = Find-MSBuild
if ($Restore) {
    Invoke-Checked -FilePath $msbuild -Arguments @(
        $ProjectPath,
        "/t:Restore",
        "/p:RestoreConfigFile=$NuGetConfig",
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        "/nologo"
    )
}

Invoke-Checked -FilePath $msbuild -Arguments @(
    $ProjectPath,
    "/t:Build",
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    "/p:OutDir=$PublishDir\",
    "/nologo"
)

$appExe = Join-Path $PublishDir "CatiaWingDesigner.App.exe"
if (-not (Test-Path -LiteralPath $appExe)) {
    throw "Release output is incomplete: $appExe was not found."
}

Copy-Item -Path (Join-Path $PublishDir "*") -Destination $PayloadDir -Recurse -Force
Get-ChildItem -LiteralPath $PayloadDir -Filter "*.pdb" -File | Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $RootDir "installer\install-current-user.ps1") -Destination $PayloadDir -Force
Copy-Item -LiteralPath (Join-Path $RootDir "installer\uninstall-current-user.ps1") -Destination $PayloadDir -Force
Set-Content -LiteralPath (Join-Path $PayloadDir "package-version.txt") -Value $Version -Encoding UTF8

$nestedDirs = @(Get-ChildItem -LiteralPath $PayloadDir -Directory)
if ($nestedDirs.Count -gt 0) {
    throw "The current IExpress packager expects a flat payload. Subdirectories were found: $($nestedDirs.FullName -join ', ')"
}

$payloadFiles = @(Get-ChildItem -LiteralPath $PayloadDir -File | Sort-Object Name)
$sourceFileLines = New-Object System.Collections.Generic.List[string]
$stringLines = New-Object System.Collections.Generic.List[string]

for ($i = 0; $i -lt $payloadFiles.Count; $i++) {
    $key = "FILE$i"
    $sourceFileLines.Add("%$key%=")
    $stringLines.Add("$key=`"$($payloadFiles[$i].Name)`"")
}

$sedContent = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$TempSetupPath
FriendlyName=CATIA Wing Designer Setup
AppLaunched=powershell.exe -NoProfile -ExecutionPolicy Bypass -File install-current-user.ps1
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles

[SourceFiles]
SourceFiles0=$PayloadDir\

[SourceFiles0]
$($sourceFileLines -join "`r`n")

[Strings]
$($stringLines -join "`r`n")
"@

Set-Content -LiteralPath $SedPath -Value $sedContent -Encoding ASCII
Invoke-Checked -FilePath $IExpressExe -Arguments @("/N", $SedPath)

for ($i = 0; $i -lt 20 -and -not (Test-Path -LiteralPath $TempSetupPath); $i++) {
    Start-Sleep -Milliseconds 500
}

if (-not (Test-Path -LiteralPath $TempSetupPath)) {
    throw "IExpress did not generate the installer: $TempSetupPath"
}

Copy-Item -LiteralPath $TempSetupPath -Destination $SetupOutPath -Force
Copy-Item -LiteralPath $SedPath -Destination (Join-Path $InstallerOutDir "CatiaWingDesigner.sed") -Force

Write-Host "Installer generated: $SetupOutPath"
