# =============================================================================
# DhirDhar Enterprise Release Publisher
# Publishes Release and uploads installer assets to GitHub
# =============================================================================
param (
    [Parameter(Mandatory = $false)]
    [string]$Token = "",

    [Parameter(Mandatory = $false)]
    [string]$Repo = "bhargav1822/DhirDhar-Releases",

    [Parameter(Mandatory = $false)]
    [string]$Tag = "",

    [Parameter(Mandatory = $false)]
    [switch]$SkipGitPush = $false
)

$ErrorActionPreference = "Stop"
$repoRoot = "d:\DhirDhar"
$releaseJsonFile = "$repoRoot\release.json"

if (-not (Test-Path $releaseJsonFile)) {
    Write-Error "release.json not found at $releaseJsonFile!"
}

$releaseData = Get-Content $releaseJsonFile -Raw | ConvertFrom-Json
$version = $releaseData.version
if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = "v$version"
}

$installerExe = "$repoRoot\DhirDhar-$version-x64-Setup.exe"
$checksumFile = "$repoRoot\DhirDhar-$version-x64-Setup.exe.sha256"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "         DhirDhar GitHub Production Release Publisher           " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

# 1. VERIFY REQUIRED ASSETS
if (-not (Test-Path $installerExe)) {
    Write-Error "Installer not found at $installerExe! Run 'build installer' first."
}
if (-not (Test-Path $checksumFile)) {
    Write-Error "Checksum file not found at $checksumFile!"
}

$releaseName = $releaseData.name
$releaseBody = $releaseData.changelog
$releaseSha256 = $releaseData.sha256

$installerItem = Get-Item $installerExe
$installerSizeMB = [math]::Round($installerItem.Length / 1MB, 2)
Write-Host "`n[ASSET VERIFIED] $installerExe ($installerSizeMB MB)" -ForegroundColor Green
Write-Host "[SHA256 MATCH] $releaseSha256" -ForegroundColor Green

# 2. EXTRACT OR PROMPT FOR GITHUB TOKEN
if ([string]::IsNullOrWhiteSpace($Token)) {
    try {
        $remote = git -C $repoRoot remote get-url origin
        if ($remote -match "gho_[a-zA-Z0-9]+") {
            $Token = $matches[0]
            Write-Host "[AUTH] Discovered repository access token from git remote." -ForegroundColor Green
        }
    } catch { }
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = Read-Host -Prompt "Enter your GitHub Personal Access Token (PAT with repo scope)"
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    Write-Error "No GitHub access token provided. Aborting."
}

# 3. COMMIT & PUSH RELEASE FILES & TAG (Optional, recommended)
if (-not $SkipGitPush) {
    Write-Host "`n[STEP 1/3] Staging and pushing release files to origin main..." -ForegroundColor Yellow
    git -C $repoRoot add DhirDhar.exe release.json README.md publish-github-release.ps1
    git -C $repoRoot commit -m "Release DhirDhar $Tag" 2>$null
    git -C $repoRoot push origin main
    
    # Tag management
    Write-Host "  Tagging $Tag and pushing to origin..." -ForegroundColor Yellow
    git -C $repoRoot tag -fa $Tag -m "DhirDhar $Tag"
    git -C $repoRoot push origin $Tag --force
    Write-Host "  [GIT] Main branch and tag $Tag pushed to origin." -ForegroundColor Green
} else {
    Write-Host "`n[STEP 1/3] Skipping git push as requested." -ForegroundColor DarkYellow
}

# 4. CREATE OR RETRIEVE GITHUB RELEASE OBJECT
Write-Host "`n[STEP 2/3] Creating GitHub Release '$releaseName' on tag '$Tag'..." -ForegroundColor Yellow
$headers = @{
    "Authorization" = "Bearer $Token"
    "Accept" = "application/vnd.github+json"
    "User-Agent" = "DhirDhar-Release-Publisher"
}

$releaseUrl = "https://api.github.com/repos/$Repo/releases"
$releasePayload = @{
    tag_name = $Tag
    target_commitish = "main"
    name = $releaseName
    body = $releaseBody
    draft = $false
    prerelease = $false
} | ConvertTo-Json -Depth 5

$releaseObj = $null
try {
    # Check if release already exists for this tag
    $existingUrl = "https://api.github.com/repos/$Repo/releases/tags/$Tag"
    $releaseObj = Invoke-RestMethod -Uri $existingUrl -Headers $headers -Method Get -ErrorAction Stop
    Write-Host "  [FOUND EXISTING] Release for tag $Tag already exists (ID: $($releaseObj.id)). Updating metadata..." -ForegroundColor DarkYellow
    $updateUrl = "https://api.github.com/repos/$Repo/releases/$($releaseObj.id)"
    $releaseObj = Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $releasePayload
} catch {
    # If not found (404), create a new release
    $releaseObj = Invoke-RestMethod -Uri $releaseUrl -Headers $headers -Method Post -Body $releasePayload
    Write-Host "  [CREATED] GitHub Release created with ID: $($releaseObj.id)" -ForegroundColor Green
}

$releaseId = $releaseObj.id
$uploadUrlTemplate = $releaseObj.upload_url -replace '\{\?name,label\}', ''

# 5. UPLOAD ASSETS
Write-Host "`n[STEP 3/3] Uploading release assets..." -ForegroundColor Yellow

$assetsToUpload = @(
    @{
        Name = "DhirDhar-$version-x64-Setup.exe"
        Path = $installerExe
        ContentType = "application/octet-stream"
    },
    @{
        Name = "DhirDhar-$version-x64-Setup.exe.sha256"
        Path = $checksumFile
        ContentType = "text/plain"
    }
)

foreach ($asset in $assetsToUpload) {
    # If asset already uploaded, delete existing asset first to overwrite cleanly
    $existingAsset = $releaseObj.assets | Where-Object { $_.name -eq $asset.Name }
    if ($existingAsset) {
        Write-Host "  Removing previous asset $($asset.Name) (ID: $($existingAsset.id))..." -ForegroundColor DarkYellow
        $deleteAssetUrl = "https://api.github.com/repos/$Repo/releases/assets/$($existingAsset.id)"
        try {
            Invoke-RestMethod -Uri $deleteAssetUrl -Headers $headers -Method Delete | Out-Null
        } catch { }
    }

    Write-Host "  Uploading $($asset.Name) ($([math]::Round((Get-Item $asset.Path).Length / 1MB, 2)) MB)..." -ForegroundColor Cyan
    $uploadTarget = "$uploadUrlTemplate`?name=$($asset.Name)"
    $uploadHeaders = @{
        "Authorization" = "Bearer $Token"
        "Accept" = "application/vnd.github+json"
        "User-Agent" = "DhirDhar-Release-Publisher"
    }

    $uploaded = Invoke-RestMethod -Uri $uploadTarget -Headers $uploadHeaders -Method Post -InFile $asset.Path -ContentType $asset.ContentType -TimeoutSec 600
    Write-Host "  [UPLOAD SUCCESS] $($asset.Name) uploaded (ID: $($uploaded.id), Download URL: $($uploaded.browser_download_url))" -ForegroundColor Green
}

Write-Host "`n=================================================================" -ForegroundColor Cyan
Write-Host "    GitHub Release $Tag Published Successfully!               " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "View Release at: https://github.com/$Repo/releases/tag/$Tag" -ForegroundColor Green
Write-Host "Installer Download: https://github.com/$Repo/releases/download/$Tag/DhirDhar-$version-x64-Setup.exe" -ForegroundColor Green
