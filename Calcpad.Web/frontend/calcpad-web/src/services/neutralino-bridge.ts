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

const SETTINGS_KEY = 'calcpad-settings';
const RECENT_FILES_KEY = 'calcpad-recent-files';
const MAX_RECENT_FILES = 10;

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

    constructor(serverUrl: string) {
        this.apiClient = new CalcpadApiClient(serverUrl);
        this.snippetService = new CalcpadSnippetService(this.apiClient);
        this.definitionsService = new CalcpadDefinitionsService(this.apiClient);
        this.settings = getDefaultSettings();

        this.loadSettingsFromStorage();
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
                filters: [
                    { name: 'Images', extensions: ['png', 'jpg', 'jpeg', 'gif', 'webp', 'svg'] },
                    { name: 'All Files', extensions: ['*'] },
                ],
            });
            if (!entries || entries.length === 0) return;

            const filePath = entries[0];
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
            case 'debug':
                break;
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
        const NL_PATH = (window as Window & { NL_PATH?: string }).NL_PATH;
        if (!NL_PATH) return null;
        return `${NL_PATH}/extensions/server/logs/server-stderr.log`;
    }

    // ---- File operations (exposed for menu actions) ----

    async openFile(): Promise<{ path: string; content: string } | null> {
        const entries = await os.showOpenDialog('Open File', {
            filters: [
                { name: 'CalcPad Files', extensions: ['cpd'] },
                { name: 'All Files', extensions: ['*'] },
            ],
        });

        if (!entries || entries.length === 0) return null;

        const filePath = entries[0];
        const content = await filesystem.readFile(filePath);
        return { path: filePath, content };
    }

    async saveFile(filePath: string, content: string): Promise<void> {
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
            filters: [
                { name: 'CalcPad Files', extensions: ['cpd'] },
                { name: 'All Files', extensions: ['*'] },
            ],
        });

        if (!filePath) return null;

        await filesystem.writeFile(filePath, content);
        return filePath;
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
            commentFormat: this._extraSettings.commentFormat || 'auto',
            enableFormattingHotkeys: this._extraSettings.formattingHotkeys !== 'false',
            linterMinSeverity: this._extraSettings.linterMinSeverity || 'information',
        });
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

        const response = await this.definitionsService.refreshDefinitions(content, 'desktop-editor');
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
        const html = await this.apiClient.convert(content, apiSettings, 'html');
        if (typeof html !== 'string') return;
        const filePath = await os.showSaveDialog('Save HTML', {
            filters: [{ name: 'HTML Files', extensions: ['html', 'htm'] }],
        });
        if (!filePath) return;
        await filesystem.writeFile(filePath, html);
    }

    private async handleSaveDocx(): Promise<void> {
        const content = getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const buf = await this.apiClient.convertDocx(content, apiSettings);
        if (!buf) return;
        const filePath = await os.showSaveDialog('Save Word Document', {
            filters: [{ name: 'Word Documents', extensions: ['docx'] }],
        });
        if (!filePath) return;
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

        try {
            // Step 1 — convert calcpad source → HTML (forPrint: true).
            // The /convert endpoint always returns HTML; an outputFormat hint
            // here is ignored, which is why the prior single-call approach
            // silently failed.
            const htmlResp = await fetch(`${baseUrl}/api/calcpad/convert`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ content, settings: apiSettings, forPrint: true }),
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
                filters: [{ name: 'PDF Files', extensions: ['pdf'] }],
            });
            if (filePath) {
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

    /** Returns the directory of the active tab's file, or '' if untitled. */
    private activeTabDirectory(): string {
        const tabs = (window as any).calcpadTabs;
        const filePath: string | null | undefined = tabs?.activeTab?.filePath;
        if (!filePath) return '';
        const idx = Math.max(filePath.lastIndexOf('/'), filePath.lastIndexOf('\\'));
        return idx > 0 ? filePath.slice(0, idx) : '';
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
