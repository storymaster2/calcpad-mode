import * as monaco from 'monaco-editor';
import type { FindReferencesResponse, SymbolLocation } from 'calcpad-frontend/types/api';
import type { EditorBridge } from './bridge';

interface SymbolHit {
    name: string;
    locations: SymbolLocation[];
    hitLocation: SymbolLocation;
}

/**
 * Find which symbol the cursor is on by checking actual token positions
 * from the server response. Position is 0-based.
 */
function findSymbolAtPosition(line: number, col: number, refs: FindReferencesResponse): SymbolHit | null {
    const buckets = [refs.variables, refs.functions, refs.macros];
    for (const bucket of buckets) {
        for (const [name, locations] of Object.entries(bucket)) {
            for (const loc of locations) {
                if (
                    loc.source === 'local' &&
                    loc.line === line &&
                    col >= loc.column &&
                    col < loc.column + loc.length
                ) {
                    return { name, locations, hitLocation: loc };
                }
            }
        }
    }
    return null;
}

async function fetchRefs(
    bridge: EditorBridge,
    model: monaco.editor.ITextModel,
): Promise<FindReferencesResponse | null> {
    return bridge.api.findReferences(model.getValue());
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
 * Go-to-Definition (F12). Jumps to the first assignment of the symbol under the cursor.
 */
export function registerDefinitionProvider(bridge: EditorBridge): monaco.IDisposable {
    return monaco.languages.registerDefinitionProvider('calcpad', {
        async provideDefinition(model, position) {
            const refs = await fetchRefs(bridge, model);
            if (!refs) return null;

            const hit = findSymbolAtPosition(position.lineNumber - 1, position.column - 1, refs);
            if (!hit) return null;

            const definition = hit.locations.find(loc => loc.isAssignment && loc.source === 'local');
            if (!definition) return null;

            return {
                uri: model.uri,
                range: locationToRange(definition),
            };
        },
    });
}

/**
 * Find All References (Shift+F12).
 */
export function registerReferenceProvider(bridge: EditorBridge): monaco.IDisposable {
    return monaco.languages.registerReferenceProvider('calcpad', {
        async provideReferences(model, position, context) {
            const refs = await fetchRefs(bridge, model);
            if (!refs) return null;

            const hit = findSymbolAtPosition(position.lineNumber - 1, position.column - 1, refs);
            if (!hit) return null;

            const filtered = context.includeDeclaration
                ? hit.locations
                : hit.locations.filter(l => !l.isAssignment);

            // Only return locations within the active document; cross-file refs
            // would require resolving include paths — punt for now.
            return filtered
                .filter(l => l.source === 'local')
                .map(l => ({ uri: model.uri, range: locationToRange(l) }));
        },
    });
}

/**
 * Rename Symbol (F2). Renames all local occurrences in the active document.
 */
export function registerRenameProvider(bridge: EditorBridge): monaco.IDisposable {
    return monaco.languages.registerRenameProvider('calcpad', {
        async resolveRenameLocation(model, position) {
            const refs = await fetchRefs(bridge, model);
            if (!refs) {
                return { text: '', range: new monaco.Range(1, 1, 1, 1), rejectReason: 'CalcPad server unavailable' };
            }
            const hit = findSymbolAtPosition(position.lineNumber - 1, position.column - 1, refs);
            if (!hit) {
                return { text: '', range: new monaco.Range(1, 1, 1, 1), rejectReason: 'No renameable symbol at cursor' };
            }
            if (hit.locations.every(l => l.source !== 'local')) {
                return { text: hit.name, range: new monaco.Range(1, 1, 1, 1), rejectReason: `'${hit.name}' is defined in an include file` };
            }
            return { text: hit.name, range: locationToRange(hit.hitLocation) };
        },

        async provideRenameEdits(model, position, newName) {
            const refs = await fetchRefs(bridge, model);
            if (!refs) return null;

            const hit = findSymbolAtPosition(position.lineNumber - 1, position.column - 1, refs);
            if (!hit || hit.name === newName) return null;

            const localLocations = hit.locations.filter(l => l.source === 'local');
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
