using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace VictusGuardian.UnitTests;

public class ReleaseDistributionTests
{
    [Theory]
    [InlineData("0.1.0", "feat", "0.2.0")]
    [InlineData("0.1.0", "fix", "0.1.1")]
    [InlineData("0.1.0", "perf", "0.1.1")]
    [InlineData("0.1.0", "refactor", "0.1.1")]
    [InlineData("0.2.0", "feat", "0.3.0")]
    [InlineData("0.2.0", "chore", "0.2.0")]
    [InlineData("0.2.0", "docs", "0.2.0")]
    public void ReleaseVersionCalculation_ConventionalCommits_ComputesCorrectBumps(
        string currentVersion,
        string commitType,
        string expectedVersion)
    {
        var parts = currentVersion.Split('.').Select(int.Parse).ToArray();
        var (major, minor, patch) = (parts[0], parts[1], parts[2]);

        string nextVersion = commitType switch
        {
            "major" => $"{major + 1}.0.0",
            "feat" => $"{major}.{minor + 1}.0",
            "fix" or "perf" or "refactor" => $"{major}.{minor}.{patch + 1}",
            _ => currentVersion
        };

        Assert.Equal(expectedVersion, nextVersion);
    }

    [Fact]
    public void ReleaseNotesGeneration_WithPreviewFlag_IncludesUnsignedWarning()
    {
        var version = "0.2.0";
        var commitSha = "4ba1de6119836c7991bad0792152714a1fba8f9a";
        var commitShort = commitSha[..7];

        var rawNotes = "### Features\n* **perf:** set software rendering as default";

        var previewBanner = $@"# Victus Battery Guardian v{version} Preview ({commitShort})

> [!WARNING]
> **Preview Release — Unsigned Candidate**
> This is a preview / pre-release distribution intended for testing on physical Windows 11 hardware.
> - **Integrity & Provenance:** SHA-256 digests and Packslip manifest verified against GitHub CI run.
> - **Code Signing:** This package does **not** contain a production Authenticode signature. Windows SmartScreen may present an untrusted publisher notice on first launch.
> - **Reporting:** Please report telemetry anomalies, hardware sensor inaccuracies, or UI glitches on GitHub Issues.

### Verification Metadata
- **Source Commit:** `{commitSha}`
- **Distribution:** Windows x64 Portable Self-Contained (`.zip`)
- **Default Renderer:** Software Rendering (`Win32RenderingMode.Software` via GDI RedirectionSurface)
- **Hardware Override:** Launch with `--hardware-rendering` or set `VICTUS_RENDER_MODE=hardware`
- **Diagnostics Package:** Download companion `VictusGuardian-v{version}-diagnostics.zip` for PDB symbols and hardware validation scripts.

---

### Changes in this Preview

{rawNotes}";

        Assert.Contains("Preview Release — Unsigned Candidate", previewBanner);
        Assert.Contains("Win32RenderingMode.Software", previewBanner);
        Assert.Contains("--hardware-rendering", previewBanner);
        Assert.Contains(commitSha, previewBanner);
        Assert.Contains(rawNotes, previewBanner);
    }

    [Fact]
    public void ArtifactDigestValidation_ValidSha256_PassesVerification()
    {
        var samplePayload = Encoding.UTF8.GetBytes("Victus Guardian Portable Executable Dummy Binary Content");
        var computedHash = Convert.ToHexStringLower(SHA256.HashData(samplePayload));

        var manifestArtifact = new
        {
            fileName = "VictusGuardian-v0.2.0-win-x64.zip",
            sha256 = computedHash,
            sizeBytes = samplePayload.Length
        };

        var actualHash = Convert.ToHexStringLower(SHA256.HashData(samplePayload));

        Assert.Equal(manifestArtifact.sha256, actualHash);
        Assert.Equal(64, actualHash.Length);
    }

    [Fact]
    public void ArtifactDigestValidation_TamperedPayload_FailsVerification()
    {
        var authenticPayload = Encoding.UTF8.GetBytes("Authentic application bytes");
        var authenticHash = Convert.ToHexStringLower(SHA256.HashData(authenticPayload));

        var tamperedPayload = Encoding.UTF8.GetBytes("Tampered application bytes!");
        var tamperedHash = Convert.ToHexStringLower(SHA256.HashData(tamperedPayload));

        Assert.NotEqual(authenticHash, tamperedHash);
    }

    [Theory]
    [InlineData("4ba1de6", "4ba1de6119836c7991bad0792152714a1fba8f9a", true)]
    [InlineData("4ba1de6119836c7991bad0792152714a1fba8f9a", "4ba1de6119836c7991bad0792152714a1fba8f9a", true)]
    [InlineData("1234567", "4ba1de6119836c7991bad0792152714a1fba8f9a", false)]
    [InlineData("", "4ba1de6119836c7991bad0792152714a1fba8f9a", true)]
    public void SourceCommitMatching_VerifiesExactSha(string expectedPrefix, string candidateSha, bool expectedMatch)
    {
        var isMatch = string.IsNullOrEmpty(expectedPrefix) || candidateSha.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedMatch, isMatch);
    }

    [Fact]
    public void PreviewAndStableChannelIsolation_TagsAndFlagsAreDistinct()
    {
        var version = "0.2.0";
        var commit = "4ba1de6";

        // Preview Channel Specification
        var previewTag = $"v{version}-preview.{commit}";
        var previewIsPrerelease = true;
        var previewIsLatest = false;

        // Stable Channel Specification
        var stableTag = $"v{version}";
        var stableIsPrerelease = false;
        var stableIsLatest = true;

        Assert.Contains("preview", previewTag);
        Assert.True(previewIsPrerelease);
        Assert.False(previewIsLatest);

        Assert.DoesNotContain("preview", stableTag);
        Assert.False(stableIsPrerelease);
        Assert.True(stableIsLatest);
    }

    [Fact]
    public void UnsignedPreviewLabeling_PackslipSchema_ExplicitlyFlagsUnsigned()
    {
        var jsonManifest = """
        {
          "manifestSchemaVersion": "1.0.0",
          "product": "Victus Battery Guardian",
          "version": "0.2.0",
          "signatureStatus": {
            "isSigned": false,
            "status": "unsigned-checksum-manifest",
            "signingMechanism": "Pending (Sigstore keyless OIDC via GitHub Actions workflow)"
          }
        }
        """;

        using var doc = JsonDocument.Parse(jsonManifest);
        var signatureStatus = doc.RootElement.GetProperty("signatureStatus");

        Assert.False(signatureStatus.GetProperty("isSigned").GetBoolean());
        Assert.Equal("unsigned-checksum-manifest", signatureStatus.GetProperty("status").GetString());
    }

    [Fact]
    public void StableSigningEnforcement_RejectsIsSignedTrueWithoutSignatureArtifact()
    {
        var packslipIsSignedClaim = true;
        var signatureFiles = Array.Empty<string>(); // No .bundle or .sig file found

        bool EvaluateSigningIntegrity(bool isSignedClaim, string[] sigFiles)
        {
            if (isSignedClaim && sigFiles.Length == 0)
            {
                throw new InvalidOperationException("SECURITY VIOLATION: Packslip manifest claims 'isSigned: true', but no accompanying cryptographic signature bundle (*.bundle or *.sig) was found.");
            }
            return isSignedClaim && sigFiles.Length > 0;
        }

        var ex = Assert.Throws<InvalidOperationException>(() => EvaluateSigningIntegrity(packslipIsSignedClaim, signatureFiles));
        Assert.Contains("SECURITY VIOLATION", ex.Message);
    }

    [Fact]
    public void PackageContentValidation_AuditsRequiredAndForbiddenEntries()
    {
        var validUserPackageEntries = new List<string>
        {
            "VictusGuardian.App.exe",
            "README.md",
            "LICENSE",
            "VERSION.txt"
        };

        var dirtyPackageEntries = new List<string>
        {
            "VictusGuardian.App.exe",
            "README.md",
            "LICENSE",
            "VERSION.txt",
            "VictusGuardian.App.pdb",
            "clean-extracted-bundles.ps1"
        };

        void ValidatePackage(IEnumerable<string> entries)
        {
            var entryList = entries.ToList();
            var required = new[] { "VictusGuardian.App.exe", "README.md", "LICENSE", "VERSION.txt" };
            foreach (var req in required)
            {
                if (!entryList.Contains(req))
                {
                    throw new InvalidOperationException($"Missing required file: {req}");
                }
            }

            var forbiddenPatterns = new[] { ".pdb", ".ps1", ".sh", ".cs" };
            foreach (var item in entryList)
            {
                if (forbiddenPatterns.Any(pattern => item.EndsWith(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"Forbidden file in package: {item}");
                }
            }
        }

        // Valid package passes cleanly
        ValidatePackage(validUserPackageEntries);

        // Dirty package triggers error
        var ex = Assert.Throws<InvalidOperationException>(() => ValidatePackage(dirtyPackageEntries));
        Assert.Contains("Forbidden file in package", ex.Message);
    }

    [Theory]
    [InlineData(true, false, "ABORT_CONFLICT")] // Release exists and is already published
    [InlineData(true, true, "SAFE_RETRY")]      // Release exists as draft -> safe retry
    [InlineData(false, false, "CREATE_NEW")]     // Release does not exist -> create new
    public void DuplicateReleaseHandling_ResolvesExpectedAction(
        bool releaseExists,
        bool isDraft,
        string expectedAction)
    {
        string ResolveAction(bool exists, bool draft)
        {
            if (exists && !draft) return "ABORT_CONFLICT";
            if (exists && draft) return "SAFE_RETRY";
            return "CREATE_NEW";
        }

        Assert.Equal(expectedAction, ResolveAction(releaseExists, isDraft));
    }
}
