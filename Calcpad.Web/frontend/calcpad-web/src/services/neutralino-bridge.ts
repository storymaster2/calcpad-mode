import * as monaco from 'monaco-editor';
import { filesystem, os, storage } from '@neutralinojs/lib';
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
// The single file that reflects live user state. Written on every edit,
// read at boot. Preset files (default.json, <name>.json) are read-only
// source-of-truth snapshots — the settings editor never writes to them.
const ACTIVE_SETTINGS_FILE = 'active-settings.json';
const ACTIVE_PRESET_KEY = 'calcpad-active-preset';
const RECENT_FILES_KEY = 'calcpad-recent-files';
const OPENED_FOLDER_KEY = 'calcpad-opened-folder';
const MAX_RECENT_FILES = 10;

// Browser-compatible path helpers (no Node.js 'path' module available in Neutralino WebView)
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
 * by reading each image off disk via Neutralino's filesystem API. The headless
 * Chromium that renders the PDF has no access to the user's filesystem, so
 * without inlining, plot images render as broken icons.
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

        const absolute = src.startsWith('/') || /^[a-zA-Z]:[\\/]/.test(src)
            ? src
            : (documentDir ? `${documentDir}/${src}` : src);

        try {
            const bytes = await filesystem.readBinaryFile(absolute);
            cache[src] = `data:${mime};base64,${bytesToBase64(new Uint8Array(bytes))}`;
        } catch {
            // missing file or permission error → leave the src untouched
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
 * Message bridge for the Neutralino.js desktop platform. Persists settings to
 * JSON files under `NL_PATH/settings/` and uses native OS dialogs for file
 * open/save/pick.
 */
export class NeutralinoMessageBridge extends BaseMessageBridge {
    private _extraSettings: CalcpadExtras = getDefaultExtras();
    /**
     * Resolves once persisted settings have been read from disk. Anything that
     * needs values from `_extraSettings` (e.g. the boot-time theme applier)
     * must await this — the constructor can't be async.
     */
    readonly ready: Promise<void>;
    /**
     * Directory used to seed native dialogs' `defaultPath`. Updated after every
     * Open/Save/Open-Folder interaction so successive dialogs land in the last
     * place the user was working. Session-scoped only (not persisted).
     */
    private _lastDialogDir: string | null = null;
    /**
     * Cached absolute library folder after env-var expansion. Invalidated
     * whenever the raw libraryPath preference changes. `null` means the user
     * hasn't configured one; `undefined` means "not yet resolved".
     */
    private _resolvedLibraryPath: string | null | undefined = undefined;
    private _activePresetName: string = DEFAULT_PRESET_NAME;

    constructor(serverUrl: string) {
        super(serverUrl);
        this.ready = this.loadSettingsFromStorage();
    }

    // ---- Extras storage (in-memory dict, async-persisted to JSON) ----

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
        // Load the pristine bundled defaults into active-settings.json.
        // Preset files (default.json, <name>.json) are untouched.
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

        const entries = await os.showOpenDialog('Insert Image', {
            defaultPath: this.getDialogDefaultPath(),
            filters: [
                { name: 'Images', extensions: [...IMAGE_EXTENSIONS] },
                { name: 'All Files', extensions: ['*'] },
            ],
        });
        if (!entries || entries.length === 0) return null;

        const filePath = entries[0];
        this.rememberDialogDir(filePath);
        const buffer = await filesystem.readBinaryFile(filePath);
        return `data:${mimeFromExtension(filePath)};base64,${bytesToBase64(buffer)}`;
    }

    protected async saveExportedFile(req: ExportRequest): Promise<void> {
        const filePath = await os.showSaveDialog(req.dialogTitle, {
            defaultPath: this.getDialogDefaultPath(),
            filters: [{ name: `${req.dialogTitle} Files`, extensions: req.extensions }],
        });
        if (!filePath) return;
        this.rememberDialogDir(filePath);
        if (typeof req.data === 'string') {
            await filesystem.writeFile(filePath, req.data);
        } else {
            await filesystem.writeBinaryFile(filePath, req.data);
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
        // Step 1: source → HTML. The /convert endpoint always returns HTML;
        // supplying outputFormat='pdf' here is ignored, which is why the prior
        // single-call approach silently failed.
        const baseUrl = this.apiClient.getBaseUrl();
        const htmlResp = await fetch(`${baseUrl}/api/calcpad/convert`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ content, settings: apiSettings, forPrint: true, sourceFilePath }),
            signal: AbortSignal.timeout(30000),
        });
        if (!htmlResp.ok) throw new Error(`HTML convert returned ${htmlResp.status}`);
        let html = await htmlResp.text();

        // Step 2: inline local <img src="..."> as base64 data URIs.
        html = await inlineLocalImages(html, this.activeTabDirectory());

        // Step 3: render the inlined HTML via /api/calcpad/pdf.
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

    // ---- Native dialog helpers ----

    /** Default path passed to native dialogs. Falls back to undefined (OS-default). */
    private getDialogDefaultPath(): string | undefined {
        return this._lastDialogDir ?? undefined;
    }

    /** Update the remembered dialog directory. Accepts either a file path
     *  (parent dir is extracted) or a directory path directly. */
    private rememberDialogDir(pathOrFile: string | null | undefined, isDirectory = false): void {
        if (!pathOrFile) return;
        this._lastDialogDir = isDirectory ? pathOrFile : pathDirname(pathOrFile) || pathOrFile;
    }

    // ---- File operations (exposed for menu actions) ----

    async openFile(): Promise<{ path: string; content: string } | null> {
        const entries = await os.showOpenDialog('Open File', {
            defaultPath: this.getDialogDefaultPath(),
            filters: [
                { name: 'CalcPad Files', extensions: ['cpd'] },
                { name: 'All Files', extensions: ['*'] },
            ],
        });
        if (!entries || entries.length === 0) return null;

        const filePath = entries[0];
        this.rememberDialogDir(filePath);
        const content = await filesystem.readFile(filePath);
        return { path: filePath, content };
    }

    async saveFile(filePath: string, content: string): Promise<void> {
        this.rememberDialogDir(filePath);
        await filesystem.writeFile(filePath, content);
    }

    async readFile(filePath: string): Promise<string> {
        return filesystem.readFile(filePath);
    }

    async getRecentFiles(): Promise<string[]> {
        try {
            const raw = await storage.getData(RECENT_FILES_KEY);
            const list = JSON.parse(raw);
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
        await storage.setData(RECENT_FILES_KEY, JSON.stringify(trimmed));
    }

    async clearRecentFiles(): Promise<void> {
        await storage.setData(RECENT_FILES_KEY, JSON.stringify([]));
    }

    async saveFileAs(content: string): Promise<string | null> {
        const filePath = await os.showSaveDialog('Save File', {
            defaultPath: this.getDialogDefaultPath(),
            filters: [
                { name: 'CalcPad Files', extensions: ['cpd'] },
                { name: 'All Files', extensions: ['*'] },
            ],
        });
        if (!filePath) return null;
        this.rememberDialogDir(filePath);
        await filesystem.writeFile(filePath, content);
        return filePath;
    }

    // ---- Folder browser ----

    /** Return the currently opened workspace folder, or null. Persisted across sessions. */
    async getOpenedFolder(): Promise<string | null> {
        try {
            const raw = await storage.getData(OPENED_FOLDER_KEY);
            return raw || null;
        } catch {
            return null;
        }
    }

    /**
     * List directory entries as FileNodes; empty on failure.
     *
     * File-leaf nodes are frozen so Vue's reactivity system skips wrapping
     * them in Proxies — a big perf win when a folder contains hundreds of
     * files. Directory nodes stay mutable because the tree builder assigns
     * `.children` and `.loaded` to them later.
     */
    async listDirectory(dirPath: string): Promise<Array<{ name: string; path: string; isDirectory: boolean }>> {
        try {
            const entries = await filesystem.readDirectory(dirPath);
            const sep = dirPath.includes('\\') ? '\\' : '/';
            const normalizedDir = dirPath.replace(/[\\/]+$/, '');
            return entries
                .filter(e => e.entry !== '.' && e.entry !== '..')
                .map(e => {
                    const isDirectory = e.type === 'DIRECTORY';
                    const node = {
                        name: e.entry,
                        path: `${normalizedDir}${sep}${e.entry}`,
                        isDirectory,
                    };
                    return isDirectory ? node : Object.freeze(node);
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
        const folder = await os.showFolderDialog('Open Folder', {
            defaultPath: this.getDialogDefaultPath(),
        });
        if (!folder) return;
        this.rememberDialogDir(folder, true);
        await storage.setData(OPENED_FOLDER_KEY, folder);
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
        // When nothing was explicitly opened (or the user closed the folder),
        // fall back to the configured library so the Files panel isn't empty
        // on first launch.
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
        // Delegate to main.ts's loadFile via a bridged event.
        window.dispatchEvent(new CustomEvent('calcpad-open-file', { detail: { path: filePath } }));
    }

    private async handleCloseFolder(): Promise<void> {
        try {
            await storage.setData(OPENED_FOLDER_KEY, '');
        } catch {
            // Storage may not be initialized on a first run — nothing to clear.
        }
    }

    private async handleOpenContainingFolder(itemPath: string): Promise<void> {
        if (!itemPath || typeof itemPath !== 'string') return;
        const parent = pathDirname(itemPath);
        const target = parent || itemPath;
        try {
            await os.open(target);
        } catch (err) {
            console.error(`Failed to open containing folder for ${itemPath}:`, err);
        }
    }

    // ---- Library path resolution ----

    /** Raw library path as typed by the user (may contain env-var refs). */
    getLibraryPathRaw(): string {
        return this._extraSettings.libraryPath || '';
    }

    /**
     * Resolve the configured library path to an absolute directory, expanding
     * `%VAR%` (Windows) and `$VAR` (Unix) references via `os.getEnv`. Returns
     * null when no library is configured or expansion leaves the path empty.
     * The resolved value is cached until `libraryPath` changes.
     */
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
                values[name] = (await os.getEnv(name)) || '';
            } catch {
                values[name] = '';
            }
        }
        return input
            .replace(/%([^%]+)%/g, (_, n) => values[n] ?? '')
            .replace(/\$([A-Za-z_][A-Za-z0-9_]*)/g, (_, n) => values[n] ?? '');
    }

    /**
     * Resolve an `#include` filename (raw text from the directive) to an
     * absolute path, using the active tab's directory as the base.
     */
    public resolveIncludePath(rawFileName: string): string {
        return pathResolve(this.activeTabSourceDir(), rawFileName);
    }

    /** Returns the full path of the active tab's file, or '' if untitled. */
    private activeTabFilePath(): string {
        const tabs = (window as any).calcpadTabs;
        return tabs?.activeTab?.filePath ?? '';
    }

    /** Returns the directory of the active tab's file, or '' if untitled. */
    private activeTabDirectory(): string {
        return pathDirname(this.activeTabFilePath());
    }

    /**
     * Base for resolving #include paths. Falls back to NL_PATH for untitled
     * tabs so includes placed alongside the app still resolve.
     */
    private activeTabSourceDir(): string {
        const dir = this.activeTabDirectory();
        if (dir) return dir;
        return (window as Window & { NL_PATH?: string }).NL_PATH ?? '';
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

    /** Resolves stored PDF settings for the /pdf endpoint. */
    private getPdfOptions(): unknown {
        return getExtraObject<Record<string, unknown>>(this._extraSettings, 'pdfSettings', {});
    }

    /**
     * Read the captured server stderr log written by start-server.sh and
     * push it to the UI's Output panel. Mirrors how the VS Code extension
     * pipes stderr from the spawned dotnet process into its Output channel.
     */
    private async handleGetServerLog(): Promise<void> {
        const path = this.getServerLogPath();
        if (!path) {
            this.postToVue({
                type: 'serverLogResponse',
                path: '',
                content: '',
                error: 'NL_PATH not available — server log only exists in the desktop build.',
            });
            return;
        }
        try {
            const raw = await filesystem.readFile(path);
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
        const raw = (window as Window & { NL_PATH?: string }).NL_PATH;
        if (!raw) return null;
        const NL_PATH = raw.replace(/\\/g, '/');
        return `${NL_PATH}/extensions/server/logs`;
    }

    /**
     * Open the server-extension logs directory in the OS file explorer.
     * The folder may not exist on a fresh install — createDirectory is
     * best-effort so the explorer always has something to open.
     */
    private async handleOpenLogsFolder(): Promise<void> {
        const dir = this.getServerLogDir();
        if (!dir) {
            console.warn('Open Logs Folder: NL_PATH unavailable — desktop-only feature.');
            return;
        }
        try { await filesystem.createDirectory(dir); } catch { /* already exists */ }
        try {
            await os.open(dir);
            console.info(`Opened logs folder: ${dir}`);
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            console.error(`Failed to open logs folder (${dir}): ${msg}`);
        }
    }

    /** True iff start-server.sh logged a "no browser found" warning. */
    private async browserMissing(): Promise<boolean> {
        const path = this.getServerLogPath();
        if (!path) return false;
        try {
            const raw = await filesystem.readFile(path);
            return /WARNING: no Chromium-family browser/i.test(raw)
                || /Could not find browser revision|Failed to launch the browser/i.test(raw);
        } catch {
            return false;
        }
    }

    /**
     * Show a native message box explaining how to install a Chromium browser
     * for PDF export, tailored to the current OS / distro.
     */
    private async warnBrowserMissing(): Promise<void> {
        const advice = await this.browserInstallAdvice();
        const message =
            'PDF export needs a Chromium-family browser, but none was found on PATH.\n\n'
            + advice
            + '\n\nAfter installing, restart CalcPad (Server → Restart App) and try again.';
        try {
            await os.showMessageBox(
                'Chromium browser required for PDF export',
                message,
                'OK' as any,
                'WARNING' as any,
            );
        } catch {
            // showMessageBox can throw if the runtime tears down — fall back
            // to surfacing the message in the Output panel.
            this.postToVue({ type: 'pdfError', message });
        }
    }

    /**
     * Per-platform install instructions. Linux additionally branches on
     * /etc/os-release ID / ID_LIKE so Arch users get pacman/AUR commands,
     * Debian/Ubuntu users get apt, Fedora users get dnf, etc.
     */
    private async browserInstallAdvice(): Promise<string> {
        const NL_OS = (window as Window & { NL_OS?: string }).NL_OS;
        if (NL_OS === 'Windows') {
            return 'Install Microsoft Edge (preinstalled on recent Windows) or Google Chrome,\n'
                + 'then set BROWSER_PATH in extensions/server/appsettings.json if it is not on PATH.';
        }
        if (NL_OS === 'Darwin') {
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
            const text = await filesystem.readFile('/etc/os-release');
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
            // /etc/os-release missing or unreadable — fall through to generic advice
        }
        return 'unknown';
    }

    // ---- Settings persistence via filesystem JSON ----
    //
    // Layout under NL_PATH/settings/:
    //   active-settings.json — live state, written on every edit
    //   default.json         — pristine defaults, refreshed on every boot
    //   <name>.json          — user-created presets, never written by the editor
    //
    // The "active preset" name is remembered in Neutralino storage purely
    // for the dropdown label — it does NOT gate writes. Any edit lands in
    // active-settings.json regardless of which preset was last loaded.

    private getSettingsDir(): string | null {
        const raw = (window as Window & { NL_PATH?: string }).NL_PATH;
        if (!raw) return null;
        const nlPath = raw.replace(/\\/g, '/').replace(/\/+$/, '');
        return `${nlPath}/${SETTINGS_DIR_NAME}`;
    }

    private getPresetPath(name: string): string | null {
        const dir = this.getSettingsDir();
        return dir ? `${dir}/${name}.json` : null;
    }

    private getActiveSettingsPath(): string | null {
        const dir = this.getSettingsDir();
        return dir ? `${dir}/${ACTIVE_SETTINGS_FILE}` : null;
    }

    /**
     * Ensure the settings folder exists and default.json is fresh from the
     * bundled defaults. Runs on boot so `default` always represents pristine
     * defaults. Never touches active-settings.json.
     */
    private async ensureSettingsFolder(): Promise<void> {
        const dir = this.getSettingsDir();
        if (!dir) return;
        try { await filesystem.createDirectory(dir); } catch { /* already exists */ }
        const defaultPath = this.getPresetPath(DEFAULT_PRESET_NAME);
        if (defaultPath) {
            const blob = getDefaultSettingsBlob();
            try {
                await filesystem.writeFile(defaultPath, JSON.stringify(blob, null, 4));
            } catch {
                // Non-writable filesystem — proceed; loads will fall back to
                // in-memory defaults.
            }
        }
    }

    private async loadActivePresetName(): Promise<string> {
        try {
            const raw = await storage.getData(ACTIVE_PRESET_KEY);
            const name = (raw ?? '').trim();
            return name || DEFAULT_PRESET_NAME;
        } catch {
            return DEFAULT_PRESET_NAME;
        }
    }

    private async setActivePresetName(name: string): Promise<void> {
        this._activePresetName = name;
        try { await storage.setData(ACTIVE_PRESET_KEY, name); } catch { /* best effort */ }
    }

    /**
     * List available presets (basenames without .json), sorted with
     * `default` first. `active-settings` is not a preset and is excluded.
     */
    private async listPresets(): Promise<string[]> {
        const dir = this.getSettingsDir();
        if (!dir) return [DEFAULT_PRESET_NAME];
        try {
            const entries = await filesystem.readDirectory(dir);
            const activeBasename = ACTIVE_SETTINGS_FILE.replace(/\.json$/i, '');
            const names = entries
                .filter(e => e.type === 'FILE' && e.entry.toLowerCase().endsWith('.json'))
                .map(e => e.entry.slice(0, -'.json'.length))
                .filter(n => n !== activeBasename);
            const rest = names.filter(n => n !== DEFAULT_PRESET_NAME).sort();
            return [DEFAULT_PRESET_NAME, ...rest];
        } catch {
            return [DEFAULT_PRESET_NAME];
        }
    }

    /**
     * Boot-time load:
     *   1. Refresh default.json from bundled defaults.
     *   2. Read active-settings.json into memory. If missing, seed it from
     *      the last-active preset (or default) and write it out.
     *   3. Restore active-preset label from Neutralino storage.
     */
    private async loadSettingsFromStorage(): Promise<void> {
        await this.ensureSettingsFolder();
        this._activePresetName = await this.loadActivePresetName();

        const loaded = await this.readActiveSettings();
        if (!loaded) {
            // First run (or the file was deleted): seed from the last-active
            // preset, then write it back so subsequent boots skip the seed.
            await this.readPresetInto(this._activePresetName);
            await this.saveActiveSettings();
        }
    }

    /**
     * Read active-settings.json into memory. Returns false when the file
     * doesn't exist / is unreadable so the caller can decide how to seed it.
     */
    private async readActiveSettings(): Promise<boolean> {
        const path = this.getActiveSettingsPath();
        if (!path) return false;
        try {
            const raw = await filesystem.readFile(path);
            const parsed = JSON.parse(raw);
            const { settings, extras } = deserializeSettingsBlob(parsed);
            this.settings = settings;
            this._extraSettings = extras;
            return true;
        } catch {
            return false;
        }
    }

    /**
     * Load a preset file (or bundled defaults for "default") into memory.
     * Does NOT write active-settings.json — callers that need the change
     * persisted must call saveActiveSettings afterward.
     */
    private async readPresetInto(name: string): Promise<void> {
        // "default" always reflects the bundled defaults; skip the disk read
        // so a corrupted default.json can't wedge us at boot.
        if (name === DEFAULT_PRESET_NAME) {
            this.settings = getDefaultSettings();
            this._extraSettings = getDefaultExtras();
            return;
        }
        const path = this.getPresetPath(name);
        if (!path) return;
        try {
            const raw = await filesystem.readFile(path);
            const parsed = JSON.parse(raw);
            const { settings, extras } = deserializeSettingsBlob(parsed);
            this.settings = settings;
            this._extraSettings = extras;
        } catch {
            this.settings = getDefaultSettings();
            this._extraSettings = getDefaultExtras();
        }
    }

    /** Write in-memory state to active-settings.json. */
    private async saveActiveSettings(): Promise<void> {
        const path = this.getActiveSettingsPath();
        if (!path) return;
        const blob = serializeSettingsBlob(this.settings, this._extraSettings);
        await filesystem.writeFile(path, JSON.stringify(blob, null, 4));
    }

    /**
     * Save current in-memory settings as a preset. Rejects the reserved
     * "default" name and filename-illegal characters. Also updates the
     * active-preset label to the saved name.
     */
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
        // Basic filename sanitization — reject path separators / control chars.
        if (/[\\/:*?"<>|\x00-\x1f]/.test(name)) {
            this.postToVue({
                type: 'saveNamedConfigError',
                message: 'Config name contains invalid characters.',
            });
            return;
        }
        const presetPath = this.getPresetPath(name);
        if (!presetPath) return;
        const blob = serializeSettingsBlob(this.settings, this._extraSettings);
        try {
            await filesystem.writeFile(presetPath, JSON.stringify(blob, null, 4));
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

    /**
     * Load a preset into live active-settings state. Writes the preset's
     * contents to active-settings.json so the preset file itself stays
     * untouched.
     */
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
        const dir = this.getSettingsDir();
        if (!dir) return;
        try { await os.open(dir); }
        catch (err) {
            console.error('Failed to open settings folder:', err);
        }
    }
}
