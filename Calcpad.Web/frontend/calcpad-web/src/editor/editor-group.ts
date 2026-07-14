import * as monaco from 'monaco-editor';
import { createCalcpadEditor, type CalcpadEditorOptions } from './setup';
import { TabManager } from '../tabs/tab-manager';

/**
 * One editor group in the split layout: a Monaco editor instance paired with
 * its own TabManager. The desktop app supports up to two groups stacked
 * top/bottom (see App.vue). Language providers (completions, hover,
 * diagnostics, semantic tokens) are registered globally per-language and apply
 * to every group's editor automatically — only per-instance wiring (commands,
 * listeners, text helpers) is attached per group in main.ts's wireGroup().
 */
export class EditorGroup {
    readonly id: string;
    readonly editor: monaco.editor.IStandaloneCodeEditor;
    readonly tabs: TabManager;

    /** Per-group diagnostics handle, set by wireGroup(). */
    diagnostics: { refresh(): Promise<void> } | null = null;
    /** Listeners/commands to tear down when the group is disposed. */
    readonly disposables: monaco.IDisposable[] = [];

    constructor(id: string, container: HTMLElement, options?: CalcpadEditorOptions) {
        this.id = id;
        this.editor = createCalcpadEditor(container, options);
        this.tabs = new TabManager(this.editor, `${id}-`);
    }

    dispose(): void {
        for (const d of this.disposables) {
            try { d.dispose(); } catch { /* ignore */ }
        }
        this.disposables.length = 0;
        this.tabs.disposeAll();
        this.editor.dispose();
    }
}
