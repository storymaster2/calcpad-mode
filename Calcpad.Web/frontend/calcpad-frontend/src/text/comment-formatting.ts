export type InlineFormat = 'bold' | 'italic' | 'underline' | 'subscript' | 'superscript';
export type CommentFormat = 'html' | 'markdown';

export const HTML_INLINE: Record<InlineFormat, [string, string]> = {
    bold: ['<strong>', '</strong>'],
    italic: ['<em>', '</em>'],
    underline: ['<ins>', '</ins>'],
    subscript: ['<sub>', '</sub>'],
    superscript: ['<sup>', '</sup>'],
};

export const MARKDOWN_INLINE: Record<InlineFormat, [string, string]> = {
    bold: ['**', '**'],
    italic: ['*', '*'],
    underline: ['++', '++'],
    subscript: ['~', '~'],
    superscript: ['^', '^'],
};

/** Length of the leading run of spaces/tabs on a line. */
export function getIndentLength(lineText: string): number {
    const m = lineText.match(/^[ \t]*/);
    return m ? m[0].length : 0;
}

/** Split a line into its leading indentation and the rest. */
export function splitIndent(lineText: string): [indent: string, rest: string] {
    const len = getIndentLength(lineText);
    return [lineText.slice(0, len), lineText.slice(len)];
}

/**
 * Strip the indentation and comment quote (') from a line, returning
 * [indent, content, trailingQuote]. The quote is looked for after the
 * indentation, not at column 1, so indented comment lines round-trip.
 */
export function stripCommentPrefix(lineText: string): [indent: string, content: string, trailingQuote: string] {
    const [indent, rest] = splitIndent(lineText);
    if (rest.startsWith("'")) {
        const inner = rest.substring(1);
        if (inner.endsWith("'")) {
            return [indent, inner.slice(0, -1), "'"];
        }
        return [indent, inner, ''];
    }
    return [indent, rest, ''];
}

/** True if the line already opens a comment (a ' right after its indentation). */
export function lineHasCommentPrefix(lineText: string): boolean {
    const [, rest] = splitIndent(lineText);
    return rest.startsWith("'");
}

/**
 * 1-based column at which a missing comment quote should be inserted —
 * right after the line's indentation — or null if the line already has one.
 * HTML/markdown formatting hotkeys need every touched line wrapped in a
 * comment for the tags to be recognized as HTML rather than literal text.
 */
export function getCommentPrefixInsertColumn(lineText: string): number | null {
    const indentLen = getIndentLength(lineText);
    if (lineText.slice(indentLen).startsWith("'")) return null;
    return indentLen + 1;
}

/** Build the replacement line text for a heading hotkey, preserving indentation. */
export function buildHeadingLine(lineText: string, level: number, format: CommentFormat): string {
    const [indent, rawContent, trailingQuote] = stripCommentPrefix(lineText);
    let content = rawContent;

    const htmlMatch = content.match(/^<h[1-6]>(.*)<\/h[1-6]>$/);
    if (htmlMatch) content = htmlMatch[1];
    const mdMatch = content.match(/^(#{1,6})\s+(.*)$/);
    if (mdMatch) content = mdMatch[2];

    if (format === 'html') {
        return `${indent}'<h${level}>${content}</h${level}>${trailingQuote}`;
    }
    return `${indent}'${'#'.repeat(level)} ${content}${trailingQuote}`;
}

/** Build the replacement line text for the paragraph hotkey, preserving indentation. */
export function buildParagraphLine(lineText: string): string {
    const [indent, content, trailingQuote] = stripCommentPrefix(lineText);
    return `${indent}'<p>${content}</p>${trailingQuote}`;
}

/**
 * Build the replacement lines for a bulleted/numbered list hotkey. The
 * wrapper tags (<ul>/<ol>) take the first selected line's indentation; each
 * item keeps its own line's indentation.
 */
export function buildListLines(lineTexts: string[], format: CommentFormat, ordered: boolean): string[] {
    if (lineTexts.length === 0) return [];
    const [wrapperIndent] = splitIndent(lineTexts[0]);
    const lines: string[] = [];

    if (format === 'html') {
        lines.push(`${wrapperIndent}'<${ordered ? 'ol' : 'ul'}>`);
        for (const lineText of lineTexts) {
            const [indent, content, tq] = stripCommentPrefix(lineText);
            lines.push(`${indent}'<li>${content}</li>${tq}`);
        }
        lines.push(`${wrapperIndent}'</${ordered ? 'ol' : 'ul'}>`);
    } else if (ordered) {
        let num = 1;
        for (const lineText of lineTexts) {
            const [indent, content, tq] = stripCommentPrefix(lineText);
            lines.push(`${indent}'${num}. ${content}${tq}`);
            num++;
        }
    } else {
        for (const lineText of lineTexts) {
            const [indent, content, tq] = stripCommentPrefix(lineText);
            lines.push(`${indent}'- ${content}${tq}`);
        }
    }

    return lines;
}
