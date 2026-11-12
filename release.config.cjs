/**
 * Semantic-release configuration for Victus Battery Guardian.
 * Implements draft-first release pattern with packslip verification.
 */
module.exports = {
  branches: [
    'main'
  ],
  plugins: [
    [
      '@semantic-release/commit-analyzer',
      {
        preset: 'conventionalcommits',
        releaseRules: [
          { type: 'feat', release: 'minor' },
          { type: 'fix', release: 'patch' },
          { type: 'perf', release: 'patch' },
          { type: 'refactor', release: 'patch' },
          { type: 'docs', release: false },
          { type: 'chore', release: false }
        ]
      }
    ],
    [
      '@semantic-release/release-notes-generator',
      {
        preset: 'conventionalcommits',
        presetConfig: {
          types: [
            { type: 'feat', section: 'Features' },
            { type: 'fix', section: 'Bug Fixes' },
            { type: 'perf', section: 'Performance Improvements' },
            { type: 'refactor', section: 'Code Refactoring' }
          ]
        }
      }
    ],
    [
      '@semantic-release/changelog',
      {
        changelogFile: 'CHANGELOG.md'
      }
    ],
    [
      '@semantic-release/exec',
      {
        prepareCmd: 'pwsh -File ./scripts/package.ps1 -Version ${nextRelease.version} -OutputDir artifacts && pwsh -File ./scripts/verify-draft.ps1 -ArtifactsDir artifacts'
      }
    ],
    [
      '@semantic-release/github',
      {
        draftRelease: true,
        assets: [
          { path: 'artifacts/*.zip', label: 'Victus Guardian Windows x64 Application (${nextRelease.version})' },
          { path: 'artifacts/*.sha256', label: 'SHA-256 Checksums' },
          { path: 'artifacts/*packslip.json', label: 'Release Packslip & Manifest' }
        ]
      }
    ],
    [
      '@semantic-release/git',
      {
        assets: ['CHANGELOG.md', 'package.json', 'package-lock.json', 'Directory.Build.props'],
        message: 'chore(release): ${nextRelease.version} [skip ci]\n\n${nextRelease.notes}'
      }
    ]
  ]
};
