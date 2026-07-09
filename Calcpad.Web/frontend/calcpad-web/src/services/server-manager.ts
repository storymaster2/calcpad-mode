import { invoke } from '@tauri-apps/api/core';
import { listen, type UnlistenFn } from '@tauri-apps/api/event';

/**
 * Manages the client-side view of the Calcpad.Server lifecycle. The actual
 * process is spawned and killed by the Tauri Rust layer (see src-tauri/src/lib.rs):
 *   - `spawn_sidecar` runs at app startup and emits `server-url` when Kestrel binds.
 *   - `stop_server` / `restart_server` are exposed as invoke commands.
 *   - The child is killed on window close and app exit — no TS shutdown work.
 *
 * This class keeps the crash counter and the "auto-restart exhausted" UI hook,
 * which are the parts the frontend has to own. Everything else is delegated.
 */

const MAX_AUTO_RESTARTS = 3;
const AUTO_RESTART_DELAY_MS = 2000;
const START_TIMEOUT_MS = 30_000;

export interface ServerManagerLogger {
    appendLine(message: string): void;
}

interface ServerCrashPayload {
    code: number | null;
    tail: string;
}

export class TauriServerManager {
    private url = '';
    private _isRunning = false;
    private _crashCount = 0;
    private unlistenUrl: UnlistenFn | null = null;
    private unlistenCrash: UnlistenFn | null = null;
    private unlistenStartupError: UnlistenFn | null = null;
    private logger: ServerManagerLogger;

    public onCrashExhausted?: (crashOutput: string) => void;
    public onUrlChanged?: (newUrl: string) => void;
    public onStartupBlocked?: (details: string) => void;

    constructor(logger: ServerManagerLogger) {
        this.logger = logger;
    }

    setLogger(logger: ServerManagerLogger): void {
        this.logger = logger;
    }

    get isRunning(): boolean { return this._isRunning; }
    getBaseUrl(): string { return this.url; }

    async start(): Promise<void> {
        // Callers block on start() so the API bridge sees a real URL from
        // its first request. Resolve as soon as we learn the URL from either
        // channel; time out after START_TIMEOUT_MS to avoid a wedged boot.
        const startedAt = performance.now();
        const t = () => Math.round(performance.now() - startedAt);
        this.log(`[timing] start() called`);
        let resolveReady: ((url: string) => void) | null = null;
        let rejectReady: ((err: Error) => void) | null = null;
        const ready = new Promise<string>((resolve, reject) => {
            resolveReady = resolve;
            rejectReady = reject;
        });

        try {
            this.unlistenUrl = await listen<string>('server-url', (evt) => {
                this.url = evt.payload;
                this._isRunning = true;
                this._crashCount = 0;
                this.log(`[timing] server-url event received ${t()}ms after start()`);
                this.log(`Server ready at ${this.url}`);
                this.onUrlChanged?.(this.url);
                resolveReady?.(this.url);
                resolveReady = null;
            });
            this.log(`[timing] server-url listener ready at ${t()}ms`);
        } catch (err) {
            this.log(`[timing] server-url listener FAILED at ${t()}ms: ${err instanceof Error ? err.message : String(err)}`);
            throw err;
        }

        this.unlistenCrash = await listen<ServerCrashPayload>('server-crashed', (evt) => {
            this._isRunning = false;
            this.url = '';
            this._crashCount++;
            this.log(`Server crashed (code=${evt.payload.code ?? 'unknown'}) — attempt ${this._crashCount}/${MAX_AUTO_RESTARTS}`);
            if (this._crashCount < MAX_AUTO_RESTARTS) {
                setTimeout(() => { void this.restart(); }, AUTO_RESTART_DELAY_MS);
            } else {
                this.onCrashExhausted?.(evt.payload.tail || '');
            }
        });

        this.unlistenStartupError = await listen<string>('server-startup-error', (evt) => {
            this.log(`Server failed to start: ${evt.payload}`);
            this.onStartupBlocked?.(evt.payload);
            rejectReady?.(new Error(evt.payload));
            rejectReady = null;
            resolveReady = null;
        });

        this.log(`[timing] all listeners ready at ${t()}ms`);

        // Rust starts spawning the sidecar in setup() before our listeners
        // registered, so first check if the URL is already known — otherwise
        // wait for the server-url event to fire.
        try {
            const current = await invoke<string | null>('server_url');
            this.log(`[timing] server_url invoke returned "${current ?? 'null'}" at ${t()}ms`);
            if (current) {
                this.url = current;
                this._isRunning = true;
                this.log(`Server already running at ${current}`);
                this.onUrlChanged?.(this.url);
                return;
            }
        } catch (err) {
            this.log(`[timing] server_url invoke failed at ${t()}ms: ${err instanceof Error ? err.message : String(err)}`);
        }

        this.log(`[timing] awaiting server-url event race at ${t()}ms`);
        const timeout = new Promise<never>((_, reject) =>
            setTimeout(() => reject(new Error(`server did not report a URL within ${START_TIMEOUT_MS}ms`)), START_TIMEOUT_MS),
        );
        try {
            await Promise.race([ready, timeout]);
            this.log(`[timing] race resolved at ${t()}ms`);
        } catch (err) {
            this.log(`[timing] race REJECTED at ${t()}ms: ${err instanceof Error ? err.message : String(err)}`);
            throw err;
        }
    }

    /** Ask Rust to stop the sidecar. */
    async stop(): Promise<void> {
        try {
            await invoke('stop_server');
        } catch (err) {
            this.log(`stop_server invoke failed: ${err instanceof Error ? err.message : String(err)}`);
        }
        this._isRunning = false;
        this.url = '';
    }

    /** Force-stop then respawn via Rust. Resets the crash counter. */
    async restart(): Promise<void> {
        this._crashCount = 0;
        try {
            const newUrl = await invoke<string>('restart_server');
            this.url = newUrl;
            this._isRunning = true;
            this.onUrlChanged?.(this.url);
            this.log(`Server restarted at ${newUrl}`);
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            this.log(`restart_server invoke failed: ${msg}`);
            this._isRunning = false;
            this.url = '';
            throw err;
        }
    }

    /** Alias — Rust owns kill-on-exit, but the menu action still exists. */
    async forceStop(): Promise<void> {
        return this.stop();
    }

    /** Detach event listeners. Rust reaps the sidecar on window close. */
    async dispose(): Promise<void> {
        try { this.unlistenUrl?.(); } catch { /* ignore */ }
        try { this.unlistenCrash?.(); } catch { /* ignore */ }
        try { this.unlistenStartupError?.(); } catch { /* ignore */ }
        this.unlistenUrl = null;
        this.unlistenCrash = null;
        this.unlistenStartupError = null;
    }

    private log(message: string): void {
        this.logger.appendLine(`[ServerManager] ${message}`);
    }
}
