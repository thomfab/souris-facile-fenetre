# Builds the portable executables and the installer in build/.
#
#   ./build.ps1                 # x64 and arm64, plus the installer
#   ./build.ps1 -NoInstaller    # executables only
#
# The installer needs Inno Setup 6 (https://jrsoftware.org/isinfo.php).

param(
    [string[]]$Runtimes = @("win-x64", "win-arm64"),
    [switch]$NoInstaller
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Select-Object -First 1
$out = Join-Path $PSScriptRoot "build"
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($runtime in $Runtimes) {
    $arch = $runtime -replace "^win-", ""
    $dir = Join-Path $out $runtime
    dotnet publish src/SourisFacile -c Release -r $runtime --self-contained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -o $dir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $runtime" }
    Copy-Item (Join-Path $dir "SourisFacileFenetre.exe") (Join-Path $out "SourisFacileFenetre-$version-$arch-portable.exe")
}

if (-not $NoInstaller) {
    $iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        $iscc = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $iscc) { throw "Inno Setup 6 not found: install it, or pass -NoInstaller" }
    & $iscc "/DAppVersion=$version" installer/SourisFacileFenetre.iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
}

Get-ChildItem $out -Filter "SourisFacileFenetre-$version-*.exe" | ForEach-Object { $_.FullName }
