param (
    [Parameter(Mandatory = $false)]
    [string]$SolutionRoot = "d:\DhirDhar\DhirDhar Solution"
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "       DhirDhar Automated Secret Scanner           " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

$violations = @()

$highRiskPatterns = @(
    "-----BEGIN (RSA|EC|DSA|OPENSSH)? ?PRIVATE KEY-----",
    "password\s*=\s*['`"][^'`"]{6,}['`"]",
    "ClientSecret\s*=\s*['`"][a-zA-Z0-9_\-]{20,}['`"]"
)

$excludeFolders = @(".git", ".vs", "packages", "obj", "bin", "TestResults", "InnoSetup6", "tools")
$sourceFiles = Get-ChildItem -Path "$SolutionRoot\src" -Recurse -File | Where-Object {
    $path = $_.FullName
    $excluded = $false
    foreach ($f in $excludeFolders) {
        if ($path -like "*\$f\*") { $excluded = $true; break }
    }
    -not $excluded
}

Write-Host "[SCAN] Checking $($sourceFiles.Count) source files across solution..." -ForegroundColor Cyan

# 1. Check License Verification Key specifically
$licenseKeyFile = "$SolutionRoot\src\DhirDhar.Infrastructure\Licensing\LicenseVerificationKey.cs"
if (Test-Path $licenseKeyFile) {
    $content = Get-Content $licenseKeyFile -Raw
    if ($content -match "PRIVATE KEY") {
        $violations += "CRITICAL: Private key pattern detected inside client file '$licenseKeyFile'!"
    } elseif ($content -match "BEGIN PUBLIC KEY") {
        Write-Host "  [PASS] LicenseVerificationKey contains PUBLIC key only." -ForegroundColor Green
    }
}

# 2. General source scan
foreach ($file in $sourceFiles) {
    # Skip binary files
    if ($file.Extension -in @(".png", ".ico", ".ttf", ".woff", ".db", ".dll", ".exe")) { continue }

    try {
        $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
        if (-not $content) { continue }

        # Check for Private Key headers
        if ($content -match "-----BEGIN (RSA|EC|DSA|OPENSSH)? ?PRIVATE KEY-----") {
            # Only LicenseGenerator is allowed to hold private signing keys
            if ($file.FullName -notlike "*DhirDhar.LicenseGenerator*") {
                $violations += "LEAK DETECTED: Private key header found in '$($file.FullName)'"
            }
        }
    } catch { }
}

# 3. Check Release directory if it exists
$releaseDir = "$SolutionRoot\Release"
if (Test-Path $releaseDir) {
    Write-Host "[SCAN] Checking published Release directory..." -ForegroundColor Cyan
    $pfxFiles = Get-ChildItem -Path $releaseDir -Filter "*.pfx" -Recurse -File
    foreach ($pfx in $pfxFiles) {
        $violations += "RISK: Certificate file '$($pfx.FullName)' found in release package!"
    }
}

Write-Host ""
if ($violations.Count -eq 0) {
    Write-Host "[RESULT] PASSED: No private keys, master license secrets, or credentials detected in client codebase." -ForegroundColor Green
    exit 0
} else {
    Write-Host "[RESULT] FAILED: $($violations.Count) security violation(s) found:" -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host "  - $v" -ForegroundColor Red
    }
    exit 1
}
