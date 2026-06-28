import * as monaco from 'monaco-editor';

export interface TabState {
    id: string;
    /** Display label — filename or "Untitled-N". */
    title: string;
    /** Absolute filesystem path; null for unsaved untitled tabs. */
    filePath: string | null;
    dirty: boolean;
}

export interface TabSnapshot extends TabState {
    isActive: boolean;
}

interface InternalTab extends TabState {
    model: monaco.editor.ITextModel;
    viewState: monaco.editor.ICodeEditorViewState | null;
    /** model.getAlternativeVersionId() at the last save/load — used for dirty tracking. */
    savedVersionId: number;
    /** Disposable for the model's content-change subscription (for dirty tracking). */
    contentSub: monaco.IDisposable;
}

export type TabsListener = (tabs: TabSnapshot[], activeId: string | null) => void;
export type ActiveModelChangeListener = (
    tabId: string | null,
    model: monaco.editor.ITextModel | null,
) => void;

/**
 * Owns the open-tabs list, their Monaco models, and view-state restoration.
 * Mirrors VS Code's tab semantics: one editor instance, many models, view
 * state saved/restored on switch.
 *
 * The TabManager is platform-agnostic — file I/O lives in the caller. It
 * just tracks `filePath` so the caller can decide what to read/write.
 */
export class TabManager {
    private tabs: InternalTab[] = [];
    private _activeId: string | null = null;
    private _untitledCounter = 0;
    private _seq = 0;

    private listeners = new Set<TabsListener>();
    private activeModelListeners = new Set<ActiveModelChangeListener>();

    constructor(private editor: monaco.editor.IStandaloneCodeEditor) {}

    // ---- Subscription ----

    onTabsChanged(listener: TabsListener): () => void {
        this.listeners.add(listener);
        return () => this.listeners.delete(listener);
    }

    onActiveModelChanged(listener: ActiveModelChangeListener): () => void {
        this.activeModelListeners.add(listener);
        return () => this.activeModelListeners.delete(listener);
    }

    // ---- Read API ----

    get activeId(): string | null {
        return this._activeId;
    }

    get activeTab(): TabState | null {
        const t = this.findActive();
        return t ? this.toState(t) : null;
    }

    get activeModel(): monaco.editor.ITextModel | null {
        return this.findActive()?.model ?? null;
    }

    get all(): TabState[] {
        return this.tabs.map(t => this.toState(t));
    }

    findByPath(filePath: string): TabState | null {
        const t = this.tabs.find(t => t.filePath === filePath);
        return t ? this.toState(t) : null;
    }

    /** Lookup the Monaco model for the tab matching `filePath`, or null. */
    findModelByPath(filePath: string): monaco.editor.ITextModel | null {
        return this.tabs.find(t => t.filePath === filePath)?.model ?? null;
    }

    isDirty(id?: string): boolean {
        const t = id ? this.tabs.find(t => t.id === id) : this.findActive();
        return !!t?.dirty;
    }

    anyDirty(): boolean {
        return this.tabs.some(t => t.dirty);
    }

    get count(): number {
        return this.tabs.length;
    }

    // ---- Mutation ----

    /**
     * Create a new untitled tab with the given content (default empty) and
     * activate it. Returns the new tab's id.
     */
    newUntitled(content: string = ''): string {
        this._untitledCounter += 1;
        const title = `Untitled-${this._untitledCounter}`;
        const tab = this.createTab({ title, filePath: null, content });
        this.activate(tab.id);
        return tab.id;
    }

    /**
     * Open a file in a new tab. If a tab with that path is already open,
     * activates it instead and ignores `content` (caller already has it open).
     * Returns the tab id.
     */
    openFile(filePath: string, content: string): string {
        const existing = this.tabs.find(t => t.filePath === filePath);
        if (existing) {
            this.activate(existing.id);
            return existing.id;
        }

        // If the active tab is an empty untitled scratch buffer, replace it
        // in place rather than stacking another tab. Matches VS Code's
        // "untitled-1 disappears when you open a file" behavior.
        const active = this.findActive();
        if (active && active.filePath === null && active.model.getValue() === '' && !active.dirty) {
            active.model.setValue(content);
            active.filePath = filePath;
            active.title = baseName(filePath);
            active.savedVersionId = active.model.getAlternativeVersionId();
            active.dirty = false;
            this.emit();
            return active.id;
        }

        const tab = this.createTab({ title: baseName(filePath), filePath, content });
        this.activate(tab.id);
        return tab.id;
    }

    /**
     * Switch the editor to the given tab. Saves the previous tab's view state
     * so cursor + scroll restore on switch-back.
     */
    activate(id: string): void {
        if (id === this._activeId) return;
        const next = this.tabs.find(t => t.id === id);
        if (!next) return;

        const prev = this.findActive();
        if (prev) {
            prev.viewState = this.editor.saveViewState();
        }

        this._activeId = id;
        this.editor.setModel(next.model);
        if (next.viewState) {
            this.editor.restoreViewState(next.viewState);
        }
        this.editor.focus();
        this.emit();
        this.emitActiveModel();
    }

    /**
     * Close a tab. Caller is responsible for the dirty-prompt (so it can
     * await the user's choice with platform-appropriate UI). When the active
     * tab closes, focus moves to the right neighbor (then left, then none).
     */
    close(id: string): void {
        const idx = this.tabs.findIndex(t => t.id === id);
        if (idx < 0) return;

        const tab = this.tabs[idx];
        const wasActive = id === this._activeId;

        tab.contentSub.dispose();
        tab.model.dispose();
        this.tabs.splice(idx, 1);

        if (wasActive) {
            const nextActive = this.tabs[idx] ?? this.tabs[idx - 1] ?? null;
            this._activeId = nextActive?.id ?? null;
            if (nextActive) {
                this.editor.setModel(nextActive.model);
                if (nextActive.viewState) {
                    this.editor.restoreViewState(nextActive.viewState);
                }
                this.editor.focus();
            } else {
                // No tabs left — give the editor an empty model so it stays usable.
                this.newUntitled();
                return; // newUntitled() emits already
            }
        }

        this.emit();
        if (wasActive) this.emitActiveModel();
    }

    /**
     * Mark the active tab saved at its current content. Optionally update its
     * file path / title (for save-as).
     */
    markActiveSaved(opts?: { filePath?: string }): void {
        const t = this.findActive();
        if (!t) return;
        if (opts?.filePath) {
            t.filePath = opts.filePath;
            t.title = baseName(opts.filePath);
        }
        t.savedVersionId = t.model.getAlternativeVersionId();
        if (t.dirty) {
            t.dirty = false;
            this.emit();
        } else {
            // Title or path may still have changed.
            this.emit();
        }
    }

    /** Replace the active tab's content as if it had just been opened from disk. */
    reloadActive(content: string): void {
        const t = this.findActive();
        if (!t) return;
        t.model.setValue(content);
        t.savedVersionId = t.model.getAlternativeVersionId();
        if (t.dirty) {
            t.dirty = false;
            this.emit();
        }
    }

    activateNext(): void {
        if (this.tabs.length < 2 || !this._activeId) return;
        const i = this.tabs.findIndex(t => t.id === this._activeId);
        const next = this.tabs[(i + 1) % this.tabs.length];
        this.activate(next.id);
    }

    activatePrev(): void {
        if (this.tabs.length < 2 || !this._activeId) return;
        const i = this.tabs.findIndex(t => t.id === this._activeId);
        const prev = this.tabs[(i - 1 + this.tabs.length) % this.tabs.length];
        this.activate(prev.id);
    }

    activateByIndex(index: number): void {
        const t = this.tabs[index];
        if (t) this.activate(t.id);
    }

    // ---- Internals ----

    private createTab(opts: { title: string; filePath: string | null; content: string }): InternalTab {
        const id = `tab-${++this._seq}`;
        // Use a unique URI per model — Monaco needs this so markers/providers
        // can distinguish tabs. Path includes the tab id so the URI is stable
        // across rename and unique even when two tabs hold the same file path.
        const uri = monaco.Uri.parse(`inmemory:///${id}.cpd`);
        const model = monaco.editor.createModel(opts.content, 'calcpad', uri);
        const savedVersionId = model.getAlternativeVersionId();
        const tab: InternalTab = {
            id,
            title: opts.title,
            filePath: opts.filePath,
            dirty: false,
            model,
            viewState: null,
            savedVersionId,
            contentSub: model.onDidChangeContent(() => this.recomputeDirty(id)),
        };
        this.tabs.push(tab);
        this.emit();
        return tab;
    }

    /**
     * Compare the model's alternative-version-id against the last-saved id.
     * Equality means the user has undone all post-save edits — model is
     * effectively clean again, so the dirty flag flips back off.
     */
    private recomputeDirty(id: string): void {
        const t = this.tabs.find(t => t.id === id);
        if (!t) return;
        const next = t.model.getAlternativeVersionId() !== t.savedVersionId;
        if (next !== t.dirty) {
            t.dirty = next;
            this.emit();
        }
    }

    private findActive(): InternalTab | null {
        return this.tabs.find(t => t.id === this._activeId) ?? null;
    }

    private toState(t: InternalTab): TabState {
        return { id: t.id, title: t.title, filePath: t.filePath, dirty: t.dirty };
    }

    private emit(): void {
        const snapshots: TabSnapshot[] = this.tabs.map(t => ({
            ...this.toState(t),
            isActive: t.id === this._activeId,
        }));
        for (const l of this.listeners) l(snapshots, this._activeId);
    }

    private emitActiveModel(): void {
        const t = this.findActive();
        for (const l of this.activeModelListeners) l(t?.id ?? null, t?.model ?? null);
    }
}

function baseName(path: string): string {
    return path.split(/[\\/]/).pop() || path;
}
