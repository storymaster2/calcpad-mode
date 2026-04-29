import * as monaco from 'monaco-editor';
import { createApp, nextTick } from 'vue';
import App from './App.vue';
import CalcpadAppVue from 'calcpad-frontend/vue/components/CalcpadApp.vue';
import { initMessaging } from 'calcpad-frontend/vue/services/messaging';
import { MessageBridge } from './services/message-bridge';
import { buildApiSettings } from 'calcpad-frontend/types/settings';
import {
    getDatagridCdnTags,
    getUiEventScript,
    htmlHasDatagrids,
} from 'calcpad-frontend/services/ui-preview';
import { registerCalcpadLanguage, registerCalcpadTheme, createCalcpadEditor } from './editor/setup';
import { registerSemanticTokensProvider } from './editor/semantic-tokens';
import { setupDiagnostics } from './editor/diagnostics';
import { registerCompletionProvider } from './editor/completions';
import { registerHoverProvider } from './editor/hover';
import {
    registerDefinitionProvider,
    registerReferenceProvider,
    registerRenameProvider,
} from './editor/references';
import { attachQuickTyper } from './editor/quick-type';
import { attachOperatorReplacer } from './editor/operator-replacer';
import { attachAutoIndenter } from './editor/auto-indent';
import { registerFormattingCommands } from './editor/formatting-commands';
import { registerFormatDocumentProvider } from './editor/format-document';
import { EDITOR_DOCUMENT_KEY, type EditorBridge } from './editor/bridge';
import './editor/vscode-variables.css';
import './styles/app.css';

// Monaco worker setup — must run before editor creation
import './editor/workers';

/** Runtime check: are we running inside a Neutralino window? */
const isNeutralino = typeof (window as any).NL_TOKEN !== 'undefined';

// Determine server URL:
// 1. ?server= query param
// 2. VITE_SERVER_URL env var
// 3. Default to same origin
function getServerUrl(): string {
    const params = new URLSearchParams(window.location.search);
    const fromParam = params.get('server');
    if (fromParam) return fromParam;

    if (import.meta.env.VITE_SERVER_URL) return import.meta.env.VITE_SERVER_URL;

    return window.location.origin;
}

function getSampleContent(): string {
    return `'CalcPad Web Editor
'Enter your calculations below

a = 3
b = 4
c = √(a² + b²)
`;
}

/**
 * Wait for the Neutralino server extension to broadcast its URL.
 * Falls back to the default URL after a timeout.
 */
async function waitForServerExtension(timeoutMs: number = 10000): Promise<string> {
    const { events, init } = await import('@neutralinojs/lib');
    init();

    return new Promise<string>((resolve) => {
        const timer = setTimeout(() => {
            // Extension didn't respond in time — use fallback
            resolve(getServerUrl());
        }, timeoutMs);

        events.on('serverReady', (evt: any) => {
            clearTimeout(timer);
            resolve(evt.detail?.url ?? getServerUrl());
        });
    });
}

/**
 * Set up the Neutralino native menu bar. The recents submenu is rebuilt
 * each time this function is called.
 */
async function setupNeutralinoMenu(recents: string[], previewMode: PreviewMode): Promise<void> {
    const { window: nWindow } = await import('@neutralinojs/lib');

    const sep = { id: '-', text: '-' };
    const recentItems: any[] = recents.length === 0
        ? [{ id: 'recent-empty', text: '(no recent files)' }]
        : recents.map((p, i) => ({ id: `recent:${i}`, text: shortenPath(p) }));
    if (recents.length > 0) {
        recentItems.push(sep);
        recentItems.push({ id: 'recent-clear', text: 'Clear Recently Opened' });
    }

    const previewModeItems: any[] = [
        { id: 'preview-mode:wrapped',    text: previewMode === 'wrapped'    ? '✓ Wrapped'    : '  Wrapped' },
        { id: 'preview-mode:unwrapped',  text: previewMode === 'unwrapped'  ? '✓ Unwrapped'  : '  Unwrapped' },
        { id: 'preview-mode:ui',         text: previewMode === 'ui'         ? '✓ Interactive' : '  Interactive' },
    ];

    await nWindow.setMainMenu([
        {
            id: 'file',
            text: 'File',
            menuItems: [
                { id: 'new', text: 'New', shortcut: 'Ctrl+N' },
                { id: 'open', text: 'Open...', shortcut: 'Ctrl+O' },
                { id: 'open-recent', text: 'Open Recent', menuItems: recentItems },
                sep,
                { id: 'save', text: 'Save', shortcut: 'Ctrl+S' },
                { id: 'save-as', text: 'Save As...', shortcut: 'Ctrl+Shift+S' },
                sep,
                { id: 'export-pdf', text: 'Export PDF...' },
                sep,
                { id: 'quit', text: 'Quit', shortcut: 'Ctrl+Q' },
            ],
        },
        {
            id: 'view',
            text: 'View',
            menuItems: [
                { id: 'toggle-sidebar', text: 'Toggle Sidebar', shortcut: 'Ctrl+B' },
                { id: 'toggle-preview', text: 'Toggle Preview', shortcut: 'Ctrl+P' },
                { id: 'preview-mode', text: 'Preview Mode', menuItems: previewModeItems },
            ],
        },
    ]);
}

type PreviewMode = 'wrapped' | 'unwrapped' | 'ui';

/** Inject datagrid CDN + UI event script into UI-mode preview HTML. */
function injectUiAssets(html: string): string {
    const cdn = htmlHasDatagrids(html) ? getDatagridCdnTags() : '';
    const script = getUiEventScript('window.parent.postMessage', true);
    const head = '<head>';
    const idx = html.indexOf(head);
    if (idx >= 0) {
        return html.slice(0, idx + head.length) + cdn + html.slice(idx + head.length) + script;
    }
    // No <head> — concatenate.
    return cdn + html + script;
}

function shortenPath(path: string, max = 60): string {
    if (path.length <= max) return path;
    const parts = path.split(/[\\/]/);
    const name = parts.pop() ?? path;
    return '…' + (path.length - name.length > 0 ? path.slice(-(max - name.length - 1)) : name);
}

async function bootstrap(): Promise<void> {
    let serverUrl: string;
    let bridge: MessageBridge | null = null;
    let neuBridge: any = null;

    if (isNeutralino) {
        // Neutralino desktop: wait for server extension and use native bridge
        serverUrl = await waitForServerExtension();
        const { NeutralinoMessageBridge } = await import('./services/neutralino-bridge');
        neuBridge = new NeutralinoMessageBridge(serverUrl);
        (window as any).calcpadBridge = neuBridge;
    } else {
        // Pure web: use in-process web bridge
        serverUrl = getServerUrl();
        bridge = new MessageBridge(serverUrl);
        (window as any).calcpadBridge = bridge;
    }

    const activeBridge = neuBridge ?? bridge!;

    // Initialize the platform messaging (reads VITE_PLATFORM='web')
    initMessaging();

    // Mount the main app layout
    const app = createApp(App, { isNeutralino });
    const appInstance = app.mount('#app') as any;

    // Wait for DOM to render, then set up Monaco editor
    await nextTick();

    const editorEl = document.querySelector('.editor-container') as HTMLElement;
    if (!editorEl) {
        throw new Error('Editor container not found');
    }

    registerCalcpadLanguage();
    registerCalcpadTheme();
    const editor = createCalcpadEditor(editorEl, {
        value: isNeutralino ? '' : getSampleContent(),
    });

    // Wire the bridge's insertText handler to Monaco
    activeBridge.onInsertText = (text: string) => {
        const selection = editor.getSelection();
        if (selection) {
            editor.executeEdits('calcpad-insert', [{
                range: selection,
                text,
                forceMoveMarkers: true,
            }]);
        }
        editor.focus();
    };

    // Wire Output panel: intercept console methods to pipe into the panel
    const origDebug = console.debug;
    const origWarn = console.warn;
    const origError = console.error;

    console.debug = (...args: any[]) => {
        origDebug.apply(console, args);
        const msg = args.map(a => typeof a === 'string' ? a : JSON.stringify(a)).join(' ');
        appInstance.appendOutput('debug', msg);
    };
    console.warn = (...args: any[]) => {
        origWarn.apply(console, args);
        const msg = args.map(a => typeof a === 'string' ? a : JSON.stringify(a)).join(' ');
        appInstance.appendOutput('warn', msg);
    };
    console.error = (...args: any[]) => {
        origError.apply(console, args);
        const msg = args.map(a => typeof a === 'string' ? a : JSON.stringify(a)).join(' ');
        appInstance.appendOutput('error', msg);
    };

    // Also capture unhandled errors
    window.addEventListener('error', (e) => {
        appInstance.appendOutput('error', `Uncaught: ${e.message} (${e.filename}:${e.lineno})`);
    });
    window.addEventListener('unhandledrejection', (e) => {
        appInstance.appendOutput('error', `Unhandled rejection: ${e.reason}`);
    });

    appInstance.appendOutput('info', `CalcPad Web started — server: ${serverUrl}`);

    // Register Monaco providers
    const editorBridge = activeBridge as unknown as EditorBridge;
    registerSemanticTokensProvider(activeBridge.api);
    setupDiagnostics(editor, activeBridge.api);
    registerCompletionProvider(activeBridge.snippets);
    registerHoverProvider(editorBridge);
    registerDefinitionProvider(editorBridge);
    registerReferenceProvider(editorBridge);
    registerRenameProvider(editorBridge);
    registerFormatDocumentProvider(editorBridge);
    attachQuickTyper(editor, editorBridge);
    attachOperatorReplacer(editor);
    attachAutoIndenter(editor);
    registerFormattingCommands(editor, editorBridge);

    // Keep the definitions cache fresh so hover provider has data to show.
    // Debounced — same cadence as TOC refresh.
    let definitionsTimer: ReturnType<typeof setTimeout> | null = null;
    const refreshDefinitions = () => {
        const content = editor.getValue();
        editorBridge.definitions.refreshDefinitions(content, EDITOR_DOCUMENT_KEY);
    };
    editor.onDidChangeModelContent(() => {
        if (definitionsTimer) clearTimeout(definitionsTimer);
        definitionsTimer = setTimeout(refreshDefinitions, 800);
    });
    setTimeout(refreshDefinitions, 500);

    // Problems panel: listen for marker changes and feed into App
    function markerToSeverityInfo(severity: monaco.MarkerSeverity) {
        switch (severity) {
            case monaco.MarkerSeverity.Error:
                return { severityClass: 'error', icon: '✕' };
            case monaco.MarkerSeverity.Warning:
                return { severityClass: 'warning', icon: '⚠' };
            default:
                return { severityClass: 'info', icon: 'ℹ' };
        }
    }

    monaco.editor.onDidChangeMarkers(([resource]) => {
        const model = editor.getModel();
        if (!model || resource.toString() !== model.uri.toString()) return;

        const markers = monaco.editor.getModelMarkers({ resource });
        const items = markers.map(m => ({
            severity: m.severity,
            ...markerToSeverityInfo(m.severity),
            message: m.message,
            code: typeof m.code === 'string' ? m.code : m.code?.value ?? '',
            startLineNumber: m.startLineNumber,
            startColumn: m.startColumn,
            endLineNumber: m.endLineNumber,
            endColumn: m.endColumn,
        }));

        // Sort: errors first, then warnings, then info
        items.sort((a, b) => b.severity - a.severity);
        appInstance.setProblems(items);
    });

    // Handle click-to-navigate from problems panel
    appInstance.onGotoProblem = (problem: any) => {
        editor.revealLineInCenter(problem.startLineNumber);
        editor.setPosition({
            lineNumber: problem.startLineNumber,
            column: problem.startColumn,
        });
        editor.focus();
    };

    // Mount the CalcPad Vue sidebar
    const sidebarApp = createApp(CalcpadAppVue);
    sidebarApp.mount('#vue-sidebar');

    // HTML preview via convert endpoint (debounced)
    let previewTimer: ReturnType<typeof setTimeout> | null = null;
    // TOC headings refresh (debounced)
    let tocTimer: ReturnType<typeof setTimeout> | null = null;

    // Initialize preview mode from saved extra setting (Neutralino) or localStorage (web).
    const savedMode = (editorBridge.getExtraSetting('previewMode') as 'wrapped' | 'unwrapped' | 'ui' | undefined);
    if (savedMode === 'wrapped' || savedMode === 'unwrapped' || savedMode === 'ui') {
        appInstance.setPreviewMode(savedMode);
    }

    async function refreshPreview(): Promise<void> {
        if (!appInstance.isPreviewVisible()) return;

        const content = editor.getValue();
        const settings = activeBridge.getSettings();
        const apiSettings = buildApiSettings(settings);
        const mode = appInstance.getPreviewMode() as 'wrapped' | 'unwrapped' | 'ui';

        let html: string | ArrayBuffer | null;
        if (mode === 'unwrapped') {
            html = await activeBridge.api.convertUnwrapped(content, apiSettings);
        } else if (mode === 'ui') {
            html = await activeBridge.api.convertUi(content, apiSettings);
        } else {
            html = await activeBridge.api.convert(content, apiSettings, 'html');
        }

        if (typeof html === 'string') {
            const finalHtml = mode === 'ui' ? injectUiAssets(html) : html;
            appInstance.setPreviewHtml(finalHtml);
        }

        // Push #write/#append outputs (if any) to the Export tab.
        if (typeof (activeBridge as any).refreshExports === 'function') {
            void (activeBridge as any).refreshExports();
        }
    }

    // Stub overwritten in the Neutralino branch below; harmless on web.
    let rebuildMenu: (mode: PreviewMode) => Promise<void> = async () => { /* no-op */ };

    appInstance.onPreviewModeChanged = (mode: 'wrapped' | 'unwrapped' | 'ui') => {
        editorBridge.setExtraSetting('previewMode', mode);
        refreshPreview();
        if (isNeutralino) {
            void rebuildMenu(mode);
        }
    };

    editor.onDidChangeModelContent(() => {
        if (appInstance.isPreviewVisible()) {
            if (previewTimer) clearTimeout(previewTimer);
            previewTimer = setTimeout(refreshPreview, 800);
        }
        // Debounced TOC refresh
        if (tocTimer) clearTimeout(tocTimer);
        tocTimer = setTimeout(() => activeBridge.refreshHeadings(), 800);
    });

    // Refresh when preview is first opened
    appInstance.onPreviewToggled = (visible: boolean) => {
        if (visible) {
            setTimeout(refreshPreview, 50);
        }
    };

    // Neutralino-specific: native menu + file operations
    if (isNeutralino && neuBridge) {
        const { events: neuEvents, app: neuApp, window: neuWin, os: neuOs } = await import('@neutralinojs/lib');

        let currentFilePath: string | null = null;
        let recents: string[] = await neuBridge.getRecentFiles();
        let menuPreviewMode: PreviewMode =
            (appInstance.getPreviewMode() as PreviewMode) ?? 'wrapped';

        rebuildMenu = async (mode: PreviewMode) => {
            menuPreviewMode = mode;
            recents = await neuBridge.getRecentFiles();
            await setupNeutralinoMenu(recents, menuPreviewMode);
        };

        await setupNeutralinoMenu(recents, menuPreviewMode);

        async function loadFile(path: string): Promise<void> {
            try {
                const content = await neuBridge!.readFile(path);
                editor.setValue(content);
                currentFilePath = path;
                const name = path.split(/[\\/]/).pop() || path;
                appInstance.setFileName(name);
                appInstance.setDirty(false);
                await neuBridge!.addRecentFile(path);
                await rebuildMenu(menuPreviewMode);
            } catch (err) {
                appInstance.appendOutput('error', 'Failed to open file: ' + (err instanceof Error ? err.message : String(err)));
            }
        }

        // Single mainMenuItemClicked listener — handles static + dynamic IDs.
        neuEvents.on('mainMenuItemClicked', async (evt: any) => {
            const id: string = evt.detail.id;

            // Recent file shortcut
            if (id.startsWith('recent:')) {
                const idx = parseInt(id.split(':')[1], 10);
                const path = recents[idx];
                if (path) await loadFile(path);
                return;
            }
            if (id === 'recent-clear') {
                await neuBridge.clearRecentFiles();
                await rebuildMenu(menuPreviewMode);
                return;
            }
            if (id === 'recent-empty') return;

            // Preview mode picker
            if (id.startsWith('preview-mode:')) {
                const mode = id.split(':')[1] as PreviewMode;
                appInstance.setPreviewMode(mode);
                return;
            }

            switch (id) {
                case 'new':
                    editor.setValue('');
                    currentFilePath = null;
                    appInstance.setFileName('');
                    appInstance.setDirty(false);
                    break;

                case 'open': {
                    const result = await neuBridge.openFile();
                    if (result) await loadFile(result.path);
                    break;
                }

                case 'save': {
                    const content = editor.getValue();
                    if (currentFilePath) {
                        await neuBridge.saveFile(currentFilePath, content);
                        appInstance.setDirty(false);
                    } else {
                        const newPath = await neuBridge.saveFileAs(content);
                        if (newPath) {
                            currentFilePath = newPath;
                            const name = newPath.split(/[\\/]/).pop() || newPath;
                            appInstance.setFileName(name);
                            appInstance.setDirty(false);
                            await neuBridge.addRecentFile(newPath);
                            await rebuildMenu(menuPreviewMode);
                        }
                    }
                    break;
                }

                case 'save-as': {
                    const content = editor.getValue();
                    const newPath = await neuBridge.saveFileAs(content);
                    if (newPath) {
                        currentFilePath = newPath;
                        const name = newPath.split(/[\\/]/).pop() || newPath;
                        appInstance.setFileName(name);
                        appInstance.setDirty(false);
                        await neuBridge.addRecentFile(newPath);
                        await rebuildMenu(menuPreviewMode);
                    }
                    break;
                }

                case 'export-pdf':
                    neuBridge.handleMessage({ type: 'generatePdf' });
                    break;

                case 'toggle-sidebar':
                    appInstance.toggleSidebar();
                    break;

                case 'toggle-preview':
                    appInstance.togglePreview();
                    break;

                case 'quit':
                    await tryExit();
                    break;
            }
        });

        // Track dirty state
        editor.onDidChangeModelContent(() => {
            appInstance.setDirty(true);
        });

        // ---- Drag-drop file open ----
        const dropTarget = document.querySelector('.editor-container') as HTMLElement | null;
        if (dropTarget) {
            dropTarget.addEventListener('dragover', e => {
                e.preventDefault();
                if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy';
            });
            dropTarget.addEventListener('drop', async e => {
                e.preventDefault();
                const files = e.dataTransfer?.files;
                if (!files || files.length === 0) return;
                // Neutralino exposes the OS path on `File.path` (Chromium extension).
                const dropped = files[0] as File & { path?: string };
                if (dropped.path) {
                    await loadFile(dropped.path);
                } else {
                    // Fallback: read text content via FileReader.
                    const text = await dropped.text();
                    editor.setValue(text);
                    currentFilePath = null;
                    appInstance.setFileName(dropped.name);
                    appInstance.setDirty(false);
                }
            });
        }

        // ---- Close-with-unsaved guard ----
        async function tryExit(): Promise<void> {
            if (!appInstance.isDirty?.()) {
                await neuApp.exit();
                return;
            }
            const choice = await neuOs.showMessageBox(
                'Unsaved changes',
                'You have unsaved changes. Save before closing?',
                'YES_NO_CANCEL' as any,
                'QUESTION' as any,
            );
            if (choice === 'CANCEL') return;
            if (choice === 'YES') {
                const content = editor.getValue();
                if (currentFilePath) {
                    await neuBridge.saveFile(currentFilePath, content);
                } else {
                    const path = await neuBridge.saveFileAs(content);
                    if (!path) return; // user cancelled save dialog → abort exit
                }
            }
            await neuApp.exit();
        }

        // Intercept the window close button.
        try {
            await neuWin.setAlwaysOnTop(false); // touch the API to verify presence; no-op
        } catch { /* ignore */ }
        neuEvents.on('windowClose', () => { void tryExit(); });
    }
}

bootstrap();
