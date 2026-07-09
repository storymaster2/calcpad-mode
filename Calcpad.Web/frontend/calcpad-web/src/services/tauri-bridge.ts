import * as monaco from 'monaco-editor';
import { invoke } from '@tauri-apps/api/core';
import { appDataDir } from '@tauri-apps/api/path';
import {
    readTextFile,
    writeTextFile,
    readFile as fsReadFile,
    writeFile as fsWriteFile,
    readDir,
    mkdir,
    exists,
} from '@tauri-apps/plugin-fs';
import { open as dialogOpen, save as dialogSave, message as dialogMessage } from '@tauri-apps/plugin-dialog';
import { openPath } from '@tauri-apps/plugin-opener';
import { Store } from '@tauri-apps/plugin-store';
import { platform } from '@tauri-apps/plugin-os';
import { BaseMessageBridge, type ExportRequest } from 'calcpad-frontend/services/message-bridge/base';
import {
    getDefaultSettings,
    getDefaultExtras,
    getDefaultSettingsBlob,
    deserializeSettingsBlob,
    serializeSettingsBlob,
    getExtraBool,
    getExtraNumber,
    getExtraObject,
} from 'calcpad-frontend/types/settings';
import type { CalcpadSettings, CalcpadExtras } from 'calcpad-frontend/types/settings';
import { readImageFromClipboard } from './image-insert';
import {
    IMAGE_EXTENSIONS,
    bytesToBase64,
    isImageExtension,
    mimeFromExtension,
} from 'calcpad-frontend';
import { setAppTheme, coerceAppTheme } from '../editor/app-theme';

const SETTINGS_DIR_NAME = 'settings';
const DEFAULT_PRESET_NAME = 'default';
const ACTIVE_SETTINGS_FILE = 'active-settings.json';
const STORE_FILE = 'storage.json';
const ACTIVE_PRESET_KEY = 'calcpad-active-preset';
const RECENT_FILES_KEY = 'calcpad-recent-files';
const OPENED_FOLDER_KEY = 'calcpad-opened-folder';
const MAX_RECENT_FILES = 10;

function pathDirname(p: string): string {
    const idx = Math.max(p.lastIndexOf('/'), p.lastIndexOf('\\'));
    return idx > 0 ? p.slice(0, idx) : '';
}

function pathIsAbsolute(p: string): boolean {
    return p.startsWith('/') || /^[a-zA-Z]:[\\/]/.test(p);
}

function pathResolve(dir: string, file: string): string {
    if (pathIsAbsolute(file)) return file;
    if (!dir) return file;
    const sep = dir.includes('\\') ? '\\' : '/';
    const raw = `${dir}${sep}${file}`.replace(/\\/g, '/');
    const parts = raw.split('/');
    const result: string[] = [];
    for (const part of parts) {
        if (part === '..') result.pop();
        else if (part !== '.') result.push(part);
    }
    const joined = result.join('/');
    return sep === '\\' ? joined.replace(/\//g, '\\') : joined;
}

/**
 * Replace `<img src="local/path">` references in `html` with base64 data URIs
 * so PuppeteerSharp's headless Chromium — which has no local-filesystem
 * access — can render user-supplied images inside exported PDFs.
 */
async function inlineLocalImages(html: string, documentDir: string): Promise<string> {
    const cache: Record<string, string> = {};
    const imgRegex = /<img\s[^>]*?src\s*=\s*["']([^"']+)["'][^>]*>/gi;
    const seen = new Set<string>();
    let m: RegExpExecArray | null;
    while ((m = imgRegex.exec(html)) !== null) {
        const src = m[1];
        if (seen.has(src)) continue;
        seen.add(src);
        if (src.startsWith('data:') || /^https?:\/\//i.test(src)) continue;

        const ext = (src.split('.').pop() ?? '').toLowerCase();
        if (!isImageExtension(ext)) continue;
        const mime = mimeFromExtension(ext);

        const absolute = pathIsAbsolute(src)
            ? src
            : (documentDir ? `${documentDir}/${src}` : src);

        try {
            const bytes = await fsReadFile(absolute);
            cache[src] = `data:${mime};base64,${bytesToBase64(bytes)}`;
        } catch {
            // missing file or permission error → leave src untouched
        }
    }

    if (Object.keys(cache).length === 0) return html;
    return html.replace(
        /<img\s([^>]*?)src\s*=\s*["']([^"']+)["']([^>]*?)>/gi,
        (full, before, src, after) =>
            cache[src] ? `<img ${before}src="${cache[src]}"${after}>` : full,
    );
}

/**
 * Message bridge for the Tauri desktop platform. Settings JSON files live
 * under `$APPDATA/settings/`; recent files and the opened folder live in a
 * plugin-store JSON. Native dialogs, filesystem, and clipboard flow through
 * the Tauri JS API plugins.
 */
export class TauriMessageBridge extends BaseMessageBridge {
    private _extraSettings: CalcpadExtras = getDefaultExtras();
    readonly ready: Promise<void>;
    private _lastDialogDir: string | null = null;
    private _resolvedLibraryPath: string | null | undefined = undefined;
    private _activePresetName: string = DEFAULT_PRESET_NAME;
    private _appDataDir: string = '';
    private _settingsDir: string = '';
    private _serverDir: string = '';
    private _platform: string = 'linux';
    private _store: Store | null = null;

    constructor(serverUrl: string) {
        super(serverUrl);
        this.ready = this.initialize();
    }

    private async initialize(): Promise<void> {
        this._platform = await platform();
        this._appDataDir = (await appDataDir()).replace(/[\\/]+$/, '');
        this._settingsDir = `${this._appDataDir}/${SETTINGS_DIR_NAME}`;
        try {
            this._serverDir = (await invoke<string>('server_dir')).replace(/[\\/]+$/, '');
        } catch {
            this._serverDir = '';
        }
        this._store = await Store.load(STORE_FILE);
        await this.loadSettingsFromStorage();
    }

    getExtraSetting(key: string): string | undefined {
        return this._extraSettings[key];
    }

    setExtraSetting(key: string, value: string): void {
        this.persistSetting(key, value);
        if (key === 'libraryPath') this._resolvedLibraryPath = undefined;
    }

    private persistSetting(key: string, value: string): void {
        this._extraSettings[key] = value;
        void this.saveActiveSettings();
    }

    // ---- BaseMessageBridge hooks ----

    protected async persistSettings(_settings: CalcpadSettings): Promise<void> {
        await this.saveActiveSettings();
    }

    protected async resetSettingsBackend(): Promise<void> {
        await this.readPresetInto(DEFAULT_PRESET_NAME);
        await this.setActivePresetName(DEFAULT_PRESET_NAME);
        await this.saveActiveSettings();
        this._resolvedLibraryPath = undefined;
    }

    protected async afterResetSettings(): Promise<void> {
        await this.handleGetSettings();
    }

    protected coerceColorTheme(raw: string | undefined | null): string {
        return coerceAppTheme(raw);
    }

    protected applyColorTheme(theme: string): void {
        setAppTheme(coerceAppTheme(theme));
    }

    protected async pickImage(): Promise<string | null> {
        const fromClipboard = await readImageFromClipboard();
        if (fromClipboard) return fromClipboard;

        const selected = await dialogOpen({
            title: 'Insert Image',
            multiple: false,
            defaultPath: this.getDialogDefaultPath(),
            filters: [
                { name: 'Images', extensions: [...IMAGE_EXTENSIONS] },
                { name: 'All Files', extensions: ['*'] },
            ],
        });
        if (!selected || Array.isArray(selected)) return null;
        this.rememberDialogDir(selected);
        const buffer = await fsReadFile(selected);
        return `data:${mimeFromExtension(selected)};base64,${bytesToBase64(buffer)}`;
    }

    protected async saveExportedFile(req: ExportRequest): Promise<void> {
        const filePath = await dialogSave({
            title: req.dialogTitle,
            defaultPath: this.getDialogDefaultPath(),
            filters: [{ name: `${req.dialogTitle} Files`, extensions: req.extensions }],
        });
        if (!filePath) return;
        this.rememberDialogDir(filePath);
        if (typeof req.data === 'string') {
            await writeTextFile(filePath, req.data);
        } else {
            const bytes = req.data instanceof Uint8Array
                ? req.data
                : new Uint8Array(req.data as ArrayBuffer);
            await fsWriteFile(filePath, bytes);
        }
    }

    protected async buildFileContext(_content: string): Promise<{ sourceFilePath?: string }> {
        const sourceFilePath = this.activeTabFilePath() || undefined;
        return { sourceFilePath };
    }

    protected getVariablesOrigin(): string {
        return 'desktop-editor';
    }

    protected async buildSettingsResponseExtras(): Promise<Record<string, unknown>> {
        return {
            libraryPath: this._extraSettings.libraryPath || '',
            activeConfig: this._activePresetName,
            availableConfigs: await this.listPresets(),
        };
    }

    protected async runPdfPreflight(): Promise<boolean> {
        if (await this.browserMissing()) {
            await this.warnBrowserMissing();
            return false;
        }
        return true;
    }

    protected async generatePdfBytes(
        content: string,
        apiSettings: unknown,
        sourceFilePath: string | undefined,
    ): Promise<ArrayBuffer | null> {
        const baseUrl = this.apiClient.getBaseUrl();
        const htmlResp = await fetch(`${baseUrl}/api/calcpad/convert`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ content, settings: apiSettings, forPrint: true, sourceFilePath }),
            signal: AbortSignal.timeout(30000),
        });
        if (!htmlResp.ok) throw new Error(`HTML convert returned ${htmlResp.status}`);
        let html = await htmlResp.text();

        html = await inlineLocalImages(html, this.activeTabDirectory());

        const pdfResp = await fetch(`${baseUrl}/api/calcpad/pdf`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ html, options: this.getPdfOptions() }),
            signal: AbortSignal.timeout(60000),
        });
        if (!pdfResp.ok) throw new Error(`PDF endpoint returned ${pdfResp.status}`);
        return await pdfResp.arrayBuffer();
    }

    protected async onPdfError(err: unknown): Promise<void> {
        const msg = err instanceof Error ? err.message : String(err);
        this.postToVue({ type: 'pdfError', message: `PDF export failed: ${msg}` });
        this.handleGetServerLog();
        if (await this.browserMissing()) {
            await this.warnBrowserMissing();
        }
    }

    protected onOpenLogsFolder(): void {
        void this.handleOpenLogsFolder();
    }

    protected handlePlatformMessage(message: any): boolean {
        switch (message.type) {
            case 'saveNamedConfig':
                this.handleSavePreset(message.name);
                return true;
            case 'switchConfig':
                this.handleLoadPreset(message.name);
                return true;
            case 'openSettingsFolder':
                this.handleOpenSettingsFolder();
                return true;
            case 'updateLibraryPath':
                this.setExtraSetting('libraryPath', message.path ?? '');
                return true;
            case 'getPrettifySettings':
                this.handleGetPrettifySettings();
                return true;
            case 'updatePrettifyIndentStyle':
                this.persistSetting('prettifyIndentStyle', message.value);
                return true;
            case 'updatePrettifyIndentSize':
                this.persistSetting('prettifyIndentSize', String(message.value));
                return true;
            case 'updatePrettifyTrim':
                this.persistSetting('prettifyTrimTrailingWhitespace', String(message.value));
                return true;
            case 'prettifyDocument':
                this.handlePrettifyDocument();
                return true;
            case 'getServerLog':
                this.handleGetServerLog();
                return true;
            case 'openFolder':
                this.handleOpenFolder();
                return true;
            case 'readDirectory':
                this.handleReadDirectory(message.path);
                return true;
            case 'getOpenedFolder':
                this.handleGetOpenedFolder();
                return true;
            case 'openFileByPath':
                this.handleOpenFileByPath(message.path);
                return true;
            case 'openContainingFolder':
                this.handleOpenContainingFolder(message.path);
                return true;
            case 'closeFolder':
                this.handleCloseFolder();
                return true;
        }
        return false;
    }

    // ---- Dialog defaults ----

    private getDialogDefaultPath(): string | undefined {
        return this._lastDialogDir ?? undefined;
    }

    private rememberDialogDir(pathOrFile: string | null | undefined, isDirectory = false): void {
        if (!pathOrFile) return;
        this._lastDialogDir = isDirectory ? pathOrFile : pathDirname(pathOrFile) || pathOrFile;
    }

    // ---- File operations (exposed for menu actions) ----

    async openFile(): Promise<{ path: string; content: string } | null> {
        const selected = await dialogOpen({
            title: 'Open File',
            multiple: false,
            defaultPath: this.getDialogDefaultPath(),
            filters: [
                { name: 'CalcPad Files', extensions: ['cpd'] },
                { name: 'All Files', extensions: ['*'] },
            ],
        });
        if (!selected || Array.isArray(selected)) return null;
        this.rememberDialogDir(selected);
        const content = await readTextFile(selected);
        return { path: selected, content };
    }

    async saveFile(filePath: string, content: string): Promise<void> {
        this.rememberDialogDir(filePath);
        await writeTextFile(filePath, content);
    }

    async readFile(filePath: string): Promise<string> {
        return readTextFile(filePath);
    }

    async getRecentFiles(): Promise<string[]> {
        try {
            const list = await this._store?.get<string[]>(RECENT_FILES_KEY);
            return Array.isArray(list) ? list : [];
        } catch {
            return [];
        }
    }

    async addRecentFile(path: string): Promise<void> {
        const list = await this.getRecentFiles();
        const filtered = list.filter(p => p !== path);
        filtered.unshift(path);
        const trimmed = filtered.slice(0, MAX_RECENT_FILES);
        if (this._store) {
            await this._store.set(RECENT_FILES_KEY, trimmed);
            await this._store.save();
        }
    }

    async clearRecentFiles(): Promise<void> {
        if (this._store) {
            await this._store.set(RECENT_FILES_KEY, []);
            await this._store.save();
        }
    }

    async saveFileAs(content: string): Promise<string | null> {
        const filePath = await dialogSave({
            title: 'Save File',
            defaultPath: this.getDialogDefaultPath(),
            filters: [
                { name: 'CalcPad Files', extensions: ['cpd'] },
                { name: 'All Files', extensions: ['*'] },
            ],
        });
        if (!filePath) return null;
        this.rememberDialogDir(filePath);
        await writeTextFile(filePath, content);
        return filePath;
    }

    // ---- Folder browser ----

    async getOpenedFolder(): Promise<string | null> {
        try {
            const raw = await this._store?.get<string>(OPENED_FOLDER_KEY);
            return (raw ?? '').trim() || null;
        } catch {
            return null;
        }
    }

    async listDirectory(dirPath: string): Promise<Array<{ name: string; path: string; isDirectory: boolean }>> {
        try {
            const entries = await readDir(dirPath);
            const sep = dirPath.includes('\\') ? '\\' : '/';
            const normalizedDir = dirPath.replace(/[\\/]+$/, '');
            return entries
                .filter(e => e.name !== '.' && e.name !== '..')
                .map(e => {
                    const node = {
                        name: e.name,
                        path: `${normalizedDir}${sep}${e.name}`,
                        isDirectory: !!e.isDirectory,
                    };
                    return node.isDirectory ? node : Object.freeze(node);
                })
                .sort((a, b) => {
                    if (a.isDirectory !== b.isDirectory) return a.isDirectory ? -1 : 1;
                    return a.name.localeCompare(b.name);
                });
        } catch {
            return [];
        }
    }

    private async handleOpenFolder(): Promise<void> {
        const folder = await dialogOpen({
            title: 'Open Folder',
            directory: true,
            multiple: false,
            defaultPath: this.getDialogDefaultPath(),
        });
        if (!folder || Array.isArray(folder)) return;
        this.rememberDialogDir(folder, true);
        if (this._store) {
            await this._store.set(OPENED_FOLDER_KEY, folder);
            await this._store.save();
        }
        const entries = await this.listDirectory(folder);
        this.postToVue({ type: 'folderOpened', path: folder, entries });
    }

    private async handleReadDirectory(dirPath: string): Promise<void> {
        if (!dirPath) return;
        const entries = await this.listDirectory(dirPath);
        this.postToVue({ type: 'folderContents', path: dirPath, entries });
    }

    private async handleGetOpenedFolder(): Promise<void> {
        const explicit = await this.getOpenedFolder();
        const folder = explicit ?? (await this.getLibraryPath());
        if (!folder) {
            this.postToVue({ type: 'folderOpened', path: null, entries: [] });
            return;
        }
        const entries = await this.listDirectory(folder);
        this.postToVue({ type: 'folderOpened', path: folder, entries });
    }

    private async handleOpenFileByPath(filePath: string): Promise<void> {
        if (!filePath || typeof filePath !== 'string') return;
        window.dispatchEvent(new CustomEvent('calcpad-open-file', { detail: { path: filePath } }));
    }

    private async handleCloseFolder(): Promise<void> {
        try {
            if (this._store) {
                await this._store.set(OPENED_FOLDER_KEY, '');
                await this._store.save();
            }
        } catch {
            /* nothing persisted yet */
        }
    }

    private async handleOpenContainingFolder(itemPath: string): Promise<void> {
        if (!itemPath || typeof itemPath !== 'string') return;
        const parent = pathDirname(itemPath);
        const target = parent || itemPath;
        try {
            await openPath(target);
        } catch (err) {
            console.error(`Failed to open containing folder for ${itemPath}:`, err);
        }
    }

    // ---- Library path resolution ----

    getLibraryPathRaw(): string {
        return this._extraSettings.libraryPath || '';
    }

    async getLibraryPath(): Promise<string | null> {
        if (this._resolvedLibraryPath !== undefined) return this._resolvedLibraryPath;
        const raw = this.getLibraryPathRaw().trim();
        if (!raw) {
            this._resolvedLibraryPath = null;
            return null;
        }
        const expanded = await this.expandEnvVars(raw);
        this._resolvedLibraryPath = expanded ? expanded : null;
        return this._resolvedLibraryPath;
    }

    private async expandEnvVars(input: string): Promise<string> {
        const names = new Set<string>();
        for (const m of input.matchAll(/%([^%]+)%/g)) names.add(m[1]);
        for (const m of input.matchAll(/\$([A-Za-z_][A-Za-z0-9_]*)/g)) names.add(m[1]);
        const values: Record<string, string> = {};
        for (const name of names) {
            try {
                values[name] = (await invoke<string | null>('get_env', { name })) ?? '';
            } catch {
                values[name] = '';
            }
        }
        return input
            .replace(/%([^%]+)%/g, (_, n) => values[n] ?? '')
            .replace(/\$([A-Za-z_][A-Za-z0-9_]*)/g, (_, n) => values[n] ?? '');
    }

    public resolveIncludePath(rawFileName: string): string {
        return pathResolve(this.activeTabSourceDir(), rawFileName);
    }

    private activeTabFilePath(): string {
        const tabs = (window as any).calcpadTabs;
        return tabs?.activeTab?.filePath ?? '';
    }

    private activeTabDirectory(): string {
        return pathDirname(this.activeTabFilePath());
    }

    private activeTabSourceDir(): string {
        const dir = this.activeTabDirectory();
        if (dir) return dir;
        return this._serverDir || this._appDataDir;
    }

    // ---- Prettify ----

    private handleGetPrettifySettings(): void {
        const { indentStyle, indentSize, trim } = this.getPrettifyOptions();
        this.postToVue({
            type: 'prettifySettingsResponse',
            indentStyle,
            indentSize,
            trimTrailingWhitespace: trim,
        });
    }

    private getPrettifyOptions(): { indentStyle: 'space' | 'tab'; indentSize: number; trim: boolean } {
        const indentStyle = this._extraSettings.prettifyIndentStyle === 'space' ? 'space' : 'tab';
        const rawSize = getExtraNumber(this._extraSettings, 'prettifyIndentSize', 4);
        const indentSize = rawSize >= 1 ? rawSize : 4;
        const trim = getExtraBool(this._extraSettings, 'prettifyTrimTrailingWhitespace', true);
        return { indentStyle, indentSize, trim };
    }

    private async handlePrettifyDocument(): Promise<void> {
        const editor = monaco.editor.getEditors()[0];
        const model = editor?.getModel();
        if (!editor || !model) {
            console.warn('[Prettify] No active Monaco editor.');
            return;
        }

        const { indentStyle, indentSize, trim } = this.getPrettifyOptions();
        const indentUnit = indentStyle === 'space' ? ' '.repeat(indentSize) : '\t';

        try {
            const response = await this.apiClient.prettify(model.getValue(), indentUnit, trim);
            if (!response?.content) {
                console.warn('[Prettify] Server returned no content.');
                return;
            }
            editor.executeEdits('prettify', [{ range: model.getFullModelRange(), text: response.content }]);
        } catch (err) {
            console.error('[Prettify] Failed:', err);
        }
    }

    // ---- Server log + browser-missing warning ----

    private getPdfOptions(): unknown {
        return getExtraObject<Record<string, unknown>>(this._extraSettings, 'pdfSettings', {});
    }

    private async handleGetServerLog(): Promise<void> {
        const path = this.getServerLogPath();
        if (!path) {
            this.postToVue({
                type: 'serverLogResponse',
                path: '',
                content: '',
                error: 'Server directory not resolved — server log only available in the desktop build.',
            });
            return;
        }
        try {
            const raw = await readTextFile(path);
            const lines = raw.split('\n');
            const tail = lines.length > 200 ? lines.slice(-200) : lines;
            this.postToVue({
                type: 'serverLogResponse',
                path,
                content: tail.join('\n'),
            });
        } catch (err) {
            this.postToVue({
                type: 'serverLogResponse',
                path,
                content: '',
                error: err instanceof Error ? err.message : String(err),
            });
        }
    }

    private getServerLogPath(): string | null {
        const dir = this.getServerLogDir();
        return dir ? `${dir}/server-stderr.log` : null;
    }

    private getServerLogDir(): string | null {
        return this._serverDir ? `${this._serverDir}/logs` : null;
    }

    private async handleOpenLogsFolder(): Promise<void> {
        const dir = this.getServerLogDir();
        if (!dir) {
            console.warn('Open Logs Folder: server directory unresolved.');
            return;
        }
        try { await mkdir(dir, { recursive: true }); } catch { /* already exists */ }
        try {
            await openPath(dir);
            console.info(`Opened logs folder: ${dir}`);
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            console.error(`Failed to open logs folder (${dir}): ${msg}`);
        }
    }

    private async browserMissing(): Promise<boolean> {
        const path = this.getServerLogPath();
        if (!path) return false;
        try {
            const raw = await readTextFile(path);
            return /WARNING: no Chromium-family browser/i.test(raw)
                || /Could not find browser revision|Failed to launch the browser/i.test(raw);
        } catch {
            return false;
        }
    }

    private async warnBrowserMissing(): Promise<void> {
        const advice = await this.browserInstallAdvice();
        const message =
            'PDF export needs a Chromium-family browser, but none was found on PATH.\n\n'
            + advice
            + '\n\nAfter installing, restart CalcPad (Server → Restart App) and try again.';
        try {
            await dialogMessage(message, {
                title: 'Chromium browser required for PDF export',
                kind: 'warning',
                okLabel: 'OK',
            });
        } catch {
            this.postToVue({ type: 'pdfError', message });
        }
    }

    private async browserInstallAdvice(): Promise<string> {
        if (this._platform === 'windows') {
            return 'Install Microsoft Edge (preinstalled on recent Windows) or Google Chrome,\n'
                + 'then set BROWSER_PATH in extensions/server/appsettings.json if it is not on PATH.';
        }
        if (this._platform === 'macos') {
            return 'Install Google Chrome from https://www.google.com/chrome/\n'
                + 'or via Homebrew:\n'
                + '    brew install --cask google-chrome';
        }
        const distro = await this.detectLinuxDistro();
        switch (distro) {
            case 'arch':
                return 'Recommended on Arch / CachyOS / Manjaro:\n'
                    + '    yay -S ungoogled-chromium-bin\n'
                    + 'Or from the official repos:\n'
                    + '    sudo pacman -S chromium';
            case 'debian':
                return 'Recommended on Debian / Ubuntu / Mint:\n'
                    + '    sudo apt install chromium    # or chromium-browser\n'
                    + 'Alternatively install Google Chrome from https://www.google.com/chrome/';
            case 'fedora':
                return 'Recommended on Fedora / RHEL:\n'
                    + '    sudo dnf install chromium\n'
                    + 'Or install Google Chrome from https://www.google.com/chrome/';
            case 'opensuse':
                return 'Recommended on openSUSE:\n'
                    + '    sudo zypper install chromium';
            case 'alpine':
                return 'Recommended on Alpine:\n'
                    + '    sudo apk add chromium';
            default:
                return 'Install one of: chromium, ungoogled-chromium, google-chrome-stable,\n'
                    + 'or microsoft-edge-stable using your distribution\'s package manager.';
        }
    }

    private async detectLinuxDistro(): Promise<string> {
        try {
            const text = await readTextFile('/etc/os-release');
            const fields: Record<string, string> = {};
            for (const line of text.split('\n')) {
                const eq = line.indexOf('=');
                if (eq <= 0) continue;
                const k = line.slice(0, eq).trim();
                let v = line.slice(eq + 1).trim();
                if (v.startsWith('"') && v.endsWith('"')) v = v.slice(1, -1);
                fields[k] = v.toLowerCase();
            }
            const id = fields['ID'] || '';
            const idLike = fields['ID_LIKE'] || '';
            const all = `${id} ${idLike}`;
            if (/\barch\b|cachyos|manjaro|endeavouros|garuda/.test(all)) return 'arch';
            if (/\bdebian\b|\bubuntu\b|\bmint\b|\bpop\b|\belementary\b/.test(all)) return 'debian';
            if (/\bfedora\b|\brhel\b|\bcentos\b|\brocky\b|\balmalinux\b/.test(all)) return 'fedora';
            if (/\bopensuse\b|\bsuse\b/.test(all)) return 'opensuse';
            if (/\balpine\b/.test(all)) return 'alpine';
        } catch {
            /* /etc/os-release missing */
        }
        return 'unknown';
    }

    // ---- Settings persistence via filesystem JSON ----
    //
    // Layout under $APPDATA/settings/:
    //   active-settings.json — live state, written on every edit
    //   default.json         — pristine defaults, refreshed on every boot
    //   <name>.json          — user-created presets, never written by the editor

    private getSettingsDir(): string { return this._settingsDir; }
    private getPresetPath(name: string): string { return `${this._settingsDir}/${name}.json`; }
    private getActiveSettingsPath(): string { return `${this._settingsDir}/${ACTIVE_SETTINGS_FILE}`; }

    private async ensureSettingsFolder(): Promise<void> {
        try { await mkdir(this._settingsDir, { recursive: true }); } catch { /* exists */ }
        const defaultPath = this.getPresetPath(DEFAULT_PRESET_NAME);
        const blob = getDefaultSettingsBlob();
        try {
            await writeTextFile(defaultPath, JSON.stringify(blob, null, 4));
        } catch {
            /* non-writable — fall back to in-memory defaults */
        }
    }

    private async loadActivePresetName(): Promise<string> {
        try {
            const raw = await this._store?.get<string>(ACTIVE_PRESET_KEY);
            const name = (raw ?? '').trim();
            return name || DEFAULT_PRESET_NAME;
        } catch {
            return DEFAULT_PRESET_NAME;
        }
    }

    private async setActivePresetName(name: string): Promise<void> {
        this._activePresetName = name;
        try {
            if (this._store) {
                await this._store.set(ACTIVE_PRESET_KEY, name);
                await this._store.save();
            }
        } catch { /* best-effort */ }
    }

    private async listPresets(): Promise<string[]> {
        try {
            const entries = await readDir(this._settingsDir);
            const activeBasename = ACTIVE_SETTINGS_FILE.replace(/\.json$/i, '');
            const names = entries
                .filter(e => e.isFile && e.name.toLowerCase().endsWith('.json'))
                .map(e => e.name.slice(0, -'.json'.length))
                .filter(n => n !== activeBasename);
            const rest = names.filter(n => n !== DEFAULT_PRESET_NAME).sort();
            return [DEFAULT_PRESET_NAME, ...rest];
        } catch {
            return [DEFAULT_PRESET_NAME];
        }
    }

    private async loadSettingsFromStorage(): Promise<void> {
        await this.ensureSettingsFolder();
        this._activePresetName = await this.loadActivePresetName();

        const loaded = await this.readActiveSettings();
        if (!loaded) {
            await this.readPresetInto(this._activePresetName);
            await this.saveActiveSettings();
        }
    }

    private async readActiveSettings(): Promise<boolean> {
        const path = this.getActiveSettingsPath();
        try {
            if (!(await exists(path))) return false;
            const raw = await readTextFile(path);
            const parsed = JSON.parse(raw);
            const { settings, extras } = deserializeSettingsBlob(parsed);
            this.settings = settings;
            this._extraSettings = extras;
            return true;
        } catch {
            return false;
        }
    }

    private async readPresetInto(name: string): Promise<void> {
        if (name === DEFAULT_PRESET_NAME) {
            this.settings = getDefaultSettings();
            this._extraSettings = getDefaultExtras();
            return;
        }
        const path = this.getPresetPath(name);
        try {
            const raw = await readTextFile(path);
            const parsed = JSON.parse(raw);
            const { settings, extras } = deserializeSettingsBlob(parsed);
            this.settings = settings;
            this._extraSettings = extras;
        } catch {
            this.settings = getDefaultSettings();
            this._extraSettings = getDefaultExtras();
        }
    }

    private async saveActiveSettings(): Promise<void> {
        const path = this.getActiveSettingsPath();
        const blob = serializeSettingsBlob(this.settings, this._extraSettings);
        await writeTextFile(path, JSON.stringify(blob, null, 4));
    }

    private async handleSavePreset(rawName: string): Promise<void> {
        const name = (rawName ?? '').trim();
        if (!name) return;
        if (name.toLowerCase() === DEFAULT_PRESET_NAME) {
            this.postToVue({
                type: 'saveNamedConfigError',
                message: 'The "default" preset is protected and cannot be overridden.',
            });
            return;
        }
        if (/[\\/:*?"<>|\x00-\x1f]/.test(name)) {
            this.postToVue({
                type: 'saveNamedConfigError',
                message: 'Config name contains invalid characters.',
            });
            return;
        }
        const presetPath = this.getPresetPath(name);
        const blob = serializeSettingsBlob(this.settings, this._extraSettings);
        try {
            await writeTextFile(presetPath, JSON.stringify(blob, null, 4));
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            this.postToVue({
                type: 'saveNamedConfigError',
                message: `Failed to write preset: ${msg}`,
            });
            return;
        }
        await this.setActivePresetName(name);
        this.handleGetSettings();
    }

    private async handleLoadPreset(name: string): Promise<void> {
        if (!name) return;
        await this.readPresetInto(name);
        await this.setActivePresetName(name);
        await this.saveActiveSettings();
        this._resolvedLibraryPath = undefined;
        if (this.settings.server?.url) {
            this.apiClient.setBaseUrl(this.settings.server.url);
        }
        this.handleGetSettings();
    }

    private async handleOpenSettingsFolder(): Promise<void> {
        try { await mkdir(this._settingsDir, { recursive: true }); } catch { /* exists */ }
        try { await openPath(this._settingsDir); }
        catch (err) {
            console.error('Failed to open settings folder:', err);
        }
    }
}
