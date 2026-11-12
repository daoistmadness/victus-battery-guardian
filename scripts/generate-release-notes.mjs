#!/usr/bin/env node

import { execSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import * as rng from '@semantic-release/release-notes-generator';

const generateNotes = rng.generateNotes || rng.default?.generateNotes || rng.default;

function parseArgs() {
  const args = process.argv.slice(2);
  const options = {
    version: '0.2.0',
    preview: false,
    commit: '',
    out: null,
    since: null
  };

  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg === '--preview') {
      options.preview = true;
    } else if (arg === '--version' && i + 1 < args.length) {
      options.version = args[++i];
    } else if (arg === '--commit' && i + 1 < args.length) {
      options.commit = args[++i];
    } else if (arg === '--out' && i + 1 < args.length) {
      options.out = args[++i];
    } else if (arg === '--since' && i + 1 < args.length) {
      options.since = args[++i];
    }
  }

  return options;
}

async function main() {
  const options = parseArgs();

  let headCommit = options.commit;
  if (!headCommit) {
    try {
      headCommit = execSync('git rev-parse HEAD', { encoding: 'utf8' }).trim();
    } catch {
      headCommit = 'unknown-commit';
    }
  }
  const headShort = headCommit.slice(0, 7);

  let logCmd = 'git log --pretty=format:"%h%x09%s"';
  if (options.since) {
    logCmd = `git log ${options.since}..HEAD --pretty=format:"%h%x09%s"`;
  }

  let gitLog = '';
  try {
    gitLog = execSync(logCmd, { encoding: 'utf8' });
  } catch {
    gitLog = execSync('git log -n 50 --pretty=format:"%h%x09%s"', { encoding: 'utf8' });
  }

  const commits = gitLog.trim().split('\n').filter(Boolean).map(line => {
    const [hash, ...rest] = line.split('\t');
    return { hash, message: rest.join('\t') };
  });

  const rawNotes = await generateNotes({
    preset: 'conventionalcommits',
    presetConfig: {
      types: [
        { type: 'feat', section: 'Features' },
        { type: 'fix', section: 'Bug Fixes' },
        { type: 'perf', section: 'Performance Improvements' },
        { type: 'refactor', section: 'Code Refactoring' }
      ]
    }
  }, {
    commits,
    lastRelease: options.since ? { version: options.since.replace(/^v/, ''), gitTag: options.since } : {},
    nextRelease: { version: options.version, gitTag: `v${options.version}`, type: 'minor' },
    options: { repositoryUrl: 'https://github.com/daoistmadness/victus-battery-guardian' },
    cwd: process.cwd(),
    logger: { log: () => {}, error: console.error }
  });

  let finalNotes = '';
  if (options.preview) {
    finalNotes = `# Victus Battery Guardian v${options.version} Preview (${headShort})

> [!WARNING]
> **Preview Release — Unsigned Candidate**
> This is a preview / pre-release distribution intended for testing on physical Windows 11 hardware.
> - **Integrity & Provenance:** SHA-256 digests and Packslip manifest verified against GitHub CI run.
> - **Code Signing:** This package does **not** contain a production Authenticode signature. Windows SmartScreen may present an untrusted publisher notice on first launch.
> - **Reporting:** Please report telemetry anomalies, hardware sensor inaccuracies, or UI glitches on GitHub Issues.

### Verification Metadata
- **Source Commit:** \`${headCommit}\`
- **Distribution:** Windows x64 Portable Self-Contained (\`.zip\`)
- **Default Renderer:** Software Rendering (\`Win32RenderingMode.Software\` via GDI RedirectionSurface)
- **Hardware Override:** Launch with \`--hardware-rendering\` or set \`VICTUS_RENDER_MODE=hardware\`
- **Diagnostics Package:** Download companion \`VictusGuardian-v${options.version}-diagnostics.zip\` for PDB symbols and hardware validation scripts.

---

### Changes in this Preview

${rawNotes ? rawNotes.trim() : 'No new conventional commits.'}
`;
  } else {
    finalNotes = (rawNotes ? rawNotes.trim() : '') + '\n';
  }

  if (options.out) {
    writeFileSync(options.out, finalNotes, 'utf8');
    console.log(`[RELEASE-NOTES] Notes written to: ${options.out}`);
  } else {
    console.log(finalNotes);
  }
}

main().catch(err => {
  console.error('Fatal error generating release notes:', err);
  process.exit(1);
});
