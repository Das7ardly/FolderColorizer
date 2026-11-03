# Builds installer\FolderColorizer-Setup-<version>.exe (self-contained, no .NET needed on the target PC).
# Used both locally and by the GitHub Actions release workflow.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o dist-installer
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Inno Setup installs per-user (winget/installer default) or machine-wide (Chocolatey, GitHub runners).
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isinfo.php" }

& $iscc /Q installer.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

Get-ChildItem installer\*.exe
