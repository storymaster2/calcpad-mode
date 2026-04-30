import * as net from 'net';
import * as path from 'path';
import * as fs from 'fs';
import { spawn, execSync, ChildProcess } from 'child_process';
import type { ILogger } from '../types/interfaces';

interface LockFileContents {
    pid: number;
    port: number;
    startedAt: number;
}

/**
 * Manages the lifecycle of the bundled CalcPad server process.
 *
 * Designed for cross-instance reuse: multiple VS Code windows share a single
 * server discovered via a lock file at `{basePath}/bin/.calcpad-server.lock`.
 * Only the first instance to start spawns the server process and becomes the
 * owner. Subsequent instances read the lock file, health-check the existing
 * server, and connect to it. The server is spawned detached so it outlives
 * the spawning process — it only exits via the `calcpad.stopServer` command
 * or an OS-level signal.
 */
export class CalcpadServerManager {
    private static readonly MAX_RESTARTS = 3;

    private serverProcess: ChildProcess | null = null;
    private port: number = 0;
    private logger: ILogger;
    private mainLogger: ILogger;
    private basePath: string;
    private dotnetPath: string;
    private _isRunning: boolean = false;
    private _owned: boolean = false;
    private _disposed: boolean = false;
    private _startingUp: boolean = false;
    private _restartCount: number = 0;
    private _lastCrashOutput: string[] = [];
    private _processClosed: boolean = false;
    /** Set when the spawn itself failed (EACCES, EPERM, ENOENT etc.). Distinguishes
     *  "Windows blocked the exe" from "process started but crashed". */
    private _spawnFailed: boolean = false;
    private _spawnFailedCode: string | null = null;
    private lockFilePath: string;

    /** Called when auto-restart retries are exhausted. Receives the last stderr output. */
    public onCrashExhausted?: (crashOutput: string) => void;

    /**
     * @param logger    Server debug channel — receives stdout (verbose server output).
     * @param mainLogger Main extension log — receives stderr only. Falls back to `logger` if omitted.
     */
    constructor(basePath: string, logger: ILogger, dotnetPath: string = 'dotnet', mainLogger?: ILogger) {
        this.basePath = basePath;
        this.logger = logger;
        this.mainLogger = mainLogger ?? logger;
        this.dotnetPath = dotnetPath;
        this.lockFilePath = path.join(basePath, 'bin', '.calcpad-server.lock');
    }

    /**
     * Check if the bundled server DLL exists.
     */
    public static dllExists(basePath: string): boolean {
        const dllPath = path.join(basePath, 'bin', 'Calcpad.Server.dll');
        return fs.existsSync(dllPath);
    }

    /**
     * Check if the bundled native apphost binary exists. When present,
     * the server can be spawned directly without a system `dotnet` —
     * the apphost is a self-contained .NET host (ships libcoreclr +
     * libhostfxr alongside it on Linux/macOS, calcpad-server.exe on
     * Windows).
     */
    public static appHostExists(basePath: string): boolean {
        const exeName = process.platform === 'win32' ? 'Calcpad.Server.exe' : 'Calcpad.Server';
        return fs.existsSync(path.join(basePath, 'bin', exeName));
    }

    /**
     * Read the lock file and verify the recorded server is alive and healthy.
     * Returns the lock contents if reusable, or null if the lock is missing/stale.
     */
    private async tryReuseExistingServer(): Promise<LockFileContents | null> {
        let lock: LockFileContents;
        try {
            if (!fs.existsSync(this.lockFilePath)) {
                return null;
            }
            lock = JSON.parse(fs.readFileSync(this.lockFilePath, 'utf-8'));
            if (typeof lock.pid !== 'number' || typeof lock.port !== 'number') {
                this.removeLockFile();
                return null;
            }
        } catch {
            this.removeLockFile();
            return null;
        }

        try {
            process.kill(lock.pid, 0);
        } catch {
            this.log(`Lock file references dead PID ${lock.pid} — ignoring`);
            this.removeLockFile();
            return null;
        }

        try {
            const response = await fetch(`http://localhost:${lock.port}/api/calcpad/snippets`, {
                signal: AbortSignal.timeout(2000)
            });
            if (!response.ok) {
                this.log(`Existing server at port ${lock.port} unhealthy (HTTP ${response.status}) — ignoring`);
                return null;
            }
        } catch (err) {
            this.log(`Existing server at port ${lock.port} unreachable: ${err instanceof Error ? err.message : String(err)}`);
            return null;
        }

        return lock;
    }

    /**
     * Start the bundled server. Cleans up any stale process, allocates a free port,
     * spawns the dotnet process, and waits for the server to become ready.
     */
    public async start(): Promise<void> {
        if (this._isRunning) {
            this.log('Server is already running');
            return;
        }

        // Reuse an existing server from another VS Code window if one is alive.
        const existing = await this.tryReuseExistingServer();
        if (existing) {
            this.port = existing.port;
            this._owned = false;
            this._isRunning = true;
            this.log(`Reusing existing server (PID ${existing.pid}) at port ${existing.port}`);
            return;
        }

        const dllPath = path.join(this.basePath, 'bin', 'Calcpad.Server.dll');
        if (!fs.existsSync(dllPath)) {
            throw new Error(`Calcpad.Server.dll not found at ${dllPath}`);
        }

        const candidatePort = await this.findFreePort();

        // Race guard: atomically claim the lock file before spawning. If another
        // window claimed it in the window between our reuse-check and this line,
        // the `wx` flag makes this throw EEXIST — we then wait for that peer's
        // server to come up and adopt it instead of spawning a duplicate.
        const placeholderLock: LockFileContents = {
            pid: process.pid,  // extension host PID — used by peers to detect if spawner died
            port: candidatePort,
            startedAt: Date.now()
        };
        if (!this.tryClaimLockExclusive(placeholderLock)) {
            this.log('Another window is spawning the server — waiting to adopt it...');
            const adopted = await this.waitForPeerServer(20000);
            if (adopted) {
                this.port = adopted.port;
                this._owned = false;
                this._isRunning = true;
                this.log(`Adopted peer-spawned server (PID ${adopted.pid}) at port ${adopted.port}`);
                return;
            }
            this.log('Timed out waiting for peer server — reclaiming lock and spawning our own');
            this.removeLockFile();
            this.tryClaimLockExclusive(placeholderLock);
        }

        this.port = candidatePort;
        this.log(`Starting server on port ${this.port}...`);

        const serverUrl = `http://localhost:${this.port}`;

        // Prefer the native apphost exe when available — it shows as
        // "Calcpad.Server" in Task Manager instead of ".NET Host".
        // Falls back to `dotnet Calcpad.Server.dll` for compatibility.
        const exeName = process.platform === 'win32' ? 'Calcpad.Server.exe' : 'Calcpad.Server';
        const exePath = path.join(this.basePath, 'bin', exeName);
        const useAppHost = fs.existsSync(exePath);

        // VSIX packaging strips the executable bit on POSIX, so the bundled
        // apphost can sit on disk but spawn fails silently with EACCES — the
        // user sees no server URL and falls back to the (possibly empty)
        // configured remote URL. Re-set the bit before every spawn so this
        // self-heals on first launch after an install. Also chmod the
        // libraries the apphost needs to dlopen at startup so a partially-
        // restored bundle doesn't half-work.
        if (useAppHost && process.platform !== 'win32') {
            try {
                fs.chmodSync(exePath, 0o755);
            } catch (err) {
                this.log(`Warning: could not chmod ${exeName}: ${err instanceof Error ? err.message : String(err)}`);
            }
            // createdump is invoked by the .NET runtime on crash; without +x
            // the runtime aborts startup on some distros.
            const createdump = path.join(this.basePath, 'bin', 'createdump');
            if (fs.existsSync(createdump)) {
                try { fs.chmodSync(createdump, 0o755); } catch { /* best-effort */ }
            }
        }

        // `detached: true` starts the child in its own process group / session,
        // so it survives when this VS Code window exits. We still pipe stdio
        // while we're alive to capture startup logs; once the owner exits,
        // Node's unref() lets the parent event loop close without waiting.
        //
        // DOTNET_DbgEnableMiniDump tells the runtime to write a minidump on
        // unrecoverable crashes (StackOverflow, FailFast) — failure modes that
        // bypass our in-process FileLogger. The .NET 6+ createdump flow handles
        // this in-runtime, no separate tool required.
        //
        // Fixed filename (no %p/%t templating) means each new crash overwrites
        // the previous dump, so we always keep exactly the most recent. Only
        // one server runs at a time per project (lock-file enforced) so there's
        // no race between concurrent dumps.
        const dumpDir = path.join(this.basePath, 'bin', 'logs');
        try { fs.mkdirSync(dumpDir, { recursive: true }); } catch { /* best-effort */ }
        const childEnv: NodeJS.ProcessEnv = {
            ...process.env,
            DOTNET_DbgEnableMiniDump: '1',
            DOTNET_DbgMiniDumpType: '2',
            DOTNET_DbgMiniDumpName: path.join(dumpDir, 'last-crash.dmp'),
            DOTNET_EnableCrashReport: '1',
            // The server defaults to "exit when stdin EOFs" so the
            // Neutralino desktop doesn't leak orphan processes. The VS Code
            // extension shares one server across multiple windows via the
            // lock file, so it must explicitly opt out — without this,
            // closing the spawning window would kill the server even if
            // other VS Code windows are still using it.
            CALCPAD_DETACHED: '1',
        };
        const spawnOpts = {
            stdio: ['pipe', 'pipe', 'pipe'] as ['pipe', 'pipe', 'pipe'],
            detached: true,
            env: childEnv,
        };
        this.serverProcess = useAppHost
            ? spawn(exePath, ['--urls', serverUrl], spawnOpts)
            : spawn(this.dotnetPath, [dllPath, '--urls', serverUrl], spawnOpts);
        this.serverProcess.unref();
        this._owned = true;
        // Reset spawn-failure state for this attempt; the 'error' handler below will
        // flip these if Windows blocks the exe (Defender / SmartScreen / AppLocker).
        this._spawnFailed = false;
        this._spawnFailedCode = null;
        this._processClosed = false;
        this.log(`Spawned via ${useAppHost ? 'apphost' : 'dotnet'} (PID ${this.serverProcess.pid}, detached)`);

        // Rewrite the lock with the actual child PID (replacing our host-PID placeholder).
        if (this.serverProcess.pid) {
            this.writeLockFile({
                pid: this.serverProcess.pid,
                port: this.port,
                startedAt: Date.now()
            });
        }

        // stdout → server debug channel only. Not buffered, not surfaced in crash messages.
        this.serverProcess.stdout?.on('data', (data: Buffer) => {
            const text = data.toString().trim();
            this.logger.appendLine(`[ServerManager] [stdout] ${text}`);
        });

        // stderr → main extension log + crash buffer. These are the lines we actually
        // want visible to the user and included in crash reports.
        this.serverProcess.stderr?.on('data', (data: Buffer) => {
            const text = data.toString().trim();
            this.mainLogger.appendLine(`[ServerManager] [stderr] ${text}`);
            this._lastCrashOutput.push(text);
            if (this._lastCrashOutput.length > 20) {
                this._lastCrashOutput.shift();
            }
        });

        // The 'close' event fires after all stdio streams are drained,
        // so _lastCrashOutput is fully populated by the time this fires.
        this.serverProcess.on('close', () => {
            this._processClosed = true;
        });

        // 'error' fires when spawn itself fails (EACCES from Windows Defender,
        // ENOENT for missing dotnet runtime, etc.). The process never starts in
        // this case, so 'exit'/'close' may not fire — we have to flip _processClosed
        // ourselves so waitForReady stops polling and surfaces the error fast.
        // We DON'T fall back to another spawn here: the user needs to unblock the
        // file in Explorer and then click Refresh to retry.
        this.serverProcess.on('error', (err: NodeJS.ErrnoException) => {
            const code = err.code ?? '';
            this.log(`[error] Failed to start server: ${err.message}${code ? ` (${code})` : ''}`);
            this._spawnFailed = true;
            this._spawnFailedCode = code;
            this._isRunning = false;

            let detail = `Spawn failed: ${err.message}${code ? ` (${code})` : ''}`;
            if (isPermissionDeniedCode(code)) {
                detail +=
                    `\nWindows blocked the executable (Defender / SmartScreen / AppLocker). ` +
                    `Right-click ${useAppHost ? path.basename(exePath) : path.basename(this.dotnetPath)} ` +
                    `in Windows Explorer → Properties → check "Unblock", then click the CalcPad refresh button to retry.`;
            }
            this._lastCrashOutput.push(detail);
            this._processClosed = true; // make waitForReady fast-fail
        });

        this.serverProcess.on('exit', (code, signal) => {
            const decoded = decodeExitCode(code);
            this.log(`[exit] Server process exited (code=${code}${decoded ? ` ${decoded}` : ''}, signal=${signal})`);
            if (code !== null && code !== 0) {
                this.persistCrashRecord(code, signal, decoded);
            }
            this._isRunning = false;
            // Don't null out serverProcess during startup — waitForReady checks
            // _processClosed (from the 'close' event) to ensure stderr is fully drained.
            // Nulling here would cause waitForReady to bail before close fires.
            if (!this._startingUp) {
                this.serverProcess = null;
            }
            // Only clear the lock if we owned the process. A non-owner will never
            // see this handler since it doesn't hold a child-process reference.
            if (this._owned) {
                this.removeLockFile();
            }

            // Auto-restart if not intentionally disposed and not in initial startup
            // (during startup, waitForReady will detect the exit and report the error)
            if (!this._disposed && !this._startingUp && code !== 0) {
                this._restartCount++;
                if (this._restartCount < CalcpadServerManager.MAX_RESTARTS) {
                    this.log(`Unexpected exit — attempting restart ${this._restartCount}/${CalcpadServerManager.MAX_RESTARTS} in 2 seconds...`);
                    setTimeout(() => {
                        if (!this._disposed) {
                            this.start().catch(err => {
                                this.log(`Restart failed: ${err instanceof Error ? err.message : String(err)}`);
                            });
                        }
                    }, 2000);
                } else {
                    const crashOutput = this._lastCrashOutput.join('\n');
                    this.log(`Server crashed ${this._restartCount} times — auto-restart disabled. Use refresh to restart manually.`);
                    this.onCrashExhausted?.(crashOutput);
                }
            }
        });

        this._startingUp = true;
        try {
            await this.waitForReady(serverUrl);
            this._isRunning = true;
            this._lastCrashOutput = [];
            this.log(`Server is ready at ${serverUrl}`);
        } catch (err) {
            // If the spawn failed (Windows blocked the .exe, dotnet missing, etc.) the
            // child PID is unknown and the placeholder lock still holds the extension
            // host PID. Clean it up so peers don't wait on a phantom server and so a
            // later stop() doesn't try to kill our own process.
            if (this._spawnFailed) {
                this.removeLockFile();
            }
            throw err;
        } finally {
            this._startingUp = false;
            // If the process exited during startup, the exit handler deferred
            // nulling serverProcess so waitForReady could use _processClosed.
            // Clean it up now.
            if (this._processClosed) {
                this.serverProcess = null;
            }
        }
    }

    /**
     * Explicitly kill the server. Used by the `calcpad.stopServer` / refresh
     * commands. Kills regardless of ownership — if this instance merely connected
     * to a server spawned by another window, we look the PID up from the lock
     * file and kill that.
     */
    public async stop(): Promise<void> {
        this._disposed = true;

        if (!this.serverProcess) {
            // We don't own the process — kill whatever PID the lock file records.
            // SAFETY: when start() fails before the child PID is known (e.g. Windows
            // blocked the .exe), the placeholder lock still carries our own process.pid.
            // Killing that would terminate the extension host (and crash VS Code), so
            // skip the kill in that case and just clean up the stale lock.
            const lock = this.readLockFile();
            if (lock) {
                if (lock.pid === process.pid) {
                    this.log(`Discarding stale placeholder lock (our own PID ${lock.pid}, no child to kill)`);
                } else {
                    this.log(`Stopping shared server (PID ${lock.pid})`);
                    this.killByPid(lock.pid);
                }
                this.removeLockFile();
            }
            this._isRunning = false;
            return;
        }

        this.log('Stopping server...');

        const proc = this.serverProcess;
        const pid = proc.pid;
        const spawnNeverStarted = this._spawnFailed || pid === undefined;
        this.serverProcess = null;
        this._isRunning = false;

        if (pid) {
            this.killByPid(pid);
        }
        if (!spawnNeverStarted) {
            // Wait for the OS to confirm the process is gone. If spawn never produced
            // a real process (e.g. Windows blocked the exe), there's no exit event to
            // wait for — short-circuit so refresh can retry immediately.
            await new Promise<void>((resolve) => {
                if (proc.exitCode !== null) {
                    resolve();
                    return;
                }
                const timeout = setTimeout(() => resolve(), 5000);
                proc.once('exit', () => {
                    clearTimeout(timeout);
                    resolve();
                });
            });
        }

        this.removeLockFile();
        this.log('Server stopped');
    }

    /**
     * Detach from the server without killing it. Used by `deactivate()` so the
     * server keeps running for other VS Code windows (and for this window if
     * the extension reactivates).
     */
    public disconnect(): void {
        this._disposed = true;
        if (this.serverProcess) {
            // We were the owner. The child was spawned detached + unref'd, so
            // it already survives our exit. Drop our handle so Node doesn't
            // keep the event loop alive on the stdio pipes.
            try {
                this.serverProcess.stdout?.destroy();
                this.serverProcess.stderr?.destroy();
                this.serverProcess.stdin?.end();
            } catch {
                // best-effort
            }
            this.serverProcess = null;
        }
        this._isRunning = false;
        this.log('Disconnected from server (left running for other instances)');
    }

    private killByPid(pid: number): void {
        if (process.platform === 'win32') {
            try {
                execSync(`taskkill /F /T /PID ${pid}`, { timeout: 10000, stdio: 'ignore' });
            } catch {
                // already dead
            }
        } else {
            try { process.kill(pid, 'SIGTERM'); } catch { /* already dead */ }
            setTimeout(() => {
                try { process.kill(pid, 'SIGKILL'); } catch { /* already dead */ }
            }, 5000);
        }
    }

    /**
     * Get the base URL of the running server.
     */
    public getBaseUrl(): string {
        return `http://localhost:${this.port}`;
    }

    public get isRunning(): boolean {
        return this._isRunning;
    }

    /**
     * Stop and restart the server, resetting the retry counter.
     * Use this for manual restarts (e.g., refresh button).
     */
    public async restart(): Promise<void> {
        this._disposed = false;
        this._restartCount = 0;
        await this.stop();
        this._disposed = false; // stop() sets _disposed = true
        await this.start();
    }

    public getLastCrashOutput(): string {
        return this._lastCrashOutput.join('\n');
    }

    /**
     * Absolute path of the directory holding server logs, crash records, and
     * minidumps. Folder is created on demand by the spawn / persist paths,
     * so callers should ensure it exists (e.g. via fs.mkdirSync(..., { recursive: true }))
     * before opening it in the OS file explorer.
     */
    public getLogsDirectory(): string {
        return path.join(this.basePath, 'bin', 'logs');
    }

    public dispose(): void {
        // Default disposal = disconnect, not kill. Use stop() for explicit kill.
        this.disconnect();
    }

    private writeLockFile(lock: LockFileContents): void {
        try {
            fs.writeFileSync(this.lockFilePath, JSON.stringify(lock), 'utf-8');
        } catch (err) {
            this.log(`Warning: Could not write lock file: ${err instanceof Error ? err.message : String(err)}`);
        }
    }

    /**
     * Atomic exclusive-create: write the lock only if no lock file currently exists.
     * Returns true if this process won the claim, false on EEXIST (peer beat us).
     */
    private tryClaimLockExclusive(lock: LockFileContents): boolean {
        try {
            fs.writeFileSync(this.lockFilePath, JSON.stringify(lock), { encoding: 'utf-8', flag: 'wx' });
            return true;
        } catch (err: unknown) {
            if (err && typeof err === 'object' && 'code' in err && (err as { code: string }).code === 'EEXIST') {
                return false;
            }
            this.log(`Warning: Could not claim lock file: ${err instanceof Error ? err.message : String(err)}`);
            return false;
        }
    }

    /**
     * Poll for a peer-spawned server to come online and become healthy.
     * Used when we lost the lock-claim race: another window is in the middle
     * of spawning, and we want to reuse its server rather than spawn our own.
     */
    private async waitForPeerServer(timeoutMs: number): Promise<LockFileContents | null> {
        const deadline = Date.now() + timeoutMs;
        while (Date.now() < deadline) {
            const existing = await this.tryReuseExistingServer();
            if (existing) {
                return existing;
            }
            // If the lock has disappeared, the peer aborted — stop waiting.
            if (!fs.existsSync(this.lockFilePath)) {
                return null;
            }
            await new Promise(r => setTimeout(r, 500));
        }
        return null;
    }

    private readLockFile(): LockFileContents | null {
        try {
            if (!fs.existsSync(this.lockFilePath)) {
                return null;
            }
            const lock = JSON.parse(fs.readFileSync(this.lockFilePath, 'utf-8'));
            if (typeof lock.pid !== 'number' || typeof lock.port !== 'number') {
                return null;
            }
            return lock;
        } catch {
            return null;
        }
    }

    private removeLockFile(): void {
        try {
            if (fs.existsSync(this.lockFilePath)) {
                fs.unlinkSync(this.lockFilePath);
            }
        } catch {
            // Ignore — best effort cleanup
        }
    }

    private async findFreePort(): Promise<number> {
        return new Promise((resolve, reject) => {
            const server = net.createServer();
            server.listen(0, '127.0.0.1', () => {
                const address = server.address();
                if (address && typeof address !== 'string') {
                    const port = address.port;
                    server.close(() => resolve(port));
                } else {
                    server.close(() => reject(new Error('Could not allocate port')));
                }
            });
            server.on('error', reject);
        });
    }

    private async waitForReady(serverUrl: string, maxAttempts: number = 60, intervalMs: number = 500): Promise<void> {
        const healthUrl = `${serverUrl}/api/calcpad/snippets`;

        for (let i = 0; i < maxAttempts; i++) {
            // Fail fast if the server process has fully closed (stdio drained).
            // We check _processClosed (set by 'close' event) instead of exitCode
            // because 'close' fires after all stderr data events, ensuring
            // _lastCrashOutput is fully populated before we read it.
            if (!this.serverProcess || this._processClosed) {
                // Crash info comes from stderr (what we actually surface) plus the
                // server log file as fallback. stdout is intentionally excluded —
                // it's informational and goes only to the server debug channel.
                const stderr = this._lastCrashOutput.join('\n');
                const logFile = this.readServerLogFile();
                const parts: string[] = [];
                if (stderr) { parts.push(`[stderr]\n${stderr}`); }
                if (!stderr && logFile) { parts.push(`[log file]\n${logFile}`); }
                const crashOutput = parts.join('\n\n');
                throw new Error(
                    crashOutput
                        ? `Server process crashed during startup:\n${crashOutput}`
                        : 'Server process exited unexpectedly during startup (no output captured)'
                );
            }

            try {
                const response = await fetch(healthUrl);
                if (response.ok) {
                    return;
                }
            } catch {
                // Server not ready yet
            }
            await new Promise(r => setTimeout(r, intervalMs));
        }

        throw new Error(`Server did not become ready within ${maxAttempts * intervalMs / 1000} seconds`);
    }

    /**
     * Read the most recent server log file as a fallback when stderr capture is empty.
     * The server writes crash details via FileLogger to bin/logs/CalcpadServer-{date}.log.
     */
    private readServerLogFile(): string {
        try {
            const today = new Date();
            const dateStr = today.getFullYear().toString()
                + (today.getMonth() + 1).toString().padStart(2, '0')
                + today.getDate().toString().padStart(2, '0');
            const logPath = path.join(this.basePath, 'bin', 'logs', `CalcpadServer-${dateStr}.log`);

            if (!fs.existsSync(logPath)) {
                return '';
            }

            const content = fs.readFileSync(logPath, 'utf-8');
            // Return the last 40 lines to capture the most recent crash
            const lines = content.split('\n');
            return lines.slice(-40).join('\n').trim();
        } catch {
            return '';
        }
    }

    private log(message: string): void {
        this.logger.appendLine(`[ServerManager] ${message}`);
    }

    /**
     * Persist a crash record to disk so it survives extension reload / VS Code restart.
     * The in-process FileLogger can't capture StackOverflow / FailFast paths — this
     * is the parent-side complement that records what the runtime printed to stderr
     * along with the decoded exit code.
     *
     * Always writes to a fixed `last-crash.txt`, so each crash overwrites the previous
     * record (matching the dump-file rolling-overwrite policy).
     */
    private persistCrashRecord(code: number | null, signal: NodeJS.Signals | null, decoded: string): void {
        try {
            const crashDir = path.join(this.basePath, 'bin', 'logs');
            fs.mkdirSync(crashDir, { recursive: true });
            const file = path.join(crashDir, 'last-crash.txt');
            const lines = [
                `Calcpad.Server crash record`,
                `Timestamp: ${new Date().toISOString()}`,
                `Exit code: ${code} (0x${(code ?? 0) >>> 0 ? ((code ?? 0) >>> 0).toString(16).toUpperCase() : '0'})${decoded ? ' ' + decoded : ''}`,
                `Signal: ${signal ?? '(none)'}`,
                '',
                '--- last stderr ---',
                this._lastCrashOutput.join('\n') || '(empty)',
                '',
            ].join('\n');
            fs.writeFileSync(file, lines, 'utf-8');
            this.mainLogger.appendLine(`[ServerManager] Crash record written: ${file}`);
        } catch (err) {
            this.log(`Warning: could not persist crash record: ${err instanceof Error ? err.message : String(err)}`);
        }
    }
}

/**
 * True when a libuv spawn-error code indicates Windows (or another OS) refused to
 * start the executable. EACCES/EPERM cover Defender, SmartScreen, AppLocker, and
 * NTFS permission denials; we treat all of them the same and tell the user to
 * unblock the file.
 */
function isPermissionDeniedCode(code: string | null | undefined): boolean {
    return code === 'EACCES' || code === 'EPERM';
}

/**
 * Map common Windows exit codes from the .NET runtime to a human-readable label.
 * These codes come back through Node as signed 32-bit integers, so we mask to
 * unsigned before comparison.
 */
function decodeExitCode(code: number | null): string {
    if (code === null) return '';
    const u = code >>> 0;
    switch (u) {
        case 0x00000000: return '(success)';
        case 0xC0000005: return '(STATUS_ACCESS_VIOLATION)';
        case 0xC00000FD: return '(STATUS_STACK_OVERFLOW)';
        case 0xC000013A: return '(STATUS_CONTROL_C_EXIT — Ctrl+C)';
        case 0x80131623: return '(COR_E_FAILFAST — Environment.FailFast)';
        case 0x80131506: return '(COR_E_EXECUTIONENGINE)';
        case 0x80131500: return '(CLR generic exception)';
        default: return `(0x${u.toString(16).toUpperCase()})`;
    }
}
