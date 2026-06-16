import * as monaco from 'monaco-editor';
import { createApp, nextTick } from 'vue';
import App from './App.vue';
import CalcpadAppVue from 'calcpad-frontend/vue/components/CalcpadApp.vue';
import { initMessaging } from 'calcpad-frontend/vue/services/messaging';
import { MessageBridge } from './services/message-bridge';
import { buildApiSettings } from 'calcpad-frontend/types/settings';
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
import { setActiveDocumentKeyResolver, type EditorBridge } from './editor/bridge';
import { TabManager } from './tabs/tab-manager';
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

/** Idle-state preview HTML — same content the VS Code extension shows when
 *  the editor buffer is empty. Quick reference for formatting hotkeys. */
function getEmptyPreviewHtml(): string {
    return `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>CalcPad Preview</title>
    <style>
        body { color: #858585; background: var(--vscode-editor-background, #1e1e1e); padding: 20px; font-family: var(--vscode-font-family, system-ui, sans-serif); }
        h3 { text-align: center; }
        p { text-align: center; }
        table { margin: 1em auto; border-collapse: collapse; text-align: left; font-size: 0.9em; }
        th, td { padding: 4px 12px; }
        th { text-align: right; font-weight: normal; opacity: 0.7; }
        td { font-family: var(--vscode-editor-font-family, monospace); }
        h4 { text-align: center; margin-top: 1.5em; margin-bottom: 0.3em; }
        a { color: #4FC1FF; }
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
</html>`;
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
 * Discover the Neutralino server-extension's URL.
 *
 * The .NET server binds to a random free port (`--urls http://localhost:0`)
 * and writes the bound URL to `extensions/server/.calcpad-server.port`
 * when Kestrel finishes binding. We poll that file with a backoff until
 * it appears, then return its contents.
 *
 * Falls back to `http://localhost:9420` if the file never appears within
 * `timeoutMs` — that shouldn't happen in practice but keeps the UI from
 * locking up if the server died on launch (the user will get the usual
 * "Disconnected" status indicator and the server-stderr.log will explain).
 */
async function waitForServerExtension(timeoutMs: number = 15000): Promise<string> {
    const { init, filesystem } = await import('@neutralinojs/lib');
    init();

    const NL_PATH = (window as Window & { NL_PATH?: string }).NL_PATH ?? '';
    const portFile = `${NL_PATH}/extensions/server/.calcpad-server.port`;
    const fallback = 'http://localhost:9420';

    const deadline = Date.now() + timeoutMs;
    let delay = 50;
    while (Date.now() < deadline) {
        try {
            const url = (await filesystem.readFile(portFile)).trim();
            if (url) return url;
        } catch {
            // File not written yet — keep polling with capped backoff.
        }
        await new Promise((r) => setTimeout(r, delay));
        delay = Math.min(delay * 1.5, 500);
    }
    console.warn(`[bootstrap] Port file ${portFile} did not appear within ${timeoutMs}ms; falling back to ${fallback}`);
    return fallback;
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
    ];

    await nWindow.setMainMenu([
        {
            id: 'file',
            text: 'File',
            menuItems: [
                { id: 'new', text: 'New Tab', shortcut: 'Ctrl+N' },
                { id: 'open', text: 'Open...', shortcut: 'Ctrl+O' },
                { id: 'open-recent', text: 'Open Recent', menuItems: recentItems },
                sep,
                { id: 'save', text: 'Save', shortcut: 'Ctrl+S' },
                { id: 'save-as', text: 'Save As...', shortcut: 'Ctrl+Shift+S' },
                sep,
                { id: 'close-tab', text: 'Close Tab', shortcut: 'Ctrl+W' },
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
                { id: 'toggle-sidebar', text: 'Toggle Sidebar', shortcut: 'Ctrl+Shift+B' },
                { id: 'toggle-preview', text: 'Toggle Preview', shortcut: 'Ctrl+P' },
                { id: 'preview-mode', text: 'Preview Mode', menuItems: previewModeItems },
            ],
        },
        {
            id: 'server',
            text: 'Server',
            menuItems: [
                { id: 'refresh', text: 'Refresh', shortcut: 'Ctrl+R' },
                { id: 'show-server-log', text: 'Show Server Log' },
                { id: 'restart-app', text: 'Restart App' },
            ],
        },
    ]);
}

type PreviewMode = 'wrapped' | 'unwrapped';

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
        value: '',
    });

    // ---- Tab manager ----
    // Owns the per-tab Monaco models. The single `editor` instance gets
    // its model swapped on tab activation; view state is saved/restored
    // automatically. This is the same pattern VS Code uses.
    const tabs = new TabManager(editor);
    (window as any).calcpadTabs = tabs;

    // Editor providers + hover/definitions cache use this to scope per-tab.
    setActiveDocumentKeyResolver(() => `tab:${tabs.activeId ?? 'none'}`);

    function activeDocumentKey(): string {
        return `tab:${tabs.activeId ?? 'none'}`;
    }

    // Push the tab list into App.vue's tab strip whenever it changes.
    // Registered BEFORE the seed tab so the strip is populated on first render.
    tabs.onTabsChanged((snapshots) => {
        appInstance.setTabs(snapshots);
        // Keep the legacy single-file-name display in sync with the active tab.
        const active = snapshots.find(t => t.isActive);
        if (active) {
            appInstance.setFileName(active.title);
            appInstance.setDirty(active.dirty);
        } else {
            appInstance.setFileName('');
            appInstance.setDirty(false);
        }
    });

    // Seed the first tab. On web we put the sample in it; on desktop it's
    // an empty Untitled-1 ready to receive an Open or paste.
    tabs.newUntitled(isNeutralino ? '' : getSampleContent());

    // Universal tab-strip callbacks. The Neutralino branch overrides the
    // close handler with a save-prompt-aware version; on web there's
    // nothing to save, so a plain close is correct.
    appInstance.onTabActivate = (id: string) => tabs.activate(id);
    appInstance.onTabCloseRequest = (id: string) => tabs.close(id);
    appInstance.onNewTabRequest = () => { tabs.newUntitled(); };

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

    // Wire Output panel: intercept console methods to pipe into the panel.
    // Covers log/info/debug/warn/error so any call from app code lands in the
    // CalcPad output channel.
    function fmtConsoleArg(a: unknown): string {
        if (typeof a === 'string') return a;
        if (a instanceof Error) return a.stack ?? a.message;
        try {
            return JSON.stringify(a);
        } catch {
            return String(a);
        }
    }
    const origLog = console.log;
    const origInfo = console.info;
    const origDebug = console.debug;
    const origWarn = console.warn;
    const origError = console.error;

    const wrap = (
        orig: (...args: any[]) => void,
        level: 'info' | 'debug' | 'warn' | 'error',
    ) => (...args: any[]) => {
        orig.apply(console, args);
        appInstance.appendOutput(level, args.map(fmtConsoleArg).join(' '));
    };

    console.log = wrap(origLog, 'info');
    console.info = wrap(origInfo, 'info');
    console.debug = wrap(origDebug, 'debug');
    console.warn = wrap(origWarn, 'warn');
    console.error = wrap(origError, 'error');

    // Also capture unhandled errors
    window.addEventListener('error', (e) => {
        appInstance.appendOutput('error', `Uncaught: ${e.message} (${e.filename}:${e.lineno})`);
    });
    window.addEventListener('unhandledrejection', (e) => {
        appInstance.appendOutput('error', `Unhandled rejection: ${e.reason}`);
    });

    // Forward console messages from the preview iframe to the Output panel's
    // "Preview Console" channel. Patched in App.vue:injectPreviewConsole.
    window.addEventListener('message', (e: MessageEvent) => {
        const data = e.data;
        if (!data || data.type !== 'previewConsole') return;
        const level: 'info' | 'warn' | 'error' | 'debug' =
            data.level === 'warn' ? 'warn'
            : data.level === 'error' ? 'error'
            : data.level === 'debug' ? 'debug'
            : 'info';
        appInstance.appendOutput(level, String(data.message ?? ''), 'preview');
    });

    appInstance.appendOutput('info', `CalcPad Web started — server: ${serverUrl}`);

    // Register Monaco providers
    const editorBridge = activeBridge as unknown as EditorBridge;
    registerSemanticTokensProvider(activeBridge.api);
    const diagnostics = setupDiagnostics(editor, activeBridge.api, () => {
        const sev = editorBridge.getExtraSetting('linterMinSeverity');
        return (sev === 'error' || sev === 'warning') ? sev : 'information';
    });
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
        editorBridge.definitions.refreshDefinitions(content, activeDocumentKey());
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

    function refreshProblemsForActiveModel(): void {
        const model = editor.getModel();
        if (!model) {
            appInstance.setProblems([]);
            return;
        }
        const markers = monaco.editor.getModelMarkers({ resource: model.uri });
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
        items.sort((a, b) => b.severity - a.severity);
        appInstance.setProblems(items);
    }

    monaco.editor.onDidChangeMarkers(([resource]) => {
        const model = editor.getModel();
        if (!model || resource.toString() !== model.uri.toString()) return;
        refreshProblemsForActiveModel();
    });

    // On tab switch, re-emit the new active model's markers so Problems
    // doesn't show stale data from the previous tab.
    tabs.onActiveModelChanged(() => {
        refreshProblemsForActiveModel();
        // Also kick the preview/TOC to repaint against the new content.
        if (appInstance.isPreviewVisible()) {
            void refreshPreview();
        }
        activeBridge.refreshHeadings();
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
    const savedMode = (editorBridge.getExtraSetting('previewMode') as 'wrapped' | 'unwrapped' | undefined);
    if (savedMode === 'wrapped' || savedMode === 'unwrapped') {
        appInstance.setPreviewMode(savedMode);
    }

    async function refreshPreview(): Promise<void> {
        if (!appInstance.isPreviewVisible()) return;

        const content = editor.getValue();
        const settings = activeBridge.getSettings();
        const apiSettings = buildApiSettings(settings);
        const mode = appInstance.getPreviewMode() as 'wrapped' | 'unwrapped';

        if (!content.trim()) {
            appInstance.setPreviewHtml(getEmptyPreviewHtml());
            return;
        }

        let html: string | ArrayBuffer | null;
        if (mode === 'unwrapped') {
            html = await activeBridge.api.convertUnwrapped(content, apiSettings);
        } else {
            html = await activeBridge.api.convert(content, apiSettings, 'html');
        }

        if (typeof html === 'string') {
            appInstance.setPreviewHtml(html);
        }
    }

    // Manual refresh: clear server cache, re-lint with current settings,
    // refresh definitions/headings, redraw preview. Called from the
    // Server > Refresh menu item.
    async function runRefresh(): Promise<void> {
        appInstance.appendOutput('info', 'Refreshing…');
        try {
            const cleared = await activeBridge.api.refreshCache();
            appInstance.appendOutput(
                cleared ? 'info' : 'warn',
                cleared ? 'Server cache cleared' : 'Server cache clear failed',
            );
        } catch (err) {
            appInstance.appendOutput('warn', `Cache clear error: ${err instanceof Error ? err.message : String(err)}`);
        }

        await diagnostics.refresh();
        editorBridge.definitions.refreshDefinitions(editor.getValue(), activeDocumentKey());
        activeBridge.refreshHeadings();
        if (appInstance.isPreviewVisible()) {
            await refreshPreview();
        }
    }

    // Stub overwritten in the Neutralino branch below; harmless on web.
    let rebuildMenu: (mode: PreviewMode) => Promise<void> = async () => { /* no-op */ };

    appInstance.onPreviewModeChanged = (mode: 'wrapped' | 'unwrapped') => {
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
        const { events: neuEvents, app: neuApp } = await import('@neutralinojs/lib');

        let recents: string[] = await neuBridge.getRecentFiles();
        let menuPreviewMode: PreviewMode =
            (appInstance.getPreviewMode() as PreviewMode) ?? 'wrapped';

        rebuildMenu = async (mode: PreviewMode) => {
            menuPreviewMode = mode;
            recents = await neuBridge.getRecentFiles();
            await setupNeutralinoMenu(recents, menuPreviewMode);
        };

        await setupNeutralinoMenu(recents, menuPreviewMode);

        /**
         * Open `path` in a tab. If a tab already holds that file, focuses it
         * (matching VS Code's "go to existing tab" behavior). Otherwise reads
         * from disk and asks TabManager to open it (which may reuse an empty
         * untitled scratch tab).
         */
        async function loadFile(path: string): Promise<void> {
            const existing = tabs.findByPath(path);
            if (existing) {
                tabs.activate(existing.id);
                return;
            }
            try {
                const content = await neuBridge!.readFile(path);
                tabs.openFile(path, content);
                await neuBridge!.addRecentFile(path);
                await rebuildMenu(menuPreviewMode);
            } catch (err) {
                appInstance.appendOutput('error', 'Failed to open file: ' + (err instanceof Error ? err.message : String(err)));
            }
        }

        /**
         * Save the active tab. If it has no file path, prompts for one.
         * Returns true if saved, false if the user cancelled the path prompt
         * or if there is no active tab.
         */
        async function saveActive(): Promise<boolean> {
            const active = tabs.activeTab;
            if (!active) return false;
            const content = tabs.activeModel?.getValue() ?? '';
            if (active.filePath) {
                await neuBridge!.saveFile(active.filePath, content);
                tabs.markActiveSaved();
                return true;
            }
            const newPath = await neuBridge!.saveFileAs(content);
            if (!newPath) return false;
            tabs.markActiveSaved({ filePath: newPath });
            await neuBridge!.addRecentFile(newPath);
            await rebuildMenu(menuPreviewMode);
            return true;
        }

        async function saveAsActive(): Promise<boolean> {
            const content = tabs.activeModel?.getValue() ?? '';
            const newPath = await neuBridge!.saveFileAs(content);
            if (!newPath) return false;
            tabs.markActiveSaved({ filePath: newPath });
            await neuBridge!.addRecentFile(newPath);
            await rebuildMenu(menuPreviewMode);
            return true;
        }

        /**
         * Close a tab, prompting if dirty. Returns true on close, false if
         * the user cancelled the prompt.
         */
        async function tryCloseTab(id: string): Promise<boolean> {
            const target = tabs.all.find(t => t.id === id);
            if (!target) return true;
            if (target.dirty) {
                // Activate the tab the user is being asked about so the
                // editor shows its content while they decide.
                if (id !== tabs.activeId) tabs.activate(id);
                const choice = await appInstance.showConfirm({
                    title: 'Unsaved changes',
                    message: `Save changes to ${target.title} before closing?`,
                    yesLabel: 'Save',
                    noLabel: "Don't Save",
                });
                if (choice === 'cancel') return false;
                if (choice === 'yes') {
                    const saved = await saveActive();
                    if (!saved) return false;
                }
            }
            tabs.close(id);
            // Closing the last tab quits the app, matching the X-button flow.
            // tryExit() re-runs the dirty prompt for any tab still open (none
            // here, since we just closed the only one), so this is safe.
            if (tabs.count === 0) {
                void tryExit();
            }
            return true;
        }

        // ---- Wire tab-strip user actions to TabManager ----
        appInstance.onTabActivate = (id: string) => tabs.activate(id);
        appInstance.onTabCloseRequest = (id: string) => { void tryCloseTab(id); };
        appInstance.onNewTabRequest = () => { tabs.newUntitled(); };

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
                    tabs.newUntitled();
                    break;

                case 'close-tab': {
                    const activeId = tabs.activeId;
                    if (activeId) await tryCloseTab(activeId);
                    break;
                }

                case 'open': {
                    const result = await neuBridge.openFile();
                    if (result) await loadFile(result.path);
                    break;
                }

                case 'save':
                    await saveActive();
                    break;

                case 'save-as':
                    await saveAsActive();
                    break;

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

                case 'refresh':
                    await runRefresh();
                    break;

                case 'show-server-log':
                    appInstance.appendOutput('info', 'Fetching server log…');
                    neuBridge.handleMessage({ type: 'getServerLog' });
                    break;

                case 'restart-app':
                    await neuApp.restartProcess();
                    break;
            }
        });

        // Server stderr (captured by start-server.sh) and PDF errors flow
        // through bridge → window message → Output panel. This is the
        // desktop analog of VS Code's stderr Output channel.
        window.addEventListener('message', (e) => {
            const data = (e as MessageEvent).data;
            if (!data || typeof data !== 'object') return;
            if (data.type === 'serverLogResponse') {
                if (data.error) {
                    appInstance.appendOutput('warn',
                        `Server log unavailable (${data.path || '<unknown>'}): ${data.error}`);
                    return;
                }
                const text = (data.content || '').trim();
                if (!text) {
                    appInstance.appendOutput('info',
                        `Server log is empty: ${data.path}`);
                    return;
                }
                appInstance.appendOutput('info', `--- Server stderr (${data.path}) ---`);
                for (const line of text.split('\n')) {
                    if (!line.trim()) continue;
                    const sev = /WARNING|warn/i.test(line) ? 'warn' : 'error';
                    appInstance.appendOutput(sev, line);
                }
                appInstance.appendOutput('info', '--- end server stderr ---');
            } else if (data.type === 'pdfError') {
                appInstance.appendOutput('error', String(data.message || 'PDF export failed'));
            }
        });

        // (Per-tab dirty tracking is handled by TabManager; the legacy
        // single-document setDirty(true)-on-edit hook is removed.)

        // ---- Drag-drop file open ----
        // Each dropped file opens (or focuses) its own tab.
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
                for (const file of Array.from(files)) {
                    // Neutralino exposes the OS path on `File.path` (Chromium extension).
                    const dropped = file as File & { path?: string };
                    if (dropped.path) {
                        await loadFile(dropped.path);
                    } else {
                        // No OS path (web-style drop) — open as untitled.
                        const text = await dropped.text();
                        tabs.newUntitled(text);
                    }
                }
            });
        }

        // ---- Keyboard shortcuts (window-level) ----
        // VS Code-style tab navigation. Ctrl+T / Ctrl+W are also bound via
        // the menu accelerators above; this catches them when the focus is
        // outside the menu's scope (e.g. preview iframe).
        // Monaco swallows several Ctrl+ keys as internal commands, so the
        // Neutralino menu accelerators never fire while the editor has focus.
        // Bind the file-management ones directly on the editor.
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => {
            void saveActive();
        });
        editor.addCommand(
            monaco.KeyMod.CtrlCmd | monaco.KeyMod.Shift | monaco.KeyCode.KeyS,
            () => { void saveAsActive(); },
        );
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyO, async () => {
            const result = await neuBridge.openFile();
            if (result) await loadFile(result.path);
        });

        window.addEventListener('keydown', (e) => {
            if (!e.ctrlKey || e.metaKey) return;
            // Ctrl+S / Ctrl+Shift+S — fallback when focus is outside the editor
            // (sidebar, preview iframe parent, etc.).
            if ((e.key === 's' || e.key === 'S') && !e.altKey) {
                e.preventDefault();
                if (e.shiftKey) void saveAsActive(); else void saveActive();
                return;
            }
            // Ctrl+O — open file picker.
            if ((e.key === 'o' || e.key === 'O') && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                void (async () => {
                    const result = await neuBridge.openFile();
                    if (result) await loadFile(result.path);
                })();
                return;
            }
            // Ctrl+Shift+B → toggle sidebar (Ctrl+B is reserved for Bold formatting).
            if (e.shiftKey && (e.key === 'B' || e.key === 'b') && !e.altKey) {
                e.preventDefault();
                appInstance.toggleSidebar();
                return;
            }
            if (e.key === 'Tab') {
                e.preventDefault();
                if (e.shiftKey) tabs.activatePrev(); else tabs.activateNext();
                return;
            }
            if (e.key === 't' && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                tabs.newUntitled();
                return;
            }
            if (e.key === 'w' && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                const id = tabs.activeId;
                if (id) void tryCloseTab(id);
                return;
            }
            // Ctrl+1..9 → activate Nth tab (Ctrl+9 = last, matching VS Code).
            if (e.key >= '1' && e.key <= '9' && !e.shiftKey && !e.altKey) {
                const n = parseInt(e.key, 10);
                if (n === 9) {
                    tabs.activateByIndex(tabs.count - 1);
                } else {
                    tabs.activateByIndex(n - 1);
                }
                e.preventDefault();
            }
        });

        // ---- Close-with-unsaved guard ----
        let isExiting = false;

        async function tryExit(): Promise<void> {
            if (isExiting) return;        // re-entry guard (multiple X clicks)
            isExiting = true;

            try {
                // Walk every dirty tab one at a time, like VS Code does on
                // window-close. Reuses tryCloseTab so the prompt copy +
                // save-as fallback are identical to manual tab close.
                const dirtyIds = tabs.all.filter(t => t.dirty).map(t => t.id);
                for (const id of dirtyIds) {
                    const closed = await tryCloseTab(id);
                    if (!closed) {
                        // User cancelled — abort exit.
                        isExiting = false;
                        return;
                    }
                }
            } finally {
                if (isExiting) {
                    appInstance.appendOutput('debug', 'Exit path: calling neuApp.exit()');
                    neuApp.exit()
                        .then(() => appInstance.appendOutput('debug', 'neuApp.exit() resolved'))
                        .catch((e) => appInstance.appendOutput('debug', `neuApp.exit() rejected: ${e?.code || e?.message || e}`));
                    setTimeout(() => {
                        appInstance.appendOutput('debug', 'Exit path: calling neuApp.killProcess() fallback');
                        neuApp.killProcess()
                            .then(() => appInstance.appendOutput('debug', 'killProcess resolved'))
                            .catch((e) => appInstance.appendOutput('debug', `killProcess rejected: ${e?.code || e?.message || e}`));
                    }, 500);
                }
            }
        }

        neuEvents.on('windowClose', () => { void tryExit(); });
    }
}

bootstrap();
