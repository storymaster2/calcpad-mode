import * as monaco from 'monaco-editor';
import { calcpadLanguage, calcpadLanguageConfiguration } from './language';
import { calcpadDarkTheme } from './theme';

/**
 * Register the CalcPad language and theme with Monaco.
 * Call this once before creating any editors.
 */
export function registerCalcpadLanguage(): void {
    monaco.languages.register({ id: 'calcpad', extensions: ['.cpd'] });
    monaco.languages.setMonarchTokensProvider('calcpad', calcpadLanguage);
    monaco.languages.setLanguageConfiguration('calcpad', calcpadLanguageConfiguration);
}

export function registerCalcpadTheme(): void {
    monaco.editor.defineTheme('calcpad-dark', calcpadDarkTheme);
}

export interface CalcpadEditorOptions {
    value?: string;
    readOnly?: boolean;
    fontSize?: number;
}

/**
 * Create a CalcPad-configured Monaco editor.
 * Caller must set up MonacoEnvironment workers before calling this.
 */
export function createCalcpadEditor(
    container: HTMLElement,
    options?: CalcpadEditorOptions
): monaco.editor.IStandaloneCodeEditor {
    const editor = monaco.editor.create(container, {
        language: 'calcpad',
        theme: 'calcpad-dark',
        value: options?.value ?? '',
        readOnly: options?.readOnly ?? false,
        automaticLayout: true,
        minimap: { enabled: false },
        fontSize: options?.fontSize ?? 14,
        fontFamily: "'Cascadia Code', 'Fira Code', Consolas, 'Courier New', monospace",
        lineNumbers: 'on',
        renderWhitespace: 'none',
        scrollBeyondLastLine: false,
        wordWrap: 'on',
        tabSize: 4,
        insertSpaces: true,
        bracketPairColorization: { enabled: true },
        'semanticHighlighting.enabled': true,
        // Match VS Code's default-but-flipped suggest behavior: Tab accepts
        // the current suggestion, Enter never does. Without this, Enter
        // accepts the suggestion when the suggest widget is open, which
        // surprises users who just want a newline.
        acceptSuggestionOnEnter: 'off',
        acceptSuggestionOnCommitCharacter: false,
        tabCompletion: 'on',
    });
    return editor;
}
