import * as monaco from 'monaco-editor';
import { createApp, nextTick } from 'vue';
import App from './App.vue';
import CalcpadAppVue from 'calcpad-frontend/vue/components/CalcpadApp.vue';
import { initMessaging } from 'calcpad-frontend/vue/services/messaging';
import { MessageBridge } from './services/message-bridge';
import { buildApiSettings } from 'calcpad-frontend/types/settings';
import { registerCalcpadLanguage, registerCalcpadTheme, createCalcpadEditor, remeasureEditorFontsWhenReady } from './editor/setup';
import { setAppTheme, coerceAppTheme } from './editor/app-theme';
import { registerSemanticTokensProvider } from './editor/semantic-tokens';
import { setupDiagnostics } from './editor/diagnostics';
import { registerCompletionProvider } from './editor/completions';
import { registerIncludeCompletionProvider } from './editor/include-completions';
import { registerHoverProvider } from './editor/hover';
import {
    registerDefinitionProvider,
    registerReferenceProvider,
    registerRenameProvider,
    type IncludeFileOpener,
} from './editor/references';
import { attachQuickTyper } from './editor/quick-type';
import { attachOperatorReplacer } from './editor/operator-replacer';
import { attachAutoIndenter } from './editor/auto-indent';
import { registerFormattingCommands } from './editor/formatting-commands';
import { registerFormatDocumentProvider } from './editor/format-document';
import { setActiveDocumentKeyResolver, type EditorBridge } from './editor/bridge';
import { TabManager } from './tabs/tab-manager';
import './editor/vscode-variables.css';
import 'calcpad-frontend/vue/styles/base.css';
import './styles/app.css';

// Monaco worker setup — must run before editor creation
import './editor/workers';

/** Runtime check: are we running inside a Tauri webview? */
const isTauri = typeof (window as any).__TAURI_INTERNALS__ !== 'undefined';

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
 * Encode raw RGBA pixels (as returned by Tauri's native clipboard readImage)
 * to PNG bytes via an offscreen canvas, so a pasted image can be embedded or
 * saved like a file-picked one.
 */
async function rgbaToPng(rgba: Uint8Array, width: number, height: number): Promise<Uint8Array | null> {
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    if (!ctx) return null;
    ctx.putImageData(new ImageData(new Uint8ClampedArray(rgba), width, height), 0, 0);
    const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/png'));
    if (!blob) return null;
    return new Uint8Array(await blob.arrayBuffer());
}

/**
 * Native message box shown when the calculation server never becomes ready.
 * The editor itself keeps working; only server-backed features (preview,
 * linting, export) need it.
 */
async function showServerBlockedDialog(details: string): Promise<void> {
    const { message: dialogMessage } = await import('@tauri-apps/plugin-dialog');
    const body =
        "CalcPad's calculation server started but never became ready.\n\n"
        + 'The editor still works, but preview, linting, and PDF/Word export '
        + 'need the server. Choose Server → Restart Server to try again.\n\n'
        + `Details: ${details}`;
    try {
        await dialogMessage(body, {
            title: 'CalcPad server unavailable',
            kind: 'warning',
            okLabel: 'OK',
        });
    } catch {
        // dialog can throw if the runtime is tearing down — the buffered log
        // line in the Output panel is the fallback.
    }
}

type PreviewMode = 'wrapped' | 'unwrapped';

async function bootstrap(): Promise<void> {
    let serverUrl: string;
    let bridge: MessageBridge | null = null;
    let tauriBridge: import('./services/tauri-bridge').TauriMessageBridge | null = null;
    let serverManager: import('./services/server-manager').TauriServerManager | null = null;
    // Server-manager log lines that arrive before the Output panel mounts
    // get buffered here, then flushed when appInstance is ready.
    const pendingServerLogs: string[] = [];
    // Raw stdout/stderr lines from the Calcpad.Server sidecar (Rust's
    // `server-log` event), buffered the same way for the same reason.
    const pendingServerRawLogs: { line: string; stream: 'stdout' | 'stderr' }[] = [];

    if (isTauri) {
        // Tauri desktop: the Rust layer owns the Calcpad.Server sidecar
        // (spawn, kill on exit, port discovery). This manager just tracks
        // its URL and surfaces crashes to the Output panel.
        const { TauriServerManager } = await import('./services/server-manager');
        serverManager = new TauriServerManager({
            appendLine: (msg: string) => pendingServerLogs.push(msg),
        });
        serverManager.onServerLog = (line: string, stream: 'stdout' | 'stderr') => {
            pendingServerRawLogs.push({ line, stream });
        };

        serverManager.onStartupBlocked = (details: string) => {
            pendingServerLogs.push(`Server did not start — ${details}`);
            void showServerBlockedDialog(details);
        };

        try {
            await serverManager.start();
        } catch (err) {
            const msg = err instanceof Error ? (err.stack ?? err.message) : String(err);
            pendingServerLogs.push(`[bootstrap] Server failed to start: ${msg}`);
            console.error('[bootstrap] Server failed to start:', err);
        }
        serverUrl = serverManager.getBaseUrl() || '';

        const { TauriMessageBridge } = await import('./services/tauri-bridge');
        tauriBridge = new TauriMessageBridge(serverUrl);
        (window as any).calcpadBridge = tauriBridge;
    } else {
        // Pure web: use in-process web bridge
        serverUrl = getServerUrl();
        bridge = new MessageBridge(serverUrl);
        (window as any).calcpadBridge = bridge;
    }

    const activeBridge = tauriBridge ?? bridge!;

    // Initialize the platform messaging (reads VITE_PLATFORM='web')
    initMessaging();

    // Mount the main app layout
    const app = createApp(App);
    const appInstance = app.mount('#app') as any;

    // Let the bridge prompt via the in-app quick-pick modal (image storage mode).
    activeBridge.setQuickPick(async ({ title, placeholder, options }) => {
        const index = await appInstance.showQuickPick({
            title,
            placeholder,
            options: options.map((o: { label: string; detail?: string }) => ({ label: o.label, detail: o.detail })),
        });
        return index == null ? null : options[index].value;
    });

    // Wait for DOM to render, then set up Monaco editor
    await nextTick();

    const editorEl = document.querySelector('.editor-container') as HTMLElement;
    if (!editorEl) {
        throw new Error('Editor container not found');
    }

    registerCalcpadLanguage();
    registerCalcpadTheme();

    // Apply the persisted app theme before Monaco initializes so the editor
    // renders with the right theme first paint. The desktop bridge loads its
    // settings asynchronously, so wait for it; the web bridge is synchronous.
    if (tauriBridge) await tauriBridge.ready;
    setAppTheme(coerceAppTheme(activeBridge.getStoredColorTheme()));

    const editor = createCalcpadEditor(editorEl, {
        value: '',
    });

    // JuliaMono is an async web font; re-measure once it loads so the cursor
    // grid isn't stuck on the fallback font's metrics (off-grid caret on PCs
    // that don't have JuliaMono installed system-wide).
    remeasureEditorFontsWhenReady();

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
    });

    // Seed the first tab. On web we put the sample in it; on desktop it's
    // an empty Untitled-1 ready to receive an Open or paste.
    tabs.newUntitled(isTauri ? '' : getSampleContent());

    // Universal tab-strip callbacks. The Tauri branch overrides the
    // close handler with a save-prompt-aware version; on web there's
    // nothing to save, so a plain close is correct.
    appInstance.onTabActivate = (id: string) => tabs.activate(id);
    appInstance.onTabCloseRequest = (id: string) => tabs.close(id);
    appInstance.onNewTabRequest = () => { tabs.newUntitled(); };
    appInstance.onTabCloseOthersRequest = (id: string) => {
        for (const t of tabs.all) {
            if (t.id !== id) tabs.close(t.id);
        }
    };
    appInstance.onTabCloseAllRequest = () => {
        for (const t of tabs.all) tabs.close(t.id);
    };

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

    // Output line the next unwrapped refresh should scroll to, set by the
    // wrapped->unwrapped two-step in the 'navigateToLine' handler and consumed
    // by refreshPreview.
    let pendingPreviewScrollLine: number | null = null;

    // Messages posted from the preview iframe (App.vue:injectPreviewConsole /
    // injectLineLinks). Two message types share this listener.
    window.addEventListener('message', (e: MessageEvent) => {
        const data = e.data;
        if (!data) return;

        // Forward console.* + uncaught errors to the Output panel's "Preview
        // Console" channel.
        if (data.type === 'previewConsole') {
            const level: 'info' | 'warn' | 'error' | 'debug' =
                data.level === 'warn' ? 'warn'
                : data.level === 'error' ? 'error'
                : data.level === 'debug' ? 'debug'
                : 'info';
            appInstance.appendOutput(level, String(data.message ?? ''), 'preview');
            return;
        }

        // Preview -> editor navigation. An 'output' line comes from the true
        // wrapped view; when the document has macros/includes that line only
        // makes sense in the unwrapped view, so flip the pane to unwrapped
        // scrolled there (the two-step) — the user then clicks a line number
        // to reach the true source line. A 'source' line (code-view .line-num
        // anchors, or a macro-free document) navigates Monaco directly.
        if (data.type === 'previewThemeChanged') {
            void refreshPreview();
            return;
        }

        if (data.type === 'navigateToLine') {
            const line = Number(data.line);
            if (!Number.isFinite(line) || line < 1) return;
            const isOutputLine = data.lineType === 'output';
            const hasMacros = /^\s*#(def|include)\b/im.test(editor.getValue());
            if (isOutputLine && appInstance.getPreviewMode() === 'wrapped' && hasMacros) {
                // Bake the target into the unwrapped refresh (avoids an
                // iframe-reload postMessage race); setPreviewMode triggers
                // onPreviewModeChanged -> refreshPreview.
                pendingPreviewScrollLine = line;
                appInstance.setPreviewMode('unwrapped');
            } else {
                editor.revealLineInCenter(line);
                editor.setPosition({ lineNumber: line, column: 1 });
                editor.focus();
            }
            return;
        }
    });

    appInstance.appendOutput('info', `CalcPad Web started — server: ${serverUrl}`);

    // Flush any server-manager log lines buffered before the Output panel mounted,
    // then redirect future ones straight into the panel.
    for (const msg of pendingServerLogs) appInstance.appendOutput('info', msg);
    pendingServerLogs.length = 0;
    for (const { line, stream } of pendingServerRawLogs) {
        appInstance.appendOutput(stream === 'stderr' ? 'error' : 'info', line, 'server');
    }
    pendingServerRawLogs.length = 0;
    if (serverManager) {
        serverManager.setLogger({
            appendLine: (msg: string) => appInstance.appendOutput('info', msg),
        });
        serverManager.onServerLog = (line: string, stream: 'stdout' | 'stderr') => {
            appInstance.appendOutput(stream === 'stderr' ? 'error' : 'info', line, 'server');
        };
        serverManager.onUrlChanged = (newUrl: string) => {
            activeBridge.api.setBaseUrl(newUrl);
            appInstance.appendOutput('info', `Server URL updated: ${newUrl}`);
        };
        serverManager.onCrashExhausted = (crashOutput: string) => {
            appInstance.appendOutput('error',
                'CalcPad server crashed repeatedly — auto-restart disabled. ' +
                'Use Server → Restart Server to try again.');
            if (crashOutput) appInstance.appendOutput('error', crashOutput);
        };
    }

    // Register Monaco providers
    const editorBridge = activeBridge as unknown as EditorBridge;
    const getFileContext = 'buildFileContext' in activeBridge
        ? (content: string) => (activeBridge as any).buildFileContext(content)
        : undefined;

    // Cross-file Go-to-Definition / Find All References needs to read include
    // files off disk. Only wire the opener up on Tauri desktop, where we
    // have filesystem access; in the pure-web build the providers silently
    // skip include locations. See `IncludeFileOpener` for the browser/remote
    // follow-up.
    const openIncludeFile: IncludeFileOpener | undefined = tauriBridge
        ? async (rawFileName: string, navigateTo?: { line: number; column: number }) => {
            try {
                const absPath = tauriBridge.resolveIncludePath(rawFileName);
                let model = tabs.findModelByPath(absPath);
                if (!model) {
                    const content = await tauriBridge.readFile(absPath);
                    const tabId = tabs.openFile(absPath, content);
                    model = tabs.findModelByPath(absPath);
                    if (!model) {
                        console.warn(`[references] opened ${absPath} as ${tabId} but no model was registered`);
                        return null;
                    }
                } else {
                    // Tab exists but may not be the active one — switch to it
                    // so the editor actually shows the include file.
                    const existing = tabs.findByPath(absPath);
                    if (existing && tabs.activeId !== existing.id) {
                        tabs.activate(existing.id);
                    }
                }
                // Standalone Monaco's openCodeEditor service doesn't reliably
                // re-apply the selection after a provider has swapped the
                // active model, so position the cursor explicitly here.
                if (navigateTo) {
                    const lineNumber = Math.max(0, navigateTo.line) + 1;
                    const column = Math.max(0, navigateTo.column) + 1;
                    editor.setPosition({ lineNumber, column });
                    editor.revealPositionInCenter({ lineNumber, column });
                }
                return model.uri;
            } catch (err) {
                console.warn(`[references] failed to open include ${rawFileName}: ${err instanceof Error ? err.message : String(err)}`);
                return null;
            }
        }
        : undefined;

    registerSemanticTokensProvider(activeBridge.api, getFileContext);
    const diagnostics = setupDiagnostics(editor, activeBridge.api, () => {
        const sev = editorBridge.getExtraSetting('linterMinSeverity');
        return (sev === 'error' || sev === 'warning') ? sev : 'information';
    }, getFileContext);
    registerCompletionProvider(editorBridge);
    if (tauriBridge) {
        registerIncludeCompletionProvider({
            listDirectory: (p) => tauriBridge.listDirectory(p),
            getCurrentFilePath: () => tabs.activeTab?.filePath ?? null,
            getOpenedFolder: () => tauriBridge.getOpenedFolder(),
            getLibraryPath: () => tauriBridge.getLibraryPath(),
        });
    }
    registerHoverProvider(editorBridge);
    registerDefinitionProvider(editorBridge, getFileContext, openIncludeFile);
    registerReferenceProvider(editorBridge, getFileContext, openIncludeFile);
    registerRenameProvider(editorBridge, getFileContext);
    registerFormatDocumentProvider(editorBridge);
    attachQuickTyper(editor, editorBridge);
    attachOperatorReplacer(editor);
    attachAutoIndenter(editor);
    registerFormattingCommands(editor, editorBridge);

    // Ctrl+D duplicates the current line (or all selected lines) — overrides
    // Monaco's default "add selection to next find match" binding.
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyD, () => {
        editor.trigger('keyboard', 'editor.action.copyLinesDownAction', null);
    });

    // Keep the definitions cache fresh so hover provider has data to show.
    // Debounced — same cadence as TOC refresh.
    let definitionsTimer: ReturnType<typeof setTimeout> | null = null;
    const refreshDefinitions = async () => {
        const content = editor.getValue();
        const ctx = getFileContext ? await getFileContext(content) : {};
        editorBridge.definitions.refreshDefinitions(content, activeDocumentKey(), ctx.sourceFilePath);
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
                return { severityClass: 'lintError', icon: '✕' };
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
        // Repopulate the definitions cache for the new tab so hover has
        // data on files opened via File > Open (no content-change event fires).
        void refreshDefinitions();
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

    // Mount the CalcPad Vue sidebar. Desktop (Tauri) shows the Files view
    // + activity icons; web mode keeps the original single-panel look.
    const sidebarApp = createApp(CalcpadAppVue, { extraTabs: isTauri });
    const sidebarInstance = sidebarApp.mount('#vue-sidebar') as {
        switchTab?: (id: string) => void;
        switchView?: (id: string) => void;
    };

    // HTML preview via convert endpoint (debounced)
    let previewTimer: ReturnType<typeof setTimeout> | null = null;
    // TOC headings refresh (debounced)
    let tocTimer: ReturnType<typeof setTimeout> | null = null;

    // Initialize preview mode from saved extra setting (Tauri) or localStorage (web).
    const savedMode = (editorBridge.getExtraSetting('previewMode') as 'wrapped' | 'unwrapped' | undefined);
    if (savedMode === 'wrapped' || savedMode === 'unwrapped') {
        appInstance.setPreviewMode(savedMode);
    }

    function resolvePreviewTheme(): 'light' | 'dark' {
        const stored = editorBridge.getExtraSetting('previewTheme') ?? 'system';
        if (stored === 'light' || stored === 'dark') return stored;
        return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }

    async function refreshPreview(): Promise<void> {
        if (!appInstance.isPreviewVisible()) return;

        const content = editor.getValue();
        const settings = activeBridge.getSettings();
        const apiSettings = buildApiSettings(settings);
        const mode = appInstance.getPreviewMode() as 'wrapped' | 'unwrapped';
        const theme = resolvePreviewTheme();

        if (!content.trim()) {
            appInstance.setPreviewHtml(getEmptyPreviewHtml());
            return;
        }

        const fileContext = getFileContext ? await getFileContext(content) : {};

        let html: string | ArrayBuffer | null;
        if (mode === 'unwrapped') {
            html = await activeBridge.api.convertUnwrapped(content, apiSettings, fileContext.sourceFilePath, theme);
        } else {
            html = await activeBridge.api.convert(content, apiSettings, 'html', false, fileContext.sourceFilePath, theme);
        }

        // Consume any pending two-step scroll target: only the unwrapped view it
        // was set for should honor it, and only once.
        const scrollToLine = (mode === 'unwrapped' && pendingPreviewScrollLine != null)
            ? pendingPreviewScrollLine
            : undefined;
        pendingPreviewScrollLine = null;

        if (typeof html === 'string') {
            // Desktop: inline on-disk images so relative <img src> paths (from
            // the images-folder / custom-path insert options) render in the
            // sandboxed preview iframe, matching PDF export.
            const finalHtml = tauriBridge
                ? await tauriBridge.inlineDocumentImages(html)
                : html;
            appInstance.setPreviewHtml(finalHtml, scrollToLine);
        }
    }

    // Manual refresh: re-lint with current settings,
    // refresh definitions/headings, redraw preview. Called from the
    // Server > Refresh menu item.
    async function runRefresh(): Promise<void> {
        appInstance.appendOutput('info', 'Refreshing…');

        await diagnostics.refresh();
        const content = editor.getValue();
        const ctx = getFileContext ? await getFileContext(content) : {};
        editorBridge.definitions.refreshDefinitions(content, activeDocumentKey(), ctx.sourceFilePath);
        activeBridge.refreshHeadings();
        if (appInstance.isPreviewVisible()) {
            await refreshPreview();
        }
    }

    appInstance.onPreviewModeChanged = (mode: 'wrapped' | 'unwrapped') => {
        editorBridge.setExtraSetting('previewMode', mode);
        refreshPreview();
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

    // Tauri-specific: native menu clicks + file operations
    if (isTauri && tauriBridge) {
        const [
            { listen: tauriListen },
            { getCurrentWindow },
            { exit: processExit, relaunch: processRelaunch },
            tauriClipboard,
        ] = await Promise.all([
            import('@tauri-apps/api/event'),
            import('@tauri-apps/api/window'),
            import('@tauri-apps/plugin-process'),
            import('@tauri-apps/plugin-clipboard-manager'),
        ]);

        // Menu is built in Rust (src-tauri/src/lib.rs:build_menu). The frontend
        // just tracks recents in the plugin-store; there is no dynamic menu
        // rebuild. Recent files remain accessible via the sidebar's Files tab.
        void tauriBridge.getRecentFiles();

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
                const content = await tauriBridge!.readFile(path);
                tabs.openFile(path, content);
                await tauriBridge!.addRecentFile(path);
                // Menu is static under Tauri — no rebuild needed.
            } catch (err) {
                appInstance.appendOutput('error', 'Failed to open file: ' + (err instanceof Error ? err.message : String(err)));
            }
        }

        // Files-tab clicks arrive via a custom event dispatched by the bridge.
        window.addEventListener('calcpad-open-file', (e: Event) => {
            const detail = (e as CustomEvent<{ path: string }>).detail;
            if (detail?.path) void loadFile(detail.path);
        });

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
                await tauriBridge!.saveFile(active.filePath, content);
                tabs.markActiveSaved();
                return true;
            }
            const newPath = await tauriBridge!.saveFileAs(content);
            if (!newPath) return false;
            tabs.markActiveSaved({ filePath: newPath });
            await tauriBridge!.addRecentFile(newPath);
            return true;
        }

        async function saveAsActive(): Promise<boolean> {
            const content = tabs.activeModel?.getValue() ?? '';
            const newPath = await tauriBridge!.saveFileAs(content);
            if (!newPath) return false;
            tabs.markActiveSaved({ filePath: newPath });
            await tauriBridge!.addRecentFile(newPath);
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

        async function tryCloseTabsSequentially(ids: string[]): Promise<void> {
            for (const id of ids) {
                const ok = await tryCloseTab(id);
                if (!ok) return;
            }
        }

        appInstance.onTabCloseOthersRequest = (id: string) => {
            const ids = tabs.all.filter(t => t.id !== id).map(t => t.id);
            void tryCloseTabsSequentially(ids);
        };
        appInstance.onTabCloseAllRequest = () => {
            const ids = tabs.all.map(t => t.id);
            void tryCloseTabsSequentially(ids);
        };
        appInstance.onTabOpenContainingFolderRequest = (id: string) => {
            const t = tabs.all.find(t => t.id === id);
            if (t?.filePath) {
                tauriBridge.handleMessage({ type: 'openContainingFolder', path: t.filePath });
            }
        };

        // Clipboard-copy helpers for the tab context menu. Route through
        // Tauri's native clipboard so the value ends up on the system
        // clipboard (the WebView's navigator.clipboard is sandboxed).
        const writeClipboardText = async (text: string) => {
            try {
                await tauriClipboard.writeText(text);
            } catch (err) {
                appInstance.appendOutput('error', `Copy failed: ${err instanceof Error ? err.message : String(err)}`);
            }
        };

        appInstance.onTabCopyFullPathRequest = (id: string) => {
            const t = tabs.all.find(t => t.id === id);
            if (t?.filePath) void writeClipboardText(t.filePath);
        };
        appInstance.onTabCopyRelativePathRequest = async (id: string) => {
            const t = tabs.all.find(t => t.id === id);
            if (!t?.filePath) return;
            const folder = await tauriBridge.getOpenedFolder();
            if (!folder) {
                void writeClipboardText(t.filePath);
                return;
            }
            const rootNorm = folder.replace(/[\\/]+$/, '');
            const sep = rootNorm.includes('\\') ? '\\' : '/';
            const rootWithSep = rootNorm + sep;
            const rel = t.filePath.startsWith(rootWithSep)
                ? t.filePath.substring(rootWithSep.length)
                : t.filePath;
            void writeClipboardText(rel);
        };

        /**
         * Read an image off the system clipboard (Tauri native, no WebView2
         * prompt) and run it through the image-insert flow. No-op when the
         * clipboard holds no image. Returns true if an image was inserted.
         */
        async function tryPasteClipboardImage(): Promise<boolean> {
            let pngBytes: Uint8Array | null = null;
            try {
                const image = await tauriClipboard.readImage();
                const rgba = await image.rgba();
                const { width, height } = await image.size();
                if (!width || !height || rgba.length === 0) return false;
                pngBytes = await rgbaToPng(rgba, width, height);
            } catch {
                // readImage throws when the clipboard has no image — nothing to paste.
                return false;
            }
            if (!pngBytes) return false;
            await tauriBridge!.insertImageData({
                data: pngBytes,
                mimeType: 'image/png',
                filename: 'pasted-image.png',
            });
            return true;
        }

        /**
         * Route a clipboard / edit action from the native menu. On WebKitGTK
         * (Linux Tauri) Ctrl+C/X/V are not bound to native clipboard ops
         * inside the WebView, so the menu accelerator is the only way to fire
         * them. We dispatch to Monaco when it has focus (which is true 99% of
         * the time in this app), and fall back for non-editor focus (sidebar
         * text fields). Copy/cut/paste go through Tauri's native clipboard
         * API so the editor can exchange text with other applications — the
         * WebView's navigator.clipboard only sees content the page itself put
         * on the system clipboard.
         */
        async function runClipboardAction(
            action: 'cut' | 'copy' | 'paste' | 'select-all' | 'undo' | 'redo' | 'find' | 'replace',
        ): Promise<void> {
            const editorHasFocus = editor.hasTextFocus();
            if (editorHasFocus) {
                if (action === 'copy' || action === 'cut') {
                    const sel = editor.getSelection();
                    const model = editor.getModel();
                    if (!sel || !model) return;
                    if (sel.isEmpty()) {
                        // Empty selection: copy/cut the whole current line, matching
                        // Monaco's default. Cut removes the line including its newline.
                        const line = sel.startLineNumber;
                        const text = model.getLineContent(line) + '\n';
                        try { await tauriClipboard.writeText(text); } catch { /* ignored */ }
                        if (action === 'cut') {
                            const lineCount = model.getLineCount();
                            const range = line < lineCount
                                ? new monaco.Range(line, 1, line + 1, 1)
                                : new monaco.Range(line, 1, line, model.getLineMaxColumn(line));
                            editor.executeEdits('menu-cut', [{ range, text: '', forceMoveMarkers: true }]);
                        }
                    } else {
                        const text = model.getValueInRange(sel);
                        try { await tauriClipboard.writeText(text); } catch { /* ignored */ }
                        if (action === 'cut') {
                            editor.executeEdits('menu-cut', [{ range: sel, text: '', forceMoveMarkers: true }]);
                        }
                    }
                    return;
                }
                if (action === 'paste') {
                    let text = '';
                    try { text = await tauriClipboard.readText(); } catch { /* ignored */ }
                    if (text) {
                        const sel = editor.getSelection();
                        if (!sel) return;
                        editor.executeEdits('menu-paste', [{ range: sel, text, forceMoveMarkers: true }]);
                        editor.pushUndoStop();
                        return;
                    }
                    // No text on the clipboard — try a native image paste. Uses
                    // Tauri's Rust-side clipboard (permitted via
                    // clipboard-manager:allow-read-image) so it doesn't trigger
                    // the WebView2 security prompt that navigator.clipboard.read does.
                    await tryPasteClipboardImage();
                    return;
                }
                const cmd = {
                    'select-all': 'editor.action.selectAll',
                    undo: 'undo',
                    redo: 'redo',
                    find: 'actions.find',
                    replace: 'editor.action.startFindReplaceAction',
                }[action];
                editor.focus();
                editor.trigger('menu', cmd, null);
                return;
            }
            // Fallback for sidebar / preview / etc.
            const el = document.activeElement as HTMLInputElement | HTMLTextAreaElement | null;
            if (action === 'paste') {
                let text = '';
                try { text = await tauriClipboard.readText(); } catch { /* ignored */ }
                if (!text) return;
                if (el && 'setRangeText' in el) {
                    const start = el.selectionStart ?? el.value.length;
                    const end = el.selectionEnd ?? start;
                    el.setRangeText(text, start, end, 'end');
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                }
                return;
            }
            if (action === 'copy' || action === 'cut') {
                if (el && 'selectionStart' in el) {
                    const start = el.selectionStart ?? 0;
                    const end = el.selectionEnd ?? start;
                    if (end > start) {
                        const text = el.value.substring(start, end);
                        try { await tauriClipboard.writeText(text); } catch { /* ignored */ }
                        if (action === 'cut') {
                            el.setRangeText('', start, end, 'end');
                            el.dispatchEvent(new Event('input', { bubbles: true }));
                        }
                    }
                }
                return;
            }
            if (action === 'select-all') {
                if (el && 'select' in el && typeof el.select === 'function') el.select();
                return;
            }
            // undo / redo work via execCommand in inputs.
            if (action === 'undo' || action === 'redo') {
                document.execCommand(action);
            }
        }

        // Native menu clicks arrive as Tauri events emitted by the Rust menu handler.
        await tauriListen<{ id: string }>('menu-click', async (evt) => {
            const id: string = evt.payload.id;

            // Preview mode picker (View → Preview Mode: Wrapped/Unwrapped)
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
                    const result = await tauriBridge.openFile();
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
                    tauriBridge.handleMessage({ type: 'generatePdf' });
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
                    tauriBridge.handleMessage({ type: 'getServerLog' });
                    break;

                case 'stop-server':
                    if (serverManager) {
                        appInstance.appendOutput('info', 'Stopping server…');
                        try {
                            await serverManager.forceStop();
                            appInstance.appendOutput('info', 'Server stopped. Use Restart Server to start it again.');
                        } catch (err) {
                            appInstance.appendOutput('error', `Stop failed: ${err instanceof Error ? err.message : String(err)}`);
                        }
                    }
                    break;

                case 'restart-server':
                    if (serverManager) {
                        appInstance.appendOutput('info', 'Restarting server…');
                        try {
                            await serverManager.restart();
                            appInstance.appendOutput('info', `Server restarted at ${serverManager.getBaseUrl()}`);
                        } catch (err) {
                            appInstance.appendOutput('error', `Restart failed: ${err instanceof Error ? err.message : String(err)}`);
                        }
                    }
                    break;

                case 'restart-app':
                    await processRelaunch();
                    break;

                case 'undo':
                    runClipboardAction('undo');
                    break;
                case 'redo':
                    runClipboardAction('redo');
                    break;
                case 'cut':
                    await runClipboardAction('cut');
                    break;
                case 'copy':
                    await runClipboardAction('copy');
                    break;
                case 'paste':
                    await runClipboardAction('paste');
                    break;
                case 'select-all':
                    runClipboardAction('select-all');
                    break;
                case 'find':
                    runClipboardAction('find');
                    break;
                case 'replace':
                    runClipboardAction('replace');
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
                appInstance.appendOutput('info', `--- Server log (${data.path}) ---`);
                for (const line of text.split('\n')) {
                    if (!line.trim()) continue;
                    const level = /\[(INFO|WARN|WARNING|ERROR|CRASH)\]/i.exec(line)?.[1]?.toUpperCase();
                    const sev = level === 'ERROR' || level === 'CRASH' ? 'error'
                        : level === 'WARN' || level === 'WARNING' ? 'warn'
                        : level === 'INFO' ? 'info'
                        : 'error';
                    appInstance.appendOutput(sev, line);
                }
                appInstance.appendOutput('info', '--- end server log ---');
            } else if (data.type === 'pdfError') {
                appInstance.appendOutput('error', String(data.message || 'PDF export failed'));
            }
        });

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
                    const dropped = file as File & { path?: string };
                    if (dropped.path) {
                        await loadFile(dropped.path);
                    } else {
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
        // Tauri menu accelerators never fire while the editor has focus.
        // Bind the file-management ones directly on the editor.
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => {
            void saveActive();
        });
        editor.addCommand(
            monaco.KeyMod.CtrlCmd | monaco.KeyMod.Shift | monaco.KeyCode.KeyS,
            () => { void saveAsActive(); },
        );
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyO, async () => {
            const result = await tauriBridge.openFile();
            if (result) await loadFile(result.path);
        });
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyN, () => {
            tabs.newUntitled();
        });
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyP, () => {
            appInstance.togglePreview();
        });
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Comma, () => {
            sidebarInstance.switchTab?.('settings');
        });
        editor.addCommand(monaco.KeyCode.F5, () => {
            void runRefresh();
        });
        // Monaco's built-in clipboard actions route through navigator.clipboard
        // and document.execCommand, neither of which sees content written by
        // other applications on WebKitGTK. Override Ctrl+C/X/V so all three
        // operations go through Tauri's native clipboard API.
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyC, () => {
            void runClipboardAction('copy');
        });
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyX, () => {
            void runClipboardAction('cut');
        });
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyV, () => {
            void runClipboardAction('paste');
        });

        window.addEventListener('keydown', (e) => {
            // F5 — refresh (no modifier; bound here so it fires from any focus).
            if (e.key === 'F5' && !e.ctrlKey && !e.shiftKey && !e.altKey && !e.metaKey) {
                e.preventDefault();
                void runRefresh();
                return;
            }
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
                    const result = await tauriBridge.openFile();
                    if (result) await loadFile(result.path);
                })();
                return;
            }
            // Ctrl+N — new tab.
            if ((e.key === 'n' || e.key === 'N') && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                tabs.newUntitled();
                return;
            }
            // Ctrl+P — toggle preview.
            if ((e.key === 'p' || e.key === 'P') && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                appInstance.togglePreview();
                return;
            }
            // Ctrl+, — open Settings tab in sidebar (VS Code convention).
            if (e.key === ',' && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                sidebarInstance.switchTab?.('settings');
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
                    // Rust owns sidecar shutdown (kill-on-exit hook). This
                    // dispose only tears down TS event listeners.
                    if (serverManager) {
                        try { await serverManager.dispose(); }
                        catch (e) { appInstance.appendOutput('debug', `serverManager.dispose() rejected: ${e}`); }
                    }
                    appInstance.appendOutput('debug', 'Exit path: calling process.exit()');
                    void processExit(0);
                }
            }
        }

        // Intercept the window close button so unsaved tabs get their save prompt
        // before Tauri tears down the webview. tryExit() calls processExit() on
        // confirmation; if the user cancels, the window stays open.
        await getCurrentWindow().onCloseRequested(async (event) => {
            event.preventDefault();
            void tryExit();
        });
    }
}

bootstrap();
