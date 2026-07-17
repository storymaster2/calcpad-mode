import { getIndentLength } from './comment-formatting';

/**
 * Parsed shape of a Calcpad definition-metadata comment
 * (`'<!--{...}-->`). Unknown keys are preserved via the index signature so a
 * round-trip through the editor UI doesn't drop properties it doesn't render.
 */
export interface MetadataCommentData {
    desc?: string;
    paramTypes?: string[];
    paramDesc?: string[];
    settings?: Record<string, string | number | boolean>;
    LintIgnore?: string[];
    EndLintIgnore?: string[];
    [key: string]: unknown;
}

/** A metadata comment located on a single document line. */
export interface MetadataCommentBlock {
    /** 0-based line index containing the comment */
    line: number;
    /** Leading whitespace of the line */
    indent: string;
    /** Closing comment quote (`'`) if the line ends with one, else '' */
    trailingQuote: string;
    /** Raw text between `<!--` and `-->`, trimmed */
    rawJson: string;
    /** Parsed object, or null when the JSON is malformed */
    data: MetadataCommentData | null;
    valid: boolean;
    /** Which properties actually apply to this line; set by the host. */
    context?: MetadataLineContext;
}

/**
 * Describes which metadata properties are meaningful for a comment line, based
 * on the surrounding document. Used to hide fields that can't apply here.
 */
export interface MetadataLineContext {
    /**
     * Parameter count of the definition this comment documents: > 0 for a
     * function/macro, 0 for a plain variable/custom-unit definition, null when
     * no definition follows the comment.
     */
    paramCount: number | null;
    /** True when a definition (any kind) follows the comment. */
    hasDefinition: boolean;
    /** True when an unclosed LintIgnore region is open at this line. */
    insideOpenLintRegion: boolean;
}

/** Valid `paramTypes` values for custom functions (f(x;y) = ...). */
export const FUNCTION_PARAM_TYPES = ['value', 'vector', 'matrix', 'any'] as const;

/** Valid `paramTypes` values for macros (#def) — TokenType enum names. */
export const MACRO_PARAM_TYPES = [
    'None', 'Const', 'Operator', 'Bracket', 'LineContinuation',
    'Variable', 'LocalVariable', 'Function', 'Macro', 'MacroParameter',
    'Units', 'Setting',
    'Keyword', 'ControlBlockKeyword', 'EndKeyword', 'Command',
    'Include', 'FilePath', 'DataExchangeKeyword',
    'Comment', 'HtmlComment', 'Tag', 'HtmlContent', 'JavaScript', 'Css', 'Svg',
    'Input', 'Format',
] as const;

export interface MetadataSettingKey {
    key: string;
    detail: string;
    type: 'number' | 'boolean' | 'string' | 'enum';
    options?: string[];
    def: string | number | boolean;
}

/** Recognized keys for the `settings` overrides object. */
export const METADATA_SETTINGS_KEYS: MetadataSettingKey[] = [
    { key: 'decimals', detail: 'Decimal places in output (0-15)', type: 'number', def: 4 },
    { key: 'degrees', detail: 'Angle unit: 0=radians, 1=degrees, 2=gradians', type: 'enum', options: ['0', '1', '2'], def: 0 },
    { key: 'complex', detail: 'Enable complex number mode', type: 'boolean', def: false },
    { key: 'substitute', detail: 'Substitute variable values into expressions', type: 'boolean', def: true },
    { key: 'formatEquations', detail: 'Format equations in output', type: 'boolean', def: true },
    { key: 'zeroSmallMatrixElements', detail: 'Zero out near-zero matrix elements', type: 'boolean', def: true },
    { key: 'maxOutputCount', detail: 'Maximum output rows (5-100)', type: 'number', def: 20 },
    { key: 'units', detail: 'Unit system string', type: 'string', def: 'm' },
    { key: 'vectorGraphics', detail: 'Render plots as SVG', type: 'boolean', def: false },
    {
        key: 'colorScale', detail: 'Plot color scale', type: 'enum', def: 'Rainbow',
        options: ['None', 'Gray', 'Rainbow', 'Terrain', 'VioletToYellow', 'GreenToYellow', 'Blues', 'BlueToYellow', 'BlueToRed', 'PurpleToYellow'],
    },
    { key: 'smoothScale', detail: 'Smooth color scale transitions', type: 'boolean', def: false },
    { key: 'shadows', detail: 'Enable 3-D plot shadows', type: 'boolean', def: true },
    { key: 'adaptivePlot', detail: 'Use adaptive sampling for plots', type: 'boolean', def: true },
];

export interface LintCode {
    code: string;
    description: string;
}

/**
 * Linter diagnostic codes, mirroring Calcpad.Highlighter's ErrorCodes catalog.
 * Used to populate the lint-ignore multi-select. Keep in sync with
 * `Calcpad.Highlighter/Linter/Constants/ErrorCodes.cs`.
 */
export const LINT_CODES: LintCode[] = [
    { code: 'CPD-1101', description: 'Malformed #include statement' },
    { code: 'CPD-1102', description: 'Missing #include filename' },
    { code: 'CPD-2201', description: 'Duplicate macro definition' },
    { code: 'CPD-2202', description: "Macro name must end with '$'" },
    { code: 'CPD-2203', description: "Macro parameter must end with '$'" },
    { code: 'CPD-2204', description: 'Invalid macro name (must start with a letter)' },
    { code: 'CPD-2205', description: 'Malformed #def syntax' },
    { code: 'CPD-2206', description: 'Unmatched #def or #end def' },
    { code: 'CPD-2207', description: 'Nested macro definition not allowed' },
    { code: 'CPD-2208', description: 'Macro parameter must start with a letter' },
    { code: 'CPD-2209', description: 'Macro definition inside control block has no effect' },
    { code: 'CPD-2210', description: 'Invalid character in macro name' },
    { code: 'CPD-2211', description: 'Invalid character in macro parameter' },
    { code: 'CPD-2212', description: 'Duplicate macro parameter' },
    { code: 'CPD-2213', description: 'Required parameter after optional parameter' },
    { code: 'CPD-3101', description: 'Unmatched opening parenthesis' },
    { code: 'CPD-3102', description: 'Unmatched closing parenthesis' },
    { code: 'CPD-3103', description: 'Unmatched opening square bracket' },
    { code: 'CPD-3104', description: 'Unmatched closing square bracket' },
    { code: 'CPD-3105', description: 'Unmatched opening curly brace or control block' },
    { code: 'CPD-3106', description: 'Unmatched closing curly brace' },
    { code: 'CPD-3201', description: 'Invalid variable name (must start with letter)' },
    { code: 'CPD-3203', description: 'Invalid function name' },
    { code: 'CPD-3204', description: 'Function name conflicts with built-in function' },
    { code: 'CPD-3205', description: 'Variable name conflicts with keyword' },
    { code: 'CPD-3206', description: 'Variable name conflicts with built-in unit' },
    { code: 'CPD-3207', description: 'Variable name conflicts with built-in constant' },
    { code: 'CPD-3208', description: 'Function must have at least one parameter' },
    { code: 'CPD-3215', description: 'Required parameter after optional parameter in function definition' },
    { code: 'CPD-3301', description: 'Undefined variable' },
    { code: 'CPD-3302', description: 'Function called with incorrect parameter count' },
    { code: 'CPD-3303', description: 'Undefined macro' },
    { code: 'CPD-3304', description: 'Macro called with incorrect parameter count' },
    { code: 'CPD-3305', description: 'Undefined function' },
    { code: 'CPD-3306', description: 'Invalid element access' },
    { code: 'CPD-3307', description: 'Too few parameters' },
    { code: 'CPD-3308', description: 'Too many parameters' },
    { code: 'CPD-3309', description: 'Parameter type mismatch' },
    { code: 'CPD-3310', description: 'Undefined unit' },
    { code: 'CPD-3311', description: 'Empty parameter in function call' },
    { code: 'CPD-3312', description: 'Unused variable' },
    { code: 'CPD-3313', description: 'Unused function' },
    { code: 'CPD-3401', description: 'Invalid operator usage' },
    { code: 'CPD-3402', description: 'Mismatched operator' },
    { code: 'CPD-3403', description: 'Command must be at the start of a statement' },
    { code: 'CPD-3404', description: 'Invalid command syntax' },
    { code: 'CPD-3405', description: 'Invalid control structure syntax' },
    { code: 'CPD-3406', description: 'Unknown directive' },
    { code: 'CPD-3407', description: 'Invalid assignment' },
    { code: 'CPD-3408', description: 'Invalid CustomUnit syntax' },
    { code: 'CPD-3409', description: '# directive not allowed inside command block' },
    { code: 'CPD-3410', description: 'Invalid command syntax' },
    { code: 'CPD-3411', description: 'Incomplete expression' },
    { code: 'CPD-3412', description: 'Command variable mismatch' },
    { code: 'CPD-3413', description: 'Reassignment of constant' },
    { code: 'CPD-3414', description: 'Outer scope assignment to undefined variable' },
    { code: 'CPD-3415', description: 'Invalid #UI format' },
    { code: 'CPD-3416', description: 'Invalid paramType value' },
    { code: 'CPD-3417', description: 'Invalid metadata comment JSON' },
    { code: 'CPD-3601', description: 'Invalid format specifier' },
];

/**
 * Detect a single-line metadata comment (`'<!--{...}-->`) on the cursor's line.
 * Mirrors the tokenizer/linter rules: the line must open with a comment quote
 * (`'` or `"`) and contain a `<!--{ ... }-->` block that closes on the same
 * line. Multi-line blocks are out of scope (returns null) so edits round-trip
 * losslessly. Returns null when the cursor line holds no such comment.
 */
export function findMetadataCommentBlock(lines: string[], cursorLine: number): MetadataCommentBlock | null {
    if (cursorLine < 0 || cursorLine >= lines.length) return null;

    const lineText = lines[cursorLine];
    const indentLen = getIndentLength(lineText);
    const indent = lineText.slice(0, indentLen);
    const rest = lineText.slice(indentLen);

    const quote = rest[0];
    if (quote !== "'" && quote !== '"') return null;

    const openIdx = rest.indexOf('<!--');
    if (openIdx < 0) return null;
    const afterOpen = rest.slice(openIdx + 4);
    const closeIdx = afterOpen.indexOf('-->');
    if (closeIdx < 0) return null;

    const rawJson = afterOpen.slice(0, closeIdx).trim();
    if (!rawJson.startsWith('{')) return null;

    const afterClose = afterOpen.slice(closeIdx + 3).trim();
    const trailingQuote = afterClose === quote ? quote : '';

    let data: MetadataCommentData | null = null;
    let valid = false;
    try {
        const parsed = JSON.parse(rawJson);
        if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
            data = parsed as MetadataCommentData;
            valid = true;
        }
    } catch {
        // Malformed JSON — surfaced to the UI via valid === false
    }

    return { line: cursorLine, indent, trailingQuote, rawJson, data, valid };
}

/**
 * Build a metadata-comment line from a data object, preserving the original
 * indentation and trailing comment quote. Empty strings and empty objects are
 * dropped so the serialized comment stays minimal. Empty arrays are kept: they
 * are meaningful for the LintIgnore/EndLintIgnore region markers, and the panel
 * never emits empty paramTypes/paramDesc arrays in the first place.
 */
export function serializeMetadataComment(data: MetadataCommentData, indent = '', trailingQuote = ''): string {
    const clean: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(data)) {
        if (value === undefined || value === null) continue;
        if (typeof value === 'string' && value.length === 0) continue;
        if (typeof value === 'object' && !Array.isArray(value) && Object.keys(value as object).length === 0) continue;
        clean[key] = value;
    }
    return `${indent}'<!--${JSON.stringify(clean)}-->${trailingQuote}`;
}

/**
 * Classify a definition line for metadata purposes. Returns the parameter count
 * for a function (`f(x;y)=`) or macro (`#def m$(a$;b$)`) definition, 0 for a
 * plain variable/custom-unit definition (`name = …`), or null when the line
 * isn't a recognizable definition.
 */
export function parseDefinitionParamCount(text: string): number | null {
    const trimmed = text.trimStart();

    const macro = trimmed.match(/^#def\s+[A-Za-z_]\w*\$\s*(?:\(([^)]*)\))?/);
    if (macro) return countParams(macro[1]);

    const func = trimmed.match(/^[A-Za-z_]\w*\s*\(([^)]*)\)\s*=/);
    if (func) return countParams(func[1]);

    if (/^[A-Za-z_][\w.]*\s*=/.test(trimmed)) return 0;

    return null;
}

function countParams(inner: string | undefined): number {
    if (!inner || inner.trim() === '') return 0;
    return inner.split(';').filter(p => p.trim() !== '').length;
}

/**
 * Analyze which metadata properties apply to a comment on the given line by
 * inspecting the document around it: the definition it documents (the next
 * non-blank, non-comment line) and whether a LintIgnore region opened earlier
 * is still open here.
 */
export function analyzeMetadataLine(lines: string[], commentLine: number): MetadataLineContext {
    let paramCount: number | null = null;
    for (let i = commentLine + 1; i < lines.length; i++) {
        const t = lines[i].trim();
        if (t === '') continue;
        // Skip stacked comment/metadata lines above the definition.
        if (t.startsWith("'") || t.startsWith('"')) continue;
        paramCount = parseDefinitionParamCount(lines[i]);
        break;
    }

    let insideOpenLintRegion = false;
    for (let i = 0; i < commentLine; i++) {
        const block = findMetadataCommentBlock(lines, i);
        if (!block?.valid || !block.data) continue;
        if (block.data.EndLintIgnore !== undefined) insideOpenLintRegion = false;
        if (block.data.LintIgnore !== undefined) insideOpenLintRegion = true;
    }

    return { paramCount, hasDefinition: paramCount !== null, insideOpenLintRegion };
}
