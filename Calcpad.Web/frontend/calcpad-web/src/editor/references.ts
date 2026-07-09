import * as monaco from 'monaco-editor';
import type { SymbolAtPositionResponse, SymbolLocation } from 'calcpad-frontend/types/api';
import type { EditorBridge } from './bridge';
import type { FileContextProvider } from './diagnostics';

/**
 * Resolves a `SymbolLocation` whose `source !== 'local'` to a Monaco model URI
 * by opening the referenced include file (reading it from disk and registering
 * a Monaco model). Activates the matching tab as a side effect so the editor
 * shows the include file when this promise resolves. When `navigateTo` is
 * provided, the opener should also position the cursor there — Monaco's
 * standalone go-to-definition action doesn't reliably reapply the selection
 * after a provider swaps the active model out from under it.
 *
 * Only available on platforms with disk access (Tauri desktop, VS Code).
 * Returns null if the file cannot be opened.
 *
 * TODO(web/remote): When running in a browser tab or against a remote server,
 * disk access is not available — we need a story for fetching include content
 * from the server (or from the in-memory `includeFiles` map the linter already
 * accepts) and creating a read-only Monaco model from it so cross-file
 * navigation works there too. Until then, navigation into includes is a
 * desktop-only feature.
 */
export type IncludeFileOpener = (
    rawFileName: string,
    navigateTo?: { line: number; column: number },
) => Promise<monaco.Uri | null>;

async function resolveSymbol(
    bridge: EditorBridge,
    model: monaco.editor.ITextModel,
    position: monaco.Position,
    getFileContext?: FileContextProvider,
): Promise<SymbolAtPositionResponse | null> {
    const content = model.getValue();
    const ctx = getFileContext ? await getFileContext(content) : {};
    return bridge.api.symbolAtPosition(
        content,
        position.lineNumber - 1,
        position.column - 1,
        ctx.sourceFilePath,
    );
}

function locationToRange(loc: SymbolLocation): monaco.IRange {
    const line = Math.max(0, loc.line) + 1; // 0-based → 1-based
    return {
        startLineNumber: line,
        startColumn: loc.column + 1,
        endLineNumber: line,
        endColumn: loc.column + loc.length + 1,
    };
}

/**
 * Resolve a SymbolLocation to a Monaco URI. Local locations stay in the active
 * model; include locations are opened on disk via the opener (desktop only).
 * `navigate` controls whether the opener also positions the cursor on the
 * target — true for the single jump in go-to-definition, false for the
 * many-result find-references panel.
 */
async function resolveLocationUri(
    loc: SymbolLocation,
    localUri: monaco.Uri,
    openIncludeFile: IncludeFileOpener | undefined,
    navigate: boolean,
): Promise<monaco.Uri | null> {
    if (loc.source === 'local') return localUri;
    if (!openIncludeFile || !loc.sourceFile) return null;
    return openIncludeFile(loc.sourceFile, navigate ? { line: loc.line, column: loc.column } : undefined);
}

/**
 * Go-to-Definition (F12). Asks the server for the symbol under the cursor,
 * then jumps to the first assignment location. When the definition lives in
 * an `#include` file and an `openIncludeFile` is provided, opens that file
 * in a new tab before returning the location.
 */
export function registerDefinitionProvider(
    bridge: EditorBridge,
    getFileContext?: FileContextProvider,
    openIncludeFile?: IncludeFileOpener,
): monaco.IDisposable {
    return monaco.languages.registerDefinitionProvider('calcpad', {
        async provideDefinition(model, position) {
            const sym = await resolveSymbol(bridge, model, position, getFileContext);
            if (!sym) return null;

            const definition = sym.locations.find(loc => loc.isAssignment);
            if (!definition) return null;

            const uri = await resolveLocationUri(definition, model.uri, openIncludeFile, /* navigate */ true);
            if (!uri) return null;

            return { uri, range: locationToRange(definition) };
        },
    });
}

/**
 * Find All References (Shift+F12). Returns every occurrence of the symbol,
 * including those that live in `#include` files when an `openIncludeFile` is
 * available (desktop only — see `IncludeFileOpener` TODO).
 */
export function registerReferenceProvider(
    bridge: EditorBridge,
    getFileContext?: FileContextProvider,
    openIncludeFile?: IncludeFileOpener,
): monaco.IDisposable {
    return monaco.languages.registerReferenceProvider('calcpad', {
        async provideReferences(model, position, context) {
            const sym = await resolveSymbol(bridge, model, position, getFileContext);
            if (!sym) return null;

            const filtered = context.includeDeclaration
                ? sym.locations
                : sym.locations.filter(l => !l.isAssignment);

            const results: monaco.languages.Location[] = [];
            for (const loc of filtered) {
                const uri = await resolveLocationUri(loc, model.uri, openIncludeFile, /* navigate */ false);
                if (uri) results.push({ uri, range: locationToRange(loc) });
            }
            return results;
        },
    });
}

/**
 * Rename Symbol (F2). Renames all local occurrences in the active document.
 * Cross-file rename is not supported — the user is told to rename in-place
 * if the definition lives in an include.
 */
export function registerRenameProvider(
    bridge: EditorBridge,
    getFileContext?: FileContextProvider,
): monaco.IDisposable {
    return monaco.languages.registerRenameProvider('calcpad', {
        async resolveRenameLocation(model, position) {
            const sym = await resolveSymbol(bridge, model, position, getFileContext);
            if (!sym) {
                return { text: '', range: new monaco.Range(1, 1, 1, 1), rejectReason: 'No renameable symbol at cursor' };
            }
            if (sym.locations.every(l => l.source !== 'local')) {
                return { text: sym.symbolName, range: new monaco.Range(1, 1, 1, 1), rejectReason: `'${sym.symbolName}' is defined in an include file` };
            }
            // Anchor the rename UI on a local occurrence at or covering the cursor.
            const line = position.lineNumber - 1;
            const col = position.column - 1;
            const anchor = sym.locations.find(l =>
                l.source === 'local' &&
                l.line === line &&
                col >= l.column &&
                col <= l.column + l.length,
            ) ?? sym.locations.find(l => l.source === 'local');
            if (!anchor) {
                return { text: sym.symbolName, range: new monaco.Range(1, 1, 1, 1), rejectReason: 'No local occurrence to anchor the rename' };
            }
            return { text: sym.symbolName, range: locationToRange(anchor) };
        },

        async provideRenameEdits(model, position, newName) {
            const sym = await resolveSymbol(bridge, model, position, getFileContext);
            if (!sym || sym.symbolName === newName) return null;

            const localLocations = sym.locations.filter(l => l.source === 'local');
            const edits: monaco.languages.IWorkspaceTextEdit[] = localLocations.map(l => ({
                resource: model.uri,
                versionId: model.getVersionId(),
                textEdit: {
                    range: locationToRange(l),
                    text: newName,
                },
            }));

            return { edits };
        },
    });
}
