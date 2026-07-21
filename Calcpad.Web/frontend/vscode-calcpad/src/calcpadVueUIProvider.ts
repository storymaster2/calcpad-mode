import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { parseHeadings, DEFAULT_PDF_SETTINGS } from 'calcpad-frontend';
import { CalcpadSettingsManager } from './calcpadSettings';
import { CalcpadInsertManager } from './calcpadInsertManager';

export class CalcpadVueUIProvider implements vscode.WebviewViewProvider {
    public static readonly viewType = 'calcpadVueUI';

    private _view?: vscode.WebviewView;
    private _outputChannel: vscode.OutputChannel;

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _context: vscode.ExtensionContext,
        private readonly _settingsManager: CalcpadSettingsManager,
        private readonly _insertManager: CalcpadInsertManager
    ) {
        this._outputChannel = vscode.window.createOutputChannel('CalcPad Vue');
        this._outputChannel.appendLine('CalcPad Vue UI Provider initialized');

        // Register callback to refresh UI when snippets are loaded from server
        this._insertManager.onSnippetsLoaded(() => {
            this._outputChannel.appendLine('Snippets loaded - refreshing Vue UI');
            this._sendInitialData();
        });
    }

    public resolveWebviewView(
        webviewView: vscode.WebviewView,
        context: vscode.WebviewViewResolveContext,
        _token: vscode.CancellationToken,
    ) {
        this._view = webviewView;
        this._outputChannel.appendLine('Resolving Vue webview view');

        webviewView.webview.options = {
            // Allow scripts in the webview
            enableScripts: true,
            localResourceRoots: [
                this._extensionUri
            ]
        };

        webviewView.webview.html = this._getHtmlForWebview(webviewView.webview);
        this._outputChannel.appendLine('Webview HTML set');

        // Handle messages from the webview
        webviewView.webview.onDidReceiveMessage(async (data) => {
            this._outputChannel.appendLine(`Received message: ${data.type}`);
            switch (data.type) {
                case 'insertText':
                    const insertEditor = vscode.window.activeTextEditor;
                    if (insertEditor) {
                        const position = insertEditor.selection.active;
                        await insertEditor.edit(editBuilder => {
                            editBuilder.insert(position, data.text);
                        });
                    }
                    break;

                case 'insertImage':
                    vscode.commands.executeCommand('vscode-calcpad.insertImage');
                    break;

                case 'getSettings':
                    const settings = this._settingsManager.getSettings();
                    const config = vscode.workspace.getConfiguration('calcpad');
                    const previewTheme = config.get<string>('previewTheme', 'system');
                    const commentFormat = config.get<string>('commentFormat', 'auto');
                    const enableFormattingHotkeys = config.get<boolean>('enableFormattingHotkeys', true);
                    const darkBackground = config.get<string>('darkBackground', '#1e1e1e');
                    const linterMinSeverity = config.get<string>('linter.minimumSeverity', 'information');
                    const libraryPath = config.get<string>('libraryPath', '');

                    const colorTheme = vscode.workspace.getConfiguration('workbench').get<string>('colorTheme', '');
                    const availableThemes = this._getInstalledThemes();

                    webviewView.webview.postMessage({
                        type: 'settingsResponse',
                        settings: settings,
                        previewTheme: previewTheme,
                        colorTheme: colorTheme,
                        availableThemes: availableThemes,
                        commentFormat: commentFormat,
                        enableFormattingHotkeys: enableFormattingHotkeys,
                        darkBackground: darkBackground,
                        linterMinSeverity: linterMinSeverity,
                        libraryPath: libraryPath
                    });
                    break;

                case 'updateSettings':
                    this._settingsManager.updateSettings(data.settings);
                    break;

                case 'resetSettings':
                    this._settingsManager.resetSettings();
                    const resetSettings = this._settingsManager.getSettings();
                    webviewView.webview.postMessage({
                        type: 'settingsReset',
                        settings: resetSettings
                    });
                    break;

                case 'updatePreviewTheme':
                    const previewConfig = vscode.workspace.getConfiguration('calcpad');
                    await previewConfig.update('previewTheme', data.theme, vscode.ConfigurationTarget.Global);
                    break;

                case 'updateColorTheme':
                    await vscode.workspace.getConfiguration('workbench').update('colorTheme', data.theme, vscode.ConfigurationTarget.Global);
                    break;

                case 'updateCommentFormat':
                    const commentFormatConfig = vscode.workspace.getConfiguration('calcpad');
                    await commentFormatConfig.update('commentFormat', data.format, vscode.ConfigurationTarget.Global);
                    break;

                case 'updateFormattingHotkeys':
                    const formattingHotkeysConfig = vscode.workspace.getConfiguration('calcpad');
                    await formattingHotkeysConfig.update('enableFormattingHotkeys', data.enabled, vscode.ConfigurationTarget.Global);
                    break;

                case 'updateDarkBackground':
                    const darkBgConfig = vscode.workspace.getConfiguration('calcpad');
                    await darkBgConfig.update('darkBackground', data.color, vscode.ConfigurationTarget.Global);
                    break;

                case 'updateLinterMinSeverity':
                    const linterConfig = vscode.workspace.getConfiguration('calcpad');
                    await linterConfig.update('linter.minimumSeverity', data.severity, vscode.ConfigurationTarget.Global);
                    break;

                case 'updateLibraryPath':
                    const libraryPathConfig = vscode.workspace.getConfiguration('calcpad');
                    await libraryPathConfig.update('libraryPath', data.path, vscode.ConfigurationTarget.Global);
                    break;


                case 'updatePdfSettings':
                    const pdfConfig = vscode.workspace.getConfiguration('calcpad');
                    for (const [key, value] of Object.entries(data.settings)) {
                        await pdfConfig.update(`pdf.${key}`, value, vscode.ConfigurationTarget.Global);
                    }
                    break;

                case 'resetPdfSettings':
                    const pdfConfigReset = vscode.workspace.getConfiguration('calcpad');
                    const pdfKeys = [
                        'format', 'marginTop', 'marginBottom', 'marginLeft', 'marginRight',
                        'documentTitle', 'dateTimeFormat'
                    ];

                    for (const key of pdfKeys) {
                        await pdfConfigReset.update(`pdf.${key}`, undefined, vscode.ConfigurationTarget.Global);
                    }

                    // Send back the reset settings
                    const resetPdfSettings = { ...DEFAULT_PDF_SETTINGS };

                    webviewView.webview.postMessage({
                        type: 'pdfSettingsReset',
                        settings: resetPdfSettings
                    });
                    break;

                case 'openS3Config':
                    vscode.commands.executeCommand('workbench.action.openSettings', 'calcpad.s3');
                    break;

                case 'openLogsFolder': {
                    // Resolve the same logs directory the server manager uses.
                    // Folder may not exist yet on a fresh install — create it so the
                    // OS file explorer has something to open instead of erroring.
                    const logsDir = path.join(this._context.extensionPath, 'bin', 'logs');
                    try { fs.mkdirSync(logsDir, { recursive: true }); } catch { /* best-effort */ }
                    try {
                        await vscode.env.openExternal(vscode.Uri.file(logsDir));
                        this._outputChannel.appendLine(`Opened logs folder: ${logsDir}`);
                    } catch (err) {
                        const msg = err instanceof Error ? err.message : String(err);
                        this._outputChannel.appendLine(`Failed to open logs folder: ${msg}`);
                        vscode.window.showErrorMessage(`Could not open logs folder: ${msg}`);
                    }
                    break;
                }

                case 'getS3Config':
                    const s3Config = vscode.workspace.getConfiguration('calcpad');
                    const s3ApiUrl = s3Config.get<string>('s3.apiUrl', '');
                    webviewView.webview.postMessage({
                        type: 's3ConfigResponse',
                        apiUrl: s3ApiUrl
                    });
                    break;

                case 'getPdfSettings':
                    const pdfConfigGet = vscode.workspace.getConfiguration('calcpad');
                    const pdfSettings = {
                        format: pdfConfigGet.get<string>('pdf.format', DEFAULT_PDF_SETTINGS.format),
                        marginTop: pdfConfigGet.get<string>('pdf.marginTop', DEFAULT_PDF_SETTINGS.marginTop),
                        marginBottom: pdfConfigGet.get<string>('pdf.marginBottom', DEFAULT_PDF_SETTINGS.marginBottom),
                        marginLeft: pdfConfigGet.get<string>('pdf.marginLeft', DEFAULT_PDF_SETTINGS.marginLeft),
                        marginRight: pdfConfigGet.get<string>('pdf.marginRight', DEFAULT_PDF_SETTINGS.marginRight),
                        documentTitle: pdfConfigGet.get<string>('pdf.documentTitle', DEFAULT_PDF_SETTINGS.documentTitle),
                        dateTimeFormat: pdfConfigGet.get<string>('pdf.dateTimeFormat', DEFAULT_PDF_SETTINGS.dateTimeFormat)
                    };

                    webviewView.webview.postMessage({
                        type: 'pdfSettingsResponse',
                        settings: pdfSettings
                    });
                    break;

                case 'generatePdf':
                    vscode.commands.executeCommand('vscode-calcpad.printToPdf');
                    break;

                case 'saveSourceHtml':
                    vscode.commands.executeCommand('vscode-calcpad.saveSourceHtml');
                    break;

                case 'saveDocx':
                    vscode.commands.executeCommand('vscode-calcpad.saveDocx');
                    break;

                case 'getInsertData':
                    this._sendInitialData();
                    break;

                case 'getVariables':
                    // Trigger a refresh of variables from the current document
                    const editor = vscode.window.activeTextEditor;
                    if (editor && (editor.document.languageId === 'calcpad' || editor.document.languageId === 'plaintext')) {
                        vscode.commands.executeCommand('calcpad.refreshVariables');
                    }
                    break;

                case 's3Login':
                    this.handleS3Login(data.credentials, webviewView.webview);
                    break;

                case 's3ListFiles':
                    this.handleS3ListFiles(data.token, webviewView.webview);
                    break;

                case 's3DownloadFile':
                    this.handleS3DownloadFile(data.fileName, data.token, webviewView.webview);
                    break;

                case 's3UploadFile':
                    this.handleS3UploadFile(data.fileName, data.fileData, data.tags, data.token, webviewView.webview);
                    break;

                case 'getHeadings':
                    {
                        const headingsEditor = vscode.window.activeTextEditor;
                        if (headingsEditor && (headingsEditor.document.languageId === 'calcpad' || headingsEditor.document.languageId === 'plaintext')) {
                            const text = headingsEditor.document.getText();
                            const headings = parseHeadings(text);
                            webviewView.webview.postMessage({
                                type: 'updateHeadings',
                                headings
                            });
                        } else {
                            webviewView.webview.postMessage({
                                type: 'updateHeadings',
                                headings: []
                            });
                        }
                    }
                    break;

                case 'goToLine':
                    {
                        const goToEditor = vscode.window.activeTextEditor;
                        if (goToEditor && typeof data.line === 'number') {
                            const lineIndex = Math.max(0, data.line - 1);
                            const lineEnd = goToEditor.document.lineAt(lineIndex).range.end;
                            goToEditor.selection = new vscode.Selection(lineEnd, lineEnd);
                            goToEditor.revealRange(goToEditor.document.lineAt(lineIndex).range, vscode.TextEditorRevealType.InCenter);
                            vscode.window.showTextDocument(goToEditor.document, goToEditor.viewColumn);
                        }
                    }
                    break;

                case 'refreshDocument':
                    this._outputChannel.appendLine('[Vue UI] Refresh document requested');
                    vscode.commands.executeCommand('calcpad.refreshDocument');
                    break;

                case 'prettifyDocument':
                    this._outputChannel.appendLine('[Vue UI] Prettify document requested');
                    vscode.commands.executeCommand('vscode-calcpad.prettifyDocument');
                    break;

                case 'getPrettifySettings': {
                    const cfg = vscode.workspace.getConfiguration('calcpad');
                    webviewView.webview.postMessage({
                        type: 'prettifySettingsResponse',
                        indentStyle: cfg.get<string>('prettify.indentStyle', 'tab'),
                        indentSize: cfg.get<number>('prettify.indentSize', 4),
                        trimTrailingWhitespace: cfg.get<boolean>('prettify.trimTrailingWhitespace', true)
                    });
                    break;
                }

                case 'updatePrettifyIndentStyle': {
                    const cfg = vscode.workspace.getConfiguration('calcpad');
                    await cfg.update('prettify.indentStyle', data.value, vscode.ConfigurationTarget.Global);
                    break;
                }

                case 'updatePrettifyIndentSize': {
                    const cfg = vscode.workspace.getConfiguration('calcpad');
                    await cfg.update('prettify.indentSize', data.value, vscode.ConfigurationTarget.Global);
                    break;
                }

                case 'updatePrettifyTrim': {
                    const cfg = vscode.workspace.getConfiguration('calcpad');
                    await cfg.update('prettify.trimTrailingWhitespace', data.value, vscode.ConfigurationTarget.Global);
                    break;
                }

                case 'getExports':
                    await this._sendExports(webviewView.webview);
                    break;

                case 'downloadExport':
                    await this._downloadExportToWorkspace(data.filename);
                    break;

                case 'downloadExportZip':
                    await this._downloadExportZipToWorkspace();
                    break;

                case 'debug':
                    this._outputChannel.appendLine(`[Vue Debug] ${data.message}`);
                    break;
            }
        });

        // Send initial data
        this._sendInitialData();

        // Refresh headings when the user switches editor tabs
        vscode.window.onDidChangeActiveTextEditor(() => {
            this._sendHeadings();
        });

        // Debounced refresh of headings when the document content changes
        let tocTimer: ReturnType<typeof setTimeout> | undefined;
        vscode.workspace.onDidChangeTextDocument((e) => {
            const editor = vscode.window.activeTextEditor;
            if (editor && e.document === editor.document) {
                if (tocTimer) clearTimeout(tocTimer);
                tocTimer = setTimeout(() => this._sendHeadings(), 800);
            }
        });
    }

    private _getInstalledThemes(): Array<{ label: string; id: string; kind: 'dark' | 'light' }> {
        const themes: Array<{ label: string; id: string; kind: 'dark' | 'light' }> = [];
        for (const ext of vscode.extensions.all) {
            const contributed = ext.packageJSON?.contributes?.themes;
            if (!Array.isArray(contributed)) continue;
            for (const t of contributed) {
                if (!t?.label) continue;
                const uiTheme: string = t.uiTheme ?? 'vs-dark';
                const kind: 'dark' | 'light' = uiTheme === 'vs' || uiTheme === 'hc-light' ? 'light' : 'dark';
                themes.push({ label: t.label, id: t.id ?? t.label, kind });
            }
        }
        themes.sort((a, b) => a.label.localeCompare(b.label));
        return themes;
    }

    private async _sendInitialData() {
        if (!this._view) return;

        // Ensure snippets are loaded
        if (!this._insertManager.isLoaded()) {
            try {
                await this._insertManager.loadSnippets();
            } catch (error) {
                this._outputChannel.appendLine('[Vue UI] Failed to load snippets: ' + error);
            }
        }

        // Send insert items as flat array
        const insertItems = this._insertManager.getAllItems();
        this._outputChannel.appendLine('Sending ' + insertItems.length + ' insert items');
        this._view.webview.postMessage({
            type: 'insertDataResponse',
            items: insertItems
        });
    }

    private _sendHeadings() {
        if (!this._view) return;

        const editor = vscode.window.activeTextEditor;
        if (editor && (editor.document.languageId === 'calcpad' || editor.document.languageId === 'plaintext')) {
            const headings = parseHeadings(editor.document.getText());
            this._view.webview.postMessage({ type: 'updateHeadings', headings });
        } else {
            this._view.webview.postMessage({ type: 'updateHeadings', headings: [] });
        }
    }

    private _activeSourceFilePath(): string | undefined {
        const editor = vscode.window.activeTextEditor;
        if (!editor) return undefined;
        if (editor.document.isUntitled) return undefined;
        return editor.document.uri.fsPath;
    }

    private async _sendExports(webview: vscode.Webview): Promise<void> {
        const apiBaseUrl = this._settingsManager.getServerUrl();
        const sourceFilePath = this._activeSourceFilePath();
        try {
            const url = `${apiBaseUrl}/api/calcpad/exports${sourceFilePath ? '?sourceFilePath=' + encodeURIComponent(sourceFilePath) : ''}`;
            const res = await fetch(url, { signal: AbortSignal.timeout(10000) });
            const exports = res.ok ? await res.json() : [];
            webview.postMessage({ type: 'exportsResponse', exports });
        } catch (err) {
            this._outputChannel.appendLine(`[Vue UI] Failed to list exports: ${err instanceof Error ? err.message : String(err)}`);
            webview.postMessage({ type: 'exportsResponse', exports: [] });
        }
    }

    private async _downloadExportToWorkspace(filename: string): Promise<void> {
        if (!filename) return;
        const apiBaseUrl = this._settingsManager.getServerUrl();
        const sourceFilePath = this._activeSourceFilePath();
        const params = new URLSearchParams();
        if (sourceFilePath) params.set('sourceFilePath', sourceFilePath);
        params.set('filename', filename);

        try {
            const res = await fetch(`${apiBaseUrl}/api/calcpad/export?${params.toString()}`, { signal: AbortSignal.timeout(60000) });
            if (!res.ok) {
                vscode.window.showErrorMessage(`Failed to download ${filename}: ${res.status}`);
                return;
            }
            const buf = Buffer.from(await res.arrayBuffer());
            const target = await vscode.window.showSaveDialog({
                defaultUri: vscode.Uri.file(sourceFilePath ? path.resolve(path.dirname(sourceFilePath), filename) : filename),
                saveLabel: 'Save export'
            });
            if (!target) return;
            await vscode.workspace.fs.writeFile(target, buf);
            vscode.window.showInformationMessage(`Saved ${filename}`);
        } catch (err) {
            vscode.window.showErrorMessage(`Download failed: ${err instanceof Error ? err.message : String(err)}`);
        }
    }

    private async _downloadExportZipToWorkspace(): Promise<void> {
        const apiBaseUrl = this._settingsManager.getServerUrl();
        const sourceFilePath = this._activeSourceFilePath();
        try {
            const res = await fetch(
                `${apiBaseUrl}/api/calcpad/exports.zip${sourceFilePath ? '?sourceFilePath=' + encodeURIComponent(sourceFilePath) : ''}`,
                { signal: AbortSignal.timeout(60000) }
            );
            if (!res.ok) {
                vscode.window.showErrorMessage(`Failed to download exports: ${res.status}`);
                return;
            }
            const buf = Buffer.from(await res.arrayBuffer());
            const target = await vscode.window.showSaveDialog({
                defaultUri: vscode.Uri.file(sourceFilePath ? path.resolve(path.dirname(sourceFilePath), 'calcpad-exports.zip') : 'calcpad-exports.zip'),
                saveLabel: 'Save exports ZIP',
                filters: { 'ZIP archives': ['zip'] }
            });
            if (!target) return;
            await vscode.workspace.fs.writeFile(target, buf);
            vscode.window.showInformationMessage('Saved calcpad-exports.zip');
        } catch (err) {
            vscode.window.showErrorMessage(`Download failed: ${err instanceof Error ? err.message : String(err)}`);
        }
    }

    public updateVariables(data: { macros: any[], variables: any[], functions: any[], customUnits: any[] }) {
        if (this._view) {
            this._outputChannel.appendLine(`Updating variables: ${data.macros.length} macros, ${data.variables.length} variables, ${data.functions.length} functions, ${data.customUnits.length} custom units`);
            this._view.webview.postMessage({
                type: 'updateVariables',
                data: data
            });
        }
    }

    public dispose() {
        this._outputChannel.dispose();
    }

    private async handleS3Login(credentials: { username: string, password: string }, webview: vscode.Webview) {
        try {
            const config = vscode.workspace.getConfiguration('calcpad');
            const apiUrl = config.get<string>('s3.apiUrl', 'http://localhost:5000');

            this._outputChannel.appendLine(`[S3] Attempting login to: ${apiUrl}/api/auth/login`);
            this._outputChannel.appendLine(`[S3] Username: ${credentials.username}`);

            const response = await fetch(`${apiUrl}/api/auth/login`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(credentials)
            });

            this._outputChannel.appendLine(`[S3] Login response status: ${response.status}`);

            if (!response.ok) {
                const errorBody = await response.text();
                throw new Error(errorBody || `HTTP ${response.status}`);
            }

            const data = await response.json();
            this._outputChannel.appendLine(`[S3] Login response data: ${JSON.stringify(data, null, 2)}`);

            const jwt = data.token;
            this._outputChannel.appendLine(`[S3] Extracted JWT: ${jwt ? `${jwt.substring(0, 20)}...` : 'EMPTY'}`);

            webview.postMessage({
                type: 's3LoginResponse',
                success: true,
                token: data.token,
                user: data.user
            });
        } catch (error: unknown) {
            this._outputChannel.appendLine(`[S3] Login error: ${error}`);
            if (error instanceof Error) {
                this._outputChannel.appendLine(`[S3] Login error message: ${error.message}`);
                this._outputChannel.appendLine(`[S3] Login error stack: ${error.stack}`);
            }
            webview.postMessage({
                type: 's3LoginResponse',
                success: false,
                error: 'Connection error. Make sure the S3 API is running.'
            });
        }
    }

    private async handleS3ListFiles(token: string, webview: vscode.Webview) {
        try {
            const config = vscode.workspace.getConfiguration('calcpad');
            const apiUrl = config.get<string>('s3.apiUrl', 'http://localhost:5000');

            this._outputChannel.appendLine(`[S3] Requesting file list from: ${apiUrl}/api/blobstorage/list-with-metadata`);
            this._outputChannel.appendLine(`[S3] Using token: ${token ? `${token.substring(0, 20)}...` : 'EMPTY'}`);

            const response = await fetch(`${apiUrl}/api/blobstorage/list-with-metadata`, {
                headers: { 'Authorization': `Bearer ${token}` }
            });

            this._outputChannel.appendLine(`[S3] Response status: ${response.status}`);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const data = await response.json();
            this._outputChannel.appendLine(`[S3] Response data: ${JSON.stringify(data, null, 2)}`);

            const files = data.files || data || [];
            this._outputChannel.appendLine(`[S3] Extracted files array: ${JSON.stringify(files, null, 2)}`);
            this._outputChannel.appendLine(`[S3] Number of files found: ${Array.isArray(files) ? files.length : 'Not an array'}`);

            webview.postMessage({
                type: 's3FilesResponse',
                success: true,
                files: files
            });
        } catch (error: unknown) {
            this._outputChannel.appendLine(`[S3] List Files error: ${error}`);
            if (error instanceof Error) {
                this._outputChannel.appendLine(`[S3] Error message: ${error.message}`);
                this._outputChannel.appendLine(`[S3] Error stack: ${error.stack}`);
            }
            webview.postMessage({
                type: 's3FilesResponse',
                success: false,
                error: 'Failed to connect to S3 API'
            });
        }
    }

    private async handleS3DownloadFile(fileName: string, token: string, webview: vscode.Webview) {
        try {
            const config = vscode.workspace.getConfiguration('calcpad');
            const apiUrl = config.get<string>('s3.apiUrl', 'http://localhost:5000');

            this._outputChannel.appendLine(`[S3] Downloading file: ${fileName}`);
            this._outputChannel.appendLine(`[S3] Download URL: ${apiUrl}/api/blobstorage/download/${fileName}`);

            const response = await fetch(`${apiUrl}/api/blobstorage/download/${fileName}`, {
                headers: { 'Authorization': `Bearer ${token}` }
            });

            this._outputChannel.appendLine(`[S3] Download response status: ${response.status}`);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const arrayBuffer = await response.arrayBuffer();
            this._outputChannel.appendLine(`[S3] Download response size: ${arrayBuffer.byteLength} bytes`);

            const base64 = Buffer.from(arrayBuffer).toString('base64');

            webview.postMessage({
                type: 's3DownloadResponse',
                success: true,
                fileName: fileName,
                fileData: `data:application/octet-stream;base64,${base64}`
            });
        } catch (error: unknown) {
            this._outputChannel.appendLine(`[S3] Download error: ${error}`);
            if (error instanceof Error) {
                this._outputChannel.appendLine(`[S3] Download error message: ${error.message}`);
            }
            webview.postMessage({
                type: 's3DownloadResponse',
                success: false,
                error: 'Download failed'
            });
        }
    }

    private async handleS3UploadFile(fileName: string, fileData: string, tags: string[], token: string, webview: vscode.Webview) {
        try {
            const config = vscode.workspace.getConfiguration('calcpad');
            const apiUrl = config.get<string>('s3.apiUrl', 'http://localhost:5000');

            this._outputChannel.appendLine(`[S3] Uploading file: ${fileName}`);
            this._outputChannel.appendLine(`[S3] Upload URL: ${apiUrl}/api/blobstorage/upload`);
            this._outputChannel.appendLine(`[S3] Tags: ${JSON.stringify(tags)}`);

            // Convert base64 data URL to buffer
            const base64Data = fileData.split(',')[1];
            const buffer = Buffer.from(base64Data, 'base64');

            this._outputChannel.appendLine(`[S3] File size: ${buffer.length} bytes`);

            // Use native FormData (available in Node.js 18+)
            const formData = new FormData();

            // Create a Blob for the file
            const fileBlob = new Blob([buffer], { type: 'application/octet-stream' });
            formData.append('file', fileBlob, fileName);

            if (tags.length > 0) {
                formData.append('tags', JSON.stringify(tags));
            }

            const response = await fetch(`${apiUrl}/api/blobstorage/upload`, {
                method: 'POST',
                headers: { 'Authorization': `Bearer ${token}` },
                body: formData
            });

            this._outputChannel.appendLine(`[S3] Upload response status: ${response.status}`);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            this._outputChannel.appendLine(`[S3] Upload successful for: ${fileName}`);

            webview.postMessage({
                type: 's3UploadResponse',
                success: true
            });
        } catch (error: unknown) {
            this._outputChannel.appendLine(`[S3] Upload error: ${error}`);
            if (error instanceof Error) {
                this._outputChannel.appendLine(`[S3] Upload error message: ${error.message}`);
            }
            webview.postMessage({
                type: 's3UploadResponse',
                success: false,
                error: 'Upload failed'
            });
        }
    }

    private _getHtmlForWebview(webview: vscode.Webview) {
        // Get the local path to main script run in the webview, then convert it to a uri we can use in the webview.
        const scriptUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'out', 'CalcpadVuePanel', 'main.js'));
        const styleUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'out', 'CalcpadVuePanel', 'main.css'));

        this._outputChannel.appendLine(`Script URI: ${scriptUri.toString()}`);
        this._outputChannel.appendLine(`Style URI: ${styleUri.toString()}`);

        // Use a nonce to only allow a specific script to be run.
        const nonce = getNonce();

        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}';">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <link href="${styleUri}" rel="stylesheet">
    <title>CalcPad Vue UI</title>
</head>
<body>
    <div id="app">
        <div style="padding: 20px; text-align: center; color: #666; font-size: 12px;">
            Loading Vue.js CalcPad UI...
            <br><small>If this message persists, check the developer console for errors</small>
        </div>
    </div>
    <script nonce="${nonce}" src="${scriptUri}"></script>
</body>
</html>`;
    }
}

function getNonce() {
    let text = '';
    const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    for (let i = 0; i < 32; i++) {
        text += possible.charAt(Math.floor(Math.random() * possible.length));
    }
    return text;
}