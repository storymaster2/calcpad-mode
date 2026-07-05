import { CalcpadApiClient } from '../../api/client';
import { CalcpadSnippetService } from '../snippets';
import { CalcpadDefinitionsService } from '../definitions';
import { parseHeadings } from '../headings';
import { getDefaultSettings, buildApiSettings } from '../../types/settings';
import type { CalcpadSettings } from '../../types/settings';
import { DEFAULT_PDF_SETTINGS } from '../../types/pdf-settings';
import { buildImageCommentLine } from '../image-utils';
import type { ILogger } from '../../types/interfaces';

export interface ExportRequest {
    defaultName: string;
    data: string | ArrayBuffer;
    mime: string;
    extensions: string[];
    dialogTitle: string;
}

const BUILTIN_THEMES = [
    { id: 'calcpad-dark',  label: 'Dark',  kind: 'dark'  as const },
    { id: 'calcpad-light', label: 'Light', kind: 'light' as const },
];

/**
 * Shared message routing and handlers for the web and Neutralino bridges.
 *
 * Subclasses inject platform-specific behavior via the abstract hooks
 * (settings storage, file save/pick, image pick, source-file resolution)
 * and can add platform-only message cases by overriding
 * `handlePlatformMessage`.
 */
export abstract class BaseMessageBridge {
    protected apiClient: CalcpadApiClient;
    protected snippetService: CalcpadSnippetService;
    protected definitionsService: CalcpadDefinitionsService;
    protected settings: CalcpadSettings;
    protected _onInsertText: ((text: string) => void) | null = null;

    constructor(serverUrl: string, logger?: ILogger) {
        const log: ILogger = logger ?? { appendLine: (msg: string) => console.debug('[CalcPad]', msg) };
        this.apiClient = new CalcpadApiClient(serverUrl, log);
        this.snippetService = new CalcpadSnippetService(this.apiClient, log);
        this.definitionsService = new CalcpadDefinitionsService(this.apiClient);
        this.settings = getDefaultSettings();
        this.snippetService.loadSnippets();
    }

    get api(): CalcpadApiClient { return this.apiClient; }
    get snippets(): CalcpadSnippetService { return this.snippetService; }
    get definitions(): CalcpadDefinitionsService { return this.definitionsService; }

    getSettings(): CalcpadSettings { return this.settings; }

    /** Read an "extra" (non-CalcpadSettings) preference. */
    abstract getExtraSetting(key: string): string | undefined;
    /** Persist an arbitrary extra preference. */
    abstract setExtraSetting(key: string, value: string): void;

    set onInsertText(handler: (text: string) => void) {
        this._onInsertText = handler;
    }

    /** Send updated TOC headings to the Vue sidebar. */
    refreshHeadings(): void {
        const content = this.getActiveEditorContent();
        const headings = parseHeadings(content);
        this.postToVue({ type: 'updateHeadings', headings });
    }

    /** Return the (coerced) stored color-theme label. */
    getStoredColorTheme(): string {
        return this.coerceColorTheme(this.getExtraSetting('colorTheme'));
    }

    handleMessage(message: any): void {
        if (this.handlePlatformMessage(message)) return;

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
                if (this._onInsertText) this._onInsertText(message.text);
                break;
            case 'insertImage':
                this.handleInsertImage();
                break;
            case 'updatePreviewTheme':
                this.setExtraSetting('previewTheme', message.theme);
                break;
            case 'updateColorTheme':
                this.setExtraSetting('colorTheme', message.theme);
                this.applyColorTheme(message.theme);
                break;
            case 'updateQuickTyping':
                this.setExtraSetting('quickTyping', String(message.enabled));
                break;
            case 'updateCommentFormat':
                this.setExtraSetting('commentFormat', message.format);
                break;
            case 'updateFormattingHotkeys':
                this.setExtraSetting('formattingHotkeys', String(message.enabled));
                break;
            case 'updateLinterMinSeverity':
                this.setExtraSetting('linterMinSeverity', message.severity);
                break;
            case 'getPdfSettings':
                this.handleGetPdfSettings();
                break;
            case 'updatePdfSettings':
                this.setExtraSetting('pdfSettings', JSON.stringify(message.settings));
                break;
            case 'resetPdfSettings':
                this.setExtraSetting('pdfSettings', '');
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
                this.refreshHeadings();
                break;
            case 'goToLine':
                this.handleGoToLine(message.line);
                break;
            case 'openLogsFolder':
                this.onOpenLogsFolder();
                break;
            case 'debug':
                break;
        }
    }

    // ---- Platform hooks (subclasses override) ----

    protected abstract persistSettings(settings: CalcpadSettings): void | Promise<void>;
    protected abstract resetSettingsBackend(): void | Promise<void>;
    protected abstract coerceColorTheme(raw: string | undefined | null): string;
    protected abstract applyColorTheme(theme: string): void;
    /** Return a fresh data URI for an inserted image, or null if the user cancelled. */
    protected abstract pickImage(): Promise<string | null>;
    protected abstract saveExportedFile(req: ExportRequest): Promise<void>;
    protected abstract buildFileContext(content: string): Promise<{ sourceFilePath?: string }>;
    protected abstract getVariablesOrigin(): string;
    protected abstract generatePdfBytes(
        content: string,
        apiSettings: unknown,
        sourceFilePath: string | undefined,
    ): Promise<ArrayBuffer | null>;

    protected buildSettingsResponseExtras(): Record<string, unknown> | Promise<Record<string, unknown>> { return {}; }
    protected async runPdfPreflight(): Promise<boolean> { return true; }
    protected async onPdfError(_err: unknown): Promise<void> { /* default no-op */ }
    protected handlePlatformMessage(_message: any): boolean { return false; }
    protected onOpenLogsFolder(): void {
        console.warn('Open Logs Folder is only available in the desktop build — server logs live on the host running CalcPad.');
    }
    protected afterResetSettings(): void | Promise<void> { /* default no-op */ }

    // ---- Shared handlers ----

    protected postToVue(message: unknown): void {
        window.dispatchEvent(new MessageEvent('message', { data: message }));
    }

    protected getActiveEditorContent(): string {
        const tabs = (window as { calcpadTabs?: { activeModel?: { getValue(): string } } }).calcpadTabs;
        const fromTabs = tabs?.activeModel?.getValue();
        if (typeof fromTabs === 'string') return fromTabs;
        const m = (window as { monaco?: MonacoLike }).monaco;
        if (!m) return '';
        const editor = m.editor.getEditors()[0];
        const model = editor?.getModel() ?? m.editor.getModels()[0];
        return model?.getValue() ?? '';
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

    protected async handleGetSettings(): Promise<void> {
        const extras = await this.buildSettingsResponseExtras();
        this.postToVue({
            type: 'settingsResponse',
            settings: this.settings,
            previewTheme: this.getExtraSetting('previewTheme') || 'system',
            colorTheme: this.getStoredColorTheme(),
            availableThemes: BUILTIN_THEMES,
            commentFormat: this.getExtraSetting('commentFormat') || 'auto',
            enableFormattingHotkeys: this.getExtraSetting('formattingHotkeys') !== 'false',
            linterMinSeverity: this.getExtraSetting('linterMinSeverity') || 'information',
            ...extras,
        });
    }

    private async handleUpdateSettings(newSettings: any): Promise<void> {
        this.settings = { ...this.settings, ...newSettings };
        await this.persistSettings(this.settings);
        if (newSettings.server?.url) {
            this.apiClient.setBaseUrl(newSettings.server.url);
        }
    }

    private async handleResetSettings(): Promise<void> {
        await this.resetSettingsBackend();
        this.postToVue({ type: 'settingsReset', settings: this.settings });
        await this.afterResetSettings();
    }

    private async handleGetVariables(): Promise<void> {
        const content = this.getActiveEditorContent();
        const { sourceFilePath } = await this.buildFileContext(content);
        const response = await this.definitionsService.refreshDefinitions(
            content,
            this.getVariablesOrigin(),
            sourceFilePath,
        );
        this.postToVue({
            type: 'updateVariables',
            data: {
                macros: response?.macros ?? [],
                variables: response?.variables ?? [],
                functions: response?.functions ?? [],
                customUnits: response?.customUnits ?? [],
            },
        });
    }

    private handleGetPdfSettings(): void {
        const stored = this.getExtraSetting('pdfSettings');
        const settings = stored ? JSON.parse(stored) : { ...DEFAULT_PDF_SETTINGS };
        this.postToVue({ type: 'pdfSettingsResponse', settings });
    }

    private async handleInsertImage(): Promise<void> {
        const dataUri = await this.pickImage();
        if (dataUri && this._onInsertText) {
            this._onInsertText(buildImageCommentLine(dataUri));
        }
    }

    private async handleSaveSourceHtml(): Promise<void> {
        const content = this.getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const { sourceFilePath } = await this.buildFileContext(content);
        const html = await this.apiClient.convert(content, apiSettings, 'html', false, sourceFilePath);
        if (typeof html !== 'string') return;
        await this.saveExportedFile({
            defaultName: 'calcpad-output.html',
            data: html,
            mime: 'text/html;charset=utf-8',
            extensions: ['html', 'htm'],
            dialogTitle: 'Save HTML',
        });
    }

    private async handleSaveDocx(): Promise<void> {
        const content = this.getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const { sourceFilePath } = await this.buildFileContext(content);
        const buf = await this.apiClient.convertDocx(content, apiSettings, sourceFilePath);
        if (!buf) return;
        await this.saveExportedFile({
            defaultName: 'calcpad-output.docx',
            data: buf,
            mime: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
            extensions: ['docx'],
            dialogTitle: 'Save Word Document',
        });
    }

    private async handleGeneratePdf(): Promise<void> {
        if (!(await this.runPdfPreflight())) return;

        const content = this.getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const { sourceFilePath } = await this.buildFileContext(content);

        try {
            const pdfBytes = await this.generatePdfBytes(content, apiSettings, sourceFilePath);
            if (!pdfBytes) return;
            await this.saveExportedFile({
                defaultName: 'calcpad-output.pdf',
                data: pdfBytes,
                mime: 'application/pdf',
                extensions: ['pdf'],
                dialogTitle: 'Export PDF',
            });
        } catch (err) {
            await this.onPdfError(err);
        }
    }

    private handleGoToLine(line: number): void {
        if (typeof line !== 'number') return;
        const editors = (window as { monaco?: MonacoLike }).monaco?.editor?.getEditors?.();
        const editor = editors?.[0];
        if (editor) {
            editor.revealLineInCenter(line);
            editor.setPosition({ lineNumber: line, column: 1 });
            editor.focus();
        }
    }
}

interface MonacoModelLike { getValue(): string; }
interface MonacoEditorLike {
    getModel(): MonacoModelLike | null;
    revealLineInCenter(line: number): void;
    setPosition(pos: { lineNumber: number; column: number }): void;
    focus(): void;
}
interface MonacoLike {
    editor: {
        getEditors(): MonacoEditorLike[];
        getModels(): MonacoModelLike[];
    };
}
