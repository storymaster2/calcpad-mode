import * as monaco from 'monaco-editor';
import { filesystem, os, storage } from '@neutralinojs/lib';
import { CalcpadApiClient } from 'calcpad-frontend/api/client';
import { CalcpadSnippetService } from 'calcpad-frontend/services/snippets';
import { CalcpadDefinitionsService } from 'calcpad-frontend/services/definitions';
import { parseHeadings } from 'calcpad-frontend/services/headings';
import { getDefaultSettings, buildApiSettings } from 'calcpad-frontend/types/settings';
import type { CalcpadSettings } from 'calcpad-frontend/types/settings';
import {
    readImageFromClipboard,
    bytesToBase64,
    mimeFromExtension,
    buildImageCommentLine,
} from './image-insert';
import { getActiveEditorContent } from './active-editor';
import { setAppTheme, coerceAppTheme } from '../editor/app-theme';

/** Themes the desktop app ships with. Fed to the Color Theme picker. */
const BUILTIN_THEMES = [
    { id: 'calcpad-dark',  label: 'Dark',  kind: 'dark'  as const },
    { id: 'calcpad-light', label: 'Light', kind: 'light' as const },
];

const SETTINGS_KEY = 'calcpad-settings';
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

const IMAGE_MIME_MAP: Record<string, string> = {
    png: 'image/png',
    jpg: 'image/jpeg',
    jpeg: 'image/jpeg',
    gif: 'image/gif',
    webp: 'image/webp',
    svg: 'image/svg+xml',
};

/**
 * Replace `<img src="local/path">` references in `html` with base64 data URIs
 * by reading each image off disk via Neutralino's filesystem API. Mirrors the
 * VS Code extension's buildImageCache + applyImageCache so plot images render
 * inside the headless Chromium that produces the PDF.
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
        const mime = IMAGE_MIME_MAP[ext];
        if (!mime) continue;

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
 * Message bridge for the Neutralino.js desktop platform.
 * Same role as the web MessageBridge, but uses Neutralino APIs for native
 * file dialogs, filesystem access, and persistent storage.
 */
export class NeutralinoMessageBridge {
    private apiClient: CalcpadApiClient;
    private snippetService: CalcpadSnippetService;
    private definitionsService: CalcpadDefinitionsService;
    private settings: CalcpadSettings;
    private _onInsertText: ((text: string) => void) | null = null;
    private _extraSettings: Record<string, string> = {};
    /**
     * Resolves once persisted settings have been read from Neutralino storage.
     * Anything that needs values from `_extraSettings` (e.g. the boot-time
     * theme applier) must await this first — the constructor can't be async
     * so the load runs in the background.
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
     * whenever the raw libraryPath preference changes. `null` means the
     * user hasn't configured one; `undefined` means "not yet resolved".
     */
    private _resolvedLibraryPath: string | null | undefined = undefined;

    constructor(serverUrl: string) {
        const logger = { appendLine: (msg: string) => console.debug('[CalcPad]', msg) };
        this.apiClient = new CalcpadApiClient(serverUrl, logger);
        this.snippetService = new CalcpadSnippetService(this.apiClient, logger);
        this.definitionsService = new CalcpadDefinitionsService(this.apiClient);
        this.settings = getDefaultSettings();

        this.ready = this.loadSettingsFromStorage();
        this.snippetService.loadSnippets();
    }

    get api(): CalcpadApiClient {
        return this.apiClient;
    }

    get snippets(): CalcpadSnippetService {
        return this.snippetService;
    }

    get definitions(): CalcpadDefinitionsService {
        return this.definitionsService;
    }

    getSettings(): CalcpadSettings {
        return this.settings;
    }

    /** Default path passed to native dialogs. Falls back to the opened
     *  workspace folder, then to undefined (OS-default). */
    private getDialogDefaultPath(): string | undefined {
        if (this._lastDialogDir) return this._lastDialogDir;
        return undefined;
    }

    /** Update the remembered dialog directory. Accepts either a file path
     *  (parent dir is extracted) or a directory path directly. */
    private rememberDialogDir(pathOrFile: string | null | undefined, isDirectory = false): void {
        if (!pathOrFile) return;
        this._lastDialogDir = isDirectory ? pathOrFile : pathDirname(pathOrFile) || pathOrFile;
    }

    /** Read an "extra" preference (commentFormat, formattingHotkeys, quickTyping, …). */
    getExtraSetting(key: string): string | undefined {
        return this._extraSettings[key];
    }

    /** Persist an arbitrary extra preference. */
    setExtraSetting(key: string, value: string): void {
        this.persistSetting(key, value);
    }

    /**
     * Insert an image into the editor. Tries the system clipboard first;
     * if no image is present, falls back to a native file picker.
     */
    private async handleInsertImage(): Promise<void> {
        let dataUri = await readImageFromClipboard();

        if (!dataUri) {
            const entries = await os.showOpenDialog('Insert Image', {
                defaultPath: this.getDialogDefaultPath(),
                filters: [
                    { name: 'Images', extensions: ['png', 'jpg', 'jpeg', 'gif', 'webp', 'svg'] },
                    { name: 'All Files', extensions: ['*'] },
                ],
            });
            if (!entries || entries.length === 0) return;

            const filePath = entries[0];
            this.rememberDialogDir(filePath);
            const buffer = await filesystem.readBinaryFile(filePath);
            const mimeType = mimeFromExtension(filePath);
            dataUri = `data:${mimeType};base64,${bytesToBase64(buffer)}`;
        }

        if (this._onInsertText) {
            this._onInsertText(buildImageCommentLine(dataUri));
        }
    }

    /** Send updated TOC headings to the Vue sidebar. */
    public refreshHeadings(): void {
        const content = getActiveEditorContent();
        const headings = parseHeadings(content);
        this.postToVue({ type: 'updateHeadings', headings });
    }

    set onInsertText(handler: (text: string) => void) {
        this._onInsertText = handler;
    }

    handleMessage(message: any): void {
        switch (message.type) {
            case 'getInsertData':
                this.handleGetInsertData();
                break;
            case 'getSettings':
                this.handleGetSettings();
                break;
            case 'updateSettings':
                this.handleUpdateSettings(message.settings);
                break;
            case 'resetSettings':
                this.handleResetSettings();
                break;
            case 'getVariables':
                this.handleGetVariables();
                break;
            case 'insertText':
                if (this._onInsertText) {
                    this._onInsertText(message.text);
                }
                break;
            case 'insertImage':
                this.handleInsertImage();
                break;
            case 'updatePreviewTheme':
                this.persistSetting('previewTheme', message.theme);
                break;
            case 'updateColorTheme':
                this.persistSetting('colorTheme', message.theme);
                setAppTheme(coerceAppTheme(message.theme));
                break;
            case 'updateQuickTyping':
                this.persistSetting('quickTyping', String(message.enabled));
                break;
            case 'updateCommentFormat':
                this.persistSetting('commentFormat', message.format);
                break;
            case 'updateFormattingHotkeys':
                this.persistSetting('formattingHotkeys', String(message.enabled));
                break;
            case 'updateLinterMinSeverity':
                this.persistSetting('linterMinSeverity', message.severity);
                break;
            case 'updateLibraryPath':
                this.persistSetting('libraryPath', message.path ?? '');
                this._resolvedLibraryPath = undefined;
                break;
            case 'getPrettifySettings':
                this.handleGetPrettifySettings();
                break;
            case 'updatePrettifyIndentStyle':
                this.persistSetting('prettifyIndentStyle', message.value);
                break;
            case 'updatePrettifyIndentSize':
                this.persistSetting('prettifyIndentSize', String(message.value));
                break;
            case 'updatePrettifyTrim':
                this.persistSetting('prettifyTrimTrailingWhitespace', String(message.value));
                break;
            case 'prettifyDocument':
                this.handlePrettifyDocument();
                break;
            case 'getPdfSettings':
                this.handleGetPdfSettings();
                break;
            case 'updatePdfSettings':
                this.persistSetting('pdfSettings', JSON.stringify(message.settings));
                break;
            case 'resetPdfSettings':
                this.persistSetting('pdfSettings', '');
                this.handleGetPdfSettings();
                break;
            case 'generatePdf':
                this.handleGeneratePdf();
                break;
            case 'saveSourceHtml':
                this.handleSaveSourceHtml();
                break;
            case 'saveDocx':
                this.handleSaveDocx();
                break;
            case 'getServerLog':
                this.handleGetServerLog();
                break;
            case 'openLogsFolder':
                this.handleOpenLogsFolder();
                break;
            case 'getHeadings':
                this.refreshHeadings();
                break;
            case 'goToLine':
                this.handleGoToLine(message.line);
                break;
            case 'debug':
                break;
            case 'openFolder':
                this.handleOpenFolder();
                break;
            case 'readDirectory':
                this.handleReadDirectory(message.path);
                break;
            case 'getOpenedFolder':
                this.handleGetOpenedFolder();
                break;
            case 'openFileByPath':
                this.handleOpenFileByPath(message.path);
                break;
            case 'openContainingFolder':
                this.handleOpenContainingFolder(message.path);
                break;
            case 'closeFolder':
                this.handleCloseFolder();
                break;
        }
    }

    private handleGoToLine(line: number): void {
        if (typeof line !== 'number') return;
        const editors = (window as Window & { monaco?: typeof import('monaco-editor') }).monaco?.editor?.getEditors?.();
        const editor = editors?.[0];
        if (editor) {
            editor.revealLineInCenter(line);
            editor.setPosition({ lineNumber: line, column: 1 });
            editor.focus();
        }
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
            // Cap to the last 200 lines so the panel stays usable on long-running sessions.
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
        // Normalize backslashes — on Windows NL_PATH uses native separators
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
        // Delegate to main.ts's loadFile via a bridged event. main.ts already
        // listens for 'loadFileFromPath' on window.
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

    // ---- Internal message handlers ----

    private postToVue(message: unknown): void {
        window.dispatchEvent(new MessageEvent('message', { data: message }));
    }

    private async handleGetInsertData(): Promise<void> {
        const items = this.snippetService.getAllItems();
        if (items.length > 0) {
            this.postToVue({ type: 'insertDataResponse', items });
        } else {
            this.snippetService.onSnippetsLoaded(() => {
                this.postToVue({
                    type: 'insertDataResponse',
                    items: this.snippetService.getAllItems(),
                });
            });
        }
    }

    private handleGetSettings(): void {
        this.postToVue({
            type: 'settingsResponse',
            settings: this.settings,
            previewTheme: this._extraSettings.previewTheme || 'system',
            colorTheme: this.getStoredColorTheme(),
            availableThemes: BUILTIN_THEMES,
            commentFormat: this._extraSettings.commentFormat || 'auto',
            enableFormattingHotkeys: this._extraSettings.formattingHotkeys !== 'false',
            linterMinSeverity: this._extraSettings.linterMinSeverity || 'information',
            libraryPath: this._extraSettings.libraryPath || '',
        });
    }

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

    /** The stored Color Theme selection, coerced to a valid label. */
    getStoredColorTheme(): string {
        return coerceAppTheme(this._extraSettings.colorTheme);
    }

    private handleUpdateSettings(newSettings: any): void {
        this.settings = { ...this.settings, ...newSettings };
        this.saveSettingsToStorage();

        if (newSettings.server?.url) {
            this.apiClient.setBaseUrl(newSettings.server.url);
        }
    }

    private handleResetSettings(): void {
        this.settings = getDefaultSettings();
        this.saveSettingsToStorage();
        this.postToVue({ type: 'settingsReset', settings: this.settings });
    }

    private async handleGetVariables(): Promise<void> {
        const content = getActiveEditorContent();
        const { sourceFilePath } = await this.buildFileContext(content);

        const response = await this.definitionsService.refreshDefinitions(content, 'desktop-editor', sourceFilePath);
        if (response) {
            this.postToVue({
                type: 'updateVariables',
                data: {
                    macros: response.macros || [],
                    variables: response.variables || [],
                    functions: response.functions || [],
                    customUnits: response.customUnits || [],
                },
            });
        } else {
            this.postToVue({
                type: 'updateVariables',
                data: { macros: [], variables: [], functions: [], customUnits: [] },
            });
        }
    }

    private handleGetPrettifySettings(): void {
        const indentStyle = this._extraSettings.prettifyIndentStyle === 'space' ? 'space' : 'tab';
        const indentSizeRaw = parseInt(this._extraSettings.prettifyIndentSize ?? '', 10);
        const indentSize = Number.isFinite(indentSizeRaw) && indentSizeRaw >= 1 ? indentSizeRaw : 4;
        const trimTrailingWhitespace = this._extraSettings.prettifyTrimTrailingWhitespace !== 'false';
        this.postToVue({ type: 'prettifySettingsResponse', indentStyle, indentSize, trimTrailingWhitespace });
    }

    private async handlePrettifyDocument(): Promise<void> {
        const editor = monaco.editor.getEditors()[0];
        const model = editor?.getModel();
        if (!editor || !model) {
            console.warn('[Prettify] No active Monaco editor.');
            return;
        }

        const indentStyle = this._extraSettings.prettifyIndentStyle === 'space' ? 'space' : 'tab';
        const indentSizeRaw = parseInt(this._extraSettings.prettifyIndentSize ?? '', 10);
        const indentSize = Number.isFinite(indentSizeRaw) && indentSizeRaw >= 1 ? indentSizeRaw : 4;
        const trim = this._extraSettings.prettifyTrimTrailingWhitespace !== 'false';
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

    private handleGetPdfSettings(): void {
        const stored = this._extraSettings.pdfSettings;
        const settings = stored ? JSON.parse(stored) : {
            enableHeader: true,
            documentTitle: '',
            documentSubtitle: '',
            headerCenter: '',
            author: '',
            enableFooter: true,
            footerCenter: '',
            company: '',
            project: '',
            showPageNumbers: true,
            format: 'A4',
            orientation: 'portrait',
            marginTop: '2cm',
            marginBottom: '2cm',
            marginLeft: '1.5cm',
            marginRight: '1.5cm',
            printBackground: true,
            scale: 1.0,
        };
        this.postToVue({ type: 'pdfSettingsResponse', settings });
    }

    /**
     * Save the rendered HTML for the active document via a native save dialog.
     * Defaults the filename to the active tab's basename when available.
     */
    private async handleSaveSourceHtml(): Promise<void> {
        const content = getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const { sourceFilePath } = await this.buildFileContext(content);
        const html = await this.apiClient.convert(content, apiSettings, 'html', false, sourceFilePath);
        if (typeof html !== 'string') return;
        const filePath = await os.showSaveDialog('Save HTML', {
            defaultPath: this.getDialogDefaultPath(),
            filters: [{ name: 'HTML Files', extensions: ['html', 'htm'] }],
        });
        if (!filePath) return;
        this.rememberDialogDir(filePath);
        await filesystem.writeFile(filePath, html);
    }

    private async handleSaveDocx(): Promise<void> {
        const content = getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const { sourceFilePath } = await this.buildFileContext(content);
        const buf = await this.apiClient.convertDocx(content, apiSettings, sourceFilePath);
        if (!buf) return;
        const filePath = await os.showSaveDialog('Save Word Document', {
            defaultPath: this.getDialogDefaultPath(),
            filters: [{ name: 'Word Documents', extensions: ['docx'] }],
        });
        if (!filePath) return;
        this.rememberDialogDir(filePath);
        await filesystem.writeBinaryFile(filePath, buf);
    }

    private async handleGeneratePdf(): Promise<void> {
        // Pre-flight: if the bundled launcher couldn't find a Chromium-family
        // browser, tell the user *before* they sit through a 10-second
        // PuppeteerSharp timeout. The launcher writes its detection result to
        // server-stderr.log on startup ("WARNING: no Chromium..." or
        // "Using browser: ..."), so a quick read tells us whether to abort.
        if (await this.browserMissing()) {
            await this.warnBrowserMissing();
            return;
        }

        const content = getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const baseUrl = this.apiClient.getBaseUrl();
        const { sourceFilePath } = await this.buildFileContext(content);

        try {
            // Step 1 — convert calcpad source → HTML (forPrint: true).
            // The /convert endpoint always returns HTML; an outputFormat hint
            // here is ignored, which is why the prior single-call approach
            // silently failed.
            const htmlResp = await fetch(`${baseUrl}/api/calcpad/convert`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ content, settings: apiSettings, forPrint: true, sourceFilePath }),
                signal: AbortSignal.timeout(30000),
            });
            if (!htmlResp.ok) throw new Error(`HTML convert returned ${htmlResp.status}`);
            let html = await htmlResp.text();

            // Step 2 — inline local <img src="..."> as base64 data URIs.
            // The headless Chromium running on the server has no access to the
            // user's filesystem; without inlining, plot images and other local
            // references render as broken icons. Resolved relative to the
            // active tab's directory when one exists.
            const documentDir = this.activeTabDirectory();
            html = await inlineLocalImages(html, documentDir);

            // Step 3 — render the inlined HTML to PDF via /api/calcpad/pdf.
            const pdfResp = await fetch(`${baseUrl}/api/calcpad/pdf`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ html, options: this.getPdfOptions() }),
                signal: AbortSignal.timeout(60000),
            });
            if (!pdfResp.ok) throw new Error(`PDF endpoint returned ${pdfResp.status}`);
            const pdfBytes = await pdfResp.arrayBuffer();

            const filePath = await os.showSaveDialog('Export PDF', {
                defaultPath: this.getDialogDefaultPath(),
                filters: [{ name: 'PDF Files', extensions: ['pdf'] }],
            });
            if (filePath) {
                this.rememberDialogDir(filePath);
                await filesystem.writeBinaryFile(filePath, pdfBytes);
            }
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            this.postToVue({ type: 'pdfError', message: `PDF export failed: ${msg}` });
            this.handleGetServerLog();
            if (await this.browserMissing()) {
                await this.warnBrowserMissing();
            }
        }
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
     * Builds the source path needed to resolve #include directives.
     * Called from the preview renderer and export handlers before any convert call.
     */
    public async buildFileContext(_content: string): Promise<{ sourceFilePath?: string }> {
        const sourceFilePath = this.activeTabFilePath() || undefined;
        return { sourceFilePath };
    }

    /**
     * Resolve an `#include` filename (raw text from the directive) to an
     * absolute path, using the active tab's directory as the base. Mirrors
     * the resolution rules the backend uses for include lookup.
     */
    public resolveIncludePath(rawFileName: string): string {
        return pathResolve(this.activeTabSourceDir(), rawFileName);
    }

    /**
     * Returns the directory to use as the base for resolving #include paths.
     * Falls back to the Neutralino install directory (NL_PATH) for untitled tabs
     * so that includes placed alongside the app still resolve.
     */
    private activeTabSourceDir(): string {
        const dir = this.activeTabDirectory();
        if (dir) return dir;
        return (window as Window & { NL_PATH?: string }).NL_PATH ?? '';
    }

    /** Resolves stored PDF settings for the /pdf endpoint. */
    private getPdfOptions(): unknown {
        const stored = this._extraSettings.pdfSettings;
        return stored ? JSON.parse(stored) : {};
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
        // Linux / FreeBSD / Unknown
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

    // ---- Settings persistence via Neutralino.storage ----

    private async loadSettingsFromStorage(): Promise<void> {
        try {
            const raw = await storage.getData(SETTINGS_KEY);
            const data = JSON.parse(raw);
            if (data.calcpadSettings) {
                this.settings = data.calcpadSettings;
            }
            if (data.extraSettings) {
                this._extraSettings = data.extraSettings;
            }
        } catch {
            // No stored settings yet — use defaults
        }
    }

    private saveSettingsToStorage(): void {
        storage.setData(SETTINGS_KEY, JSON.stringify({
            calcpadSettings: this.settings,
            extraSettings: this._extraSettings,
        }));
    }

    private persistSetting(key: string, value: string): void {
        this._extraSettings[key] = value;
        this.saveSettingsToStorage();
    }
}
