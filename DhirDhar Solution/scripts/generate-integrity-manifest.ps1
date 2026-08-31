# =============================================================================
# DhirDhar Application Integrity Manifest Generator
# Generates and HMAC-SHA256 signs app_integrity.sig for the specified directory.
# =============================================================================
param (
    [Parameter(Mandatory = $true)]
    [string]$TargetDir
)

$ErrorActionPreference = "Stop"

# Sanitize TargetDir: strip any quotes or escaped quotes passed from MSBuild CLI
$TargetDir = $TargetDir.Trim().Trim('"', "'").TrimEnd('\', '/')

if (-not (Test-Path -LiteralPath $TargetDir)) {
    Write-Error "[INTEGRITY ERROR] Target directory does not exist: $TargetDir"
    exit 1
}

# Resolve full absolute path
$TargetDir = (Resolve-Path -LiteralPath $TargetDir).Path

# Critical files protected by Application Integrity
$criticalFiles = @(
    "DhirDhar.Desktop.exe",
    "DhirDhar.Application.dll",
    "DhirDhar.Domain.dll",
    "DhirDhar.Infrastructure.dll",
    "DhirDhar.Desktop.dll",
    "DhirDharUpdater.exe",
    "DhirDharUpdater.dll",
    "createdump.exe",
    "RestartAgent.exe",
    "appsettings.json",
    "google_client_secrets.json",
    "DhirDhar.Desktop.runtimeconfig.json",
    "DhirDhar.Desktop.deps.json",
    "DhirDharUpdater.deps.json",
    "resources.pri",
    "Microsoft.UI.Xaml.Controls.pri",
    "coreclr.dll",
    "clrjit.dll",
    "Microsoft.ui.xaml.dll",
    "Microsoft.WinUI.dll",
    "Microsoft.WindowsAppRuntime.Bootstrap.dll"
)

$fileEntries = @()
$canonicalLines = @()

# Filter existing files and explicitly exclude app_integrity.sig to prevent self-hash recursion
$foundFiles = @()
foreach ($cf in $criticalFiles) {
    if ($cf -eq "app_integrity.sig") { continue }
    $targetFile = Join-Path $TargetDir $cf
    if (Test-Path $targetFile) {
        $foundFiles += $cf
    }
}

# Invariant Ordinal Sorting by uppercase relative path
$sortedFiles = $foundFiles | Sort-Object { $_.ToUpperInvariant() }

foreach ($cf in $sortedFiles) {
    $targetFile = Join-Path $TargetDir $cf
    $sha = (Get-FileHash -Path $targetFile -Algorithm SHA256).Hash.ToUpperInvariant()
    $size = (Get-Item $targetFile).Length
    $fileEntries += @{
        RelativePath = $cf
        Sha256 = $sha
        SizeBytes = $size
    }
    $normalizedRel = $cf.Replace('/', '\').ToUpperInvariant()
    $canonicalLines += "$normalizedRel|$sha|$size"
}

if ($fileEntries.Count -eq 0) {
    Write-Warning "[INTEGRITY WARNING] No critical application files found in $TargetDir to sign."
    exit 0
}

$canonicalString = ($canonicalLines -join "`n") + "`n"
$hmacKey = [System.Text.Encoding]::UTF8.GetBytes("DhirDhar.Enterprise.ApplicationIntegrity.v1.2026")
$hmac = [System.Security.Cryptography.HMACSHA256]::new($hmacKey)
$sigBytes = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($canonicalString))
$signature = [System.BitConverter]::ToString($sigBytes).Replace("-", "").ToUpperInvariant()

$manifestObj = [ordered]@{
    ApplicationVersion = "2.0.0"
    GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    Files = $fileEntries
    Signature = $signature
}

$manifestJson = $manifestObj | ConvertTo-Json -Depth 5
$outPath = Join-Path $TargetDir "app_integrity.sig"
[System.IO.File]::WriteAllText($outPath, $manifestJson, [System.Text.Encoding]::UTF8)

Write-Host "  [INTEGRITY MANIFEST] Successfully generated and signed $outPath ($($fileEntries.Count) binaries protected)." -ForegroundColor Green
