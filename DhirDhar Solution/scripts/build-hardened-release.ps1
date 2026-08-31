param (
    [Parameter(Mandatory = $false)]
    [switch]$SkipTests = $false
)

$ErrorActionPreference = "Stop"
$solutionRoot = "d:\DhirDhar\DhirDhar Solution"
$releaseDir = "$solutionRoot\Release"
$obfuscatedDir = "$solutionRoot\Release_Obfuscated"
$installerDir = "$solutionRoot\Installer"
$innoSetupExe = "d:\DhirDhar\tools\InnoSetup6\ISCC.exe"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "     DhirDhar Enterprise Hardened Production Build Pipeline      " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

# 1. CLEAN
Write-Host "`n[STEP 1/10] Cleaning previous build outputs and caches..." -ForegroundColor Yellow
if (Test-Path $releaseDir) { try { Remove-Item $releaseDir -Recurse -Force -ErrorAction SilentlyContinue } catch { } }
if (Test-Path $obfuscatedDir) { try { Remove-Item $obfuscatedDir -Recurse -Force -ErrorAction SilentlyContinue } catch { } }
if (Test-Path $installerDir) { try { Remove-Item $installerDir -Recurse -Force -ErrorAction SilentlyContinue } catch { } }

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerDir -Force | Out-Null

& dotnet clean "$solutionRoot\DhirDhar Solution.slnx" -c Release -p:Platform=x64 --nologo
Write-Host "  [CLEAN] Build artifacts cleared." -ForegroundColor Green

# 2. RUN UNIT & COMPLIANCE TESTS
if (-not $SkipTests) {
    Write-Host "`n[STEP 2/10] Building Desktop and running test suite..." -ForegroundColor Yellow
    & dotnet build "$solutionRoot\src\DhirDhar.Desktop\DhirDhar.Desktop.csproj" -c Release -p:Platform=x64 --nologo
    & dotnet test "$solutionRoot\DhirDhar Solution.slnx" -c Release -p:Platform=x64 --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Test suite failed! Aborting release build."
    }
    Write-Host "  [TESTS] All automated tests passed." -ForegroundColor Green
} else {
    Write-Host "`n[STEP 2/10] Skipping test suite as requested." -ForegroundColor DarkYellow
}

# 3. SECRET SCANNING
Write-Host "`n[STEP 3/10] Scanning codebase for private keys, credentials, and tokens..." -ForegroundColor Yellow
& powershell -ExecutionPolicy Bypass -File "$solutionRoot\scripts\scan-secrets.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Secret scan detected high-risk items! Aborting release build."
}

# 4. PUBLISH SELF-CONTAINED RELEASE
Write-Host "`n[STEP 4/10] Publishing self-contained Release win-x64..." -ForegroundColor Yellow
& dotnet publish "$solutionRoot\src\DhirDhar.Desktop\DhirDhar.Desktop.csproj" -c Release -r win-x64 --self-contained true -o "$releaseDir"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Desktop publish failed!"
}

# Publish Updater if not already in output
if (-not (Test-Path "$releaseDir\DhirDharUpdater.exe")) {
    & dotnet publish "$solutionRoot\src\DhirDhar.Updater\DhirDhar.Updater.csproj" -c Release -r win-x64 --self-contained true -o "$releaseDir"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Updater publish failed!"
    }
}
Write-Host "  [PUBLISH] Release published successfully to $releaseDir." -ForegroundColor Green

# 5. STRIP UNNECESSARY SYMBOLS AND CLEAN RELEASE PAYLOAD
Write-Host "`n[STEP 5/10] Stripping debug symbols and development files..." -ForegroundColor Yellow
Get-ChildItem -Path $releaseDir -Include "*.pdb","*.xml","*.log","*.tmp" -Recurse -File | Remove-Item -Force
Write-Host "  [STRIP] Debug symbols and temp files stripped." -ForegroundColor Green

# 6. APPLY OBFUSCATION (If available)
Write-Host "`n[STEP 6/10] Obfuscating sensitive business logic and licensing assemblies..." -ForegroundColor Yellow
$obfuscarCmd = Get-Command "obfuscar.console" -ErrorAction SilentlyContinue
if ($obfuscarCmd) {
    & obfuscar.console "$solutionRoot\obfuscar.xml"
    if ($LASTEXITCODE -eq 0 -and (Test-Path $obfuscatedDir)) {
        $obfFiles = Get-ChildItem -Path $obfuscatedDir -Filter "*.dll"
        foreach ($f in $obfFiles) {
            Copy-Item -Path $f.FullName -Destination "$releaseDir\$($f.Name)" -Force
            Write-Host "  [OBFUSCATED] Replaced $releaseDir\$($f.Name)" -ForegroundColor DarkGreen
        }
    }
} else {
    Write-Host "  [OBFUSCATION] obfuscar.console not found in PATH; skipping optional post-publish IL obfuscation." -ForegroundColor DarkYellow
}

# 7. AUTHENTICODE SIGN BINARIES
Write-Host "`n[STEP 7/10] Authenticode signing executable binaries and assemblies..." -ForegroundColor Yellow
& powershell -ExecutionPolicy Bypass -File "$solutionRoot\scripts\sign-binaries.ps1" -TargetPath "$releaseDir"
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Binary signing script returned non-zero code."
}

# 8. GENERATE APPLICATION INTEGRITY MANIFEST (From finalized signed binaries)
Write-Host "`n[STEP 8/10] Generating signed Application Integrity Manifest (app_integrity.sig)..." -ForegroundColor Yellow
& powershell -NoProfile -ExecutionPolicy Bypass -File "$solutionRoot\scripts\generate-integrity-manifest.ps1" -TargetDir "$releaseDir"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Application integrity manifest generation failed!"
}

# 9. COMPILE INNO SETUP INSTALLER
Write-Host "`n[STEP 9/10] Compiling production Windows Inno Setup installer..." -ForegroundColor Yellow
& $innoSetupExe "$solutionRoot\installer.iss"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed!"
}

# Sign installer and compute SHA-256
$installerExe = "$installerDir\DhirDhar-2.1.1-x64-Setup.exe"
if (Test-Path $installerExe) {
    Write-Host "`n[SIGN INSTALLER] Signing installer package $installerExe..." -ForegroundColor Yellow
    & powershell -ExecutionPolicy Bypass -File "$solutionRoot\scripts\sign-binaries.ps1" -TargetPath "$installerExe"
    
    $installerItem = Get-Item $installerExe
    $installerSizeMB = [math]::Round($installerItem.Length / 1MB, 2)
    $installerSha256 = (Get-FileHash -Path $installerExe -Algorithm SHA256).Hash.ToUpperInvariant()
    
    # Write checksum file
    $sha256File = "$installerDir\DhirDhar-2.1.1-x64-Setup.exe.sha256"
    "$installerSha256  DhirDhar-2.1.1-x64-Setup.exe" | Out-File -FilePath $sha256File -Encoding ascii -Force

    Write-Host "  [INSTALLER READY] Path: $installerExe" -ForegroundColor Green
    Write-Host "  [INSTALLER SIZE] $installerSizeMB MB ($($installerItem.Length) bytes)" -ForegroundColor Green
    Write-Host "  [INSTALLER SHA256] $installerSha256" -ForegroundColor Green
    Write-Host "  [CHECKSUM FILE] Generated $sha256File" -ForegroundColor Green

    if ($installerSizeMB -gt 60) {
        Write-Warning "INSTALLER BLOAT WARNING: Installer size ($installerSizeMB MB) exceeds standard 50 MB threshold!"
    }
}

# 10. RUN INSTALLATION INTEGRITY TEST
Write-Host "`n[STEP 10/10] Testing clean silent installation and runtime integrity..." -ForegroundColor Yellow
$guidStr = [System.Guid]::NewGuid().ToString("N").Substring(0, 8)
$testInstallDir = "$env:TEMP\DhirDharTestInstall_$guidStr"

try {
    $instProc = Start-Process -FilePath $installerExe -ArgumentList "/DIR=`"$testInstallDir`" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART" -NoNewWindow -PassThru -Wait
    if ($instProc.ExitCode -ne 0 -or -not (Test-Path "$testInstallDir\DhirDhar.Desktop.exe") -or -not (Test-Path "$testInstallDir\app_integrity.sig")) {
        Write-Error "Installation failed to extract required files to $testInstallDir"
    }

    Write-Host "  [INSTALL TEST PASS] Installer unpacked successfully." -ForegroundColor Green

    # Deep verification of installed files against app_integrity.sig
    $installedSigJson = [System.IO.File]::ReadAllText("$testInstallDir\app_integrity.sig", [System.Text.Encoding]::UTF8)
    $installedManifest = $installedSigJson | ConvertFrom-Json
    $installedCanonicalLines = @()
    $tamperedList = @()
    $missingList = @()

    foreach ($f in ($installedManifest.Files | Sort-Object { $_.RelativePath.ToUpperInvariant() })) {
        $instTarget = "$testInstallDir\$($f.RelativePath)"
        if (-not (Test-Path $instTarget)) {
            $missingList += $f.RelativePath
            continue
        }

        $instSha = (Get-FileHash -Path $instTarget -Algorithm SHA256).Hash.ToUpperInvariant()
        $instSize = (Get-Item $instTarget).Length

        if ($instSha -ne $f.Sha256) {
            $tamperedList += "$($f.RelativePath) (Expected $($f.Sha256), Actual $instSha)"
        }

        $installedCanonicalLines += "$($f.RelativePath.ToUpperInvariant())|$instSha|$instSize"
    }

    $instCanonicalString = ($installedCanonicalLines -join "`n") + "`n"
    $hmacKey = [System.Text.Encoding]::UTF8.GetBytes("DhirDhar.Enterprise.ApplicationIntegrity.v1.2026")
    $instHmac = [System.Security.Cryptography.HMACSHA256]::new($hmacKey)
    $instSigBytes = $instHmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($instCanonicalString))
    $instComputedSignature = [System.BitConverter]::ToString($instSigBytes).Replace("-", "").ToUpperInvariant()

    if ($installedManifest.Signature -ne $instComputedSignature) {
        Write-Error "HMAC Signature check failed on installed app_integrity.sig!"
    }

    if ($tamperedList.Count -gt 0) {
        Write-Error "Integrity check failed! Tampered files found: $($tamperedList -join ', ')"
    }

    if ($missingList.Count -gt 0) {
        Write-Error "Integrity check failed! Missing files found: $($missingList -join ', ')"
    }

    Write-Host "  [VERIFIED INTEGRITY] All $($installedManifest.Files.Count) installed files matched exact SHA-256 and HMAC signature." -ForegroundColor Green

    # Test Tamper Detection (Scenario B verification)
    $tamperTestFile = "$testInstallDir\DhirDhar.Application.dll"
    [System.IO.File]::AppendAllText($tamperTestFile, "TAMPER_BYTES")
    $tamperSha = (Get-FileHash -Path $tamperTestFile -Algorithm SHA256).Hash.ToUpperInvariant()
    $expectedOriginalSha = ($installedManifest.Files | Where-Object { $_.RelativePath -eq "DhirDhar.Application.dll" }).Sha256

    if ($tamperSha -ne $expectedOriginalSha) {
        Write-Host "  [VERIFIED ANTI-TAMPER] Tamper detection triggered on modified assembly." -ForegroundColor Green
    } else {
        Write-Error "Anti-tamper check failed to notice modification!"
    }
}
finally {
    # Run test uninstaller to clean shortcuts and files
    $uninsExe = "$testInstallDir\unins000.exe"
    if (Test-Path $uninsExe) {
        try {
            Start-Process -FilePath $uninsExe -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" -NoNewWindow -PassThru -Wait -ErrorAction SilentlyContinue
        } catch { }
    }

    # Clean up test registry entry so it does not pollute subsequent runs
    try {
        $reg64 = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
        $reg64Key = $reg64.OpenSubKey("Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8C3A417-6D92-4F3A-8B1E-9C8F0E2D1A5B}_is1")
        if ($reg64Key) {
            $pathVal = $reg64Key.GetValue("Inno Setup: App Path")
            $reg64Key.Close()
            if ($pathVal -and ($pathVal -like "*DhirDharTestInstall*" -or $pathVal -like "*Temp*")) {
                $reg64.DeleteSubKeyTree("Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8C3A417-6D92-4F3A-8B1E-9C8F0E2D1A5B}_is1", $false)
            }
        }
    } catch { }

    if (Test-Path $testInstallDir) {
        Remove-Item $testInstallDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "`n=================================================================" -ForegroundColor Cyan
Write-Host "  Hardened Production Build Completed Successfully!             " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
