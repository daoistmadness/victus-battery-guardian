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

## 2. Integrity vs. Authenticity: Why Signing Must Not Be Fabricated

In Milestone 1 and Milestone 2:
- The packslip manifest verifies **integrity** (confirming that downloaded files match the bits produced during compilation via SHA-256 checksums).
- It does **NOT** yet provide cryptographic **authenticity** or non-repudiation.
- Describing a checksum-only manifest as "signed" is a violation of software supply chain standards. Therefore, our manifests explicitly state `isSigned: false` until keyless signing infrastructure is deployed.

---

## 3. Future Keyless Signing: Sigstore & Rekor Architecture

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
   The semantic release publishing job is gated behind the `ENABLE_PRODUCTION_RELEASE` repository variable. By default, builds run tests and local packaging validation without publishing.
3. **Immutable Tagging**:
   Tags are published strictly through automated semantic release using Conventional Commits.

---

## 5. Official Packslip GitHub Action Workflow Integration

When Sigstore keyless signing is activated in production, the workflow utilizes GitHub's OIDC tokens (`id-token: write`) alongside the official Packslip signing step:

```yaml
  release:
    name: Semantic Release & Packslip Publishing
    needs: [test-linux, test-windows]
    if: github.event_name == 'push' && github.ref == 'refs/heads/main' && vars.ENABLE_PRODUCTION_RELEASE == 'true'
    runs-on: ubuntu-latest
    permissions:
      contents: write
      issues: write
      pull-requests: write
      id-token: write # Required for keyless Sigstore Fulcio certificate issuance

    steps:
      - name: Checkout Code
        uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Publish & Package Win-x64 Release
        run: pwsh -File ./scripts/package.ps1 -Version ${{ env.RELEASE_VERSION }}

      - name: Sign Packslip with Sigstore Cosign
        uses: sigstore/cosign-installer@v3.8.0

      - name: Generate Keyless Signature
        run: |
          cosign sign-blob \
            --yes \
            --bundle artifacts/VictusGuardian-packslip.bundle \
            artifacts/*-packslip.json

      - name: Verify Signature before Release Publication
        run: |
          cosign verify-blob \
            --bundle artifacts/VictusGuardian-packslip.bundle \
            --certificate-identity "https://github.com/daoistmadness/victus-battery-guardian/.github/workflows/ci-release.yml@refs/heads/main" \
            --certificate-oidc-issuer "https://token.actions.githubusercontent.com" \
            artifacts/*-packslip.json
```

### Strict Signing Policy:
> [!CAUTION]
> **No Silent Fallback**: The release pipeline must never silently fall back from a required signature to an unsigned publication. If the Sigstore token exchange, certificate issuance, or transparency log recording fails, the release job must fail immediately, halting publication.
