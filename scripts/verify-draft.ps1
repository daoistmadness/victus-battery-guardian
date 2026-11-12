[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$ArtifactsDir = "artifacts",

    [Parameter(Mandatory = $false)]
    [switch]$RequireSigned
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================="
Write-Host "Victus Battery Guardian - Draft Verification"
Write-Host "Scanning directory: $ArtifactsDir"
Write-Host "Require Signed:     $RequireSigned"
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

if ($packslip.signatureStatus) {
    Write-Host "Signature Status: $($packslip.signatureStatus.status) (isSigned = $($packslip.signatureStatus.isSigned))"
    Write-Host "Signing Mechanism: $($packslip.signatureStatus.signingMechanism)"
    if (-not $packslip.signatureStatus.isSigned) {
        Write-Host "Note: Cryptographic signing is incomplete pending Sigstore keyless workflow integration." -ForegroundColor Yellow
        if ($RequireSigned) {
            throw "FATAL: Cryptographic signature is required for production publication, but manifest status is '$($packslip.signatureStatus.status)'. Silent fallback to unsigned artifacts is strictly prohibited."
        }
    }
}
elseif ($RequireSigned) {
    throw "FATAL: Release manifest missing signatureStatus block and -RequireSigned was specified."
}

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

    Write-Host "  -> Verified SHA-256: $actualHash"
}

Write-Host "All release artifacts verified against packslip manifest."
Write-Host "Result: PASS"
