/**
 * Serialization and content-update utilities for persisting UI overrides
 * as HTML comment JSON blocks in Calcpad files.
 *
 * Reading/parsing is handled server-side by HtmlCommentParser via the definitions API.
 * These functions handle the write-side: serializing overrides into the comment format
 * and inserting/replacing the comment in the file content.
 */

const COMMENT_OPEN = "'<!--";
const COMMENT_CLOSE = "-->";

/**
 * Serialize UI overrides into a Calcpad HTML comment string.
 * If `existingJson` is provided, merges `uiOverrides` into it (preserving sibling keys like `settings`).
 * Returns the full comment line, e.g. `'<!--{"uiOverrides": {"x": "5.5"}}-->`
 */
export function serializeUiOverrides(
    overrides: Record<string, string>,
    existingJson?: Record<string, unknown>
): string {
    const json: Record<string, unknown> = existingJson ? { ...existingJson } : {};

    if (Object.keys(overrides).length > 0) {
        json.uiOverrides = overrides;
    } else {
        delete json.uiOverrides;
    }

    return `${COMMENT_OPEN}${JSON.stringify(json)}${COMMENT_CLOSE}`;
}

/**
 * Try to parse a line as an HTML comment JSON block.
 * Returns the parsed JSON object if successful, or null.
 */
function tryParseCommentLine(line: string): Record<string, unknown> | null {
    const trimmed = line.trim();

    // Strip leading comment quote character (' or ")
    let inner = trimmed;
    if (inner.startsWith("'") || inner.startsWith('"')) {
        inner = inner.slice(1);
    }
    // Strip trailing comment quote if present
    if (inner.endsWith("'") || inner.endsWith('"')) {
        inner = inner.slice(0, -1);
    }

    const openIdx = inner.indexOf('<!--');
    if (openIdx < 0) return null;

    const afterOpen = inner.slice(openIdx + 4);
    const closeIdx = afterOpen.indexOf('-->');
    if (closeIdx < 0) return null;

    const jsonStr = afterOpen.slice(0, closeIdx).trim();
    if (!jsonStr) return null;

    try {
        const parsed = JSON.parse(jsonStr);
        if (typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)) {
            return parsed as Record<string, unknown>;
        }
    } catch {
        // Not valid JSON
    }
    return null;
}

/**
 * Update or insert a uiOverrides HTML comment in the file content.
 *
 * - If `commentLine` is provided (from the definitions API), replaces that line in-place.
 * - Otherwise, scans for a line containing `"uiOverrides"` as a fallback.
 * - If no existing comment is found and overrides are non-empty, inserts at line 0.
 * - If overrides are empty, keeps the comment line as `'<!--{}-->'` (preserving sibling keys).
 *
 * When replacing, parses the existing line's JSON to preserve sibling keys like `"settings"`.
 */
export function updateUiOverridesInContent(
    content: string,
    overrides: Record<string, string>,
    commentLine?: number
): string {
    const lines = content.split('\n');

    // Find the target line: either from API-provided commentLine or by scanning
    let targetLine = -1;
    let existingJson: Record<string, unknown> | null = null;

    if (commentLine !== undefined && commentLine >= 0 && commentLine < lines.length) {
        existingJson = tryParseCommentLine(lines[commentLine]);
        if (existingJson) {
            targetLine = commentLine;
        }
    }

    // Fallback: scan for a line containing "uiOverrides"
    if (targetLine < 0) {
        for (let i = 0; i < lines.length; i++) {
            if (!lines[i].includes('"uiOverrides"')) continue;
            const parsed = tryParseCommentLine(lines[i]);
            if (parsed && 'uiOverrides' in parsed) {
                targetLine = i;
                existingJson = parsed;
                break;
            }
        }
    }

    const newCommentLine = serializeUiOverrides(overrides, existingJson ?? undefined);

    if (targetLine >= 0) {
        // Replace existing line
        lines[targetLine] = newCommentLine;
    } else if (Object.keys(overrides).length > 0) {
        // Insert at line 0
        lines.unshift(newCommentLine);
    }

    return lines.join('\n');
}
