#!/usr/bin/env node

import { appendFileSync } from 'node:fs';
import { execSync } from 'node:child_process';
import * as ca from '@semantic-release/commit-analyzer';

const analyzeCommits = ca.analyzeCommits || ca.default?.analyzeCommits || ca.default;

let gitLog = '';
try {
  gitLog = execSync('git log --pretty=format:"%h%x09%s"', { encoding: 'utf8' });
} catch {
  gitLog = '';
}

const commits = gitLog.trim().split('\n').filter(Boolean).map(line => {
  const [hash, ...rest] = line.split('\t');
  return { hash, message: rest.join('\t') };
});

const releaseType = await analyzeCommits({
  preset: 'conventionalcommits',
  releaseRules: [
    { type: 'feat', release: 'minor' },
    { type: 'fix', release: 'patch' },
    { type: 'perf', release: 'patch' },
    { type: 'refactor', release: 'patch' },
    { type: 'docs', release: false },
    { type: 'chore', release: false }
  ]
}, { commits, logger: { log: () => {}, error: console.error }, cwd: process.cwd() });

let currentVersion = '0.1.0';
try {
  // Only match stable SemVer release tags (vMAJOR.MINOR.PATCH) to prevent preview/prerelease tag pollution
  const latestTag = execSync('git describe --tags --abbrev=0 --match "v[0-9]*.[0-9]*.[0-9]*" 2>/dev/null', { encoding: 'utf8' }).trim();
  if (latestTag) {
    const rawVersion = latestTag.replace(/^v/, '');
    const cleanSemver = rawVersion.split('-')[0]; // Strip any prerelease identifier
    if (/^\d+\.\d+\.\d+$/.test(cleanSemver)) {
      currentVersion = cleanSemver;
    }
  }
} catch {
  // Use default if no matching stable tags exist
}

const [major, minor, patch] = currentVersion.split('.').map(v => parseInt(v, 10) || 0);
let nextVersion = currentVersion;
if (releaseType === 'major') {
  nextVersion = `${major + 1}.0.0`;
} else if (releaseType === 'minor') {
  nextVersion = `${major}.${minor + 1}.0`;
} else if (releaseType === 'patch') {
  nextVersion = `${major}.${minor}.${patch + 1}`;
}

if (process.argv.includes('--github-output') && process.env.GITHUB_OUTPUT) {
  appendFileSync(process.env.GITHUB_OUTPUT, `version=${nextVersion}\n`);
}

console.log(nextVersion);
