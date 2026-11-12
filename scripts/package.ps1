[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$Version = "0.3.0-dev",

    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "artifacts",

    [Parameter(Mandatory = $false)]
    [string]$CandidatePackage = $null,

    [Parameter(Mandatory = $false)]
    [string]$CandidateDiagnosticsPackage = $null
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================="
Write-Host "Victus Battery Guardian - Packaging Script"
Write-Host "Version:           $Version"
Write-Host "Configuration:     $Configuration"
Write-Host "Output:            $OutputDir"
if ($CandidatePackage) {
    Write-Host "Candidate Package: $CandidatePackage (Immutable Promotion Mode)"
}
Write-Host "=================================================="

# Ensure output directory exists and purge stale artifacts for this version
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$userZipFileName = "VictusGuardian-v$Version-win-x64.zip"
$userZipFilePath = Join-Path $OutputDir $userZipFileName

$diagZipFileName = "VictusGuardian-v$Version-diagnostics.zip"
$diagZipFilePath = Join-Path $OutputDir $diagZipFileName

if ($CandidatePackage) {
    if (-not (Test-Path $CandidatePackage)) {
        throw "Candidate package not found at: $CandidatePackage"
    }

    Write-Host "Reusing pre-built candidate package without recompilation: $CandidatePackage"
    if ((Resolve-Path $CandidatePackage).Path -ne (Resolve-Path $userZipFilePath -ErrorAction SilentlyContinue).Path) {
        Copy-Item -Path $CandidatePackage -Destination $userZipFilePath -Force
    }

    # Diagnostics package promotion
    $diagCandidate = $CandidateDiagnosticsPackage
    if (-not $diagCandidate) {
        $candidateDir = Split-Path -Parent $CandidatePackage
        $potentialDiag = Join-Path $candidateDir $diagZipFileName
        if (Test-Path $potentialDiag) {
            $diagCandidate = $potentialDiag
        }
    }

    if ($diagCandidate -and (Test-Path $diagCandidate)) {
        Write-Host "Reusing candidate diagnostics package: $diagCandidate"
        if ((Resolve-Path $diagCandidate).Path -ne (Resolve-Path $diagZipFilePath -ErrorAction SilentlyContinue).Path) {
            Copy-Item -Path $diagCandidate -Destination $diagZipFilePath -Force
        }
    }
} else {
    # Purge stale artifacts for this version when doing a fresh compilation
    Get-ChildItem -Path $OutputDir -Filter "VictusGuardian-v$Version*" | Remove-Item -Force

    $projectPath = "src/VictusGuardian.App/VictusGuardian.App.csproj"
    $buildStaging = Join-Path $OutputDir "staging-build"
    $userStaging = Join-Path $OutputDir "staging-user"
    $diagStaging = Join-Path $OutputDir "staging-diag"

    foreach ($dir in @($buildStaging, $userStaging, $diagStaging)) {
        if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    Write-Host "Publishing self-contained win-x64 binary..."
    dotnet publish $projectPath `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:Version=$Version `
        -o $buildStaging

    $exePath = Join-Path $buildStaging "VictusGuardian.App.exe"
    if (-not (Test-Path $exePath)) {
        throw "Published executable not found at: $exePath"
    }

    # A. Staging User Package (Clean: exe + README + LICENSE only)
    Write-Host "Preparing User Package staging (excluding PDBs, scripts, and internal docs)..."
    Copy-Item $exePath -Destination $userStaging
    if (Test-Path "README.md") { Copy-Item "README.md" -Destination $userStaging }
    if (Test-Path "LICENSE") { Copy-Item "LICENSE" -Destination $userStaging }

    Write-Host "Creating user release archive: $userZipFilePath..."
    Compress-Archive -Path "$userStaging/*" -DestinationPath $userZipFilePath -Force

    # B. Staging Diagnostics Package (PDBs + validation script + validation guides)
    Write-Host "Preparing Diagnostics Package staging..."
    Get-ChildItem -Path $buildStaging -Filter "*.pdb" | ForEach-Object {
        Copy-Item $_.FullName -Destination $diagStaging
    }
    if (Test-Path "scripts/hardware-validate.ps1") { Copy-Item "scripts/hardware-validate.ps1" -Destination $diagStaging }
    if (Test-Path "docs/HARDWARE_VALIDATION.md") { Copy-Item "docs/HARDWARE_VALIDATION.md" -Destination $diagStaging }
    if (Test-Path "docs/RELEASE_AND_PACKSLIP.md") { Copy-Item "docs/RELEASE_AND_PACKSLIP.md" -Destination $diagStaging }
    if (Test-Path "README.md") { Copy-Item "README.md" -Destination $diagStaging }
    if (Test-Path "LICENSE") { Copy-Item "LICENSE" -Destination $diagStaging }

    Write-Host "Creating diagnostics archive: $diagZipFilePath..."
    Compress-Archive -Path "$diagStaging/*" -DestinationPath $diagZipFilePath -Force

    # Cleanup staging directories
    Remove-Item -Recurse -Force $buildStaging
    Remove-Item -Recurse -Force $userStaging
    Remove-Item -Recurse -Force $diagStaging
}

Write-Host "Computing SHA-256 hashes..."
$userHash = (Get-FileHash -Path $userZipFilePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$userHash  $userZipFileName" | Out-File -FilePath "$userZipFilePath.sha256" -Encoding utf8

$diagHash = $null
if (Test-Path $diagZipFilePath) {
    $diagHash = (Get-FileHash -Path $diagZipFilePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$diagHash  $diagZipFileName" | Out-File -FilePath "$diagZipFilePath.sha256" -Encoding utf8
}

$gitCommit = "initial-commit"
try {
    $commitOutput = git rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($commitOutput)) {
        $gitCommit = $commitOutput.Trim()
    }
} catch {
    $gitCommit = "uncommitted"
}

# Packslip manifest - Checksum-only draft; explicitly marked as unsigned pending Sigstore integration
$artifactsList = @(
    [ordered]@{
        fileName = $userZipFileName
        sha256 = $userHash
        sizeBytes = (Get-Item $userZipFilePath).Length
    }
)

if ($diagHash -and (Test-Path $diagZipFilePath)) {
    $artifactsList += [ordered]@{
        fileName = $diagZipFileName
        sha256 = $diagHash
        sizeBytes = (Get-Item $diagZipFilePath).Length
    }
}

$packslip = [ordered]@{
    manifestSchemaVersion = "1.0.0"
    product = "Victus Battery Guardian"
    version = $Version
    commit = $gitCommit
    builtAtUtc = [DateTime]::UtcNow.ToString("o")
    targetRuntime = "win-x64"
    artifacts = $artifactsList
    signatureStatus = [ordered]@{
        isSigned = $false
        status = "unsigned-checksum-manifest"
        signingMechanism = "Pending (Sigstore keyless OIDC via GitHub Actions workflow)"
        details = "This manifest contains computed SHA-256 digests. Cryptographic signature and transparency log entry require Sigstore cosign/gh release integration."
    }
}

$packslipPath = Join-Path $OutputDir "VictusGuardian-v$Version-packslip.json"
$packslip | ConvertTo-Json -Depth 5 | Out-File -FilePath $packslipPath -Encoding utf8

Write-Host "Packaging completed successfully!"
Write-Host "Packslip generated at: $packslipPath"
Write-Host "User package checksum: $userHash"
if ($diagHash) {
    Write-Host "Diagnostics checksum:  $diagHash"
}
Write-Host "Signature status: $($packslip.signatureStatus.status) (isSigned = $($packslip.signatureStatus.isSigned))"
