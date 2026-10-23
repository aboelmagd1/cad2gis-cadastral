$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$workspaceRoot = Split-Path -Parent $scriptDir
$binDir = Join-Path $scriptDir "Cad2Gis.AddIn\bin\Debug\net8.0-windows"
$configDaml = Join-Path $scriptDir "Cad2Gis.AddIn\Config.daml"
$outputDir = Join-Path $scriptDir "bin\Package"
$addinFile = Join-Path $outputDir "Cad2Gis.esriAddinX"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "   CAD2GIS ArcGIS Pro Add-in Packager     " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Build Solution
Write-Host "`n1. Building Cad2Gis.sln..." -ForegroundColor Yellow
dotnet build (Join-Path $scriptDir "Cad2Gis.sln") -c Debug
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
}

# 2. Stage Files (Strict Esri ArcGIS Pro .esriAddinX specification)
Write-Host "`n2. Staging Add-in files (Root: Config.daml & Images, Subfolder: Install\ for DLLs)..." -ForegroundColor Yellow
$stagingDir = Join-Path $outputDir "staging"
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

# All assemblies and dependencies go into the Install/ subfolder
$installDir = Join-Path $stagingDir "Install"
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item (Join-Path $binDir "*") $installDir -Recurse -Force

# Config.daml sits at the root
Copy-Item $configDaml (Join-Path $stagingDir "Config.daml") -Force

# Images sit at the root
$imagesDir = Join-Path $scriptDir "Cad2Gis.AddIn\Images"
if (Test-Path $imagesDir) {
    Copy-Item $imagesDir (Join-Path $stagingDir "Images") -Recurse -Force
}

# HelperFiles seed copy packaged with Add-in (Section 10)
$helperFilesDir = Join-Path $workspaceRoot "HelperFiles"
if (-not (Test-Path $helperFilesDir)) {
    $helperFilesDir = Join-Path $scriptDir "HelperFiles"
}
if (Test-Path $helperFilesDir) {
    Copy-Item $helperFilesDir (Join-Path $stagingDir "HelperFiles") -Recurse -Force
    Copy-Item $helperFilesDir (Join-Path $installDir "HelperFiles") -Recurse -Force
}

# 3. Create .esriAddinX ZIP archive
Write-Host "`n3. Creating Cad2Gis.esriAddinX package..." -ForegroundColor Yellow
if (Test-Path $addinFile) { Remove-Item $addinFile -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDir, $addinFile)
Remove-Item $stagingDir -Recurse -Force

# Also copy to workspace root for easy user access
$rootAddinFile = Join-Path $workspaceRoot "Cad2Gis.esriAddinX"
Copy-Item $addinFile $rootAddinFile -Force

Write-Host "============================================================" -ForegroundColor Green
Write-Host "Package created successfully at:" -ForegroundColor Green
Write-Host "  -> $addinFile" -ForegroundColor Cyan
Write-Host "  -> $rootAddinFile" -ForegroundColor Cyan
Write-Host "Ready for manual user installation (Double-click Cad2Gis.esriAddinX to install)." -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
