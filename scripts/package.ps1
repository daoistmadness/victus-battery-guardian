[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "artifacts"
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================="
Write-Host "Victus Battery Guardian - Release Packager"
Write-Host "Version:       $Version"
Write-Host "Configuration: $Configuration"
Write-Host "Output:        $OutputDir"
Write-Host "=================================================="

$projectPath = "src/VictusGuardian.App/VictusGuardian.App.csproj"
$stagingDir = Join-Path $OutputDir "staging-win-x64"

if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

Write-Host "Building and publishing self-contained Windows x64 binary..."
$publishArgs = @(
    "publish", $projectPath,
    "-r", "win-x64",
    "-c", $Configuration,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:Version=$Version",
    "-o", $stagingDir
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $stagingDir "VictusGuardian.App.exe"
if (-not (Test-Path $exePath)) {
    throw "Expected binary was not found at $exePath"
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

$packslip = [ordered]@{
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
    verificationPolicy = "packslip-verified-draft"
}

$packslipPath = Join-Path $OutputDir "VictusGuardian-v$Version-packslip.json"
$packslip | ConvertTo-Json -Depth 5 | Out-File -FilePath $packslipPath -Encoding utf8

# Cleanup staging
Remove-Item -Recurse -Force $stagingDir

Write-Host "Packaging completed successfully!"
Write-Host "Packslip generated at: $packslipPath"
Write-Host "Checksum: $fileHash"
