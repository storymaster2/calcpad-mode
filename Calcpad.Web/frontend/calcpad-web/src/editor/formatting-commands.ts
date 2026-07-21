import * as monaco from 'monaco-editor';
import type { EditorBridge } from './bridge';

type InlineFormat = 'bold' | 'italic' | 'underline' | 'subscript' | 'superscript';
type CommentFormat = 'html' | 'markdown';

const HTML_INLINE: Record<InlineFormat, [string, string]> = {
    bold: ['<strong>', '</strong>'],
    italic: ['<em>', '</em>'],
    underline: ['<ins>', '</ins>'],
    subscript: ['<sub>', '</sub>'],
    superscript: ['<sup>', '</sup>'],
};

const MARKDOWN_INLINE: Record<InlineFormat, [string, string]> = {
    bold: ['**', '**'],
    italic: ['*', '*'],
    underline: ['++', '++'],
    subscript: ['~', '~'],
    superscript: ['^', '^'],
};

/**
 * Register the 18 CalcPad text-formatting commands plus their keybindings on the editor.
 * Every command is gated on `extraSettings.formattingHotkeys !== 'false'`.
 */
export function registerFormattingCommands(
    editor: monaco.editor.IStandaloneCodeEditor,
    bridge: EditorBridge,
): monaco.IDisposable {
    const disposables: monaco.IDisposable[] = [];

    const guard = (run: () => void | Promise<void>) => async () => {
        if (bridge.getExtraSetting('formattingHotkeys') === 'false') return;
        await run();
    };

    function add(id: string, label: string, keybindings: number[], run: () => void | Promise<void>): void {
        disposables.push(editor.addAction({
            id,
            label,
            keybindings,
            run: guard(run),
        }));
    }

    const KM = monaco.KeyMod;
    const KC = monaco.KeyCode;

    // Inline formatting
    add('calcpad.formatBold',         'CalcPad: Bold',         [KM.CtrlCmd | KC.KeyB], () => wrapInline(editor, bridge, 'bold'));
    add('calcpad.formatItalic',       'CalcPad: Italic',       [KM.CtrlCmd | KC.KeyI], () => wrapInline(editor, bridge, 'italic'));
    add('calcpad.formatUnderline',    'CalcPad: Underline',    [KM.CtrlCmd | KC.KeyU], () => wrapInline(editor, bridge, 'underline'));
    add('calcpad.formatSubscript',    'CalcPad: Subscript',    [KM.CtrlCmd | KC.Equal], () => wrapInline(editor, bridge, 'subscript'));
    add('calcpad.formatSuperscript',  'CalcPad: Superscript',  [KM.CtrlCmd | KM.Shift | KC.Equal], () => wrapInline(editor, bridge, 'superscript'));

    // Headings (Ctrl+1..Ctrl+6)
    const digitKeys = [KC.Digit1, KC.Digit2, KC.Digit3, KC.Digit4, KC.Digit5, KC.Digit6];
    for (let i = 0; i < 6; i++) {
        const level = i + 1;
        add(`calcpad.formatHeading${level}`, `CalcPad: Heading ${level}`,
            [KM.CtrlCmd | digitKeys[i]],
            () => insertHeading(editor, bridge, level));
    }

    // Block elements
    add('calcpad.formatParagraph',    'CalcPad: Paragraph',     [KM.CtrlCmd | KC.KeyL], () => insertParagraph(editor));
    add('calcpad.formatLineBreak',    'CalcPad: Line Break',    [KM.CtrlCmd | KC.KeyR], () => insertLineBreak(editor));
    add('calcpad.formatBulletedList', 'CalcPad: Bulleted List', [KM.CtrlCmd | KM.Shift | KC.KeyL], () => insertBulletedList(editor, bridge));
    add('calcpad.formatNumberedList', 'CalcPad: Numbered List', [KM.CtrlCmd | KM.Shift | KC.KeyN], () => insertNumberedList(editor, bridge));

    // Comments
    add('calcpad.toggleComment',  'CalcPad: Toggle Comment',   [KM.CtrlCmd | KC.KeyQ], () => toggleComment(editor));
    add('calcpad.uncomment',      'CalcPad: Uncomment',        [KM.CtrlCmd | KM.Shift | KC.KeyQ], () => uncomment(editor));
    add('calcpad.pasteAsComment', 'CalcPad: Paste as Comment', [KM.CtrlCmd | KM.Shift | KC.KeyV], () => pasteAsComment(editor));

    return { dispose() { for (const d of disposables) d.dispose(); } };
}

function getFormat(editor: monaco.editor.IStandaloneCodeEditor, bridge: EditorBridge): CommentFormat {
    const setting = bridge.getExtraSetting('commentFormat') ?? 'auto';
    if (setting === 'markdown') return 'markdown';
    if (setting === 'auto') return detectFormatAtCursor(editor);
    return 'html';
}

function detectFormatAtCursor(editor: monaco.editor.IStandaloneCodeEditor): CommentFormat {
    const model = editor.getModel();
    const pos = editor.getPosition();
    if (!model || !pos) return 'html';

    let mdActive = false;
    for (let i = 1; i <= pos.lineNumber; i++) {
        const t = model.getLineContent(i).trim();
        if (t === '#md on') mdActive = true;
        else if (t === '#md off') mdActive = false;
    }
    return mdActive ? 'markdown' : 'html';
}

function stripCommentPrefix(lineText: string): [string, string, string] {
    if (lineText.startsWith("'")) {
        const inner = lineText.substring(1);
        if (inner.endsWith("'")) {
            return ["'", inner.slice(0, -1), "'"];
        }
        return ["'", inner, ''];
    }
    return ['', lineText, ''];
}

function wrapInline(
    editor: monaco.editor.IStandaloneCodeEditor,
    bridge: EditorBridge,
    type: InlineFormat,
): void {
    const model = editor.getModel();
    if (!model) return;
    const format = getFormat(editor, bridge);
    const [prefix, suffix] = format === 'html' ? HTML_INLINE[type] : MARKDOWN_INLINE[type];
    const selections = editor.getSelections() ?? [];
    if (selections.length === 0) return;

    const edits: monaco.editor.IIdentifiedSingleEditOperation[] = [];
    const newSelections: monaco.Selection[] = [];

    for (const sel of selections) {
        const isEmpty = sel.isEmpty();
        if (isEmpty) {
            const text = prefix + suffix;
            edits.push({ range: sel, text, forceMoveMarkers: true });
            // After insert, cursor sits between prefix and suffix.
            // Insert is at sel.getStartPosition(); after insert the cursor will be after the inserted text;
            // we want it before suffix.
            const start = sel.getStartPosition();
            // Translate: column advances by prefix.length, then we want it there.
            const placedCol = start.column + prefix.length;
            newSelections.push(new monaco.Selection(start.lineNumber, placedCol, start.lineNumber, placedCol));
        } else {
            const selectedText = model.getValueInRange(sel);
            edits.push({ range: sel, text: prefix + selectedText + suffix, forceMoveMarkers: true });
            newSelections.push(new monaco.Selection(sel.startLineNumber, sel.startColumn, sel.endLineNumber, sel.endColumn + prefix.length + suffix.length));
        }
    }

    editor.executeEdits('calcpad-format-inline', edits, newSelections);
}

function insertHeading(
    editor: monaco.editor.IStandaloneCodeEditor,
    bridge: EditorBridge,
    level: number,
): void {
    const model = editor.getModel();
    if (!model) return;
    const format = getFormat(editor, bridge);
    const selections = editor.getSelections() ?? [];

    const edits: monaco.editor.IIdentifiedSingleEditOperation[] = [];
    for (const sel of selections) {
        const lineNumber = sel.positionLineNumber;
        const lineText = model.getLineContent(lineNumber);
        let [, content, trailingQuote] = stripCommentPrefix(lineText);

        const htmlMatch = content.match(/^<h[1-6]>(.*)<\/h[1-6]>$/);
        if (htmlMatch) content = htmlMatch[1];
        const mdMatch = content.match(/^(#{1,6})\s+(.*)$/);
        if (mdMatch) content = mdMatch[2];

        let newLine: string;
        if (format === 'html') {
            newLine = `'<h${level}>${content}</h${level}>${trailingQuote}`;
        } else {
            newLine = `'${'#'.repeat(level)} ${content}${trailingQuote}`;
        }

        edits.push({
            range: new monaco.Range(lineNumber, 1, lineNumber, lineText.length + 1),
            text: newLine,
            forceMoveMarkers: true,
        });
    }
    editor.executeEdits('calcpad-format-heading', edits);
}

function insertParagraph(editor: monaco.editor.IStandaloneCodeEditor): void {
    const model = editor.getModel();
    if (!model) return;
    const selections = editor.getSelections() ?? [];

    const edits: monaco.editor.IIdentifiedSingleEditOperation[] = [];
    for (const sel of selections) {
        const lineNumber = sel.positionLineNumber;
        const lineText = model.getLineContent(lineNumber);
        const [, content, trailingQuote] = stripCommentPrefix(lineText);
        edits.push({
            range: new monaco.Range(lineNumber, 1, lineNumber, lineText.length + 1),
            text: `'<p>${content}</p>${trailingQuote}`,
            forceMoveMarkers: true,
        });
    }
    editor.executeEdits('calcpad-format-paragraph', edits);
}

function insertLineBreak(editor: monaco.editor.IStandaloneCodeEditor): void {
    const selections = editor.getSelections() ?? [];
    const edits: monaco.editor.IIdentifiedSingleEditOperation[] = selections.map(sel => ({
        range: sel,
        text: "'<br/>\n",
        forceMoveMarkers: true,
    }));
    editor.executeEdits('calcpad-format-linebreak', edits);
}

function insertBulletedList(
    editor: monaco.editor.IStandaloneCodeEditor,
    bridge: EditorBridge,
): void {
    const model = editor.getModel();
    if (!model) return;
    const sel = editor.getSelection();
    if (!sel) return;
    const format = getFormat(editor, bridge);
    const startLine = sel.startLineNumber;
    const endLine = sel.endLineNumber;

    const lines: string[] = [];
    if (format === 'html') {
        lines.push("'<ul>");
        for (let i = startLine; i <= endLine; i++) {
            const [, content, tq] = stripCommentPrefix(model.getLineContent(i));
            lines.push(`'<li>${content}</li>${tq}`);
        }
        lines.push("'</ul>");
    } else {
        for (let i = startLine; i <= endLine; i++) {
            const [, content, tq] = stripCommentPrefix(model.getLineContent(i));
            lines.push(`'- ${content}${tq}`);
        }
    }

    editor.executeEdits('calcpad-format-ul', [{
        range: new monaco.Range(startLine, 1, endLine, model.getLineLength(endLine) + 1),
        text: lines.join('\n'),
        forceMoveMarkers: true,
    }]);
}

function insertNumberedList(
    editor: monaco.editor.IStandaloneCodeEditor,
    bridge: EditorBridge,
): void {
    const model = editor.getModel();
    if (!model) return;
    const sel = editor.getSelection();
    if (!sel) return;
    const format = getFormat(editor, bridge);
    const startLine = sel.startLineNumber;
    const endLine = sel.endLineNumber;

    const lines: string[] = [];
    if (format === 'html') {
        lines.push("'<ol>");
        for (let i = startLine; i <= endLine; i++) {
            const [, content, tq] = stripCommentPrefix(model.getLineContent(i));
            lines.push(`'<li>${content}</li>${tq}`);
        }
        lines.push("'</ol>");
    } else {
        let num = 1;
        for (let i = startLine; i <= endLine; i++) {
            const [, content, tq] = stripCommentPrefix(model.getLineContent(i));
            lines.push(`'${num}. ${content}${tq}`);
            num++;
        }
    }

    editor.executeEdits('calcpad-format-ol', [{
        range: new monaco.Range(startLine, 1, endLine, model.getLineLength(endLine) + 1),
        text: lines.join('\n'),
        forceMoveMarkers: true,
    }]);
}

function toggleComment(editor: monaco.editor.IStandaloneCodeEditor): void {
    const model = editor.getModel();
    if (!model) return;
    const sel = editor.getSelection();
    if (!sel) return;
    const startLine = sel.startLineNumber;
    const endLine = sel.endLineNumber;

    let allCommented = true;
    for (let i = startLine; i <= endLine; i++) {
        if (!model.getLineContent(i).startsWith("'")) { allCommented = false; break; }
    }

    const edits: monaco.editor.IIdentifiedSingleEditOperation[] = [];
    for (let i = startLine; i <= endLine; i++) {
        const text = model.getLineContent(i);
        if (allCommented) {
            if (text.startsWith("'")) {
                edits.push({ range: new monaco.Range(i, 1, i, 2), text: '', forceMoveMarkers: true });
            }
        } else {
            edits.push({ range: new monaco.Range(i, 1, i, 1), text: "'", forceMoveMarkers: true });
        }
    }
    editor.executeEdits('calcpad-toggle-comment', edits);
}

function uncomment(editor: monaco.editor.IStandaloneCodeEditor): void {
    const model = editor.getModel();
    if (!model) return;
    const sel = editor.getSelection();
    if (!sel) return;

    const edits: monaco.editor.IIdentifiedSingleEditOperation[] = [];
    for (let i = sel.startLineNumber; i <= sel.endLineNumber; i++) {
        if (model.getLineContent(i).startsWith("'")) {
            edits.push({ range: new monaco.Range(i, 1, i, 2), text: '', forceMoveMarkers: true });
        }
    }
    editor.executeEdits('calcpad-uncomment', edits);
}

async function pasteAsComment(editor: monaco.editor.IStandaloneCodeEditor): Promise<void> {
    let clipboardText = '';
    try {
        clipboardText = await navigator.clipboard.readText();
    } catch {
        return;
    }
    if (!clipboardText) return;

    const commented = clipboardText.split('\n').map(l => "'" + l).join('\n');
    const sel = editor.getSelection();
    if (!sel) return;

    editor.executeEdits('calcpad-paste-as-comment', [{
        range: sel,
        text: commented,
        forceMoveMarkers: true,
    }]);
}
