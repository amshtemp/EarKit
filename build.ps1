# EarKit build script (Windows PowerShell 5+ / PowerShell 7)
# Requires: .NET 10 SDK and Inno Setup 7 (ISCC on PATH or in the default install location)
# Usage: ./build.ps1              -> dist\EarKit-<version>-Setup.exe
#        ./build.ps1 -NoInstaller -> publish the app to publish\
param([switch]$NoInstaller)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$proj = "$root\src\EarKit\EarKit.csproj"
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "EarKit $version"

if (Test-Path "$root\publish") { Remove-Item "$root\publish" -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained false -o "$root\publish"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
if ($NoInstaller) { return }

# .NET 10 Desktop Runtime (downloaded during the build; not committed to the repository)
$redist = "$root\redist\windowsdesktop-runtime-win-x64.exe"
if (-not (Test-Path $redist)) {
  New-Item -ItemType Directory -Force "$root\redist" | Out-Null
  Write-Host "Downloading the .NET 10 Desktop Runtime..."
  Invoke-WebRequest "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe" -OutFile $redist
}

$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if ($iscc -and (Get-Item $iscc).VersionInfo.ProductMajorPart -ne 7) { $iscc = $null }
if (-not $iscc) { $iscc = "${env:ProgramFiles}\Inno Setup 7\ISCC.exe" }
if (-not (Test-Path $iscc)) { $iscc = "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe" }
if (-not (Test-Path $iscc)) { throw "ISCC.exe not found. Install Inno Setup 7: https://jrsoftware.org/isinfo.php" }

New-Item -ItemType Directory -Force "$root\dist" | Out-Null
$defs = @("/DVERSION=$version", "/DPUBLISHDIR=$root\publish", "/DREDIST=$redist", "/DOUTDIR=$root\dist")
if ($env:GITHUB_REPOSITORY) { $defs += "/DPROJECTURL=https://github.com/$($env:GITHUB_REPOSITORY)" }
& $iscc @defs "$root\installer\EarKit.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }
$out = "$root\dist\EarKit-$version-Setup.exe"
Write-Host "Completed: $out"
