[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$ArtifactsDir = "artifacts",

    [Parameter(Mandatory = $false)]
    [switch]$RequireSigned,

    [Parameter(Mandatory = $false)]
    [switch]$RequireProvenance,

    [Parameter(Mandatory = $false)]
    [string]$ExpectedRepo = "daoistmadness/victus-battery-guardian",

    [Parameter(Mandatory = $false)]
    [string]$ExpectedWorkflow = "",

    [Parameter(Mandatory = $false)]
    [string]$ExpectedCommit = ""
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================="
Write-Host "Victus Battery Guardian - Release Verification Gate"
Write-Host "Scanning directory: $ArtifactsDir"
Write-Host "Require Signed:     $RequireSigned"
Write-Host "Require Provenance: $RequireProvenance"
if ($ExpectedRepo) { Write-Host "Expected Repo:      $ExpectedRepo" }
if ($ExpectedWorkflow) { Write-Host "Expected Workflow:  $ExpectedWorkflow" }
if ($ExpectedCommit) { Write-Host "Expected Commit:    $ExpectedCommit" }
Write-Host "=================================================="

if (-not (Test-Path $ArtifactsDir)) {
    throw "Artifacts directory not found: $ArtifactsDir"
}

$packslipFiles = Get-ChildItem -Path $ArtifactsDir -Filter "*packslip.json"
if ($packslipFiles.Count -eq 0) {
    throw "No packslip JSON found in $ArtifactsDir"
}

$packslipFile = $packslipFiles[0]
Write-Host "Loading packslip: $($packslipFile.FullName)"
$packslip = Get-Content -Path $packslipFile.FullName -Raw | ConvertFrom-Json

Write-Host "Product:          $($packslip.product)"
Write-Host "Version:          $($packslip.version)"
Write-Host "Commit:           $($packslip.commit)"
Write-Host "Target Runtime:   $($packslip.targetRuntime)"

# --- TIER 1: SHA-256 DIGEST INTEGRITY & PACKAGE CONTENT AUDIT ---
Write-Host "`n--- [TIER 1] Integrity & Content Audit ---" -ForegroundColor Cyan

foreach ($artifact in $packslip.artifacts) {
    $expectedName = $artifact.fileName
    $expectedHash = $artifact.sha256.ToLowerInvariant()
    $artifactPath = Join-Path $ArtifactsDir $expectedName

    if (-not (Test-Path $artifactPath)) {
        throw "Missing expected artifact: $artifactPath"
    }

    Write-Host "Verifying checksum for $expectedName..."
    $actualHash = (Get-FileHash -Path $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()

    if ($actualHash -ne $expectedHash) {
        throw "Checksum mismatch for $expectedName! Expected: $expectedHash, Actual: $actualHash"
    }

    $shaFile = "$artifactPath.sha256"
    if (Test-Path $shaFile) {
        $shaContent = Get-Content -Path $shaFile -Raw
        if (-not $shaContent.Contains($expectedHash)) {
            throw "SHA256 file $shaFile does not match expected hash!"
        }
    }

    # Content quality audit for user zip packages
    if ($expectedName -like "*win-x64.zip" -and -not ($expectedName -like "*diagnostics*") -and -not ($expectedName -like "*framework-dependent*")) {
        Write-Host "Auditing package contents for $expectedName..."
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
        $zip = [System.IO.Compression.ZipFile]::OpenRead($artifactPath)
        try {
            $entries = @($zip.Entries | ForEach-Object { $_.FullName })

            # 1. Must contain required files
            $requiredFiles = @("VictusGuardian.App.exe", "README.md", "LICENSE", "VERSION.txt")
            foreach ($req in $requiredFiles) {
                if ($entries -notcontains $req) {
                    throw "Package content audit failed: $expectedName is missing required file '$req'"
                }
            }

            # 2. Must NOT contain development or debug files
            $forbiddenPatterns = @("*.pdb", "*.ps1", "*.sh", "*.cs")
            foreach ($pattern in $forbiddenPatterns) {
                $matches = @($entries | Where-Object { $_ -like $pattern })
                if ($matches.Count -gt 0) {
                    throw "Package content audit failed: $expectedName contains forbidden development artifact(s): $($matches -join ', ')"
                }
            }
            Write-Host "  -> Content audit PASSED (contains required files, zero PDBs/scripts)"
        }
        finally {
            $zip.Dispose()
        }
    }

    Write-Host "  -> Verified SHA-256: $actualHash"
}
Write-Host "Tier 1 (Digest Integrity & Packaging): PASS" -ForegroundColor Green

# --- TIER 2: CRYPTOGRAPHIC BUILD PROVENANCE (SIGSTORE / GITHUB ATTESTATIONS) ---
Write-Host "`n--- [TIER 2] Cryptographic Build Provenance ---" -ForegroundColor Cyan

$provenanceVerified = $false
$ghCmd = Get-Command "gh" -ErrorAction SilentlyContinue
$bundleFiles = @(Get-ChildItem -Path $ArtifactsDir -Filter "*attestation*.jsonl")
$bundleFiles += @(Get-ChildItem -Path $ArtifactsDir -Filter "*packslip*.bundle")

# Anti-fabrication check: manifest claiming isSigned: true without genuine attestation/bundle
if ($packslip.signatureStatus) {
    if ($packslip.signatureStatus.isSigned -eq $true -and $bundleFiles.Count -eq 0) {
        throw "SECURITY VIOLATION: Packslip manifest claims 'isSigned: true', but no accompanying cryptographic signature bundle (*attestation*.jsonl or *.bundle) was found in $ArtifactsDir. Treating unverified claims as signed is strictly prohibited."
    }
}

if ($ghCmd) {
    $mainZip = Join-Path $ArtifactsDir "VictusGuardian-v$($packslip.version)-win-x64.zip"
    if (Test-Path $mainZip) {
        try {
            $verifyArgs = @("attestation", "verify", $mainZip, "--repo", $ExpectedRepo, "--format", "json")
            if ($bundleFiles.Count -gt 0) {
                $verifyArgs += @("--bundle", $bundleFiles[0].FullName)
            }
            if (-not [string]::IsNullOrWhiteSpace($ExpectedWorkflow)) {
                $verifyArgs += @("--signer-workflow", $ExpectedWorkflow)
            }

            Write-Host "Executing cryptographic attestation verification via GitHub CLI..."
            $attestOutput = & $ghCmd.Source $verifyArgs 2>&1
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($attestOutput)) {
                Write-Host "  -> Cryptographic attestation signature verified via Sigstore / Rekor!" -ForegroundColor Green
                $provenanceVerified = $true

                # If commit verification is requested, check commit claim in attestation statement
                if (-not [string]::IsNullOrWhiteSpace($ExpectedCommit)) {
                    if ($attestOutput -notmatch $ExpectedCommit) {
                        throw "Attestation verification failed: source commit does not match expected SHA $ExpectedCommit"
                    }
                    Write-Host "  -> Provenance matches expected source commit: $ExpectedCommit"
                }
            } else {
                Write-Host "  -> No verified online or bundle attestation found: $attestOutput" -ForegroundColor Yellow
            }
        } catch {
            Write-Host "  -> Attestation verification check error: $_" -ForegroundColor Yellow
            if ($RequireProvenance -or $RequireSigned) {
                throw $_
            }
        }
    }
} else {
    Write-Host "Note: GitHub CLI (gh) not found in PATH; skipping external attestation verification." -ForegroundColor Yellow
}

if (-not $provenanceVerified) {
    if ($RequireProvenance -or $RequireSigned) {
        throw "FATAL: Cryptographic build provenance is strictly required, but no valid attestation could be cryptographically verified. Rejecting publication."
    }
    Write-Host "Tier 2 (Cryptographic Provenance): PENDING / UNSIGNED (Preview Channel)" -ForegroundColor Yellow
} else {
    Write-Host "Tier 2 (Cryptographic Provenance): PASS (Sigstore Authenticated)" -ForegroundColor Green
}

# --- TIER 3: WINDOWS AUTHENTICODE SIGNING ---
Write-Host "`n--- [TIER 3] Windows Authenticode Signing ---" -ForegroundColor Cyan
Write-Host "Status: PENDING (Unsigned Developer Distribution)"
Write-Host "Note:   Authenticode certificate signing is pending hardware token or HSM integration."
Write-Host "        Windows SmartScreen untrusted publisher prompt is expected on first launch."
Write-Host "Tier 3 (Windows Authenticode): PENDING" -ForegroundColor Yellow

Write-Host "`n=================================================="
Write-Host "Verification Gate Completed."
Write-Host "Result: PASS"
Write-Host "=================================================="
