# Release Pipeline & Packslip Verification Architecture

Victus Battery Guardian follows a verifiable, reproducible release architecture designed for enterprise-grade supply chain security.

---

## 1. Packslip Manifest Specification

Every packaged release produces a Packslip JSON manifest (e.g. `VictusGuardian-v0.2.0-beta-packslip.json`) capturing:
- Schema version and product metadata
- Target semantic version and Git commit SHA
- Exact build timestamp (UTC ISO 8601)
- Target runtime (`win-x64`)
- List of release artifacts with cryptographically computed SHA-256 digests and file sizes
- **Signature Status Block**: Explicit declaration of whether the manifest has been signed cryptographically.

### Example Manifest Schema:
```json
{
  "manifestSchemaVersion": "1.0.0",
  "product": "Victus Battery Guardian",
  "version": "0.2.0-beta",
  "commit": "3a8b4c2",
  "builtAtUtc": "2026-11-12T03:45:00.0000000Z",
  "targetRuntime": "win-x64",
  "artifacts": [
    {
      "fileName": "VictusGuardian-v0.2.0-beta-win-x64.zip",
      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      "sizeBytes": 42104512
    }
  ],
  "signatureStatus": {
    "isSigned": false,
    "status": "unsigned-checksum-manifest",
    "signingMechanism": "Pending (Sigstore keyless OIDC via GitHub Actions workflow)",
    "details": "This manifest contains computed SHA-256 digests. Cryptographic signature and transparency log entry require Sigstore cosign/gh release integration."
  }
}
```

---

## 2. The Three-Tier Trust Model: Integrity, Provenance, and Authenticity

Victus Battery Guardian strictly separates and defines three distinct verification tiers:

### Tier 1: SHA-256 Digest Integrity
- **What it proves:** Bit-level data integrity against transmission corruption, truncated downloads, or accidental file mutations.
- **What it does NOT prove:** Author identity, origin authenticity, or protection against an attacker who modified both the ZIP file and the `.sha256` text file.
- **Guarantee:** Used as baseline hygiene across all builds.

### Tier 2: Sigstore Keyless Cryptographic Provenance (SLSA Level 3)
- **What it proves:** That the artifact bits and manifest were produced by an authorized, untampered GitHub Actions CI workflow run on the protected `main` branch of this repository.
- **Mechanism:** Short-lived OIDC tokens issued to the GitHub Actions runner, signed by Sigstore's Fulcio CA, and recorded immutably in the Rekor transparency log.
- **Verification Rule:** `verify-draft.ps1` strictly enforces that `isSigned: true` is **rejected** unless accompanied by a verified cryptographic bundle (`*.bundle` or `*.sig`). Blindly trusting an `isSigned: true` JSON property without cryptographic proof is strictly forbidden.

### Tier 3: Windows Authenticode Executable Signing
- **What it proves:** The Portable Executable (`VictusGuardian.App.exe`) header is signed with an X.509 certificate rooted in Microsoft's Trusted Root Certification Authorities program.
- **User Impact:** Prevents Windows SmartScreen "Unknown Publisher" warnings and administrative execution blocks.
- **Guarantee:** Establishes organizational identity and executable immutability on Windows 11.

---

## 3. Channel Architecture: Preview vs. Stable Releases

| Channel / Feature | Preview Release (`.github/workflows/preview-release.yml`) | Stable Release (`.github/workflows/ci-release.yml`) |
|---|---|---|
| **Trigger Mechanism** | Explicit manual invocation via `workflow_dispatch` only | Automated on merge to protected `main` (gated) |
| **Prerequisites** | Successful CI run on `main` (`test-linux`, `test-windows`) | Successful CI run on `main` + `ENABLE_PRODUCTION_RELEASE=true` |
| **Artifact Promotion** | Exact CI package reused (Zero recompilation) | Exact CI package reused (Zero recompilation) |
| **Release Tag Format** | `v<version>-preview.<commit>` | `v<version>` |
| **GitHub Flag** | `prerelease: true`, `latest: false` | `prerelease: false`, `latest: true` |
| **Draft-First Gate** | Created as Draft; published only when authorized | Draft release created and validated prior to promotion |
| **Signing Status** | Clearly labeled **Unsigned Candidate** in notes & manifest | Signature-gated (Authenticode / Sigstore verified) |
| **Duplicate Prevention** | Rejects conflicting published tags; allows safe retry on drafts | Semantic-release detects existing tags via Git |

The production release workflow (`.github/workflows/ci-release.yml`) is designed to integrate keyless signing using **Sigstore Cosign**:

1. **GitHub Actions OIDC Token (`id-token: write`)**:
   During release execution on `main`, GitHub Actions requests a short-lived OpenID Connect token containing repository claims (`ref`, `repository`, `workflow`).
2. **Fulcio Certificate Authority**:
   Cosign presents the OIDC token to Sigstore's Fulcio CA, receiving a short-lived X.509 certificate bound to GitHub's identity.
3. **Rekor Transparency Log**:
   The signature and certificate are posted to the public Rekor transparency log, creating an immutable, auditable proof of publication.
4. **Offline Verification**:
   Consumers verify the signature using:
   ```bash
   cosign verify-blob \
     --certificate-identity "https://github.com/daoistmadness/victus-battery-guardian/.github/workflows/ci-release.yml@refs/heads/main" \
     --certificate-oidc-issuer "https://token.actions.githubusercontent.com" \
     --cert VictusGuardian-v0.2.0-beta-packslip.json.cert \
     --signature VictusGuardian-v0.2.0-beta-packslip.json.sig \
     VictusGuardian-v0.2.0-beta-packslip.json
   ```

---

## 4. Release Governance & Safety Controls

1. **Protected Branch Requirement**:
   Production releases cannot be triggered from feature branches or forks; only protected `main`.
2. **Release Gate Variable (`vars.ENABLE_PRODUCTION_RELEASE`)**:
   The semantic release publishing job is gated behind the `ENABLE_PRODUCTION_RELEASE` repository variable. By default, builds run tests, packaging, and provenance attestation without publishing.
3. **Immutable Tagging & Namespace Isolation**:
   - Preview releases use the isolated namespace `preview-v<version>.<commit>` (or `preview-<commit>`).
   - Production stable releases strictly use `v<version>`.
   - Tooling (`get-next-version.mjs`, `git describe`) is constrained to `v[0-9]*.[0-9]*.[0-9]*`, guaranteeing preview tags never pollute SemVer bump calculations.
4. **Idempotent Asset Uploading & Safe Recovery**:
   - Existing draft releases can be safely retried.
   - When an asset already exists on a draft, its SHA-256 digest is compared against the candidate package.
   - Identical digests are safely reused without redundant re-upload.
   - Differing digests abort immediately with a fatal security error; silent clobbering of conflicting bytes is prohibited.

---

## 5. Official GitHub Actions Artifact Attestation Integration

Milestone 5.2 utilizes official GitHub Actions artifact attestation (`actions/attest-build-provenance@v2`), binding release archives to GitHub's OIDC issuer and transparency log:

```yaml
      - name: Attest Build Provenance (Sigstore)
        id: attest
        uses: actions/attest-build-provenance@v2
        with:
          subject-path: 'artifacts-ci/VictusGuardian-*.zip'

      - name: Export Attestation Bundle for Candidate Artifact
        run: |
          if [ -n "${{ steps.attest.outputs.bundle-path }}" ] && [ -f "${{ steps.attest.outputs.bundle-path }}" ]; then
            cp "${{ steps.attest.outputs.bundle-path }}" artifacts-ci/VictusGuardian-v${{ needs.version.outputs.version }}-attestation.jsonl
            echo "Attestation bundle exported to artifacts-ci/"
          fi
```

### Verifying Provenance with GitHub CLI:
Operators and automated verification gates verify the candidate archive using:
```bash
gh attestation verify VictusGuardian-v0.2.0-win-x64.zip \
  --repo daoistmadness/victus-battery-guardian \
  --signer-workflow daoistmadness/victus-battery-guardian/.github/workflows/ci-release.yml \
  --format json
```
Or for air-gapped / offline verification with the bundled attestation file:
```bash
gh attestation verify VictusGuardian-v0.2.0-win-x64.zip \
  --bundle VictusGuardian-v0.2.0-attestation.jsonl \
  --repo daoistmadness/victus-battery-guardian
```

---

## 6. Windows Authenticode vs. Sigstore vs. SmartScreen Guidance

| Security Layer | Cryptographic Mechanism | Threat Mitigated | Windows 11 SmartScreen Impact |
|---|---|---|---|
| **Tier 1 (SHA-256)** | Cryptographic Digest | Bit rot, incomplete downloads | None (SmartScreen does not evaluate SHA-256 alone) |
| **Tier 2 (Sigstore / Attestation)** | GitHub OIDC + Sigstore Rekor | Supply-chain tampering, unauthorized build systems | None (Windows kernel / Defender do not check Sigstore OIDC claims) |
| **Tier 3 (Authenticode)** | X.509 Code Signing Certificate | Impersonation, executable tampering | **Primary factor**: Builds reputation; prevents "Unknown Publisher" warnings |

### Guidance for Physical Hardware Testers (HP Victus 15.6):
Because preview candidates are signed cryptographically via Sigstore but do not yet carry an Authenticode certificate rooted in Microsoft's CA:
1. Verify SHA-256 against `VictusGuardian-v<version>-win-x64.zip.sha256` or run `scripts/verify-draft.ps1`.
2. Extract the portable ZIP into an unprivileged folder (e.g. `C:\Tools\VictusGuardian`).
3. On first execution of `VictusGuardian.App.exe`, Windows SmartScreen will display:
   > *"Windows protected your PC — Microsoft Defender SmartScreen prevented an unrecognized app from starting."*
4. Click **"More info"**, confirm Application is `VictusGuardian.App.exe` and Publisher is `Unknown publisher`, then click **"Run anyway"**.
5. Once launched, verify the status bar displays live Windows IOCTL battery metrics.

### Strict Signing Policy:
> [!CAUTION]
> **No Silent Fallback**: The release pipeline must never silently fall back from a required signature to an unsigned publication. If the Sigstore token exchange, certificate issuance, or transparency log recording fails, the release job must fail immediately, halting publication.
