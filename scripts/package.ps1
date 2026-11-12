[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$Version = "0.2.0-beta",

    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "artifacts"
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================="
Write-Host "Victus Battery Guardian - Packaging Script"
Write-Host "Version:       $Version"
Write-Host "Configuration: $Configuration"
Write-Host "Output:        $OutputDir"
Write-Host "=================================================="

# Ensure output directory exists
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$projectPath = "src/VictusGuardian.App/VictusGuardian.App.csproj"
$stagingDir = Join-Path $OutputDir "staging"

if (Test-Path $stagingDir) {
    Remove-Item -Recurse -Force $stagingDir
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
    -o $stagingDir

$exePath = Join-Path $stagingDir "VictusGuardian.App.exe"
if (-not (Test-Path $exePath)) {
    throw "Published executable not found at: $exePath"
}

# Copy documentation, licenses, and validation tooling
$docsToInclude = @(
    "README.md",
    "LICENSE",
    "docs/HARDWARE_VALIDATION.md",
    "docs/RELEASE_AND_PACKSLIP.md",
    "scripts/hardware-validate.ps1"
)
foreach ($doc in $docsToInclude) {
    if (Test-Path $doc) {
        Copy-Item $doc -Destination $stagingDir
    }
}

$zipFileName = "VictusGuardian-v$Version-win-x64.zip"
$zipFilePath = Join-Path $OutputDir $zipFileName

Write-Host "Creating release archive: $zipFilePath..."
Compress-Archive -Path "$stagingDir/*" -DestinationPath $zipFilePath -Force

Write-Host "Computing SHA-256 hash..."
$fileHash = (Get-FileHash -Path $zipFilePath -Algorithm SHA256).Hash.ToLowerInvariant()
$sha256FilePath = "$zipFilePath.sha256"
"$fileHash  $zipFileName" | Out-File -FilePath $sha256FilePath -Encoding utf8

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
$packslip = [ordered]@{
    manifestSchemaVersion = "1.0.0"
    product = "Victus Battery Guardian"
    version = $Version
    commit = $gitCommit
    builtAtUtc = [DateTime]::UtcNow.ToString("o")
    targetRuntime = "win-x64"
    artifacts = @(
        [ordered]@{
            fileName = $zipFileName
            sha256 = $fileHash
            sizeBytes = (Get-Item $zipFilePath).Length
        }
    )
    signatureStatus = [ordered]@{
        isSigned = $false
        status = "unsigned-checksum-manifest"
        signingMechanism = "Pending (Sigstore keyless OIDC via GitHub Actions workflow)"
        details = "This manifest contains computed SHA-256 digests. Cryptographic signature and transparency log entry require Sigstore cosign/gh release integration."
    }
}

$packslipPath = Join-Path $OutputDir "VictusGuardian-v$Version-packslip.json"
$packslip | ConvertTo-Json -Depth 5 | Out-File -FilePath $packslipPath -Encoding utf8

# Cleanup staging
Remove-Item -Recurse -Force $stagingDir

Write-Host "Packaging completed successfully!"
Write-Host "Packslip generated at: $packslipPath"
Write-Host "Checksum: $fileHash"
Write-Host "Signature status: $($packslip.signatureStatus.status) (isSigned = $($packslip.signatureStatus.isSigned))"
