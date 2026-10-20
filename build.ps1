# EarKit 构建脚本（Windows PowerShell 5+ / PowerShell 7）
# 需要：.NET 10 SDK、NSIS 3（makensis 在 PATH 中或位于默认安装路径）
# 用法：./build.ps1            -> dist\EarKit-<版本>-Setup.exe
#       ./build.ps1 -NoInstaller  只发布程序到 publish\
param([switch]$NoInstaller)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$proj = "$root\src\EarKit\EarKit.csproj"
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "EarKit $version"

if (Test-Path "$root\publish") { Remove-Item "$root\publish" -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained false -o "$root\publish"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }
if ($NoInstaller) { return }

# .NET 10 桌面运行时（不提交到仓库，构建时下载）
$redist = "$root\redist\windowsdesktop-runtime-win-x64.exe"
if (-not (Test-Path $redist)) {
  New-Item -ItemType Directory -Force "$root\redist" | Out-Null
  Write-Host "下载 .NET 10 桌面运行时..."
  Invoke-WebRequest "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe" -OutFile $redist
}

$nsis = (Get-Command makensis -ErrorAction SilentlyContinue).Source
if (-not $nsis) { $nsis = "${env:ProgramFiles(x86)}\NSIS\makensis.exe" }
if (-not (Test-Path $nsis)) { throw "未找到 makensis，请安装 NSIS 3：https://nsis.sourceforge.io/" }

New-Item -ItemType Directory -Force "$root\dist" | Out-Null
$out = "$root\dist\EarKit-$version-Setup.exe"
$defs = @("/DVERSION=$version", "/DPUBLISHDIR=$root\publish", "/DREDIST=$redist", "/DOUTFILE=$out")
if ($env:GITHUB_REPOSITORY) { $defs += "/DPROJECTURL=https://github.com/$($env:GITHUB_REPOSITORY)" }
& $nsis @defs "$root\installer\EarKit.nsi"
if ($LASTEXITCODE -ne 0) { throw "makensis 失败" }
Write-Host "完成：$out"
