#!/usr/bin/env node

import { execSync } from 'node:child_process';

const isDryRun = process.argv.includes('--dry-run') || process.env.DRY_RUN === 'true';

console.log('==================================================');
console.log('Victus Battery Guardian - Release Automation');
console.log(`Mode: ${isDryRun ? 'DRY RUN (Simulation)' : 'PRODUCTION'}`);
console.log('==================================================');

const args = ['npx', 'semantic-release'];
if (isDryRun) {
  args.push('--dry-run');
}

try {
  execSync(args.join(' '), { stdio: 'inherit', env: process.env });
} catch (error) {
  console.error('Release execution failed:', error.message);
  process.exit(error.status || 1);
}
