import * as vscode from 'vscode';
import * as path from 'path';
import { CalcpadApiClient, buildClientFileCacheFromContent, DEFAULT_PDF_SETTINGS, getDatagridCdnTags, htmlHasDatagrids, getUiEventScript, updateUiOverridesInContent } from 'calcpad-frontend';
import type { PdfSettings as FrontendPdfSettings } from 'calcpad-frontend';
import { CalcpadServerLinter } from './calcpadServerLinter';
import { CalcpadSemanticTokensProvider, semanticTokensLegend } from './calcpadSemanticTokensProvider';
import { CalcpadVueUIProvider } from './calcpadVueUIProvider';
import { CalcpadSettingsManager } from './calcpadSettings';
import { OperatorReplacer } from './operatorReplacer';
import { QuickTyper } from './quickTyper';
import { CalcpadCompletionProvider } from './calcpadCompletionProvider';
import { CalcpadIncludeCompletionProvider } from './calcpadIncludeCompletionProvider';
import { CalcpadInsertManager } from './calcpadInsertManager';
import { CalcpadDefinitionsService } from './calcpadDefinitionsService';
import { AutoIndenter } from './autoIndenter';
import { ImageInserter } from './imageInserter';
import { CalcpadDefinitionProvider } from './calcpadDefinitionProvider';
import { CalcpadReferenceProvider } from './calcpadReferenceProvider';
import { CalcpadRenameProvider } from './calcpadRenameProvider';
import { CalcpadHoverProvider } from './calcpadHoverProvider';
import { CommentFormatter } from './commentFormatter';
import { CalcpadServerManager } from './calcpadServerManager';
import { DotnetRuntimeManager } from './dotnetRuntimeManager';
import { VSCodeLogger, VSCodeFileSystem } from './adapters';
import { UiInputModel } from './uiInputModel';

let activePreviewPanel: vscode.WebviewPanel | unknown = undefined;
let activePreviewType: 'regular' | 'unwrapped' | 'ui' | undefined = undefined;
let uiInputModel: UiInputModel | null = null;
let uiOverridesCommentLine: number | undefined = undefined;
let previewUpdateTimeout: NodeJS.Timeout | unknown = undefined;
let previewSourceEditor: vscode.TextEditor | undefined = undefined;
let linter: CalcpadServerLinter;
let definitionsService: CalcpadDefinitionsService;
let serverManager: CalcpadServerManager | undefined;
let outputChannel: vscode.OutputChannel;
let calcpadOutputHtmlChannel: vscode.OutputChannel;
let calcpadWebviewConsoleChannel: vscode.OutputChannel;
let extensionContext: vscode.ExtensionContext;

// Extends the shared PdfSettings with additional server-side fields
interface FullPdfSettings extends FrontendPdfSettings {
    enableHeader: boolean;
    documentSubtitle: string;
    headerCenter: string;
    author: string;
    enableFooter: boolean;
    footerCenter: string;
    company: string;
    project: string;
    showPageNumbers: boolean;
    orientation: string;
    printBackground: boolean;
    scale: number;
    headerTemplate: string;
    footerTemplate: string;
    backgroundSvgPath: string;
}


function getPdfSettings(): FullPdfSettings {
    const config = vscode.workspace.getConfiguration('calcpad');
    const activeEditor = vscode.window.activeTextEditor;

    const fileName = activeEditor
        ? path.basename(activeEditor.document.fileName, path.extname(activeEditor.document.fileName))
        : 'CalcPad Document';

    return {
        // User-configurable settings (defaults from shared module)
        format: config.get<string>('pdf.format', DEFAULT_PDF_SETTINGS.format),
        marginTop: config.get<string>('pdf.marginTop', DEFAULT_PDF_SETTINGS.marginTop),
        marginBottom: config.get<string>('pdf.marginBottom', DEFAULT_PDF_SETTINGS.marginBottom),
        marginLeft: config.get<string>('pdf.marginLeft', DEFAULT_PDF_SETTINGS.marginLeft),
        marginRight: config.get<string>('pdf.marginRight', DEFAULT_PDF_SETTINGS.marginRight),
        documentTitle: config.get<string>('pdf.documentTitle') || fileName,
        dateTimeFormat: config.get<string>('pdf.dateTimeFormat', DEFAULT_PDF_SETTINGS.dateTimeFormat),

        // Hardcoded defaults (to be re-exposed in UI later)
        enableHeader: true,
        documentSubtitle: '',
        headerCenter: '',
        author: '',
        enableFooter: true,
        footerCenter: '',
        company: '',
        project: '',
        showPageNumbers: true,
        orientation: 'portrait',
        printBackground: true,
        scale: 1.0,
        headerTemplate: 'default',
        footerTemplate: 'default',
        backgroundSvgPath: ''
    };
}

function getEffectivePreviewTheme(): 'light' | 'dark' {
    const config = vscode.workspace.getConfiguration('calcpad');
    const previewTheme = config.get<string>('previewTheme', 'system');

    if (previewTheme === 'light') {
        return 'light';
    } else if (previewTheme === 'dark') {
        return 'dark';
    } else {
        // System - follow VS Code theme
        const colorTheme = vscode.window.activeColorTheme;
        return colorTheme.kind === vscode.ColorThemeKind.Dark ||
               colorTheme.kind === vscode.ColorThemeKind.HighContrast ? 'dark' : 'light';
    }
}

const IMAGE_MIME_MAP: Record<string, string> = {
    'png': 'image/png',
    'jpg': 'image/jpeg',
    'jpeg': 'image/jpeg',
    'gif': 'image/gif',
    'webp': 'image/webp',
    'svg': 'image/svg+xml'
};

/**
 * Scan HTML for <img src="..."> tags with local file paths, read the files from disk,
 * and return a cache mapping original src values to base64 data URIs.
 */
async function buildImageCache(html: string, documentDir: string): Promise<Record<string, string>> {
    const cache: Record<string, string> = {};
    const imgSrcRegex = /<img\s[^>]*?src\s*=\s*["']([^"']+)["'][^>]*>/gi;
    let match;

    while ((match = imgSrcRegex.exec(html)) !== null) {
        const src = match[1];

        // Skip data URIs and remote URLs
        if (src.startsWith('data:') || src.startsWith('http://') || src.startsWith('https://')) {
            continue;
        }

        // Skip if already cached (same src used multiple times)
        if (cache[src]) {
            continue;
        }

        try {
            const absolutePath = path.resolve(documentDir, src);
            const ext = path.extname(absolutePath).toLowerCase().replace('.', '');
            const mimeType = IMAGE_MIME_MAP[ext];

            if (!mimeType) {
                outputChannel.appendLine(`[IMAGE CACHE] Skipping unsupported image type: ${src}`);
                continue;
            }

            const fileUri = vscode.Uri.file(absolutePath);
            const imageData = await vscode.workspace.fs.readFile(fileUri);
            const b64 = Buffer.from(imageData).toString('base64');
            cache[src] = `data:${mimeType};base64,${b64}`;

            outputChannel.appendLine(`[IMAGE CACHE] Cached: ${src} (${imageData.length} bytes)`);
        } catch (error) {
            outputChannel.appendLine(`[IMAGE CACHE] Could not read image: ${src} (${error instanceof Error ? error.message : 'Unknown error'})`);
        }
    }

    return cache;
}

/**
 * Directly replace local image src attributes in the HTML string with
 * cached base64 data URIs. Unlike getImageCacheScript (which injects a
 * <script> for webview runtime replacement), this performs a static
 * string replacement suitable for headless PDF rendering.
 */
function applyImageCache(html: string, imageCache: Record<string, string>): string {
    if (Object.keys(imageCache).length === 0) {
        return html;
    }

    return html.replace(/<img\s([^>]*?)src\s*=\s*["']([^"']+)["']([^>]*?)>/gi,
        (match, before, src, after) => {
            if (imageCache[src]) {
                return `<img ${before}src="${imageCache[src]}"${after}>`;
            }
            return match;
        });
}

/**
 * Generate a <script> block that replaces local image src attributes
 * with cached base64 data URIs on DOMContentLoaded.
 */
function getImageCacheScript(imageCache: Record<string, string>): string {
    if (Object.keys(imageCache).length === 0) {
        return '';
    }

    const cacheJson = JSON.stringify(imageCache);
    return `
        <script>
            (function() {
                const imageCache = ${cacheJson};
                document.addEventListener('DOMContentLoaded', function() {
                    const images = document.querySelectorAll('img');
                    images.forEach(function(img) {
                        const src = img.getAttribute('src');
                        if (src && imageCache[src]) {
                            img.src = imageCache[src];
                        }
                    });
                });
            })();
        </script>
    `;
}

/**
 * Generate a <script> that strips VS Code's auto-injected theme from the webview.
 * VS Code injects 400+ --vscode-* CSS variables on <html> and sets body classes
 * (vscode-dark/vscode-light) which override the server-generated theme CSS.
 * There is no API to disable this: https://github.com/microsoft/vscode/issues/209253
 * VS Code only re-injects on VS Code theme change, not continuously,
 * so stripping at DOMContentLoaded is stable.
 */
function getThemeOverrideScript(previewTheme: 'light' | 'dark'): string {
    const bodyClass = previewTheme === 'light' ? 'vscode-light' : 'vscode-dark';
    const themeKind = previewTheme === 'light' ? 'vscode-light' : 'vscode-dark';
    const config = vscode.workspace.getConfiguration('calcpad');
    const darkBg = config.get<string>('darkBackground', '#1e1e1e');
    const bg = previewTheme === 'light' ? '#ffffff' : darkBg;
    return `
        <script>
            (function() {
                // Remove VS Code's injected inline styles (--vscode-* variables) from <html>
                document.documentElement.removeAttribute('style');
                // Set explicit background to prevent the webview container's grey from showing through
                document.documentElement.style.backgroundColor = '${bg}';

                // Set body classes to match the selected preview theme
                document.body.classList.remove('vscode-light', 'vscode-dark', 'vscode-high-contrast');
                document.body.classList.add('${bodyClass}');
                document.body.setAttribute('data-vscode-theme-kind', '${themeKind}');
                document.body.style.backgroundColor = '${bg}';
            })();
        </script>
    `;
}

// Escape stray '<' that aren't part of complete HTML tags to prevent
// malformed user content (e.g. '<h' from Calcpad notes) from breaking the DOM.
// A complete tag is <...> where the content between < and > contains no nested <.
function sanitizeServerHtml(html: string): string {
    const bodyOpen = html.indexOf('<body');
    const bodyClose = html.lastIndexOf('</body>');
    if (bodyOpen === -1 || bodyClose === -1) return html;

    const bodyStart = html.indexOf('>', bodyOpen) + 1;
    const body = html.substring(bodyStart, bodyClose);
    const sanitized = body.replace(/(<!--[\s\S]*?-->)|(<script[\s\S]*?<\/script>)|(<style[\s\S]*?<\/style>)|(<\/?[a-zA-Z][^<>]*>)|(<)/g,
        (_match, comment, script, style, tag) => comment ?? script ?? style ?? tag ?? '&lt;');

    return html.substring(0, bodyStart) + sanitized + html.substring(bodyClose);
}

function getVsCodeApiInitScript(): string {
    return `<script>const vscode = acquireVsCodeApi();</script>`;
}

function getErrorNavigationScript(): string {
    return `
        <script>

            // Intercept console methods and send to VS Code
            (function() {
                const originalConsole = {
                    log: console.log,
                    warn: console.warn,
                    error: console.error,
                    info: console.info,
                    debug: console.debug
                };

                function sendConsoleMessage(level, args) {
                    const message = Array.from(args).map(arg => {
                        if (typeof arg === 'object') {
                            try {
                                return JSON.stringify(arg, null, 2);
                            } catch (e) {
                                return String(arg);
                            }
                        }
                        return String(arg);
                    }).join(' ');

                    vscode.postMessage({
                        type: 'consoleMessage',
                        level: level,
                        message: message
                    });
                }

                console.log = function() {
                    originalConsole.log.apply(console, arguments);
                    sendConsoleMessage('log', arguments);
                };

                console.warn = function() {
                    originalConsole.warn.apply(console, arguments);
                    sendConsoleMessage('warn', arguments);
                };

                console.error = function() {
                    originalConsole.error.apply(console, arguments);
                    sendConsoleMessage('error', arguments);
                };

                console.info = function() {
                    originalConsole.info.apply(console, arguments);
                    sendConsoleMessage('info', arguments);
                };

                console.debug = function() {
                    originalConsole.debug.apply(console, arguments);
                    sendConsoleMessage('debug', arguments);
                };
            })();

            // Catch uncaught errors and unhandled promise rejections
            window.onerror = function(message, source, lineno, colno, error) {
                const detail = error ? (error.message || String(error)) : message;
                vscode.postMessage({ type: 'consoleMessage', level: 'error', message: '[Uncaught] ' + detail + ' (' + lineno + ':' + colno + ')' });
            };
            window.onunhandledrejection = function(event) {
                const reason = event.reason;
                const detail = reason ? (reason.message || String(reason)) : String(reason);
                vscode.postMessage({ type: 'consoleMessage', level: 'error', message: '[Unhandled Rejection] ' + detail });
            };

            // Test console interception
            console.log('CalcPad webview console interception initialized');

            // Handle error link clicks
            document.addEventListener('DOMContentLoaded', function() {
                // Find all error links with data-text attributes
                const errorLinks = document.querySelectorAll('a[data-text]');

                errorLinks.forEach(link => {
                    link.addEventListener('click', function(e) {
                        e.preventDefault();
                        const lineNumber = this.getAttribute('data-text');
                        if (lineNumber) {
                            vscode.postMessage({
                                type: 'navigateToLine',
                                line: parseInt(lineNumber, 10)
                            });
                        }
                    });
                });
            });
        </script>
    `;
}

async function updatePreviewContent(panel: vscode.WebviewPanel, content: string, sourceFileUri: vscode.Uri, unwrapped: boolean = false) {
    const mode = unwrapped ? 'unwrapped' : 'wrapped';
    outputChannel.appendLine(`Starting updatePreviewContent (${mode})...`);
    outputChannel.appendLine(`Content length: ${content.length} characters`);

    // Update panel title with current file name
    const activeEditor = vscode.window.activeTextEditor;
    if (activeEditor) {
        const fileName = activeEditor.document.fileName.split('/').pop() || 'CalcPad';
        panel.title = unwrapped ? `CalcPad Preview Unwrapped - ${fileName}` : `CalcPad Preview - ${fileName}`;
    }

    // Check if content is empty
    if (!content || content.trim().length === 0) {
        outputChannel.appendLine('Content is empty - showing empty state');
        panel.webview.html = `
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <title>CalcPad Preview${unwrapped ? ' Unwrapped' : ''}</title>
                <style>
                    body { color: #858585; background: var(--vscode-editor-background); padding: 20px; font-family: var(--vscode-font-family); }
                    h3 { text-align: center; }
                    p { text-align: center; }
                    table { margin: 1em auto; border-collapse: collapse; text-align: left; font-size: 0.9em; }
                    th, td { padding: 4px 12px; }
                    th { text-align: right; font-weight: normal; opacity: 0.7; }
                    td { font-family: var(--vscode-editor-font-family, monospace); }
                    h4 { text-align: center; margin-top: 1.5em; margin-bottom: 0.3em; }
                </style>
            </head>
            <body>
                <h3>Empty Document</h3>
                <p>Start typing CalcPad code to see the preview.</p>
                <h4>Formatting Hotkeys</h4>
                <table>
                    <tr><th>Bold</th><td>Ctrl+B</td></tr>
                    <tr><th>Italic</th><td>Ctrl+I</td></tr>
                    <tr><th>Underline</th><td>Ctrl+U</td></tr>
                    <tr><th>Subscript</th><td>Ctrl+=</td></tr>
                    <tr><th>Superscript</th><td>Ctrl+Shift+=</td></tr>
                    <tr><th>Heading 1-6</th><td>Ctrl+1 ... Ctrl+6</td></tr>
                    <tr><th>Paragraph</th><td>Ctrl+L</td></tr>
                    <tr><th>Line Break</th><td>Ctrl+R</td></tr>
                    <tr><th>Bulleted List</th><td>Ctrl+Shift+L</td></tr>
                    <tr><th>Numbered List</th><td>Ctrl+Shift+N</td></tr>
                    <tr><th>Toggle Comment</th><td>Ctrl+Q</td></tr>
                </table>
                <h4>Resources</h4>
                <p><a href="https://github.com/imartincei/CalcpadCE">CalcpadCE on GitHub</a></p>
                <p><a href="https://calcpad-ce.org/">calcpad-ce.org</a></p>
                <p><a href="https://imartincei.github.io/CalcpadCE/">CalcpadCE Documentation</a></p>
            </body>
            </html>
        `;
        return;
    }

    try {
        outputChannel.appendLine('Getting settings...');
        const settingsManager = CalcpadSettingsManager.getInstance(extensionContext);

        const settings = await settingsManager.getApiSettings();
        const apiBaseUrl = settingsManager.getServerUrl();

        if (!apiBaseUrl) {
            outputChannel.appendLine('ERROR: Server URL not configured');
            throw new Error('Server URL not configured');
        }
        outputChannel.appendLine(`Server URL: ${apiBaseUrl}`);
        outputChannel.appendLine(`Settings retrieved: ${JSON.stringify(settings)}`);

        // Build client file cache for referenced files
        const vsFileSystem = new VSCodeFileSystem();
        const vsLogger = new VSCodeLogger(outputChannel);
        const sourceDir = path.dirname(sourceFileUri.fsPath);
        const clientFileCache = await buildClientFileCacheFromContent(content, sourceDir, vsFileSystem, vsLogger, '[Convert]');

        // Select API endpoint based on unwrapped parameter
        const endpoint = unwrapped ? '/api/calcpad/convert-unwrapped' : '/api/calcpad/convert';
        outputChannel.appendLine(`Making API call to ${endpoint}...`);

        const theme = getEffectivePreviewTheme();
        const response = await fetch(`${apiBaseUrl}${endpoint}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                content: content,
                settings: settings,
                theme: theme,
                forceUnwrappedCode: unwrapped,
                clientFileCache: clientFileCache,
                sourceFilePath: sourceFileUri.fsPath
            }),
            signal: AbortSignal.timeout(10000)
        });
        if (!response.ok) {
            throw new Error(`Server returned ${response.status}`);
        }
        outputChannel.appendLine('API call successful');

        // Use the entire API response as the webview HTML
        const apiResponse = await response.text();

        // Log to dedicated HTML output channel (without stealing focus)
        calcpadOutputHtmlChannel.clear();
        calcpadOutputHtmlChannel.appendLine(apiResponse);

        outputChannel.appendLine(`HTML Length: ${apiResponse.length} characters`);

        // Build image cache: read local image files and convert to base64 data URIs
        let imageCacheScript = '';
        if (activeEditor && !activeEditor.document.isUntitled) {
            const documentDir = path.dirname(activeEditor.document.uri.fsPath);
            const imageCache = await buildImageCache(apiResponse, documentDir);
            imageCacheScript = getImageCacheScript(imageCache);
        }

        // Inject JavaScript for error link navigation and console interception
        const errorNavigationScript = getErrorNavigationScript();

        // Override VS Code's injected theme to match the selected preview theme
        const themeOverrideScript = getThemeOverrideScript(theme);

        // Sanitize server HTML to escape stray '<' that aren't part of valid tags
        const sanitizedResponse = sanitizeServerHtml(apiResponse);

        // Inject console interception + error nav at end of <head> so it runs before any user scripts in body
        const vsCodeApiInit = getVsCodeApiInitScript();
        let htmlWithScript = sanitizedResponse.replace('</head>', vsCodeApiInit + errorNavigationScript + '</head>');
        // Inject image cache + theme override before closing body tag
        htmlWithScript = htmlWithScript.replace('</body>', imageCacheScript + themeOverrideScript + '</body>');

        panel.webview.html = htmlWithScript;

        outputChannel.appendLine('Webview HTML set directly');

    } catch (error) {
        outputChannel.appendLine(`ERROR in updatePreviewContent: ${error instanceof Error ? error.message : 'Unknown error'}`);
        const settingsManager = CalcpadSettingsManager.getInstance(extensionContext);
        const errorApiBaseUrl = settingsManager.getServerUrl();
        const endpoint = unwrapped ? 'convert-unwrapped' : 'convert';
        const errorHtml = `
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <title>CalcPad Preview Error</title>
            </head>
            <body>
                <div style="color: #d32f2f; background: #ffebee; padding: 15px; border-radius: 4px; margin: 20px;">
                    <h3>Preview Error${unwrapped ? ' (Unwrapped)' : ''}</h3>
                    <p>${error instanceof Error ? error.message : 'Unknown error'}</p>
                    <p>Server URL: ${errorApiBaseUrl}/api/calcpad/${endpoint}</p>
                </div>
            </body>
            </html>
        `;

        panel.webview.html = errorHtml;
    }
}

async function updateUiPreviewContent(panel: vscode.WebviewPanel, content: string, sourceFileUri: vscode.Uri) {
    outputChannel.appendLine('Starting updateUiPreviewContent...');

    if (!content || content.trim().length === 0) {
        panel.webview.html = `<!DOCTYPE html><html><body><p>Empty document</p></body></html>`;
        return;
    }

    try {
        const settingsManager = CalcpadSettingsManager.getInstance(extensionContext);
        const settings = await settingsManager.getApiSettings();
        const apiBaseUrl = settingsManager.getServerUrl();

        if (!apiBaseUrl) {
            throw new Error('Server URL not configured');
        }

        // Build client file cache for referenced files
        const vsFileSystem = new VSCodeFileSystem();
        const vsLogger = new VSCodeLogger(outputChannel);
        const sourceDir = path.dirname(sourceFileUri.fsPath);
        const clientFileCache = await buildClientFileCacheFromContent(content, sourceDir, vsFileSystem, vsLogger, '[ConvertUI]');

        const theme = getEffectivePreviewTheme();
        const overrides = uiInputModel?.getOverrides() ?? {};

        outputChannel.appendLine(`Making API call to /api/calcpad/convert-ui with ${Object.keys(overrides).length} overrides...`);

        const response = await fetch(`${apiBaseUrl}/api/calcpad/convert-ui`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                content: content,
                settings: settings,
                theme: theme,
                forceUnwrappedCode: false,
                clientFileCache: clientFileCache,
                sourceFilePath: sourceFileUri.fsPath,
                uiOverrides: overrides
            }),
            signal: AbortSignal.timeout(10000)
        });

        if (!response.ok) {
            throw new Error(`Server returned ${response.status}`);
        }

        const apiResponse = await response.text();
        outputChannel.appendLine(`UI Preview HTML length: ${apiResponse.length}`);

        // Build image cache
        let imageCacheScript = '';
        const activeEditor = vscode.window.activeTextEditor;
        if (activeEditor && !activeEditor.document.isUntitled) {
            const documentDir = path.dirname(activeEditor.document.uri.fsPath);
            const imageCache = await buildImageCache(apiResponse, documentDir);
            imageCacheScript = getImageCacheScript(imageCache);
        }

        const errorNavigationScript = getErrorNavigationScript();
        const themeOverrideScript = getThemeOverrideScript(theme);

        // Use shared UI event script from calcpad-frontend
        const uiEventScript = getUiEventScript('vscode.postMessage');

        const sanitizedResponse = sanitizeServerHtml(apiResponse);
        const vsCodeApiInit = getVsCodeApiInitScript();

        // Conditionally inject datagrid CDN only when needed
        const datagridCdn = htmlHasDatagrids(apiResponse) ? getDatagridCdnTags() : '';

        // Inject "Save UI State" button
        const saveUiButton = `<div id="calcpad-save-ui-bar" style="position:fixed;top:0;right:0;z-index:9999;padding:6px 10px;">
<button onclick="vscode.postMessage({type:'saveUiState'})" style="
    background:#0078d4;color:#fff;border:none;border-radius:4px;padding:4px 12px;
    font-size:12px;cursor:pointer;opacity:0.85;
" onmouseover="this.style.opacity='1'" onmouseout="this.style.opacity='0.85'"
>Save UI State</button></div>`;

        let htmlWithScript = sanitizedResponse.replace('</head>', vsCodeApiInit + errorNavigationScript + datagridCdn + '</head>');
        htmlWithScript = htmlWithScript.replace('</body>', saveUiButton + imageCacheScript + themeOverrideScript + uiEventScript + '</body>');

        // Log the final HTML to the dedicated output channel for debugging
        calcpadOutputHtmlChannel.clear();
        calcpadOutputHtmlChannel.appendLine('=== UI PREVIEW HTML ===');
        calcpadOutputHtmlChannel.appendLine(htmlWithScript);

        panel.webview.html = htmlWithScript;
        outputChannel.appendLine('UI Preview webview HTML set');
        outputChannel.appendLine(`Datagrid CDN injected: ${datagridCdn.length > 0 ? 'yes' : 'no'}`);

    } catch (error) {
        outputChannel.appendLine(`ERROR in updateUiPreviewContent: ${error instanceof Error ? error.message : 'Unknown error'}`);
        const settingsManager = CalcpadSettingsManager.getInstance(extensionContext);
        const errorApiBaseUrl = settingsManager.getServerUrl();
        panel.webview.html = `
            <!DOCTYPE html><html><head><meta charset="UTF-8"><title>CalcPad UI Preview Error</title></head>
            <body>
                <div style="color: #d32f2f; background: #ffebee; padding: 15px; border-radius: 4px; margin: 20px;">
                    <h3>UI Preview Error</h3>
                    <p>${error instanceof Error ? error.message : 'Unknown error'}</p>
                    <p>Server URL: ${errorApiBaseUrl}/api/calcpad/convert-ui</p>
                </div>
            </body></html>`;
    }
}

async function generatePdf(panel: vscode.WebviewPanel, content: string, sourceFileUri: vscode.Uri) {
    const settingsManager = CalcpadSettingsManager.getInstance();
    const apiBaseUrl = settingsManager.getServerUrl();
    if (!apiBaseUrl) {
        vscode.window.showErrorMessage('Server URL not configured');
        return;
    }

    try {
        const settingsManager = CalcpadSettingsManager.getInstance(extensionContext);
        const settings = await settingsManager.getApiSettings();

        // Build client file cache for referenced files
        const vsFileSystem = new VSCodeFileSystem();
        const vsLogger = new VSCodeLogger(outputChannel);
        const sourceDir = path.dirname(sourceFileUri.fsPath);
        const clientFileCache = await buildClientFileCacheFromContent(content, sourceDir, vsFileSystem, vsLogger, '[PDF]');

        // Step 1: Convert calcpad content to HTML
        const htmlResponse = await fetch(`${apiBaseUrl}/api/calcpad/convert`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                content,
                settings: settings,
                clientFileCache: clientFileCache
            }),
            signal: AbortSignal.timeout(30000)
        });
        if (!htmlResponse.ok) {
            throw new Error(`Server returned ${htmlResponse.status}`);
        }
        let html = await htmlResponse.text();

        // Inline local images as base64 data URIs for the headless browser
        const documentDir = path.dirname(sourceFileUri.fsPath);
        const imageCache = await buildImageCache(html, documentDir);
        html = applyImageCache(html, imageCache);

        // Step 2: Generate PDF from HTML
        const pdfResponse = await fetch(`${apiBaseUrl}/api/calcpad/pdf`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                html: html,
                options: getPdfSettings()
            }),
            signal: AbortSignal.timeout(60000)
        });
        if (!pdfResponse.ok) {
            throw new Error(`PDF generation failed: ${pdfResponse.status}`);
        }

        // Get the active editor to determine the filename
        const activeEditor = vscode.window.activeTextEditor;
        const baseFilename = activeEditor
            ? activeEditor.document.fileName.split('/').pop()?.replace(/\.[^/.]+$/, '') || 'calcpad'
            : 'calcpad';

        // Show save dialog
        const saveUri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(`${baseFilename}.pdf`),
            filters: {
                'PDF Files': ['pdf']
            }
        });

        if (saveUri) {
            // Write the PDF file
            const pdfBuffer = await pdfResponse.arrayBuffer();
            await vscode.workspace.fs.writeFile(saveUri, new Uint8Array(pdfBuffer));

            // Show success message with option to open
            const openChoice = await vscode.window.showInformationMessage(
                `PDF saved to ${saveUri.fsPath}`,
                'Open PDF'
            );

            if (openChoice === 'Open PDF') {
                vscode.env.openExternal(saveUri);
            }
        }

    } catch (error) {
        vscode.window.showErrorMessage(
            `Failed to generate PDF: ${error instanceof Error ? error.message : 'Unknown error'}`
        );
    }
}

async function printToPdf() {
    const activeEditor = vscode.window.activeTextEditor;
    if (!activeEditor) {
        vscode.window.showErrorMessage('No active CalcPad document found');
        return;
    }

    try {
        // Get PDF settings
        const pdfSettings = getPdfSettings();
        
        // Get the active editor to determine the filename and directory
        const currentDir = path.dirname(activeEditor.document.fileName);
        const baseFilename = path.basename(activeEditor.document.fileName, path.extname(activeEditor.document.fileName));
        const defaultPath = path.join(currentDir, baseFilename + '.pdf');

        // Show save dialog
        const saveUri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(defaultPath),
            filters: {
                'PDF Files': ['pdf']
            }
        });

        if (!saveUri) {
            return;
        }

        try {
            // Show progress notification
            await vscode.window.withProgress({
                location: vscode.ProgressLocation.Notification,
                title: "Generating PDF...",
                cancellable: false
            }, async (progress) => {
                progress.report({ increment: 0, message: "Starting PDF generation..." });

                const settingsManager = CalcpadSettingsManager.getInstance(extensionContext);
                const apiBaseUrl = settingsManager.getServerUrl();
                if (!apiBaseUrl) {
                    throw new Error('Server URL not configured');
                }
                const settings = await settingsManager.getApiSettings();
                const documentContent = activeEditor.document.getText();

                if (!documentContent || documentContent.trim().length === 0) {
                    throw new Error('Document is empty. Please add some CalcPad content first.');
                }

                progress.report({ increment: 10, message: "Loading referenced files..." });

                // Build client file cache for referenced files
                const vsFileSystem = new VSCodeFileSystem();
                const vsLogger = new VSCodeLogger(outputChannel);
                const sourceDir = path.dirname(activeEditor.document.uri.fsPath);
                const clientFileCache = await buildClientFileCacheFromContent(documentContent, sourceDir, vsFileSystem, vsLogger, '[PDF]');

                progress.report({ increment: 20, message: "Converting to HTML..." });

                // Step 1: Convert calcpad content to HTML
                const htmlResponse = await fetch(`${apiBaseUrl}/api/calcpad/convert`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        content: documentContent,
                        settings: settings,
                        clientFileCache: clientFileCache
                    }),
                    signal: AbortSignal.timeout(30000)
                });
                if (!htmlResponse.ok) {
                    throw new Error(`Server returned ${htmlResponse.status}`);
                }
                let html = await htmlResponse.text();

                // Inline local images as base64 data URIs so the headless
                // browser can render them (it has no access to the local filesystem)
                const documentDir = path.dirname(activeEditor.document.uri.fsPath);
                const imageCache = await buildImageCache(html, documentDir);
                html = applyImageCache(html, imageCache);

                progress.report({ increment: 50, message: "Generating PDF..." });

                // Step 2: Generate PDF from HTML
                const pdfResponse = await fetch(`${apiBaseUrl}/api/calcpad/pdf`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        html: html,
                        options: pdfSettings
                    }),
                    signal: AbortSignal.timeout(60000)
                });
                if (!pdfResponse.ok) {
                    throw new Error(`PDF generation failed: ${pdfResponse.status}`);
                }

                progress.report({ increment: 80, message: "Saving PDF file..." });

                // Write the PDF file
                const pdfBuffer = await pdfResponse.arrayBuffer();
                await vscode.workspace.fs.writeFile(saveUri, new Uint8Array(pdfBuffer));

                progress.report({ increment: 100, message: "PDF generation complete!" });
            });

            // Show success message with option to open
            const openChoice = await vscode.window.showInformationMessage(
                `PDF saved to ${saveUri.fsPath}`,
                'Open PDF'
            );

            if (openChoice === 'Open PDF') {
                vscode.env.openExternal(saveUri);
            }
        } catch (error) {
            outputChannel.appendLine(`ERROR in printToPdf: ${error instanceof Error ? error.message : 'Unknown error'}`);
            vscode.window.showErrorMessage(
                `Failed to generate PDF: ${error instanceof Error ? error.message : 'Unknown error'}`
            );
        }

    } catch (error) {
        outputChannel.appendLine(`ERROR in printToPdf (outer): ${error instanceof Error ? error.message : 'Unknown error'}`);
        vscode.window.showErrorMessage(
            `Failed to generate PDF: ${error instanceof Error ? error.message : 'Unknown error'}`
        );
    }
}

async function createHtmlPreview(context: vscode.ExtensionContext) {
    const activeEditor = vscode.window.activeTextEditor;
    if (!activeEditor) {
        vscode.window.showErrorMessage('No active editor found');
        return;
    }

    // Store the source editor for navigation
    previewSourceEditor = activeEditor;

    if (activePreviewPanel) {
        (activePreviewPanel as vscode.WebviewPanel).reveal(vscode.ViewColumn.Beside);
        await updatePreviewContent(activePreviewPanel as vscode.WebviewPanel, activeEditor.document.getText(), activeEditor.document.uri);
        return;
    }

    const panel = vscode.window.createWebviewPanel(
        'htmlPreview',
        'CalcPad Preview',
        vscode.ViewColumn.Beside,
        {
            enableScripts: true,
            enableFindWidget: true
        }
    );

    activePreviewPanel = panel;
    activePreviewType = 'regular';

    panel.onDidDispose(() => {
        activePreviewPanel = undefined;
        activePreviewType = undefined;
        previewSourceEditor = undefined;
    });

    // Handle messages from webview
    panel.webview.onDidReceiveMessage(
        message => {
            switch (message.type) {
                case 'navigateToLine':
                    const sourceEditor = previewSourceEditor;
                    if (sourceEditor && message.line) {
                        // data-text already contains the original source line (1-based)
                        // Just convert from 1-based to 0-based indexing
                        const lineIndex = Math.max(0, message.line - 1);
                        outputChannel.appendLine(`Navigating to source line ${message.line}`);

                        const position = new vscode.Position(lineIndex, 0);
                        const selection = new vscode.Selection(position, position);
                        sourceEditor.selection = selection;
                        sourceEditor.revealRange(selection, vscode.TextEditorRevealType.InCenter);
                        vscode.window.showTextDocument(sourceEditor.document, vscode.ViewColumn.One);
                    }
                    break;
                case 'consoleMessage':
                    const timestamp = new Date().toISOString();
                    const level = message.level.toUpperCase();
                    calcpadWebviewConsoleChannel.appendLine(`[${timestamp}] [${level}] ${message.message}`);
                    break;
                default:
                    break;
            }
        }
    );

    await updatePreviewContent(panel, activeEditor.document.getText(), activeEditor.document.uri);
}

async function createHtmlPreviewUnwrapped(context: vscode.ExtensionContext) {
    const activeEditor = vscode.window.activeTextEditor;
    if (!activeEditor) {
        vscode.window.showErrorMessage('No active editor found');
        return;
    }

    // Store the source editor for navigation
    previewSourceEditor = activeEditor;

    const panel = vscode.window.createWebviewPanel(
        'htmlPreviewUnwrapped',
        'CalcPad Preview Unwrapped',
        vscode.ViewColumn.Beside,
        {
            enableScripts: true,
            enableFindWidget: true
        }
    );

    // Update global references for unwrapped preview
    activePreviewPanel = panel;
    activePreviewType = 'unwrapped';

    panel.onDidDispose(() => {
        activePreviewPanel = undefined;
        activePreviewType = undefined;
        previewSourceEditor = undefined;
    });

    // Handle messages from webview
    panel.webview.onDidReceiveMessage(
        message => {
            switch (message.type) {
                case 'navigateToLine':
                    const sourceEditor = previewSourceEditor;
                    if (sourceEditor && message.line) {
                        // data-text already contains the original source line (1-based)
                        // Just convert from 1-based to 0-based indexing
                        const lineIndex = Math.max(0, message.line - 1);
                        outputChannel.appendLine(`Navigating to source line ${message.line}`);

                        const position = new vscode.Position(lineIndex, 0);
                        const selection = new vscode.Selection(position, position);
                        sourceEditor.selection = selection;
                        sourceEditor.revealRange(selection, vscode.TextEditorRevealType.InCenter);
                        vscode.window.showTextDocument(sourceEditor.document, vscode.ViewColumn.One);
                    }
                    break;
                case 'consoleMessage':
                    const timestamp = new Date().toISOString();
                    const level = message.level.toUpperCase();
                    calcpadWebviewConsoleChannel.appendLine(`[${timestamp}] [${level}] ${message.message}`);
                    break;
                default:
                    break;
            }
        }
    );

    await updatePreviewContent(panel, activeEditor.document.getText(), activeEditor.document.uri, true);
}

async function createUiPreview(context: vscode.ExtensionContext) {
    const activeEditor = vscode.window.activeTextEditor;
    if (!activeEditor) {
        vscode.window.showErrorMessage('No active editor found');
        return;
    }

    previewSourceEditor = activeEditor;
    uiInputModel = new UiInputModel();
    uiOverridesCommentLine = undefined;

    // Load persisted UI overrides from file via definitions API
    try {
        const defs = await definitionsService.refreshDefinitions(activeEditor.document);
        if (defs?.uiOverrides) {
            uiInputModel.loadFromPersisted(defs.uiOverrides.overrides, defs.variables);
            uiOverridesCommentLine = defs.uiOverrides.commentLine;
            outputChannel.appendLine(`Loaded ${Object.keys(defs.uiOverrides.overrides).length} persisted UI overrides from line ${defs.uiOverrides.commentLine}`);
        }
    } catch (error) {
        outputChannel.appendLine(`Failed to load persisted UI overrides: ${error instanceof Error ? error.message : 'Unknown error'}`);
    }

    if (activePreviewPanel && activePreviewType === 'ui') {
        (activePreviewPanel as vscode.WebviewPanel).reveal(vscode.ViewColumn.Beside);
        await updateUiPreviewContent(activePreviewPanel as vscode.WebviewPanel, activeEditor.document.getText(), activeEditor.document.uri);
        return;
    }

    const panel = vscode.window.createWebviewPanel(
        'htmlPreviewUi',
        'CalcPad UI Preview',
        vscode.ViewColumn.Beside,
        {
            enableScripts: true,
            enableFindWidget: true
        }
    );

    activePreviewPanel = panel;
    activePreviewType = 'ui';

    panel.onDidDispose(() => {
        activePreviewPanel = undefined;
        activePreviewType = undefined;
        previewSourceEditor = undefined;
        uiInputModel = null;
        uiOverridesCommentLine = undefined;
    });

    panel.webview.onDidReceiveMessage(
        async message => {
            switch (message.type) {
                case 'uiValueChange': {
                    outputChannel.appendLine(`UI value change: ${message.varName} = ${message.newValue}`);
                    if (uiInputModel) {
                        uiInputModel.setValue(message.varName, message.newValue, message.sourceLine);
                    }
                    // Re-run convert-ui with updated overrides
                    const editor = previewSourceEditor;
                    if (editor) {
                        await updateUiPreviewContent(panel, editor.document.getText(), editor.document.uri);
                    }
                    break;
                }
                case 'navigateToLine': {
                    const srcEditor = previewSourceEditor;
                    if (srcEditor && message.line) {
                        const lineIndex = Math.max(0, message.line - 1);
                        const position = new vscode.Position(lineIndex, 0);
                        const selection = new vscode.Selection(position, position);
                        srcEditor.selection = selection;
                        srcEditor.revealRange(selection, vscode.TextEditorRevealType.InCenter);
                        vscode.window.showTextDocument(srcEditor.document, vscode.ViewColumn.One);
                    }
                    break;
                }
                case 'saveUiState': {
                    await saveUiStateToFile();
                    break;
                }
                case 'consoleMessage': {
                    const ts = new Date().toISOString();
                    const lvl = message.level.toUpperCase();
                    calcpadWebviewConsoleChannel.appendLine(`[${ts}] [${lvl}] ${message.message}`);
                    break;
                }
            }
        }
    );

    await updateUiPreviewContent(panel, activeEditor.document.getText(), activeEditor.document.uri);
}

function schedulePreviewUpdate() {
    if (!activePreviewPanel) return;
    
    const activeEditor = vscode.window.activeTextEditor;
    if (!activeEditor) return;
    
    // Only update for .cpd files or plaintext files
    if (activeEditor.document.languageId !== 'calcpad' && activeEditor.document.languageId !== 'plaintext') {
        return;
    }

    if (previewUpdateTimeout) {
        clearTimeout(previewUpdateTimeout as NodeJS.Timeout);
    }

    previewUpdateTimeout = setTimeout(async () => {
        if (activePreviewPanel && activeEditor) {
            // Update the source editor reference when updating preview
            previewSourceEditor = activeEditor;
            if (activePreviewType === 'ui') {
                // Source changed — merge persisted overrides without discarding in-memory user changes
                try {
                    const defs = await definitionsService.refreshDefinitions(activeEditor.document);
                    if (defs?.uiOverrides) {
                        uiInputModel?.mergeFromPersisted(defs.uiOverrides.overrides, defs.variables);
                        uiOverridesCommentLine = defs.uiOverrides.commentLine;
                    }
                } catch {
                    // Keep existing in-memory overrides on error
                }
                await updateUiPreviewContent(activePreviewPanel as vscode.WebviewPanel, activeEditor.document.getText(), activeEditor.document.uri);
            } else {
                const unwrapped = activePreviewType === 'unwrapped';
                await updatePreviewContent(activePreviewPanel as vscode.WebviewPanel, activeEditor.document.getText(), activeEditor.document.uri, unwrapped);
            }
        }
    }, 500);
}

async function saveUiStateToFile() {
    if (!uiInputModel || !previewSourceEditor) {
        vscode.window.showWarningMessage('No active UI preview.');
        return;
    }

    const overrides = uiInputModel.getOverrides();
    if (Object.keys(overrides).length === 0) {
        vscode.window.showInformationMessage('No UI overrides to save.');
        return;
    }

    const editor = previewSourceEditor;
    const content = editor.document.getText();
    const newContent = updateUiOverridesInContent(content, overrides, uiOverridesCommentLine);

    const fullRange = new vscode.Range(
        editor.document.positionAt(0),
        editor.document.positionAt(content.length)
    );

    const success = await editor.edit(editBuilder => {
        editBuilder.replace(fullRange, newContent);
    });

    if (success) {
        // Update the comment line reference after the edit
        // If we inserted at line 0, the comment is now on line 0
        if (uiOverridesCommentLine === undefined) {
            uiOverridesCommentLine = 0;
        }
        outputChannel.appendLine(`Saved UI overrides to file (${Object.keys(overrides).length} overrides)`);
        vscode.window.showInformationMessage(`Saved ${Object.keys(overrides).length} UI override(s) to file.`);
    } else {
        vscode.window.showErrorMessage('Failed to save UI overrides to file.');
    }
}

export async function activate(context: vscode.ExtensionContext) {
    console.log('VS Code CalcPad extension is now active!');
    
    try {
        // Store extension context for global access
        extensionContext = context;
        
        // Create output channel for debugging
        outputChannel = vscode.window.createOutputChannel('CalcPad Extension');
        outputChannel.appendLine('CalcPad extension activated');

        // Create dedicated output channels for HTML
        calcpadOutputHtmlChannel = vscode.window.createOutputChannel('Calcpad Output HTML');
        calcpadWebviewConsoleChannel = vscode.window.createOutputChannel('Calcpad Webview Console');

        // Create debug channel for linter/highlighter
        const serverDebugChannel = vscode.window.createOutputChannel('CalcPad Server Debug');

        outputChannel.appendLine('Initializing settings manager...');
        const settingsManager = CalcpadSettingsManager.getInstance(context);

        // Create shared API client (uses remote URL initially, switches to local when server starts)
        const apiClient = new CalcpadApiClient(
            settingsManager.getServerUrl(),
            new VSCodeLogger(serverDebugChannel)
        );

        // Start bundled server if available
        const serverMode = settingsManager.getSettings().server.mode || 'auto';
        outputChannel.appendLine(`Server mode: ${serverMode}`);

        if (serverMode === 'auto' || serverMode === 'local') {
            const dllExists = CalcpadServerManager.dllExists(context.extensionPath);
            outputChannel.appendLine(`Bundled DLL exists: ${dllExists}`);

            if (dllExists) {
                const config = vscode.workspace.getConfiguration('calcpad');
                const configuredDotnetPath = config.get<string>('server.dotnetPath', 'dotnet');
                const dotnetManager = new DotnetRuntimeManager(outputChannel);
                const globalStorage = context.globalStorageUri.fsPath;

                // Resolve dotnet path: local runtime → system dotnet → prompt user
                dotnetManager.resolveDotnetPath(globalStorage, configuredDotnetPath, serverMode).then((resolvedDotnetPath) => {
                    if (!resolvedDotnetPath) {
                        if (serverMode === 'local') {
                            outputChannel.appendLine('.NET runtime not available, server cannot start');
                        } else {
                            outputChannel.appendLine('.NET runtime not available, falling back to remote API');
                        }
                        return;
                    }

                    outputChannel.appendLine(`Using dotnet at: ${resolvedDotnetPath}`);
                    serverManager = new CalcpadServerManager(context.extensionPath, serverDebugChannel, resolvedDotnetPath, outputChannel);
                    context.subscriptions.push(serverManager);

                    // Notify user when server crashes repeatedly
                    serverManager.onCrashExhausted = (crashOutput: string) => {
                        serverDebugChannel.appendLine('[ServerManager] Server crashed 3 times — stopping auto-restart');
                        serverDebugChannel.appendLine('[ServerManager] Last crash output:\n' + crashOutput);
                        vscode.window.showErrorMessage(
                            'CalcPad server crashed repeatedly (possibly due to your file). Use the refresh button to restart.',
                            'Show Debug Output'
                        ).then(choice => {
                            if (choice === 'Show Debug Output') {
                                serverDebugChannel.show();
                            }
                        });
                    };

                    // Start the server in the background so activation isn't blocked
                    serverManager.start().then(() => {
                        const serverUrl = serverManager!.getBaseUrl();
                        settingsManager.setLocalServerUrl(serverUrl);
                        apiClient.setBaseUrl(serverUrl);
                        outputChannel.appendLine(`Local server started at ${serverUrl}`);
                    }).catch((err) => {
                        const message = err instanceof Error ? err.message : String(err);
                        outputChannel.appendLine(`Failed to start local server: ${message}`);
                        // Keep `serverManager` around when Windows blocked the exe —
                        // the user can unblock the file and click refresh to retry.
                        // Discarding it here would leave refresh with nothing to call.
                        const blocked = /Windows blocked the executable|EACCES|EPERM/i.test(message);
                        if (!blocked) {
                            serverManager = undefined;
                        }

                        if (blocked) {
                            vscode.window.showErrorMessage(
                                'CalcPad: Windows blocked Calcpad.Server.exe. ' +
                                'Unblock the file (right-click → Properties → Unblock) ' +
                                'then click the CalcPad refresh button to retry.',
                                'Show Output'
                            ).then(choice => {
                                if (choice === 'Show Output') {
                                    serverDebugChannel.show();
                                }
                            });
                        } else if (serverMode === 'local') {
                            vscode.window.showErrorMessage(`CalcPad: Failed to start local server: ${message}`);
                        } else {
                            outputChannel.appendLine('Falling back to remote API');
                        }
                    });
                }).catch((err) => {
                    const message = err instanceof Error ? err.message : String(err);
                    outputChannel.appendLine(`Dotnet resolution failed: ${message}`);
                });
            } else if (serverMode === 'local') {
                vscode.window.showErrorMessage('CalcPad: Server mode is "local" but CalcpadServer.dll was not found in the extension.');
            } else {
                outputChannel.appendLine('No bundled DLL found, using remote API');
            }
        }

        outputChannel.appendLine('Initializing linter...');
        linter = new CalcpadServerLinter(apiClient, serverDebugChannel);

        outputChannel.appendLine('Initializing definitions service...');
        definitionsService = new CalcpadDefinitionsService(apiClient, serverDebugChannel);

        // Initialize semantic token provider
        outputChannel.appendLine('Initializing semantic token provider...');
        const semanticTokensProvider = new CalcpadSemanticTokensProvider(apiClient, serverDebugChannel);
        const semanticTokensDisposable = vscode.languages.registerDocumentSemanticTokensProvider(
            { language: 'calcpad' },
            semanticTokensProvider,
            semanticTokensLegend
        );

        // Initialize operator replacer
        outputChannel.appendLine('Initializing operator replacer...');
        const operatorReplacer = new OperatorReplacer(outputChannel);
        const operatorReplacerDisposable = operatorReplacer.registerDocumentChangeListener(context);

        // Initialize auto-indenter
        outputChannel.appendLine('Initializing auto-indenter...');
        const autoIndenter = new AutoIndenter(outputChannel);
        const autoIndenterDisposable = autoIndenter.registerDocumentChangeListener(context);

        // Initialize image inserter
        outputChannel.appendLine('Initializing image inserter...');
        const imageInserter = new ImageInserter(outputChannel);
        const imagePasteDisposable = imageInserter.registerPasteProvider();
        const imageInsertCommandDisposable = imageInserter.registerInsertCommand();

        // Initialize comment formatter
        outputChannel.appendLine('Initializing comment formatter...');
        const commentFormatter = new CommentFormatter(outputChannel);
        const commentFormatterDisposables = commentFormatter.registerCommands();

        // Initialize insert manager (snippet service)
        outputChannel.appendLine('Initializing insert manager...');
        const insertManager = new CalcpadInsertManager(apiClient, outputChannel);

        // Initialize quick typer (uses snippet data for quick type map)
        outputChannel.appendLine('Initializing quick typer...');
        const quickTyper = new QuickTyper(outputChannel, insertManager);
        const quickTyperDisposable = quickTyper.registerDocumentChangeListener(context);

        // Initialize autocomplete provider
        outputChannel.appendLine('Initializing autocomplete provider...');
        const completionProviderDisposable = CalcpadCompletionProvider.register(definitionsService, insertManager, outputChannel);

        // Initialize #include file completion provider
        outputChannel.appendLine('Initializing include file completion provider...');
        const includeCompletionDisposable = CalcpadIncludeCompletionProvider.register(outputChannel);

        // Initialize definition provider (Go to Definition)
        outputChannel.appendLine('Initializing definition provider...');
        const definitionProviderDisposable = CalcpadDefinitionProvider.register(apiClient, outputChannel);

        // Initialize reference provider (Find All References)
        outputChannel.appendLine('Initializing reference provider...');
        const referenceProviderDisposable = CalcpadReferenceProvider.register(apiClient, outputChannel);

        // Initialize rename provider (F2 Rename Symbol)
        outputChannel.appendLine('Initializing rename provider...');
        const renameProviderDisposable = CalcpadRenameProvider.register(apiClient, outputChannel);

        // Initialize hover provider (Hover Tooltips)
        outputChannel.appendLine('Initializing hover provider...');
        const hoverProviderDisposable = CalcpadHoverProvider.register(definitionsService, insertManager, outputChannel);

    // Unified document processing function
    let isProcessingDocument = false;
    async function processDocument(document: vscode.TextDocument) {
        if (document.languageId !== 'calcpad' && document.languageId !== 'plaintext') {
            return;
        }
        if (isProcessingDocument) return;

        isProcessingDocument = true;
        try {
            await _doProcessDocument(document);
        } finally {
            isProcessingDocument = false;
        }
    }

    async function _doProcessDocument(document: vscode.TextDocument) {
        outputChannel.appendLine('[processDocument] Processing document: ' + document.uri.fsPath);

        // Run linting and definitions in parallel
        const [, definitions] = await Promise.all([
            linter.lintDocument(document),
            definitionsService.refreshDefinitions(document).catch((error: unknown) => {
                outputChannel.appendLine('Error fetching definitions: ' + error);
                return null;
            }),
        ]);

        if (definitions) {
            outputChannel.appendLine('[processDocument] Found ' + definitions.macros.length + ' macros, ' + definitions.variables.length + ' variables, ' + definitions.functions.length + ' functions, ' + definitions.customUnits.length + ' custom units');

            // Send definitions to Vue UI provider
            vueUiProvider.updateVariables({
                macros: definitions.macros.map(m => ({
                    name: m.name,
                    params: m.parameters.length > 0 ? m.parameters.join('; ') : undefined,
                    definition: m.content.join('\n'),
                    source: m.source as 'local' | 'include',
                    sourceFile: m.sourceFile,
                    description: m.description,
                    paramTypes: m.paramTypes,
                    paramDescriptions: m.paramDescriptions,
                    defaults: m.defaults
                })),
                variables: definitions.variables.map(v => ({
                    name: v.name,
                    definition: v.expression,
                    source: v.source as 'local' | 'include',
                    sourceFile: v.sourceFile
                })),
                functions: definitions.functions.map(f => ({
                    name: f.name,
                    params: f.parameters.join('; '),
                    source: f.source as 'local' | 'include',
                    sourceFile: f.sourceFile,
                    description: f.description,
                    paramTypes: f.paramTypes,
                    paramDescriptions: f.paramDescriptions,
                    defaults: f.defaults
                })),
                customUnits: definitions.customUnits.map(u => ({
                    name: u.name,
                    definition: u.expression,
                    source: u.source as 'local' | 'include',
                    sourceFile: u.sourceFile
                }))
            });
        } else {
            outputChannel.appendLine('[processDocument] No definitions returned from server');
        }
    }

    // Centralized refresh function for when settings change
    async function refreshAllComponents() {
        outputChannel.appendLine('[Settings] Refreshing all components after settings change');

        // Use the effective server URL (local if running, remote otherwise)
        const effectiveUrl = settingsManager.getServerUrl();
        apiClient.setBaseUrl(effectiveUrl);
        outputChannel.appendLine(`[Settings] Using server URL: ${effectiveUrl}`);

        // Reload snippets from server
        try {
            await insertManager.reloadSnippets();
            outputChannel.appendLine('[Settings] Snippets reloaded');
        } catch (error) {
            outputChannel.appendLine('[Settings] Failed to reload snippets: ' + error);
        }

        // Refresh semantic tokens for all visible editors
        vscode.window.visibleTextEditors.forEach(editor => {
            if (editor.document.languageId === 'calcpad' || editor.document.languageId === 'plaintext') {
                semanticTokensProvider.refresh();
            }
        });

        // Reprocess active document (linting + definitions)
        const activeEditor = vscode.window.activeTextEditor;
        if (activeEditor) {
            await processDocument(activeEditor.document);
        }

        // Refresh preview if open
        if (activePreviewPanel && activeEditor) {
            const unwrapped = activePreviewType === 'unwrapped';
            await updatePreviewContent(activePreviewPanel as vscode.WebviewPanel, activeEditor.document.getText(), activeEditor.document.uri, unwrapped);
            outputChannel.appendLine('[Settings] Preview refreshed');
        }

        outputChannel.appendLine('[Settings] All components refreshed');
    }

    // Register webview provider for CalcPad Vue UI panel (NEW)
    const vueUiProvider = new CalcpadVueUIProvider(context.extensionUri, context, settingsManager, insertManager);
    const vueUiProviderDisposable = vscode.window.registerWebviewViewProvider(
        CalcpadVueUIProvider.viewType,
        vueUiProvider
    );



    const disposable = vscode.commands.registerCommand('vscode-calcpad.activate', () => {
        vscode.window.showInformationMessage('CalcPad activated!');
    });

    const previewCommand = vscode.commands.registerCommand('vscode-calcpad.previewHtml', () => {
        createHtmlPreview(context);
    });

    const previewUnwrappedCommand = vscode.commands.registerCommand('vscode-calcpad.previewUnwrapped', () => {
        createHtmlPreviewUnwrapped(context);
    });

    const previewUiCommand = vscode.commands.registerCommand('vscode-calcpad.previewUi', () => {
        createUiPreview(context);
    });

    const saveUiStateCommand = vscode.commands.registerCommand('calcpad.saveUiState', () => {
        saveUiStateToFile();
    });

    const showInsertCommand = vscode.commands.registerCommand('vscode-calcpad.showInsert', () => {
        vscode.commands.executeCommand('workbench.view.extension.calcpad-ui');
    });


    const printToPdfCommand = vscode.commands.registerCommand('vscode-calcpad.printToPdf', () => {
        printToPdf();
    });

    // Readonly virtual document provider for viewing webview source HTML
    let webviewSourceHtml = '';
    const webviewSourceProvider = new class implements vscode.TextDocumentContentProvider {
        onDidChangeEmitter = new vscode.EventEmitter<vscode.Uri>();
        onDidChange = this.onDidChangeEmitter.event;
        provideTextDocumentContent() { return webviewSourceHtml; }
    };
    const webviewSourceScheme = 'calcpad-webview-source';
    const webviewSourceRegistration = vscode.workspace.registerTextDocumentContentProvider(webviewSourceScheme, webviewSourceProvider);
    const webviewSourceUri = vscode.Uri.parse(`${webviewSourceScheme}:Webview Source.html`);

    const viewWebviewSourceCommand = vscode.commands.registerCommand('vscode-calcpad.viewWebviewSource', async () => {
        if (!activePreviewPanel) {
            vscode.window.showWarningMessage('No active CalcPad preview to inspect.');
            return;
        }
        webviewSourceHtml = (activePreviewPanel as vscode.WebviewPanel).webview.html;
        webviewSourceProvider.onDidChangeEmitter.fire(webviewSourceUri);
        const doc = await vscode.workspace.openTextDocument(webviewSourceUri);
        await vscode.window.showTextDocument(doc, vscode.ViewColumn.Beside, true);
    });

    const refreshVariablesCommand = vscode.commands.registerCommand('calcpad.refreshVariables', async () => {
        const activeEditor = vscode.window.activeTextEditor;
        if (activeEditor) {
            await processDocument(activeEditor.document);
        }
    });

    const stopServerCommand = vscode.commands.registerCommand('calcpad.stopServer', async () => {
        outputChannel.appendLine('[Stop] Manual server stop triggered');
        if (serverManager && serverManager.isRunning) {
            try {
                await serverManager.stop();
                outputChannel.appendLine('[Stop] Server stopped successfully');
                vscode.window.showInformationMessage('CalcPad server stopped. Use the refresh button to restart.');
            } catch (err) {
                const msg = err instanceof Error ? err.message : String(err);
                outputChannel.appendLine(`[Stop] Server stop failed: ${msg}`);
                vscode.window.showErrorMessage(`CalcPad: Failed to stop server: ${msg}`);
            }
        } else {
            outputChannel.appendLine('[Stop] Server is not running');
            vscode.window.showInformationMessage('CalcPad server is not running.');
        }
    });

    const refreshDocumentCommand = vscode.commands.registerCommand('calcpad.refreshDocument', async () => {
        outputChannel.appendLine('[Refresh] Manual document refresh triggered');

        // Check server health and restart if down
        if (serverManager && !serverManager.isRunning) {
            outputChannel.appendLine('[Refresh] Server is down, attempting restart...');
            try {
                await serverManager.restart();
                const serverUrl = serverManager.getBaseUrl();
                settingsManager.setLocalServerUrl(serverUrl);
                apiClient.setBaseUrl(serverUrl);
                outputChannel.appendLine(`[Refresh] Server restarted at ${serverUrl}`);
            } catch (err) {
                const msg = err instanceof Error ? err.message : String(err);
                outputChannel.appendLine(`[Refresh] Server restart failed: ${msg}`);
                const blocked = /Windows blocked the executable|EACCES|EPERM/i.test(msg);
                if (blocked) {
                    vscode.window.showErrorMessage(
                        'CalcPad: Windows is still blocking Calcpad.Server.exe. ' +
                        'Right-click the file in Windows Explorer → Properties → check "Unblock", ' +
                        'then click refresh again.'
                    );
                } else {
                    vscode.window.showErrorMessage(`CalcPad: Server restart failed: ${msg}`);
                }
                return;
            }
        } else if (serverManager) {
            // Server thinks it's running — verify with health check
            const healthy = await apiClient.checkHealth();
            if (!healthy) {
                outputChannel.appendLine('[Refresh] Server health check failed, restarting...');
                try {
                    await serverManager.restart();
                    const serverUrl = serverManager.getBaseUrl();
                    settingsManager.setLocalServerUrl(serverUrl);
                    apiClient.setBaseUrl(serverUrl);
                    outputChannel.appendLine(`[Refresh] Server restarted at ${serverUrl}`);
                } catch (err) {
                    const msg = err instanceof Error ? err.message : String(err);
                    outputChannel.appendLine(`[Refresh] Server restart failed: ${msg}`);
                    vscode.window.showErrorMessage(`CalcPad: Server restart failed: ${msg}`);
                    return;
                }
            }
        }

        // Clear server-side caches (remote content + disk file cache)
        const cacheCleared = await apiClient.refreshCache();
        outputChannel.appendLine(cacheCleared
            ? '[Refresh] Server cache cleared'
            : '[Refresh] Failed to clear server cache');

        const activeEditor = vscode.window.activeTextEditor;
        if (activeEditor) {
            // Re-lint and refresh definitions
            await processDocument(activeEditor.document);
            // Re-highlight (semantic tokens)
            semanticTokensProvider.refresh();
            outputChannel.appendLine('[Refresh] Document re-linted and re-highlighted');
        }
    });

    const prettifyDocumentCommand = vscode.commands.registerCommand('vscode-calcpad.prettifyDocument', async () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            vscode.window.showInformationMessage('CalcPad: open a .cpd file to prettify.');
            return;
        }
        if (editor.document.languageId !== 'calcpad' && editor.document.languageId !== 'plaintext') {
            vscode.window.showInformationMessage('CalcPad: prettify is only available for CalcPad documents.');
            return;
        }

        const config = vscode.workspace.getConfiguration('calcpad');
        const indentStyle = config.get<string>('prettify.indentStyle', 'tab');
        const indentSize = config.get<number>('prettify.indentSize', 4);
        const trim = config.get<boolean>('prettify.trimTrailingWhitespace', true);
        const indentUnit = indentStyle === 'space' ? ' '.repeat(Math.max(1, indentSize)) : '\t';

        try {
            const response = await apiClient.prettify(editor.document.getText(), indentUnit, trim);
            if (!response) {
                vscode.window.showErrorMessage('CalcPad: prettify request failed (no response from server).');
                return;
            }
            const fullRange = new vscode.Range(
                editor.document.positionAt(0),
                editor.document.positionAt(editor.document.getText().length)
            );
            const ok = await editor.edit(eb => eb.replace(fullRange, response.content));
            if (!ok) {
                vscode.window.showErrorMessage('CalcPad: prettify edit was rejected by the editor.');
            }
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            outputChannel.appendLine('[Prettify] Error: ' + msg);
            vscode.window.showErrorMessage('CalcPad: prettify failed — ' + msg);
        }
    });

    const exportToPdfCommand = vscode.commands.registerCommand('vscode-calcpad.exportToPdf', () => {
        const activeEditor = vscode.window.activeTextEditor;
        if (activeEditor && activePreviewPanel) {
            generatePdf(activePreviewPanel as vscode.WebviewPanel, activeEditor.document.getText(), activeEditor.document.uri);
        } else if (activeEditor) {
            // Create a temporary panel just for PDF generation
            const tempPanel = vscode.window.createWebviewPanel(
                'tempPdfPanel',
                'PDF Export',
                vscode.ViewColumn.Active,
                { enableScripts: false }
            );
            generatePdf(tempPanel, activeEditor.document.getText(), activeEditor.document.uri);
            tempPanel.dispose();
        }
    });


    // Process document on open
    const onDidOpenTextDocument = vscode.workspace.onDidOpenTextDocument(document => {
        processDocument(document).catch(e => outputChannel.appendLine('[processDocument] Error: ' + e));
    });

    // Process document on save
    const onDidSaveTextDocument = vscode.workspace.onDidSaveTextDocument(document => {
        processDocument(document).catch(e => outputChannel.appendLine('[processDocument] Error: ' + e));
    });

    // Lint on document change (with debouncing)
    let lintTimeout: NodeJS.Timeout | unknown = undefined;
    const onDidChangeTextDocument = vscode.workspace.onDidChangeTextDocument(event => {
        if (event.document.languageId === 'calcpad' || event.document.languageId === 'plaintext') {
            if (lintTimeout) {
                clearTimeout(lintTimeout as NodeJS.Timeout);
            }
            lintTimeout = setTimeout(() => {
                processDocument(event.document).catch(e => outputChannel.appendLine('[processDocument] Error: ' + e));
            }, 500);
            // Only schedule preview update for CalcPad files
            schedulePreviewUpdate();
        }
    });

    // Update preview and variables when active editor changes
    const onDidChangeActiveTextEditor = vscode.window.onDidChangeActiveTextEditor(editor => {
        if (editor && (editor.document.languageId === 'calcpad' || editor.document.languageId === 'plaintext')) {
            // Update preview if panel is open
            if (activePreviewPanel) {
                schedulePreviewUpdate();
            }
            // Update Variables tab
            processDocument(editor.document).catch(e => outputChannel.appendLine('[processDocument] Error: ' + e));
        }
    });

    // Refresh all components when calcpad settings change
    const onDidChangeConfiguration = vscode.workspace.onDidChangeConfiguration(async event => {
        // Check if any calcpad settings changed
        if (event.affectsConfiguration('calcpad')) {
            outputChannel.appendLine('[Settings] Calcpad settings changed - triggering refresh');
            await refreshAllComponents();
        }
    });

    // Process all open calcpad documents on activation
    vscode.workspace.textDocuments.forEach(document => processDocument(document));

        outputChannel.appendLine('Registering subscriptions...');
        context.subscriptions.push(
            disposable,
            previewCommand,
            previewUnwrappedCommand,
            previewUiCommand,
            saveUiStateCommand,
            showInsertCommand,
            printToPdfCommand,
            refreshVariablesCommand,
            refreshDocumentCommand,
            stopServerCommand,
            exportToPdfCommand,
            prettifyDocumentCommand,
            vueUiProviderDisposable,
            vueUiProvider, // Add the provider itself for disposal
            linter,
            semanticTokensDisposable,
            outputChannel,
            serverDebugChannel,
            onDidChangeTextDocument,
            onDidOpenTextDocument,
            onDidSaveTextDocument,
            onDidChangeActiveTextEditor,
            onDidChangeConfiguration,
            operatorReplacerDisposable,
            quickTyperDisposable,
            autoIndenterDisposable,
            imagePasteDisposable,
            imageInsertCommandDisposable,
            ...commentFormatterDisposables,
            completionProviderDisposable,
            includeCompletionDisposable,
            definitionProviderDisposable,
            referenceProviderDisposable,
            renameProviderDisposable,
            hoverProviderDisposable,
            insertManager,
            viewWebviewSourceCommand,
            webviewSourceRegistration
        );
        
        outputChannel.appendLine('CalcPad extension activation completed successfully');
        
    } catch (error) {
        console.error('CalcPad extension activation failed:', error);
        if (outputChannel) {
            outputChannel.appendLine(`FATAL ERROR during activation: ${error}`);
        }
        // Still try to show the error to user
        vscode.window.showErrorMessage(`CalcPad extension failed to activate: ${error instanceof Error ? error.message : 'Unknown error'}`);
        throw error; // Re-throw to mark extension as failed
    }
}

export async function deactivate() {
    if (serverManager) {
        // Leave the server running for other VS Code instances (option C).
        // Use the `CalcPad: Stop Server` command to actually kill it.
        serverManager.disconnect();
    }
    if (linter) {
        linter.dispose();
    }
}