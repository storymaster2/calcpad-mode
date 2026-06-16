/**
 * Single-source-of-truth for "what's the user looking at right now?"
 * across the bridges. Returns the content of the model attached to the
 * first registered Monaco editor — which, with multi-tab editing, is
 * the active tab's model (TabManager swaps the model in place).
 *
 * Falls back to the first model in the registry if no editor is mounted
 * yet (e.g. very early bootstrap). Returns '' as a final fallback so
 * callers can stay synchronous.
 */
export function getActiveEditorContent(): string {
    // Preferred: pull from the TabManager exposed by main.ts. Its activeModel
    // is the model currently swapped into the editor.
    const tabs = (window as { calcpadTabs?: { activeModel?: { getValue(): string } } }).calcpadTabs;
    const fromTabs = tabs?.activeModel?.getValue();
    if (typeof fromTabs === 'string') return fromTabs;

    // Fallback: query Monaco's global registry. Only works if main.ts exposed
    // it on window — kept for safety / future contexts.
    const m = (window as Window & { monaco?: typeof import('monaco-editor') }).monaco;
    if (!m) return '';
    const editor = m.editor.getEditors()[0];
    const model = editor?.getModel() ?? m.editor.getModels()[0];
    return model?.getValue() ?? '';
}
