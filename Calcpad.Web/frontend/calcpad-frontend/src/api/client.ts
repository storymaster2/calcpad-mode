import type { ILogger } from '../types/interfaces';
import type {
    LintRequest,
    LintResponse,
    ClientFileCache,
    HighlightRequest,
    HighlightResponse,
    HighlightToken,
    DefinitionsRequest,
    DefinitionsResponse,
    FindReferencesResponse,
    PrettifyRequest,
    PrettifyResponse,
    ExportMeta,
} from '../types/api';
import type { SnippetsResponse } from '../types/snippets';

/**
 * Unified fetch-based API client for the CalcPad server.
 * Replaces scattered axios calls across the extension codebase.
 * Works in Node.js 18+, Electron, and browsers.
 */
export class CalcpadApiClient {
    private baseUrl: string;
    private logger?: ILogger;

    constructor(baseUrl: string, logger?: ILogger) {
        this.baseUrl = baseUrl;
        this.logger = logger;
    }

    public setBaseUrl(url: string): void {
        this.baseUrl = url;
    }

    public getBaseUrl(): string {
        return this.baseUrl;
    }

    public async lint(content: string, clientFileCache?: ClientFileCache, sourceFilePath?: string): Promise<LintResponse | null> {
        const request: LintRequest = { content, clientFileCache, sourceFilePath };
        return this.post<LintResponse>('/api/calcpad/lint', request, 'Lint');
    }

    public async highlight(content: string, includeText: boolean = false, clientFileCache?: ClientFileCache, sourceFilePath?: string): Promise<HighlightToken[] | null> {
        const request: HighlightRequest = { content, includeText, clientFileCache, sourceFilePath };
        const response = await this.post<HighlightResponse>('/api/calcpad/highlight', request, 'Highlight');
        return response?.tokens ?? null;
    }

    public async definitions(content: string, clientFileCache?: ClientFileCache, sourceFilePath?: string): Promise<DefinitionsResponse | null> {
        const request: DefinitionsRequest = { content, clientFileCache, sourceFilePath };
        return this.post<DefinitionsResponse>('/api/calcpad/definitions', request, 'Definitions');
    }

    public async findReferences(content: string, clientFileCache?: ClientFileCache, sourceFilePath?: string): Promise<FindReferencesResponse | null> {
        const request: DefinitionsRequest = { content, clientFileCache, sourceFilePath };
        return this.post<FindReferencesResponse>('/api/calcpad/find-references', request, 'FindReferences');
    }

    public async snippets(): Promise<SnippetsResponse | null> {
        return this.get<SnippetsResponse>('/api/calcpad/snippets', 'Snippets');
    }

    public async prettify(
        content: string,
        indentUnit?: string,
        trimTrailingWhitespace?: boolean
    ): Promise<PrettifyResponse | null> {
        const request: PrettifyRequest = { content, indentUnit, trimTrailingWhitespace };
        return this.post<PrettifyResponse>('/api/calcpad/prettify', request, 'Prettify');
    }

    public async convert(
        content: string,
        settings: unknown,
        outputFormat: string = 'html',
        forPrint: boolean = false
    ): Promise<ArrayBuffer | string | null> {
        const url = this.baseUrl + '/api/calcpad/convert';
        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ content, settings, outputFormat, forPrint }),
                signal: AbortSignal.timeout(60000),
            });
            if (!response.ok) return null;

            if (outputFormat === 'pdf') {
                return response.arrayBuffer();
            }
            return response.text();
        } catch (error) {
            this.logError('Convert', error);
            return null;
        }
    }

    /**
     * Convert calcpad to "unwrapped" HTML — server returns just the body markup
     * without the document chrome. Used for preview-pane rendering.
     */
    public async convertUnwrapped(
        content: string,
        settings: unknown,
        clientFileCache?: ClientFileCache,
    ): Promise<string | null> {
        const url = this.baseUrl + '/api/calcpad/convert-unwrapped';
        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ content, settings, clientFileCache }),
                signal: AbortSignal.timeout(60000),
            });
            if (!response.ok) return null;
            return response.text();
        } catch (error) {
            this.logError('ConvertUnwrapped', error);
            return null;
        }
    }

    /**
     * Convert with the interactive UI shell — inputs, dropdowns, datagrids.
     * Pass `uiOverrides` to apply the user's input edits (empty `{}` for first render).
     */
    public async convertUi(
        content: string,
        settings: unknown,
        uiOverrides: Record<string, unknown> = {},
        clientFileCache?: ClientFileCache,
    ): Promise<string | null> {
        const url = this.baseUrl + '/api/calcpad/convert-ui';
        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ content, settings, uiOverrides, clientFileCache }),
                signal: AbortSignal.timeout(60000),
            });
            if (!response.ok) return null;
            return response.text();
        } catch (error) {
            this.logError('ConvertUi', error);
            return null;
        }
    }

    /**
     * Lists files captured by #write/#append for the most recent convert run.
     * Mirrors how the backend keys #read content by URL: exports are keyed by
     * the client's source file path so the frontend can poll after each convert.
     */
    public async listExports(sourceFilePath?: string): Promise<ExportMeta[]> {
        const url = this.baseUrl + '/api/calcpad/exports' + (sourceFilePath ? `?sourceFilePath=${encodeURIComponent(sourceFilePath)}` : '');
        try {
            const response = await fetch(url, { signal: AbortSignal.timeout(10000) });
            if (!response.ok) return [];
            return (await response.json()) as ExportMeta[];
        } catch (error) {
            this.logError('ListExports', error);
            return [];
        }
    }

    /** Downloads a single export file. */
    public async downloadExport(filename: string, sourceFilePath?: string): Promise<Blob | null> {
        const params = new URLSearchParams();
        if (sourceFilePath) params.set('sourceFilePath', sourceFilePath);
        params.set('filename', filename);
        const url = this.baseUrl + '/api/calcpad/export?' + params.toString();
        try {
            const response = await fetch(url, { signal: AbortSignal.timeout(60000) });
            if (!response.ok) return null;
            return await response.blob();
        } catch (error) {
            this.logError('DownloadExport', error);
            return null;
        }
    }

    /** Downloads all exports for the source file as a single ZIP. */
    public async downloadExportZip(sourceFilePath?: string): Promise<Blob | null> {
        const url = this.baseUrl + '/api/calcpad/exports.zip' + (sourceFilePath ? `?sourceFilePath=${encodeURIComponent(sourceFilePath)}` : '');
        try {
            const response = await fetch(url, { signal: AbortSignal.timeout(60000) });
            if (!response.ok) return null;
            return await response.blob();
        } catch (error) {
            this.logError('DownloadExportZip', error);
            return null;
        }
    }

    public async refreshCache(): Promise<boolean> {
        try {
            const response = await fetch(this.baseUrl + '/api/calcpad/refresh-cache', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: '{}',
                signal: AbortSignal.timeout(5000),
            });
            return response.ok;
        } catch {
            return false;
        }
    }

    public async checkHealth(): Promise<boolean> {
        try {
            const response = await fetch(this.baseUrl + '/api/calcpad/snippets', {
                signal: AbortSignal.timeout(5000),
            });
            return response.ok;
        } catch {
            return false;
        }
    }

    private async post<T>(endpoint: string, body: unknown, tag: string): Promise<T | null> {
        const url = this.baseUrl + endpoint;
        try {
            this.logger?.appendLine(`[${tag}] Sending request to server...`);
            const response = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body),
                signal: AbortSignal.timeout(30000),
            });
            if (!response.ok) {
                this.logger?.appendLine(`[${tag}] Server returned ${response.status}`);
                return null;
            }
            const data: T = await response.json();
            return data;
        } catch (error) {
            this.logError(tag, error);
            return null;
        }
    }

    private async get<T>(endpoint: string, tag: string): Promise<T | null> {
        const url = this.baseUrl + endpoint;
        try {
            this.logger?.appendLine(`[${tag}] Sending request to server...`);
            const response = await fetch(url, {
                signal: AbortSignal.timeout(30000),
            });
            if (!response.ok) {
                this.logger?.appendLine(`[${tag}] Server returned ${response.status}`);
                return null;
            }
            const data: T = await response.json();
            return data;
        } catch (error) {
            this.logError(tag, error);
            return null;
        }
    }

    private logError(tag: string, error: unknown): void {
        if (!this.logger) return;
        if (error instanceof DOMException && error.name === 'AbortError') {
            this.logger.appendLine(`[${tag}] Request timed out`);
        } else if (error instanceof TypeError && error.message.includes('fetch')) {
            this.logger.appendLine(`[${tag}] Server connection refused`);
        } else {
            this.logger.appendLine(`[${tag}] Error: ${error instanceof Error ? error.message : String(error)}`);
        }
    }
}
