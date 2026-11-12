[CmdletBinding()]
param ()

$ErrorActionPreference = "Stop"
$testDir = "test-release-harness"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Victus Battery Guardian - Release Pipeline Tests" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

if (Test-Path $testDir) {
    Remove-Item -Recurse -Force $testDir
}
New-Item -ItemType Directory -Path $testDir -Force | Out-Null

$testResults = [ordered]@{}

try {
    # -------------------------------------------------------------
    # Test 1: Checksum-only manifest passes draft verification
    # -------------------------------------------------------------
    Write-Host "`n[TEST 1] Verifying unsigned manifest in draft mode..."
    $dummyZip = Join-Path $testDir "VictusGuardian-v0.2.0-win-x64.zip"
    Set-Content -Path $dummyZip -Value "MOCK_BINARY_PAYLOAD_TEST_DATA"
    $hash = (Get-FileHash -Path $dummyZip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$dummyZip.sha256" -Value "$hash  VictusGuardian-v0.2.0-win-x64.zip"

    $unsignedManifest = [ordered]@{
        manifestSchemaVersion = "1.0.0"
        product = "Victus Battery Guardian"
        version = "0.2.0"
        commit = "test-commit"
        targetRuntime = "win-x64"
        artifacts = @(
            [ordered]@{
                fileName = "VictusGuardian-v0.2.0-win-x64.zip"
                sha256 = $hash
                sizeBytes = (Get-Item $dummyZip).Length
            }
        )
        signatureStatus = [ordered]@{
            isSigned = $false
            status = "unsigned-checksum-manifest"
            signingMechanism = "Pending (Sigstore keyless OIDC)"
        }
    }
    $manifestPath = Join-Path $testDir "VictusGuardian-v0.2.0-packslip.json"
    $unsignedManifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath

    & ./scripts/verify-draft.ps1 -ArtifactsDir $testDir | Out-Null
    $testResults["1_Draft_Unsigned_Manifest_Allowed"] = "PASS"
    Write-Host "  -> Result: PASS" -ForegroundColor Green

    # -------------------------------------------------------------
    # Test 2: Mandatory signing requirement REJECTS unsigned manifest
    # -------------------------------------------------------------
    Write-Host "`n[TEST 2] Verifying -RequireSigned strictly blocks unsigned manifest..."
    $failedAsExpected = $false
    try {
        & ./scripts/verify-draft.ps1 -ArtifactsDir $testDir -RequireSigned | Out-Null
    }
    catch {
        if ($_.Exception.Message -match "Silent fallback to unsigned artifacts is strictly prohibited") {
            $failedAsExpected = $true
        }
    }

    if ($failedAsExpected) {
        $testResults["2_RequireSigned_Blocks_Unsigned"] = "PASS"
        Write-Host "  -> Result: PASS (Rejected unsigned manifest as required)" -ForegroundColor Green
    } else {
        $testResults["2_RequireSigned_Blocks_Unsigned"] = "FAIL"
        Write-Host "  -> Result: FAIL (Did not catch required exception)" -ForegroundColor Red
    }

    # -------------------------------------------------------------
    # Test 3: Signed manifest PASSES -RequireSigned verification
    # -------------------------------------------------------------
    Write-Host "`n[TEST 3] Verifying -RequireSigned passes when cryptographic signature is present..."
    $signedManifest = [ordered]@{
        manifestSchemaVersion = "1.0.0"
        product = "Victus Battery Guardian"
        version = "0.2.0"
        commit = "test-commit"
        targetRuntime = "win-x64"
        artifacts = @(
            [ordered]@{
                fileName = "VictusGuardian-v0.2.0-win-x64.zip"
                sha256 = $hash
                sizeBytes = (Get-Item $dummyZip).Length
            }
        )
        signatureStatus = [ordered]@{
            isSigned = $true
            status = "sigstore-signed"
            signingMechanism = "Sigstore Cosign Keyless OIDC (Fulcio + Rekor)"
            transparencyLogEntry = "https://rekor.sigstore.dev/api/v1/log/entries/test-uuid"
        }
    }
    $signedManifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath

    & ./scripts/verify-draft.ps1 -ArtifactsDir $testDir -RequireSigned | Out-Null
    $testResults["3_RequireSigned_Passes_Signed"] = "PASS"
    Write-Host "  -> Result: PASS" -ForegroundColor Green

    # -------------------------------------------------------------
    # Test 4: Checksum mismatch is detected and rejected
    # -------------------------------------------------------------
    Write-Host "`n[TEST 4] Verifying corrupted artifact is rejected..."
    Set-Content -Path $dummyZip -Value "TAMPERED_OR_CORRUPT_PAYLOAD"
    $tamperDetected = $false
    try {
        & ./scripts/verify-draft.ps1 -ArtifactsDir $testDir | Out-Null
    }
    catch {
        if ($_.Exception.Message -match "Checksum mismatch") {
            $tamperDetected = $true
        }
    }

    if ($tamperDetected) {
        $testResults["4_Tamper_Detection"] = "PASS"
        Write-Host "  -> Result: PASS (Detected corrupted/tampered payload)" -ForegroundColor Green
    } else {
        $testResults["4_Tamper_Detection"] = "FAIL"
        Write-Host "  -> Result: FAIL (Did not detect tamper)" -ForegroundColor Red
    }

    # -------------------------------------------------------------
    # Test 5: Missing artifact is detected and rejected
    # -------------------------------------------------------------
    Write-Host "`n[TEST 5] Verifying missing artifact is rejected..."
    Remove-Item -Force $dummyZip
    $missingDetected = $false
    try {
        & ./scripts/verify-draft.ps1 -ArtifactsDir $testDir | Out-Null
    }
    catch {
        if ($_.Exception.Message -match "Missing expected artifact") {
            $missingDetected = $true
        }
    }

    if ($missingDetected) {
        $testResults["5_Missing_Artifact_Detection"] = "PASS"
        Write-Host "  -> Result: PASS (Detected missing artifact)" -ForegroundColor Green
    } else {
        $testResults["5_Missing_Artifact_Detection"] = "FAIL"
        Write-Host "  -> Result: FAIL (Did not detect missing artifact)" -ForegroundColor Red
    }
}
finally {
    if (Test-Path $testDir) {
        Remove-Item -Recurse -Force $testDir
    }
}

Write-Host "`n=================================================="
Write-Host " Release Pipeline Test Summary"
Write-Host "=================================================="
$allPassed = $true
foreach ($k in $testResults.Keys) {
    Write-Host (" {0,-40} : {1}" -f $k, $testResults[$k])
    if ($testResults[$k] -ne "PASS") { $allPassed = $false }
}

if (-not $allPassed) {
    throw "One or more release pipeline tests failed!"
}

Write-Host "`nALL RELEASE PIPELINE TESTS PASSED!" -ForegroundColor Green
