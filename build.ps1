# EarKit build script (Windows PowerShell 5+ / PowerShell 7)
# Requires: .NET 10 SDK and NSIS 3 (makensis on PATH or in the default install location)
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

$nsis = (Get-Command makensis -ErrorAction SilentlyContinue).Source
if (-not $nsis) { $nsis = "${env:ProgramFiles(x86)}\NSIS\makensis.exe" }
if (-not (Test-Path $nsis)) { throw "makensis not found. Install NSIS 3: https://nsis.sourceforge.io/" }

New-Item -ItemType Directory -Force "$root\dist" | Out-Null
$out = "$root\dist\EarKit-$version-Setup.exe"
$defs = @("/DVERSION=$version", "/DPUBLISHDIR=$root\publish", "/DREDIST=$redist", "/DOUTFILE=$out")
if ($env:GITHUB_REPOSITORY) { $defs += "/DPROJECTURL=https://github.com/$($env:GITHUB_REPOSITORY)" }
& $nsis @defs "$root\installer\EarKit.nsi"
if ($LASTEXITCODE -ne 0) { throw "makensis failed" }
Write-Host "Completed: $out"
