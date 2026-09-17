param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "release\app"
)

$ErrorActionPreference = "Stop"
$project = "src\SecureFolder.App\SecureFolder.App.csproj"

Write-Host "=== SecureFolder Build ===" -ForegroundColor Cyan

# Clean
if (Test-Path "release") { Remove-Item "release" -Recurse -Force }

# Publish self-contained
Write-Host "Publishing self-contained ($Runtime)..." -ForegroundColor Yellow
dotnet publish $project -c $Configuration -r $Runtime --self-contained true `
    /p:PublishSingleFile=false `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$size = [math]::Round((Get-ChildItem $OutputDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
Write-Host "Published: $OutputDir ($size MB)" -ForegroundColor Green

# Build Inno Setup installer
$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    Write-Host "Building installer..." -ForegroundColor Yellow
    & $iscc installer\SecureFolder.iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
    $installer = Get-ChildItem "release\SecureFolderSetup*.exe" -ErrorAction SilentlyContinue
    if ($installer) {
        $isize = [math]::Round($installer.Length / 1MB, 1)
        Write-Host "Installer: $($installer.Name) ($isize MB)" -ForegroundColor Green
    }
} else {
    Write-Host "Inno Setup no encontrado. Instalar con: winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
}

Write-Host "=== Done ===" -ForegroundColor Cyan
