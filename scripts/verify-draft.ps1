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

    if ($packslip.signatureStatus.isSigned -eq $true) {
        # Never treat isSigned: true as proof without genuine cryptographic evidence
        $sigFiles = @(Get-ChildItem -Path $ArtifactsDir -Filter "*packslip*.bundle")
        $sigFiles += @(Get-ChildItem -Path $ArtifactsDir -Filter "*packslip*.sig")
        if ($sigFiles.Count -eq 0) {
            throw "SECURITY VIOLATION: Packslip manifest claims 'isSigned: true', but no accompanying cryptographic signature bundle (*.bundle or *.sig) was found in $ArtifactsDir. Treating unverified claims as signed is strictly prohibited."
        }
        Write-Host "  -> Verified cryptographic signature bundle present: $($sigFiles[0].Name)"
    }
    elseif ($RequireSigned) {
        throw "FATAL: Cryptographic signature is required for production publication, but manifest status is '$($packslip.signatureStatus.status)'. Silent fallback to unsigned artifacts is strictly prohibited."
    }
    else {
        Write-Host "Note: Cryptographic signing is marked unsigned. Preview/development distribution only." -ForegroundColor Yellow
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

Write-Host "All release artifacts verified against packslip manifest."
Write-Host "Result: PASS"
