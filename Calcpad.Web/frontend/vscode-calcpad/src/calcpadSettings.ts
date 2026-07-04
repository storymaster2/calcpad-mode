import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import {
    CalcpadSettings,
    CalcpadExtras,
    getDefaultSettings,
    getDefaultExtras,
    getDefaultSettingsBlob,
    deserializeSettingsBlob,
    serializeSettingsBlob,
    buildApiSettings,
} from 'calcpad-frontend';

export type { CalcpadSettings, CalcpadExtras };

const SETTINGS_DIR_NAME = 'settings';
const DEFAULT_CONFIG_NAME = 'default';
const ACTIVE_CONFIG_KEY = 'calcpad-active-settings';

// The only key that stays as a workspace configuration entry, because
// contributes.keybindings clauses read `config.calcpad.enableFormattingHotkeys`
// and there is no way to expose our JSON-backed value to a `when` clause.
const HOTKEYS_WORKSPACE_KEY = 'enableFormattingHotkeys';
const HOTKEYS_EXTRA_KEY = 'formattingHotkeys';

let _outputChannel: vscode.OutputChannel | undefined;

function getOutputChannel(): vscode.OutputChannel {
    if (!_outputChannel) {
        _outputChannel = vscode.window.createOutputChannel('CalcPad Settings');
    }
    return _outputChannel;
}

export class CalcpadSettingsManager {
    private static instance: CalcpadSettingsManager;
    private _settings: CalcpadSettings;
    private _extras: CalcpadExtras;
    private _activeConfigName: string = DEFAULT_CONFIG_NAME;
    private _onDidChangeSettings = new vscode.EventEmitter<CalcpadSettings>();
    public readonly onDidChangeSettings = this._onDidChangeSettings.event;
    private _context?: vscode.ExtensionContext;
    private _localServerUrl?: string;
    /** Resolves once the on-disk config has been loaded. */
    public readonly ready: Promise<void>;

    private constructor(context?: vscode.ExtensionContext) {
        this._settings = getDefaultSettings();
        this._extras = getDefaultExtras();
        if (context) {
            this._context = context;
        }
        this.ready = this.loadFromDisk();
    }

    public static getInstance(context?: vscode.ExtensionContext): CalcpadSettingsManager {
        if (!CalcpadSettingsManager.instance) {
            CalcpadSettingsManager.instance = new CalcpadSettingsManager(context);
        }
        if (context && !CalcpadSettingsManager.instance._context) {
            CalcpadSettingsManager.instance._context = context;
            // Re-run load with context now available (initial constructor call
            // may have skipped disk access if context was missing).
            CalcpadSettingsManager.instance.loadFromDisk();
        }
        return CalcpadSettingsManager.instance;
    }

    public getDefaultSettings(): CalcpadSettings {
        return getDefaultSettings();
    }

    public getSettings(): CalcpadSettings {
        return { ...this._settings };
    }

    public getExtras(): CalcpadExtras {
        return { ...this._extras };
    }

    /** Get a string extra; falls back to `defaultValue`. */
    public getExtra(key: string, defaultValue: string = ''): string {
        const v = this._extras[key];
        return v === undefined || v === '' ? defaultValue : v;
    }

    /** Get a boolean extra; falls back to `defaultValue`. */
    public getExtraBool(key: string, defaultValue: boolean): boolean {
        const v = this._extras[key];
        if (v === undefined) return defaultValue;
        if (v === 'true') return true;
        if (v === 'false') return false;
        return defaultValue;
    }

    /** Get a numeric extra; falls back to `defaultValue`. */
    public getExtraNumber(key: string, defaultValue: number): number {
        const v = this._extras[key];
        if (v === undefined || v === '') return defaultValue;
        const n = Number(v);
        return Number.isFinite(n) ? n : defaultValue;
    }

    /** Get a JSON-object extra; falls back to `defaultValue`. */
    public getExtraObject<T>(key: string, defaultValue: T): T {
        const v = this._extras[key];
        if (!v) return defaultValue;
        try { return JSON.parse(v) as T; } catch { return defaultValue; }
    }

    /** Persist an extra to the active config file (string, boolean, number, or object). */
    public setExtra(key: string, value: unknown): void {
        if (value === null || value === undefined) {
            this._extras[key] = '';
        } else if (typeof value === 'object') {
            this._extras[key] = JSON.stringify(value);
        } else {
            this._extras[key] = String(value);
        }
        if (key === HOTKEYS_EXTRA_KEY) {
            void this.mirrorFormattingHotkeys();
        }
        void this.saveToDisk();
    }

    /**
     * Returns the effective server URL for API calls.
     * Uses the local server URL if a bundled server is running,
     * otherwise falls back to the user-configured remote URL.
     */
    public getServerUrl(): string {
        return this._localServerUrl ?? this._settings.server.url;
    }

    /** Returns the user-configured remote server URL (always from settings, ignores local server). */
    public getRemoteServerUrl(): string {
        return this._settings.server.url;
    }

    public updateSettings(newSettings: Partial<CalcpadSettings>): void {
        this._settings = { ...this._settings, ...newSettings };
        void this.saveToDisk();
        this._onDidChangeSettings.fire(this._settings);
    }

    /**
     * Sets the local server URL (from the running bundled server).
     * Ephemeral — not persisted.
     */
    public setLocalServerUrl(url: string): void {
        this._localServerUrl = url;
        this._onDidChangeSettings.fire(this._settings);
    }

    public async resetSettings(): Promise<void> {
        // Switch active back to the pristine default.json (rewritten on every
        // activation), leaving other named configs untouched.
        await this.setActiveConfigName(DEFAULT_CONFIG_NAME);
        await this.loadConfigFile(DEFAULT_CONFIG_NAME);
        this._onDidChangeSettings.fire(this._settings);
    }

    public async getApiSettings(): Promise<unknown> {
        const apiSettings = buildApiSettings(this._settings);

        const outputChannel = getOutputChannel();
        outputChannel.appendLine('API settings being sent:');
        outputChannel.appendLine(`  Local Server URL: ${this._localServerUrl ?? '(none)'}`);
        outputChannel.appendLine(`  Remote Server URL: ${this._settings.server.url}`);
        outputChannel.appendLine(`  Effective Server URL: ${this.getServerUrl()}`);

        return apiSettings;
    }

    // ---- Named-config file I/O ----

    public getActiveConfigName(): string {
        return this._activeConfigName;
    }

    /**
     * List available named configs (basenames without `.json`) with `default`
     * first. Returns `['default']` if the folder can't be read.
     */
    public async listConfigs(): Promise<string[]> {
        const dir = this.getSettingsDir();
        if (!dir) return [DEFAULT_CONFIG_NAME];
        try {
            const entries = await fs.promises.readdir(dir, { withFileTypes: true });
            const names = entries
                .filter(e => e.isFile() && e.name.toLowerCase().endsWith('.json'))
                .map(e => e.name.slice(0, -'.json'.length));
            const rest = names.filter(n => n !== DEFAULT_CONFIG_NAME).sort();
            return [DEFAULT_CONFIG_NAME, ...rest];
        } catch {
            return [DEFAULT_CONFIG_NAME];
        }
    }

    /**
     * Save current in-memory settings to a user-named config and switch active
     * to it. Rejects the reserved `default` name and paths that contain
     * filename-illegal characters.
     */
    public async saveNamedConfig(rawName: string): Promise<{ ok: true } | { ok: false; message: string }> {
        const name = (rawName ?? '').trim();
        if (!name) return { ok: false, message: 'Config name cannot be empty.' };
        if (name.toLowerCase() === DEFAULT_CONFIG_NAME) {
            return { ok: false, message: 'The "default" config is protected and cannot be overridden.' };
        }
        if (/[\\/:*?"<>|\x00-\x1f]/.test(name)) {
            return { ok: false, message: 'Config name contains invalid characters.' };
        }
        await this.setActiveConfigName(name);
        await this.saveToDisk();
        return { ok: true };
    }

    public async switchConfig(name: string): Promise<void> {
        if (!name) return;
        await this.setActiveConfigName(name);
        await this.loadConfigFile(name);
        this._onDidChangeSettings.fire(this._settings);
    }

    public async openSettingsFolder(): Promise<void> {
        const dir = this.getSettingsDir();
        if (!dir) return;
        try { await fs.promises.mkdir(dir, { recursive: true }); } catch { /* already exists */ }
        try {
            await vscode.env.openExternal(vscode.Uri.file(dir));
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            getOutputChannel().appendLine(`[Settings] Failed to open settings folder: ${msg}`);
        }
    }

    // ---- Internal ----

    private getSettingsDir(): string | null {
        if (!this._context) return null;
        return path.join(this._context.globalStorageUri.fsPath, SETTINGS_DIR_NAME);
    }

    private getConfigPath(name: string): string | null {
        const dir = this.getSettingsDir();
        return dir ? path.join(dir, `${name}.json`) : null;
    }

    /**
     * Ensure the settings folder exists and `default.json` is fresh from the
     * bundled defaults. Runs on activation so `default` always represents
     * pristine defaults even if the user edited it in a previous session.
     */
    private async ensureSettingsFolder(): Promise<void> {
        const dir = this.getSettingsDir();
        if (!dir) return;
        await fs.promises.mkdir(dir, { recursive: true });
        const defaultPath = this.getConfigPath(DEFAULT_CONFIG_NAME);
        if (defaultPath) {
            const blob = getDefaultSettingsBlob();
            try {
                await fs.promises.writeFile(defaultPath, JSON.stringify(blob, null, 4), 'utf8');
            } catch (err) {
                const msg = err instanceof Error ? err.message : String(err);
                getOutputChannel().appendLine(`[Settings] Could not refresh default.json: ${msg}`);
            }
        }
    }

    private async setActiveConfigName(name: string): Promise<void> {
        this._activeConfigName = name;
        if (this._context) {
            await this._context.globalState.update(ACTIVE_CONFIG_KEY, name);
        }
    }

    private loadActiveConfigName(): string {
        if (!this._context) return DEFAULT_CONFIG_NAME;
        const stored = this._context.globalState.get<string>(ACTIVE_CONFIG_KEY);
        return (stored ?? '').trim() || DEFAULT_CONFIG_NAME;
    }

    private async loadFromDisk(): Promise<void> {
        if (!this._context) return;
        await this.ensureSettingsFolder();
        this._activeConfigName = this.loadActiveConfigName();
        await this.loadConfigFile(this._activeConfigName);
    }

    private async loadConfigFile(name: string): Promise<void> {
        const configPath = this.getConfigPath(name);
        if (!configPath) return;
        try {
            const raw = await fs.promises.readFile(configPath, 'utf8');
            const parsed = JSON.parse(raw);
            const { settings, extras } = deserializeSettingsBlob(parsed);
            this._settings = settings;
            this._extras = extras;
        } catch {
            this._settings = getDefaultSettings();
            this._extras = getDefaultExtras();
        }
        await this.mirrorFormattingHotkeys();
    }

    private async saveToDisk(): Promise<void> {
        const configPath = this.getConfigPath(this._activeConfigName);
        if (!configPath) return;
        const blob = serializeSettingsBlob(this._settings, this._extras);
        try {
            await fs.promises.mkdir(path.dirname(configPath), { recursive: true });
            await fs.promises.writeFile(configPath, JSON.stringify(blob, null, 4), 'utf8');
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            getOutputChannel().appendLine(`[Settings] Failed to write ${configPath}: ${msg}`);
        }
    }

    /**
     * Mirror the JSON-backed `formattingHotkeys` extra onto the workspace
     * setting `calcpad.enableFormattingHotkeys`, because keybinding `when`
     * clauses can only reference `config.*` values from workspace/user config.
     */
    private async mirrorFormattingHotkeys(): Promise<void> {
        const desired = this.getExtraBool(HOTKEYS_EXTRA_KEY, true);
        const workspace = vscode.workspace.getConfiguration('calcpad');
        const current = workspace.get<boolean>(HOTKEYS_WORKSPACE_KEY);
        if (current !== desired) {
            try {
                await workspace.update(HOTKEYS_WORKSPACE_KEY, desired, vscode.ConfigurationTarget.Global);
            } catch (err) {
                const msg = err instanceof Error ? err.message : String(err);
                getOutputChannel().appendLine(`[Settings] Could not mirror ${HOTKEYS_WORKSPACE_KEY}: ${msg}`);
            }
        }
    }
}
