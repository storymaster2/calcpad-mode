#!/usr/bin/env node
/**
 * Bundle the .NET server into a frontend app's bin directory.
 *
 * Why: vscode-calcpad and calcpad-desktop both ship the .NET server
 * alongside their frontend. Hand-syncing a single Calcpad.Server.dll
 * across the package boundary has been the source of every
 * "AWSSDK.S3 not found", "endpoint 404", and "deps.json out of date"
 * bug we've hit. This script publishes the backend with `dotnet publish`,
 * then mirrors the output into the chosen target so the deps.json,
 * runtimeconfig.json, native apphost, and every transitive dependency
 * stay in lock-step across both apps.
 *
 * Flags:
 *   --target=<abs-dir>     Where to mirror the publish output. Default:
 *                          vscode-calcpad/bin/ (script's own consumer).
 *                          For calcpad-desktop, pass
 *                          calcpad-desktop/extensions/server.
 *   --rid=<rid>            Target RID for self-contained publish (default: host RID)
 *   --framework-dependent  Emit a slim, framework-dependent bundle instead of
 *                          a self-contained one. Smaller (~3 MB vs ~80 MB)
 *                          but requires the user to have .NET 10 installed.
 *                          Only safe for vscode-calcpad — calcpad-desktop
 *                          ships standalone and must be self-contained.
 *   --configuration=<c>    Debug | Release (default: Release)
 *   --skip-build           Reuse an existing publish directory; only re-mirror
 *   --keep-skia-natives    Keep the published `runtimes/` tree (SkiaSharp +
 *                          others) instead of expecting the consumer to
 *                          download SkiaSharp natives at runtime. Required
 *                          for calcpad-desktop (no runtime download path).
 *
 * Files matching PRESERVE_PATTERNS in the target are kept across syncs so
 * runtime-downloaded SkiaSharp natives, lock files, and logs aren't wiped.
 */

import { execSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, readdirSync, rmSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { arch as nodeArch, platform as nodePlatform } from 'node:os';

const __dirname = dirname(fileURLToPath(import.meta.url));
const VSCODE_DIR = resolve(__dirname, '..');
const BACKEND_DIR = resolve(VSCODE_DIR, '..', '..', 'backend');
const CSPROJ = join(BACKEND_DIR, 'Calcpad.Server.csproj');
const DEFAULT_TARGET_BIN = join(VSCODE_DIR, 'bin');

// Filenames that the extension generates at runtime (downloaded native libs,
// log files, lock files, …). We never want to wipe these on resync.
const PRESERVE_PATTERNS = [
    /^runtimes$/,                 // SkiaSharp native libs downloaded by ensureNativeLibs
    /^logs$/,                     // FileLogger output
    /^cache$/,                    // Server-side caches
    /^\.calcpad-server\.lock$/,   // Lock file used by tryReuseExistingServer
    /^CalcpadServer-.*\.log$/,    // Per-day server logs
    /^\.gitkeep$/,                // Placeholder used by calcpad-desktop's empty extensions dir
];

function shouldPreserve(name) {
    return PRESERVE_PATTERNS.some(re => re.test(name));
}

function parseArgs(argv) {
    const out = {
        skipBuild: false,
        configuration: 'Release',
        frameworkDependent: false,
        rid: null,
        target: DEFAULT_TARGET_BIN,
        keepSkiaNatives: false,
    };
    for (const arg of argv.slice(2)) {
        if (arg === '--skip-build') out.skipBuild = true;
        else if (arg === '--framework-dependent') out.frameworkDependent = true;
        else if (arg === '--keep-skia-natives') out.keepSkiaNatives = true;
        else if (arg.startsWith('--configuration=')) out.configuration = arg.split('=')[1];
        else if (arg.startsWith('--rid=')) out.rid = arg.split('=')[1];
        else if (arg.startsWith('--target=')) out.target = resolve(arg.split('=')[1]);
        else throw new Error(`Unknown flag: ${arg}`);
    }
    return out;
}

function defaultRid() {
    const platform = nodePlatform();
    const arch = nodeArch();
    const archMap = { x64: 'x64', arm64: 'arm64' };
    const a = archMap[arch];
    if (!a) throw new Error(`Unsupported arch for default RID: ${arch}`);
    if (platform === 'linux') return `linux-${a}`;
    if (platform === 'darwin') return `osx-${a}`;
    if (platform === 'win32') return `win-${a}`;
    throw new Error(`Unsupported platform for default RID: ${platform}`);
}

function publishOutputDir(rid, frameworkDependent, configuration) {
    // Match `dotnet publish` default layout: bin/<config>/<tfm>/[rid/]publish
    const tfm = 'net10.0';
    const base = join(BACKEND_DIR, 'bin', configuration, tfm);
    return frameworkDependent
        ? join(base, 'publish')
        : join(base, rid, 'publish');
}

function run(cmd, cwd) {
    console.log(`> ${cmd}`);
    execSync(cmd, { cwd, stdio: 'inherit' });
}

function cleanTarget(targetBin) {
    if (!existsSync(targetBin)) {
        mkdirSync(targetBin, { recursive: true });
        return;
    }
    for (const entry of readdirSync(targetBin)) {
        if (shouldPreserve(entry)) continue;
        const full = join(targetBin, entry);
        rmSync(full, { recursive: true, force: true });
    }
}

function copyPublishedTree(src, targetBin, { keepSkiaNatives, rid }) {
    if (!existsSync(src)) {
        throw new Error(`Publish output not found: ${src}\nDid \`dotnet publish\` succeed?`);
    }
    for (const entry of readdirSync(src)) {
        // Skip pdbs (debug symbols).
        if (entry.endsWith('.pdb')) continue;
        // Skip the runtimes/ tree by default — vscode-calcpad downloads
        // SkiaSharp natives at runtime via ensureNativeLibs, so shipping the
        // full .NET runtimes/ folder just bloats the bundle. Calcpad-Desktop
        // has no download path and must keep them (--keep-skia-natives).
        if (entry === 'runtimes' && !keepSkiaNatives) continue;
        const srcPath = join(src, entry);
        const dstPath = join(targetBin, entry);
        cpSync(srcPath, dstPath, { recursive: true });
    }
    // When keeping runtimes, prune sibling-platform SkiaSharp / Playwright
    // assets so we don't ship Linux libs in a Windows bundle.
    if (keepSkiaNatives) {
        pruneForeignRuntimes(join(targetBin, 'runtimes'), rid);
        pruneForeignPlaywright(join(targetBin, '.playwright', 'node'), rid);
    }
}

function pruneForeignRuntimes(runtimesDir, rid) {
    if (!existsSync(runtimesDir)) return;
    // Keep both the precise RID and the OS-only fallback (e.g. linux-x64
    // and linux), since some packages publish under one or the other.
    const osOnly = rid.split('-')[0];
    for (const entry of readdirSync(runtimesDir)) {
        if (entry === rid || entry === osOnly) continue;
        rmSync(join(runtimesDir, entry), { recursive: true, force: true });
    }
}

function pruneForeignPlaywright(playwrightDir, rid) {
    if (!existsSync(playwrightDir)) return;
    for (const entry of readdirSync(playwrightDir)) {
        if (entry === rid) continue;
        rmSync(join(playwrightDir, entry), { recursive: true, force: true });
    }
}

function bundleSizeMb(dir) {
    let total = 0;
    function walk(p) {
        for (const entry of readdirSync(p)) {
            const full = join(p, entry);
            const st = statSync(full);
            if (st.isDirectory()) walk(full); else total += st.size;
        }
    }
    walk(dir);
    return (total / 1024 / 1024).toFixed(1);
}

function main() {
    const args = parseArgs(process.argv);
    const rid = args.rid ?? defaultRid();
    const targetBin = args.target;
    console.log(`[sync-bundled-server] target=${targetBin}`);
    console.log(`[sync-bundled-server] mode=${args.frameworkDependent ? 'framework-dependent' : `self-contained (${rid})`}`);
    console.log(`[sync-bundled-server] configuration=${args.configuration}`);
    if (args.keepSkiaNatives) console.log('[sync-bundled-server] keeping published runtimes/ tree');

    if (!args.skipBuild) {
        const publishCmd = args.frameworkDependent
            ? `dotnet publish "${CSPROJ}" -c ${args.configuration} --no-self-contained`
            : `dotnet publish "${CSPROJ}" -c ${args.configuration} -r ${rid} --self-contained true`;
        run(publishCmd, BACKEND_DIR);
    } else {
        console.log('[sync-bundled-server] --skip-build given, reusing existing publish output');
    }

    const src = publishOutputDir(rid, args.frameworkDependent, args.configuration);
    console.log(`[sync-bundled-server] copying from ${src}`);

    cleanTarget(targetBin);
    copyPublishedTree(src, targetBin, { keepSkiaNatives: args.keepSkiaNatives, rid });

    // Quick sanity check — the server DLL and its deps manifest must be present,
    // and the apphost needs +x on POSIX.
    const requiredFiles = ['Calcpad.Server.dll', 'Calcpad.Server.deps.json', 'Calcpad.Server.runtimeconfig.json'];
    for (const f of requiredFiles) {
        if (!existsSync(join(targetBin, f))) {
            throw new Error(`Sync incomplete: ${f} missing from ${targetBin}`);
        }
    }

    if (process.platform !== 'win32') {
        for (const f of ['Calcpad.Server', 'createdump']) {
            const p = join(targetBin, f);
            if (existsSync(p)) {
                try { execSync(`chmod 0755 "${p}"`); } catch { /* best-effort */ }
            }
        }
    }

    console.log(`[sync-bundled-server] OK — bundle size ${bundleSizeMb(targetBin)} MB`);
}

try {
    main();
} catch (err) {
    console.error(`[sync-bundled-server] FAILED: ${err instanceof Error ? err.message : err}`);
    process.exit(1);
}
