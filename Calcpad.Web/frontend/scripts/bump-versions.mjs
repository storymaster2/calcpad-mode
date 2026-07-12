#!/usr/bin/env node
// Bumps calcpad-frontend, calcpad-desktop, and vscode-calcpad to the same
// version. Uses the highest current version across the three package.json
// files as the baseline, then applies the requested semver bump.
//
// Also updates:
//   * package-lock.json (root + packages[""] entries)
//   * calcpad-desktop/src-tauri/Cargo.toml (package.version)
//   * calcpad-desktop/src-tauri/Cargo.lock (calcpad-desktop [[package]] entry)
//   * calcpad-desktop/src-tauri/tauri.conf.json (version)

import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const kind = process.argv[2];
if (!['patch', 'minor', 'major'].includes(kind)) {
    console.error('Usage: bump-versions.mjs <patch|minor|major>');
    process.exit(1);
}

const here = dirname(fileURLToPath(import.meta.url));
const frontendRoot = resolve(here, '..');
const pkgDirs = ['calcpad-frontend', 'calcpad-desktop', 'vscode-calcpad'];

const parse = v => v.split('.').map(n => parseInt(n, 10));
const cmp = (a, b) => a[0] - b[0] || a[1] - b[1] || a[2] - b[2];

const pkgJsonPaths = pkgDirs.map(name => resolve(frontendRoot, name, 'package.json'));
const versions = pkgJsonPaths.map(p => parse(JSON.parse(readFileSync(p, 'utf8')).version));
const baseline = versions.reduce((max, v) => (cmp(v, max) > 0 ? v : max));

let [major, minor, patch] = baseline;
if (kind === 'patch') patch += 1;
else if (kind === 'minor') { minor += 1; patch = 0; }
else { major += 1; minor = 0; patch = 0; }
const next = `${major}.${minor}.${patch}`;

const updateJson = (path, mutate) => {
    if (!existsSync(path)) return false;
    const json = JSON.parse(readFileSync(path, 'utf8'));
    const prev = mutate(json);
    writeFileSync(path, JSON.stringify(json, null, 2) + '\n');
    console.log(`${path.replace(frontendRoot + '/', '')}: ${prev} -> ${next}`);
    return true;
};

for (const name of pkgDirs) {
    const dir = resolve(frontendRoot, name);
    updateJson(resolve(dir, 'package.json'), j => {
        const p = j.version; j.version = next; return p;
    });
    updateJson(resolve(dir, 'package-lock.json'), j => {
        const p = j.version;
        j.version = next;
        if (j.packages && j.packages['']) j.packages[''].version = next;
        return p;
    });
}

const cargoToml = resolve(frontendRoot, 'calcpad-desktop/src-tauri/Cargo.toml');
if (existsSync(cargoToml)) {
    const src = readFileSync(cargoToml, 'utf8');
    const updated = src.replace(
        /(\[package\][\s\S]*?\nversion\s*=\s*")([^"]+)(")/,
        (_, a, prev, c) => {
            console.log(`calcpad-desktop/src-tauri/Cargo.toml: ${prev} -> ${next}`);
            return a + next + c;
        },
    );
    writeFileSync(cargoToml, updated);
}

const cargoLock = resolve(frontendRoot, 'calcpad-desktop/src-tauri/Cargo.lock');
if (existsSync(cargoLock)) {
    const src = readFileSync(cargoLock, 'utf8');
    const updated = src.replace(
        /(\[\[package\]\]\nname = "calcpad-desktop"\nversion = ")([^"]+)(")/,
        (_, a, prev, c) => {
            console.log(`calcpad-desktop/src-tauri/Cargo.lock: ${prev} -> ${next}`);
            return a + next + c;
        },
    );
    writeFileSync(cargoLock, updated);
}

updateJson(resolve(frontendRoot, 'calcpad-desktop/src-tauri/tauri.conf.json'), j => {
    const p = j.version; j.version = next; return p;
});
