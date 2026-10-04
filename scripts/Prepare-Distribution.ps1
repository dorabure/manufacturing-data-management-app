param(
    [string]$PublishDirectory = (Join-Path $PSScriptRoot "..\artifacts\publish\win-x64"),
    [string]$DistributionDirectory = (Join-Path $PSScriptRoot "..\dist\ManufacturingDataApp")
)

$ErrorActionPreference = "Stop"

$requiredFiles = @(
    @{ Source = (Join-Path $PublishDirectory "ManufacturingDataApp.exe"); Destination = "ManufacturingDataApp.exe" },
    @{ Source = (Join-Path $PSScriptRoot "..\docs\DISTRIBUTION_README.md"); Destination = "README.md" },
    @{ Source = (Join-Path $PSScriptRoot "..\docs\USER_GUIDE.md"); Destination = "USER_GUIDE.md" },
    @{ Source = (Join-Path $PSScriptRoot "..\data\sample.csv"); Destination = "sample_measurement.csv" }
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $file.Source -PathType Leaf)) {
        throw "配布に必要なファイルが見つかりません: $($file.Source)"
    }
}

New-Item -ItemType Directory -Path $DistributionDirectory -Force | Out-Null

foreach ($file in $requiredFiles) {
    Copy-Item -LiteralPath $file.Source -Destination (Join-Path $DistributionDirectory $file.Destination) -Force
}

# Publish成果物のデバッグシンボルは、配布用フォルダーに含めない。
foreach ($pdbName in @("ManufacturingDataApp.pdb", "ManufacturingDataApp.Application.pdb", "ManufacturingDataApp.Domain.pdb", "ManufacturingDataApp.Infrastructure.pdb")) {
    $pdbPath = Join-Path $DistributionDirectory $pdbName
    if (Test-Path -LiteralPath $pdbPath -PathType Leaf) {
        Remove-Item -LiteralPath $pdbPath -Force
    }
}

$allowedNames = $requiredFiles | ForEach-Object { $_.Destination }
$unexpectedEntries = Get-ChildItem -LiteralPath $DistributionDirectory | Where-Object { $_.Name -notin $allowedNames }
if ($unexpectedEntries) {
    throw "配布フォルダーに想定外の項目があります: $($unexpectedEntries.Name -join ', ')"
}

Write-Host "配布フォルダーを更新しました: $DistributionDirectory"
