import { os, filesystem, events } from '@neutralinojs/lib';

/**
 * Manages the lifecycle of the bundled Calcpad.Server process in the
 * Neutralino desktop app. Mirrors the VS Code calcpad-frontend
 * server-manager pattern (lock-file reuse, crash auto-restart with a
 * 3-strike cap, explicit Stop/Restart commands), but spawns the server
 * via `os.spawnProcess` instead of using Neutralino's extension mechanism.
 *
 * Why not Neutralino extensions?
 *   - Neutralino does not restart a crashed extension.
 *   - There is no API to stop/restart a single extension at runtime.
 *   - Each `window.create()` spawns a fresh Neutralino instance and a fresh
 *     copy of every extension — multi-window sharing is impossible.
 * Owning the lifecycle ourselves solves all three.
 */

const MAX_RESTARTS = 3;
const RESTART_DELAY_MS = 2000;
const PORT_FILE_TIMEOUT_MS = 15_000;

interface LockFileContents {
    pid: number;
    url: string;
    startedAt: number;
}

export interface ServerManagerLogger {
    appendLine(message: string): void;
}

/**
 * Detail payload of Neutralino's `spawnedProcess` global event.
 * The runtime fires this for every spawned process (stdout chunk, stderr
 * chunk, or exit). We filter by `id`.
 */
interface SpawnedProcessEventDetail {
    id: number;
    pid: number;
    action: 'stdOut' | 'stdErr' | 'exit';
    data: string | number;
}

export class NeutralinoServerManager {
    private spawnId: number | null = null;
    private spawnPid: number | null = null;
    private url = '';
    private _isRunning = false;
    private _owned = false;
    private _disposed = false;
    private _restartCount = 0;
    private _lastCrashOutput: string[] = [];
    private _intentionalStop = false;
    /** Guards the one-shot auto Unblock-File retry so a still-blocked exe
     *  can't loop. Reset on success and on user-initiated restart. */
    private _unblockAttempted = false;
    private spawnedListener: ((evt: Event) => void) | null = null;
    private logger: ServerManagerLogger;

    /** Called after auto-restart retries are exhausted; receives last stderr. */
    public onCrashExhausted?: (crashOutput: string) => void;

    /** Called whenever the active server URL changes (start, restart, reuse). */
    public onUrlChanged?: (newUrl: string) => void;

    /**
     * Called when startup times out with the process *still alive* — the
     * signature of a Windows SmartScreen / Defender block (the child is
     * spawned but held behind the "Windows protected your PC" modal, so it
     * never binds a port, never writes the port file, and never exits).
     * Receives the full path of the server executable so the UI can point
     * the user at the file to unblock. Distinct from onCrashExhausted, which
     * fires only after the process has actually exited and been retried.
     */
    public onStartupBlocked?: (exePath: string) => void;

    constructor(
        private readonly serverDir: string,
        logger: ServerManagerLogger,
        private readonly platform: string,
    ) {
        this.logger = logger;
    }

    /** Swap the logger sink. Used to redirect early-bootstrap logs into the
     *  Output panel once it has mounted. */
    setLogger(logger: ServerManagerLogger): void {
        this.logger = logger;
    }

    get isRunning(): boolean { return this._isRunning; }
    getBaseUrl(): string { return this.url; }

    private get portFilePath(): string { return `${this.serverDir}/.calcpad-server.port`; }
    private get lockFilePath(): string { return `${this.serverDir}/.calcpad-server.lock`; }
    private get logsDir(): string { return `${this.serverDir}/logs`; }
    private get stderrLogPath(): string { return `${this.logsDir}/server-stderr.log`; }
    private get serverExePath(): string {
        const name = this.platform === 'Windows' ? 'Calcpad.Server.exe' : 'Calcpad.Server';
        return `${this.serverDir}/${name}`;
    }

    /**
     * Start the server. Reuses a peer-owned server if the lock file points
     * at a live, healthy process. Otherwise spawns a fresh one, waits for
     * its port file, and adopts the bound URL.
     */
    async start(): Promise<void> {
        if (this._isRunning) {
            this.log('start() called but server is already running');
            return;
        }

        const existing = await this.tryReuseExistingServer();
        if (existing) {
            this.url = existing.url;
            this.spawnPid = existing.pid;
            this._owned = false;
            this._isRunning = true;
            this.log(`Reusing existing server (PID ${existing.pid}) at ${existing.url}`);
            this.onUrlChanged?.(this.url);
            return;
        }

        // Wipe stale state from a previous run that didn't clean up.
        await this.safeRemove(this.portFilePath);
        try { await filesystem.createDirectory(this.logsDir); } catch { /* exists */ }

        const cmd = this.buildSpawnCommand();
        this.log(`Spawning: ${cmd}`);
        // NOTE: do NOT pass the `envs` option here. Neutralino's Windows core
        // implementation of os.spawnProcess fails to launch the child when
        // `envs` is supplied — the process never executes (no port file, no
        // server log) and the spawn effectively no-ops, leaving serverUrl empty
        // and the app making doomed relative /api requests. We previously used
        // `envs: { CALCPAD_DETACHED: '1' }` to make the server skip the
        // Neutralino extension handshake / stdin-EOF watchdog; that is now done
        // with the `--no-exit-on-stdin-close` CLI flag in buildSpawnCommand(),
        // which is functionally identical and avoids the broken `envs` path.
        // cwd must use native separators — CreateProcess rejects a forward-slash
        // working directory on Windows.
        const result = await os.spawnProcess(cmd, {
            cwd: this.toNativePath(this.serverDir),
        });
        this.spawnId = result.id;
        this.spawnPid = result.pid;
        this._owned = true;
        this._intentionalStop = false;
        this.log(`Spawned (id=${result.id}, pid=${result.pid})`);

        // Truncate the stderr log for this fresh instance so the Output
        // panel "Show Server Log" only shows the current run.
        try { await filesystem.writeFile(this.stderrLogPath, ''); } catch { /* best-effort */ }

        this.attachSpawnedListener();

        try {
            this.url = await this.waitForPortFile(PORT_FILE_TIMEOUT_MS);
            this._isRunning = true;
            this._lastCrashOutput = [];
            this._unblockAttempted = false;
            await this.writeLockFile({
                pid: this.spawnPid,
                url: this.url,
                startedAt: Date.now(),
            });
            this.log(`Server ready at ${this.url}`);
            this.onUrlChanged?.(this.url);
        } catch (err) {
            // If the process is STILL ALIVE at the timeout (spawnId not nulled
            // by an exit event), it never bound a port — on Windows the usual
            // cause is SmartScreen / Defender holding the unsigned exe behind
            // the "Windows protected your PC" modal. Treat it as a block:
            // re-spawning blindly just re-blocks, so DON'T trip the 3-strike
            // auto-restart. A genuine crash nulls spawnId via handleExit, so
            // this branch is skipped for crashes.
            const likelyBlocked = this.spawnId !== null;
            if (likelyBlocked) {
                this._intentionalStop = true; // suppress handleExit auto-restart
            }
            await this.removeLockFile();
            await this.killSpawned();

            if (likelyBlocked) {
                // First block on Windows: the exe almost certainly carries
                // Mark-of-the-Web (the Zone.Identifier stream SmartScreen
                // gates on). Strip it with Unblock-File and retry startup
                // once before bothering the user. The one-shot guard plus
                // start()'s own reset-on-success keep this from looping.
                if (this.platform === 'Windows' && !this._unblockAttempted) {
                    this._unblockAttempted = true;
                    const unblocked = await this.tryUnblockWindows();
                    if (unblocked) {
                        this.log('Unblocked server files — retrying startup');
                        this._isRunning = false;
                        return await this.start();
                    }
                }
                this.log(`Server process started but never became ready — Windows may be blocking ${this.serverExePath}`);
                this.onStartupBlocked?.(this.serverExePath);
            }
            throw err;
        }
    }

    /**
     * Stop the server. If we own it, signal it via `updateSpawnedProcess`.
     * If we merely connected to a peer's server, leave the peer alive
     * (`disconnect`-style) — the caller can call `forceStop` to override.
     */
    async stop(): Promise<void> {
        this._intentionalStop = true;
        this._disposed = false;

        if (this.spawnId !== null && this._owned) {
            await this.killSpawned();
        } else if (this.spawnPid !== null && !this._owned) {
            this.log(`Disconnecting from peer server (PID ${this.spawnPid}); leaving it running`);
        }

        this.spawnId = null;
        this.spawnPid = null;
        this._isRunning = false;
        if (this._owned) {
            await this.removeLockFile();
        }
        this._owned = false;
        this.log('Server stopped');
    }

    /**
     * Stop the server unconditionally — kills the peer-owned process too.
     * Used by the explicit "Stop Server" menu action where the user wants
     * the server gone regardless of who spawned it.
     */
    async forceStop(): Promise<void> {
        this._intentionalStop = true;
        this._disposed = false;

        if (this.spawnId !== null && this._owned) {
            await this.killSpawned();
        } else if (this.spawnPid !== null) {
            this.log(`Stopping peer-owned server (PID ${this.spawnPid})`);
            await this.killByPid(this.spawnPid);
        }

        this.spawnId = null;
        this.spawnPid = null;
        this._isRunning = false;
        await this.removeLockFile();
        await this.safeRemove(this.portFilePath);
        this._owned = false;
        this.log('Server stopped');
    }

    /** Stop then start. Resets the crash counter so the user gets a clean retry. */
    async restart(): Promise<void> {
        this._restartCount = 0;
        await this.forceStop();
        await this.start();
    }

    /**
     * App-exit cleanup. Kills our spawned process so it doesn't outlive
     * the desktop window. Peer-owned processes are left alone — another
     * window may still be using them.
     */
    async dispose(): Promise<void> {
        this._disposed = true;
        this._intentionalStop = true;
        if (this._owned && this.spawnId !== null) {
            await this.killSpawned();
            await this.removeLockFile();
        }
        if (this.spawnedListener) {
            try { await events.off('spawnedProcess', this.spawnedListener); } catch { /* ignored */ }
            this.spawnedListener = null;
        }
    }

    private attachSpawnedListener(): void {
        if (this.spawnedListener) return; // already attached
        const handler = (evt: Event) => {
            const detail = (evt as CustomEvent<SpawnedProcessEventDetail>).detail;
            if (!detail || detail.id !== this.spawnId) return;
            switch (detail.action) {
                case 'stdOut':
                    // Server INFO messages → log file only; not surfaced as crash output.
                    this.appendStderrLog(String(detail.data));
                    break;
                case 'stdErr': {
                    const text = String(detail.data);
                    this._lastCrashOutput.push(text);
                    if (this._lastCrashOutput.length > 20) this._lastCrashOutput.shift();
                    this.appendStderrLog(text);
                    break;
                }
                case 'exit':
                    this.handleExit(Number(detail.data));
                    break;
            }
        };
        this.spawnedListener = handler;
        events.on('spawnedProcess', handler);
    }

    private async appendStderrLog(text: string): Promise<void> {
        try { await filesystem.appendFile(this.stderrLogPath, text); } catch { /* best-effort */ }
    }

    private handleExit(exitCode: number): void {
        this.log(`Server exited (code=${exitCode})`);
        this.spawnId = null;
        this.spawnPid = null;
        this._isRunning = false;
        if (this._owned) {
            this.removeLockFile().catch(() => { /* best-effort */ });
        }

        if (this._intentionalStop || this._disposed) return;

        this._restartCount++;
        if (this._restartCount < MAX_RESTARTS) {
            this.log(`Unexpected exit — restart ${this._restartCount}/${MAX_RESTARTS} in ${RESTART_DELAY_MS}ms`);
            setTimeout(() => {
                if (this._disposed) return;
                this.start().catch(err => {
                    this.log(`Restart failed: ${err instanceof Error ? err.message : String(err)}`);
                });
            }, RESTART_DELAY_MS);
        } else {
            const crashOutput = this._lastCrashOutput.join('\n');
            this.log(`Server crashed ${this._restartCount} times — auto-restart disabled`);
            this.onCrashExhausted?.(crashOutput);
        }
    }

    private buildSpawnCommand(): string {
        // Quote both for paths with spaces (common on Windows + macOS), and use
        // native separators — CreateProcess on Windows is unreliable with a
        // forward-slash program path. `--no-exit-on-stdin-close` disables the
        // server's stdin-EOF watchdog and Neutralino extension handshake (the
        // role CALCPAD_DETACHED=1 used to play via the `envs` option, which is
        // broken on the Windows Neutralino core — see start()).
        const exe = this.toNativePath(this.serverExePath);
        const portFile = this.toNativePath(this.portFilePath);
        return `"${exe}" --port-file "${portFile}" --no-exit-on-stdin-close`;
    }

    /**
     * Convert a forward-slash path (our internal canonical form, since
     * getServerDir normalizes NL_PATH with `/`) to native OS separators.
     * Neutralino's filesystem.* APIs accept forward slashes, but anything that
     * flows into os.spawnProcess (the command's program path and the cwd) hits
     * Win32 CreateProcess, which wants backslashes on Windows.
     */
    private toNativePath(p: string): string {
        return this.platform === 'Windows' ? p.replace(/\//g, '\\') : p;
    }

    private async waitForPortFile(timeoutMs: number): Promise<string> {
        const deadline = Date.now() + timeoutMs;
        let delay = 50;
        while (Date.now() < deadline) {
            // Bail out early if the process died during startup — the exit
            // handler will have nulled spawnId in that case.
            if (this.spawnId === null) {
                const crash = this._lastCrashOutput.join('\n').trim();
                throw new Error(
                    crash
                        ? `Server exited during startup:\n${crash}`
                        : 'Server process exited unexpectedly during startup (no output captured)',
                );
            }
            try {
                const url = (await filesystem.readFile(this.portFilePath)).trim();
                if (url) return url;
            } catch { /* not written yet */ }
            await new Promise(r => setTimeout(r, delay));
            delay = Math.min(delay * 1.5, 500);
        }
        throw new Error(`Server did not become ready within ${timeoutMs}ms`);
    }

    private async tryReuseExistingServer(): Promise<LockFileContents | null> {
        let lock: LockFileContents;
        try {
            const raw = await filesystem.readFile(this.lockFilePath);
            lock = JSON.parse(raw);
            if (typeof lock.pid !== 'number' || typeof lock.url !== 'string') {
                await this.removeLockFile();
                return null;
            }
        } catch {
            return null;
        }

        try {
            const resp = await fetch(`${lock.url}/api/calcpad/snippets`, {
                signal: AbortSignal.timeout(2000),
            });
            if (!resp.ok) {
                this.log(`Lock URL ${lock.url} responded HTTP ${resp.status} — treating as stale`);
                await this.removeLockFile();
                return null;
            }
        } catch (err) {
            this.log(`Lock URL ${lock.url} unreachable: ${err instanceof Error ? err.message : String(err)}`);
            await this.removeLockFile();
            return null;
        }
        return lock;
    }

    private async writeLockFile(lock: LockFileContents): Promise<void> {
        try {
            await filesystem.writeFile(this.lockFilePath, JSON.stringify(lock));
        } catch (err) {
            this.log(`Could not write lock file: ${err instanceof Error ? err.message : String(err)}`);
        }
    }

    private async removeLockFile(): Promise<void> {
        await this.safeRemove(this.lockFilePath);
    }

    private async safeRemove(path: string): Promise<void> {
        try { await filesystem.remove(path); } catch { /* not there or permission */ }
    }

    private async killSpawned(): Promise<void> {
        if (this.spawnId === null) return;
        try {
            await os.updateSpawnedProcess(this.spawnId, 'exit');
        } catch (err) {
            this.log(`updateSpawnedProcess(exit) failed: ${err instanceof Error ? err.message : String(err)}`);
            if (this.spawnPid !== null) {
                await this.killByPid(this.spawnPid);
            }
        }
    }

    private async killByPid(pid: number): Promise<void> {
        const cmd = this.platform === 'Windows'
            ? `taskkill /F /T /PID ${pid}`
            : `kill -TERM ${pid}`;
        try {
            await os.execCommand(cmd, { background: false });
        } catch (err) {
            this.log(`killByPid(${pid}) failed: ${err instanceof Error ? err.message : String(err)}`);
        }
    }

    private log(message: string): void {
        this.logger.appendLine(`[ServerManager] ${message}`);
    }
}
