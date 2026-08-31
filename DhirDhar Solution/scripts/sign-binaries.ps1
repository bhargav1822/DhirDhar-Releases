param (
    [Parameter(Mandatory = $false)]
    [string]$TargetPath = "d:\DhirDhar\DhirDhar Solution\Release",

    [Parameter(Mandatory = $false)]
    [string]$CertificateThumbprint = ""
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "   DhirDhar Authenticode Binary Signing Pipeline   " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Locate signtool.exe
$signtoolPaths = @(
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"
)

$signtool = $null
foreach ($path in $signtoolPaths) {
    if (Test-Path $path) {
        $signtool = $path
        break
    }
}

if (-not $signtool) {
    $discovered = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Filter "signtool.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
    if ($discovered) { $signtool = $discovered }
}

if (-not $signtool) {
    Write-Warning "signtool.exe not found in Windows Kits. Using PowerShell Set-AuthenticodeSignature fallback."
} else {
    Write-Host "[SIGNTOOL] Found signtool at: $signtool" -ForegroundColor Green
}

# 2. Locate or Create Code Signing Certificate
$cert = $null
if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $cert = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
    if (-not $cert) {
        $cert = Get-Item "Cert:\LocalMachine\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
    }
}

if (-not $cert) {
    # Look for existing code signing cert
    $certs = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue
    if ($certs) {
        $cert = $certs | Where-Object { $_.Subject -like "*DhirDhar*" } | Select-Object -First 1
        if (-not $cert) { $cert = $certs | Select-Object -First 1 }
    }
}

if (-not $cert) {
    Write-Host "[CERT] Creating local dedicated Authenticode code signing certificate in CurrentUser\My..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate -Type CodeSigningCert `
        -Subject "CN=DhirDhar Production Release, O=DhirDhar Solutions, C=IN" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(5) `
        -FriendlyName "DhirDhar Authenticode Release Certificate"
    Write-Host "[CERT] Created certificate with Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green
} else {
    Write-Host "[CERT] Using Code Signing Certificate: $($cert.Subject) [Thumbprint: $($cert.Thumbprint)]" -ForegroundColor Green
}

# 3. Identify files to sign
$filesToSign = @()
if (Test-Path $TargetPath -PathType Leaf) {
    $filesToSign += (Get-Item $TargetPath)
} elseif (Test-Path $TargetPath -PathType Container) {
    $filesToSign = Get-ChildItem -Path $TargetPath -Include "DhirDhar*.exe","DhirDhar*.dll" -Recurse -File
}

Write-Host "[SIGNING] Found $($filesToSign.Count) file(s) to sign." -ForegroundColor Cyan

$signedCount = 0
$failedCount = 0

foreach ($file in $filesToSign) {
    $filePath = $file.FullName
    $signed = $false

    if ($signtool) {
        # Try signtool with timestamp server first
        $proc = Start-Process -FilePath $signtool -ArgumentList "sign /fd SHA256 /sha1 $($cert.Thumbprint) /tr http://timestamp.digicert.com /td SHA256 `"$filePath`"" -NoNewWindow -PassThru -Wait
        if ($proc.ExitCode -eq 0) {
            $signed = $true
        } else {
            # Retry without timestamp if offline or network failure
            $procRetry = Start-Process -FilePath $signtool -ArgumentList "sign /fd SHA256 /sha1 $($cert.Thumbprint) `"$filePath`"" -NoNewWindow -PassThru -Wait
            if ($procRetry.ExitCode -eq 0) {
                $signed = $true
            }
        }
    }

    if (-not $signed) {
        # Fallback to Set-AuthenticodeSignature
        try {
            $status = Set-AuthenticodeSignature -FilePath $filePath -Certificate $cert -HashAlgorithm SHA256
            if ($status.Status -eq "Valid" -or $status.Status -eq "UnknownError") {
                $signed = $true
            }
        } catch {
            Write-Warning "Set-AuthenticodeSignature failed on $filePath : $_"
        }
    }

    if ($signed) {
        $signedCount++
        Write-Host "  [SIGNED] $filePath" -ForegroundColor DarkGreen
    } else {
        $failedCount++
        Write-Warning "  [FAILED] $filePath"
    }
}

Write-Host "`n[SUMMARY] Successfully signed: $signedCount, Failed: $failedCount" -ForegroundColor Cyan
if ($failedCount -gt 0) {
    exit 1
}
exit 0
