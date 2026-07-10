import * as monaco from 'monaco-editor';
import { calcpadLanguage, calcpadLanguageConfiguration } from './language';
import { calcpadDarkTheme, calcpadLightTheme } from './theme';

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
    monaco.editor.defineTheme('calcpad-light', calcpadLightTheme);
}

/**
 * Switch Monaco to the matching CalcPad theme. Called by the app-theme
 * applier whenever the resolved theme (system → light/dark, or an explicit
 * choice) changes.
 */
export function setCalcpadEditorTheme(resolved: 'light' | 'dark'): void {
    monaco.editor.setTheme(resolved === 'light' ? 'calcpad-light' : 'calcpad-dark');
}

export interface CalcpadEditorOptions {
    value?: string;
    readOnly?: boolean;
    fontSize?: number;
    wordWrap?: 'on' | 'off';
}

/**
 * Monaco measures glyph widths once when an editor is created. JuliaMono ships
 * as an async web font, so on machines that don't have it installed the first
 * measurement happens against a fallback and the cursor/glyph grid ends up
 * misaligned. Force the font to load, then re-measure all editors.
 */
export function remeasureEditorFontsWhenReady(fontSize = 14): void {
    if (!('fonts' in document)) return;
    Promise.all([
        document.fonts.load(`${fontSize}px "JuliaMono"`),
        document.fonts.load(`bold ${fontSize}px "JuliaMono"`),
    ])
        .catch(() => undefined)
        .then(() => monaco.editor.remeasureFonts());
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
        // Theme is set globally by setCalcpadEditorTheme() via the app-theme
        // applier — omitted here so it doesn't clobber the current selection.
        value: options?.value ?? '',
        readOnly: options?.readOnly ?? false,
        automaticLayout: true,
        minimap: { enabled: false },
        fontSize: options?.fontSize ?? 14,
        fontFamily: "'JuliaMono', 'Cascadia Code', 'Fira Code', Consolas, 'Courier New', monospace",
        lineNumbers: 'on',
        renderWhitespace: 'none',
        scrollBeyondLastLine: false,
        wordWrap: options?.wordWrap ?? 'on',
        tabSize: 4,
        insertSpaces: true,
        // Rainbow bracket colorization clobbers the theme's `bracket` token
        // color. VS Code extension disables it for the same reason.
        bracketPairColorization: { enabled: false },
        matchBrackets: 'always',
        'semanticHighlighting.enabled': true,
        // Match VS Code's default-but-flipped suggest behavior: Tab accepts
        // the current suggestion, Enter never does. Without this, Enter
        // accepts the suggestion when the suggest widget is open, which
        // surprises users who just want a newline.
        acceptSuggestionOnEnter: 'off',
        acceptSuggestionOnCommitCharacter: false,
        tabCompletion: 'on',
        // Calcpad source is full of Greek letters, math symbols, and other
        // non-ASCII glyphs — Monaco's unicode highlighter flags all of them.
        unicodeHighlight: {
            ambiguousCharacters: false,
            invisibleCharacters: false,
            nonBasicASCII: false,
        },
    });
    return editor;
}
