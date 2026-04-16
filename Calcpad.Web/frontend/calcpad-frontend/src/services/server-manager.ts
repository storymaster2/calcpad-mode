import * as net from 'net';
import * as path from 'path';
import * as fs from 'fs';
import { spawn, execSync, ChildProcess } from 'child_process';
import type { ILogger } from '../types/interfaces';

/**
 * Manages the lifecycle of the bundled CalcPad server process.
 * Adapted from the VS Code extension's CalcpadServerManager with
 * vscode.OutputChannel replaced by ILogger interface.
 *
 * Uses a PID file to track the server process across extension restarts,
 * ensuring stale processes are cleaned up even after crashes or abrupt exits.
 */
export class CalcpadServerManager {
    private static readonly MAX_RESTARTS = 3;

    private serverProcess: ChildProcess | null = null;
    private port: number = 0;
    private logger: ILogger;
    private basePath: string;
    private dotnetPath: string;
    private _isRunning: boolean = false;
    private _disposed: boolean = false;
    private _startingUp: boolean = false;
    private _restartCount: number = 0;
    private _lastCrashOutput: string[] = [];
    private _lastStdoutOutput: string[] = [];
    private _processClosed: boolean = false;
    private pidFilePath: string;

    /** Called when auto-restart retries are exhausted. Receives the last stderr output. */
    public onCrashExhausted?: (crashOutput: string) => void;

    constructor(basePath: string, logger: ILogger, dotnetPath: string = 'dotnet') {
        this.basePath = basePath;
        this.logger = logger;
        this.dotnetPath = dotnetPath;
        this.pidFilePath = path.join(basePath, 'bin', '.calcpad-server.pid');
    }

    /**
     * Check if the bundled server DLL exists.
     */
    public static dllExists(basePath: string): boolean {
        const dllPath = path.join(basePath, 'bin', 'Calcpad.Server.dll');
        return fs.existsSync(dllPath);
    }

    /**
     * Kill any stale server process left over from a previous session.
     * Reads the PID from the PID file and kills it if still running.
     */
    public killStaleProcess(): void {
        try {
            if (!fs.existsSync(this.pidFilePath)) {
                return;
            }

            const stalePid = parseInt(fs.readFileSync(this.pidFilePath, 'utf-8').trim(), 10);
            if (isNaN(stalePid)) {
                this.removePidFile();
                return;
            }

            // Check if the process is still alive
            try {
                process.kill(stalePid, 0); // Signal 0 = existence check only
            } catch {
                this.log(`Stale PID ${stalePid} is no longer running`);
                this.removePidFile();
                return;
            }

            this.log(`Killing stale server process (PID ${stalePid})...`);
            if (process.platform === 'win32') {
                try {
                    execSync(`taskkill /F /T /PID ${stalePid}`, { timeout: 10000, stdio: 'ignore' });
                } catch {
                    // Process may have exited between check and kill
                }
            } else {
                try {
                    process.kill(stalePid, 'SIGKILL');
                } catch {
                    // Already dead
                }
            }
            this.log(`Stale process ${stalePid} cleaned up`);
            this.removePidFile();
        } catch (err) {
            this.log(`Error cleaning up stale process: ${err instanceof Error ? err.message : String(err)}`);
        }
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

        // Clean up any orphaned server from a previous session
        this.killStaleProcess();

        const dllPath = path.join(this.basePath, 'bin', 'Calcpad.Server.dll');
        if (!fs.existsSync(dllPath)) {
            throw new Error(`Calcpad.Server.dll not found at ${dllPath}`);
        }

        this.port = await this.findFreePort();
        this.log(`Starting server on port ${this.port}...`);

        const serverUrl = `http://localhost:${this.port}`;

        // Prefer the native apphost exe when available — it shows as
        // "Calcpad.Server" in Task Manager instead of ".NET Host".
        // Falls back to `dotnet Calcpad.Server.dll` for compatibility.
        const exeName = process.platform === 'win32' ? 'Calcpad.Server.exe' : 'Calcpad.Server';
        const exePath = path.join(this.basePath, 'bin', exeName);
        const useAppHost = fs.existsSync(exePath);

        this.serverProcess = useAppHost
            ? spawn(exePath, ['--urls', serverUrl], { stdio: ['pipe', 'pipe', 'pipe'] })
            : spawn(this.dotnetPath, [dllPath, '--urls', serverUrl], { stdio: ['pipe', 'pipe', 'pipe'] });
        this.log(`Spawned via ${useAppHost ? 'apphost' : 'dotnet'} (PID ${this.serverProcess.pid})`);

        // Write PID file so we can clean up orphaned processes on next startup
        if (this.serverProcess.pid) {
            this.writePidFile(this.serverProcess.pid);
        }

        this._lastStdoutOutput = [];
        this.serverProcess.stdout?.on('data', (data: Buffer) => {
            const text = data.toString().trim();
            this.log(`[stdout] ${text}`);
            // Buffer recent stdout lines — .NET runtime errors sometimes go to stdout
            this._lastStdoutOutput.push(text);
            if (this._lastStdoutOutput.length > 20) {
                this._lastStdoutOutput.shift();
            }
        });

        this.serverProcess.stderr?.on('data', (data: Buffer) => {
            const text = data.toString().trim();
            this.log(`[stderr] ${text}`);
            // Buffer recent stderr lines for crash reporting
            this._lastCrashOutput.push(text);
            if (this._lastCrashOutput.length > 20) {
                this._lastCrashOutput.shift();
            }
        });

        // The 'close' event fires after all stdio streams are drained,
        // so _lastCrashOutput is fully populated by the time this fires.
        this._processClosed = false;
        this.serverProcess.on('close', () => {
            this._processClosed = true;
        });

        this.serverProcess.on('error', (err: Error) => {
            this.log(`[error] Failed to start server: ${err.message}`);
            this._isRunning = false;
        });

        this.serverProcess.on('exit', (code, signal) => {
            this.log(`[exit] Server process exited (code=${code}, signal=${signal})`);
            this._isRunning = false;
            // Don't null out serverProcess during startup — waitForReady checks
            // _processClosed (from the 'close' event) to ensure stderr is fully drained.
            // Nulling here would cause waitForReady to bail before close fires.
            if (!this._startingUp) {
                this.serverProcess = null;
            }
            this.removePidFile();

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
            this._lastStdoutOutput = [];
            this.log(`Server is ready at ${serverUrl}`);
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
     * Stop the server process gracefully.
     */
    public async stop(): Promise<void> {
        this._disposed = true;

        if (!this.serverProcess) {
            return;
        }

        this.log('Stopping server...');

        const proc = this.serverProcess;
        const pid = proc.pid;
        this.serverProcess = null;
        this._isRunning = false;

        const isWindows = process.platform === 'win32';

        // On Windows, use taskkill /T to kill the entire process tree
        // (including child processes like chrome-headless-shell.exe).
        // Node's proc.kill() only kills the parent process on Windows.
        if (isWindows && pid) {
            try {
                execSync(`taskkill /F /T /PID ${pid}`, { timeout: 10000, stdio: 'ignore' });
                this.log('Server process tree killed');
            } catch {
                // Process may already be dead
                this.log('taskkill completed (process may have already exited)');
            }
        } else {
            proc.kill('SIGTERM');

            // Force kill after timeout
            await new Promise<void>((resolve) => {
                const timeout = setTimeout(() => {
                    try {
                        proc.kill('SIGKILL');
                    } catch {
                        // Process may already be dead
                    }
                    resolve();
                }, 5000);

                proc.on('exit', () => {
                    clearTimeout(timeout);
                    resolve();
                });
            });
        }

        this.removePidFile();
        this.log('Server stopped');
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

    public dispose(): void {
        this.stop();
    }

    private writePidFile(pid: number): void {
        try {
            fs.writeFileSync(this.pidFilePath, String(pid), 'utf-8');
        } catch (err) {
            this.log(`Warning: Could not write PID file: ${err instanceof Error ? err.message : String(err)}`);
        }
    }

    private removePidFile(): void {
        try {
            if (fs.existsSync(this.pidFilePath)) {
                fs.unlinkSync(this.pidFilePath);
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
                // Combine all available crash info: stderr, stdout, and log file
                const stderr = this._lastCrashOutput.join('\n');
                const stdout = this._lastStdoutOutput.join('\n');
                const logFile = this.readServerLogFile();
                const parts: string[] = [];
                if (stderr) { parts.push(`[stderr]\n${stderr}`); }
                if (stdout) { parts.push(`[stdout]\n${stdout}`); }
                if (!stderr && !stdout && logFile) { parts.push(`[log file]\n${logFile}`); }
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
     * The server writes crash details via FileLogger to CalcpadServer-{date}.log in the bin directory.
     */
    private readServerLogFile(): string {
        try {
            const today = new Date();
            const dateStr = today.getFullYear().toString()
                + (today.getMonth() + 1).toString().padStart(2, '0')
                + today.getDate().toString().padStart(2, '0');
            const logPath = path.join(this.basePath, 'bin', `CalcpadServer-${dateStr}.log`);

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
}
