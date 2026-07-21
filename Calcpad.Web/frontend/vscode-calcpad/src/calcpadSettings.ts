import * as vscode from 'vscode';
import {
    CalcpadSettings,
    getDefaultSettings,
    buildApiSettings,
} from 'calcpad-frontend';

export type { CalcpadSettings };

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
    private _onDidChangeSettings = new vscode.EventEmitter<CalcpadSettings>();
    public readonly onDidChangeSettings = this._onDidChangeSettings.event;
    private _context?: vscode.ExtensionContext;
    private _localServerUrl?: string; // Dynamic URL from running local server (not persisted)

    private constructor(context?: vscode.ExtensionContext) {
        this._settings = getDefaultSettings();
        this.loadSettings();
        if (context) {
            this._context = context;
        }
    }

    public static getInstance(context?: vscode.ExtensionContext): CalcpadSettingsManager {
        if (!CalcpadSettingsManager.instance) {
            CalcpadSettingsManager.instance = new CalcpadSettingsManager(context);
        }
        if (context && !CalcpadSettingsManager.instance._context) {
            CalcpadSettingsManager.instance._context = context;
        }
        return CalcpadSettingsManager.instance;
    }

    public getDefaultSettings(): CalcpadSettings {
        return getDefaultSettings();
    }

    public getSettings(): CalcpadSettings {
        return { ...this._settings };
    }

    /**
     * Returns the effective server URL for API calls.
     * Uses the local server URL if a bundled server is running,
     * otherwise falls back to the user-configured remote URL.
     */
    public getServerUrl(): string {
        return this._localServerUrl ?? this._settings.server.url;
    }

    /**
     * Returns the user-configured remote server URL (always from settings, ignores local server).
     * Use this for operations that must go to the remote server (e.g., auth).
     */
    public getRemoteServerUrl(): string {
        return this._settings.server.url;
    }

    public updateSettings(newSettings: Partial<CalcpadSettings>): void {
        this._settings = { ...this._settings, ...newSettings };
        this.saveSettings();
        this._onDidChangeSettings.fire(this._settings);
    }

    /**
     * Sets the local server URL (from the running bundled server).
     * This is ephemeral — not persisted to workspace config since the port changes every restart.
     */
    public setLocalServerUrl(url: string): void {
        this._localServerUrl = url;
        this._onDidChangeSettings.fire(this._settings);
    }

    public resetSettings(): void {
        this._settings = getDefaultSettings();
        this.saveSettings();
        this._onDidChangeSettings.fire(this._settings);
    }

    private loadSettings(): void {
        const config = vscode.workspace.getConfiguration('calcpad');
        const savedSettings = config.get<CalcpadSettings>('settings');
        if (savedSettings) {
            this._settings = { ...getDefaultSettings(), ...savedSettings };
        }
    }

    private saveSettings(): void {
        const config = vscode.workspace.getConfiguration('calcpad');
        const outputChannel = getOutputChannel();
        outputChannel.appendLine(`[Settings] Saving settings: ${JSON.stringify(this._settings, null, 2)}`);
        config.update('settings', this._settings, vscode.ConfigurationTarget.Workspace);
        outputChannel.appendLine(`[Settings] Settings saved to workspace configuration`);
    }

    public async getStoredS3JWT(): Promise<string> {
        if (!this._context) {
            const outputChannel = getOutputChannel();
            outputChannel.appendLine('Warning: Extension context not available for S3 JWT retrieval');
            return '';
        }

        const jwt = await this._context.secrets.get('calcpad.s3.jwt') || '';

        const outputChannel = getOutputChannel();
        outputChannel.appendLine(`Getting stored S3 JWT: ${jwt ? `${jwt.substring(0, 20)}...` : 'EMPTY'}`);
        outputChannel.appendLine(`S3 JWT length: ${jwt ? jwt.length : 0}`);

        return jwt;
    }

    public async getApiSettings(): Promise<unknown> {
        const storedS3JWT = await this.getStoredS3JWT();

        const apiSettings = buildApiSettings(this._settings, storedS3JWT);

        const outputChannel = getOutputChannel();
        outputChannel.appendLine('API settings being sent:');
        outputChannel.appendLine(`  Local Server URL: ${this._localServerUrl ?? '(none)'}`);
        outputChannel.appendLine(`  Remote Server URL: ${this._settings.server.url}`);
        outputChannel.appendLine(`  Effective Server URL: ${this.getServerUrl()}`);
        outputChannel.appendLine(`  S3 JWT: ${storedS3JWT ? `${storedS3JWT.substring(0, 20)}...` : 'EMPTY'}`);
        outputChannel.appendLine(`  S3 JWT Length: ${storedS3JWT ? storedS3JWT.length : 0}`);

        return apiSettings;
    }
}
