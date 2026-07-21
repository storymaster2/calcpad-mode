import { CalcpadApiClient } from 'calcpad-frontend/api/client';
import { CalcpadSnippetService } from 'calcpad-frontend/services/snippets';
import { CalcpadDefinitionsService } from 'calcpad-frontend/services/definitions';
import { parseHeadings } from 'calcpad-frontend/services/headings';
import { getDefaultSettings, buildApiSettings } from 'calcpad-frontend/types/settings';
import type { CalcpadSettings } from 'calcpad-frontend/types/settings';
import {
    readImageFromClipboard,
    blobToDataUri,
    buildImageCommentLine,
} from './image-insert';
import { getActiveEditorContent } from './active-editor';

const SETTINGS_KEY = 'calcpad-settings';
const PDF_SETTINGS_KEY = 'calcpad-pdf-settings';

function triggerBlobDownload(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
}

/**
 * In-process message bridge for the web platform.
 * Handles Vue sidebar messages locally — same role as calcpadVueUIProvider.ts
 * in the VS Code extension, but without IPC.
 */
export class MessageBridge {
    private apiClient: CalcpadApiClient;
    private snippetService: CalcpadSnippetService;
    private definitionsService: CalcpadDefinitionsService;
    private settings: CalcpadSettings;
    private _onInsertText: ((text: string) => void) | null = null;

    constructor(serverUrl: string) {
        const logger = { appendLine: (msg: string) => console.debug('[CalcPad]', msg) };
        this.apiClient = new CalcpadApiClient(serverUrl, logger);
        this.snippetService = new CalcpadSnippetService(this.apiClient, logger);
        this.definitionsService = new CalcpadDefinitionsService(this.apiClient);
        this.settings = this.loadSettings();

        // Bootstrap URL (VITE_SERVER_URL / ?server=) wins over a stale localStorage
        // default so Settings and API calls stay pointed at the same engine.
        if (serverUrl && serverUrl !== this.settings.server.url) {
            this.settings = {
                ...this.settings,
                server: { ...this.settings.server, url: serverUrl, mode: 'remote' },
            };
            localStorage.setItem(SETTINGS_KEY, JSON.stringify(this.settings));
        }

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

    /** Read an "extra" (non-CalcpadSettings) preference like commentFormat / formattingHotkeys. */
    getExtraSetting(key: string): string | undefined {
        const stored = localStorage.getItem('calcpad-' + camelToKebab(key));
        return stored ?? undefined;
    }

    /** Persist an arbitrary extra preference. */
    setExtraSetting(key: string, value: string): void {
        localStorage.setItem('calcpad-' + camelToKebab(key), value);
    }

    set onInsertText(handler: (text: string) => void) {
        this._onInsertText = handler;
    }

    /**
     * Handle a message from the Vue sidebar.
     * This is called by the messaging adapter when Vue calls postMessage().
     */
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
                localStorage.setItem('calcpad-preview-theme', message.theme);
                break;

            case 'updateQuickTyping':
                localStorage.setItem('calcpad-quick-typing', String(message.enabled));
                break;

            case 'updateCommentFormat':
                localStorage.setItem('calcpad-comment-format', message.format);
                break;

            case 'updateFormattingHotkeys':
                localStorage.setItem('calcpad-formatting-hotkeys', String(message.enabled));
                break;

            case 'updateLinterMinSeverity':
                localStorage.setItem('calcpad-linter-severity', message.severity);
                break;

            case 'getPdfSettings':
                this.handleGetPdfSettings();
                break;

            case 'updatePdfSettings':
                localStorage.setItem(PDF_SETTINGS_KEY, JSON.stringify(message.settings));
                break;

            case 'resetPdfSettings':
                localStorage.removeItem(PDF_SETTINGS_KEY);
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

            case 'getHeadings':
                this.handleGetHeadings();
                break;

            case 'goToLine':
                this.handleGoToLine(message.line);
                break;

            case 'getExports':
                this.refreshExports();
                break;

            case 'downloadExport':
                this.handleDownloadExport(message.filename);
                break;

            case 'downloadExportZip':
                this.handleDownloadExportZip();
                break;

            case 'debug':
                console.debug('[Vue]', message.message);
                break;
        }
    }

    /**
     * Fetches the current export list from the server and pushes it to the Vue sidebar.
     * Web platform has no source file path; the server falls back to the anonymous bucket.
     */
    public async refreshExports(): Promise<void> {
        const exports = await this.apiClient.listExports();
        this.postToVue({ type: 'exportsResponse', exports });
    }

    private async handleDownloadExport(filename: string): Promise<void> {
        if (!filename) return;
        const blob = await this.apiClient.downloadExport(filename);
        if (!blob) return;
        triggerBlobDownload(blob, filename);
    }

    private async handleDownloadExportZip(): Promise<void> {
        const blob = await this.apiClient.downloadExportZip();
        if (!blob) return;
        triggerBlobDownload(blob, 'calcpad-exports.zip');
    }

    /**
     * Post a message back to the Vue sidebar.
     * Dispatches a window MessageEvent so CalcpadApp.vue's listener picks it up.
     */
    private postToVue(message: unknown): void {
        window.dispatchEvent(new MessageEvent('message', { data: message }));
    }

    private async handleGetInsertData(): Promise<void> {
        const items = this.snippetService.getAllItems();
        if (items.length > 0) {
            this.postToVue({ type: 'insertDataResponse', items });
        } else {
            // Wait for snippets to load, then respond
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
            previewTheme: localStorage.getItem('calcpad-preview-theme') || 'system',
            commentFormat: localStorage.getItem('calcpad-comment-format') || 'auto',
            enableFormattingHotkeys: localStorage.getItem('calcpad-formatting-hotkeys') !== 'false',
            linterMinSeverity: localStorage.getItem('calcpad-linter-severity') || 'information',
        });
    }

    private handleUpdateSettings(newSettings: any): void {
        this.settings = { ...this.settings, ...newSettings };
        localStorage.setItem(SETTINGS_KEY, JSON.stringify(this.settings));

        // Update API client base URL if server URL changed
        if (newSettings.server?.url) {
            this.apiClient.setBaseUrl(newSettings.server.url);
        }
    }

    private handleResetSettings(): void {
        this.settings = getDefaultSettings();
        localStorage.setItem(SETTINGS_KEY, JSON.stringify(this.settings));
        this.postToVue({ type: 'settingsReset', settings: this.settings });
    }

    private async handleGetVariables(): Promise<void> {
        // Get content from the Monaco editor via the current model
        const content = getActiveEditorContent();

        const response = await this.definitionsService.refreshDefinitions(content, 'web-editor');
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
        const stored = localStorage.getItem(PDF_SETTINGS_KEY);
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

    private async handleGeneratePdf(): Promise<void> {
        const content = getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);

        const result = await this.apiClient.convert(content, apiSettings, 'pdf', true);
        if (result instanceof ArrayBuffer) {
            // Trigger browser download
            const blob = new Blob([result], { type: 'application/pdf' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = 'calcpad-output.pdf';
            a.click();
            URL.revokeObjectURL(url);
        }
    }

    /**
     * Save the rendered HTML for the active document. On the web platform
     * we trigger a browser download; the user picks the path via the
     * browser's standard save dialog.
     */
    private async handleSaveSourceHtml(): Promise<void> {
        const content = getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const html = await this.apiClient.convert(content, apiSettings, 'html');
        if (typeof html !== 'string') return;
        const blob = new Blob([html], { type: 'text/html;charset=utf-8' });
        triggerBlobDownload(blob, 'calcpad-output.html');
    }

    private async handleSaveDocx(): Promise<void> {
        const content = getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const buf = await this.apiClient.convertDocx(content, apiSettings);
        if (!buf) return;
        const blob = new Blob([buf], {
            type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        });
        triggerBlobDownload(blob, 'calcpad-output.docx');
    }

    /**
     * Insert an image into the editor. Tries the system clipboard first;
     * if no image is present, falls back to an HTML file input dialog.
     */
    private async handleInsertImage(): Promise<void> {
        let dataUri = await readImageFromClipboard();

        if (!dataUri) {
            dataUri = await pickImageViaInput();
        }

        if (dataUri && this._onInsertText) {
            this._onInsertText(buildImageCommentLine(dataUri));
        }
    }

    /** Send updated headings to the Vue sidebar. Called on-demand and on debounced content changes. */
    public refreshHeadings(): void {
        const content = getActiveEditorContent();
        const headings = parseHeadings(content);
        this.postToVue({ type: 'updateHeadings', headings });
    }

    private handleGetHeadings(): void {
        this.refreshHeadings();
    }

    private handleGoToLine(line: number): void {
        if (typeof line !== 'number') return;
        const editors = (window as any).monaco?.editor?.getEditors?.();
        const editor = editors?.[0];
        if (editor) {
            editor.revealLineInCenter(line);
            editor.setPosition({ lineNumber: line, column: 1 });
            editor.focus();
        }
    }

    private loadSettings(): CalcpadSettings {
        const stored = localStorage.getItem(SETTINGS_KEY);
        if (stored) {
            try {
                return JSON.parse(stored);
            } catch {
                // Fall through to defaults
            }
        }
        return getDefaultSettings();
    }
}

function camelToKebab(s: string): string {
    return s.replace(/([A-Z])/g, '-$1').toLowerCase();
}

/** Pop a hidden `<input type="file">` to let the user choose an image; resolve with a data URI. */
function pickImageViaInput(): Promise<string | null> {
    return new Promise(resolve => {
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = 'image/png,image/jpeg,image/gif,image/webp,image/svg+xml';
        input.style.display = 'none';
        let settled = false;
        const cleanup = () => { if (input.parentNode) input.parentNode.removeChild(input); };
        input.onchange = async () => {
            settled = true;
            const file = input.files?.[0];
            if (!file) { cleanup(); resolve(null); return; }
            const uri = await blobToDataUri(file);
            cleanup();
            resolve(uri);
        };
        // If the user cancels, no event fires reliably across browsers; clean up on next tick
        // when focus returns to the window.
        const onFocus = () => {
            window.removeEventListener('focus', onFocus);
            setTimeout(() => {
                if (!settled) { cleanup(); resolve(null); }
            }, 200);
        };
        window.addEventListener('focus', onFocus);
        document.body.appendChild(input);
        input.click();
    });
}
