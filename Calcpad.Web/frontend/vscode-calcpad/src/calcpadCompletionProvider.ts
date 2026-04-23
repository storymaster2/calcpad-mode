import * as vscode from 'vscode';
import { CalcpadInsertManager, InsertItem } from './calcpadInsertManager';
import { CalcpadDefinitionsService } from './calcpadDefinitionsService';

export class CalcpadCompletionProvider implements vscode.CompletionItemProvider {
    private insertManager: CalcpadInsertManager;
    private definitionsService: CalcpadDefinitionsService;
    private outputChannel: vscode.OutputChannel;

    constructor(definitionsService: CalcpadDefinitionsService, insertManager: CalcpadInsertManager, outputChannel: vscode.OutputChannel) {
        this.insertManager = insertManager;
        this.definitionsService = definitionsService;
        this.outputChannel = outputChannel;
    }

    async provideCompletionItems(
        document: vscode.TextDocument,
        position: vscode.Position,
        token: vscode.CancellationToken,
        context: vscode.CompletionContext
    ): Promise<vscode.CompletionItem[]> {
        // Check if we're inside a metadata comment JSON block first
        const lineText = document.lineAt(position.line).text;
        const beforeCursor = lineText.substring(0, position.character);
        const metadataCompletions = this.provideMetadataJsonCompletions(beforeCursor, position);
        if (metadataCompletions) {
            return metadataCompletions;
        }

        const completionItems: vscode.CompletionItem[] = [];

        // Get the word being typed
        const wordRange = document.getWordRangeAtPosition(position);
        const word = wordRange ? document.getText(wordRange) : '';

        this.outputChannel.appendLine('[COMPLETION] Word: "' + word + '" at position ' + position.line + ':' + position.character);

        // Ensure snippets are loaded
        if (!this.insertManager.isLoaded()) {
            try {
                await this.insertManager.loadSnippets();
            } catch (error) {
                this.outputChannel.appendLine('[COMPLETION] Failed to load snippets: ' + error);
            }
        }

        // Get user-defined content from cached definitions (highest priority)
        try {
            const definitions = this.definitionsService.getCachedDefinitions(document.uri.toString());

            if (definitions) {
                // Add user-defined variables
                for (const variable of definitions.variables) {
                    if (!word || variable.name.toLowerCase().includes(word.toLowerCase())) {
                        const item = new vscode.CompletionItem(variable.name, vscode.CompletionItemKind.Variable);
                        item.detail = variable.description || ('Variable = ' + (variable.expression || ''));
                        let varDoc = '**User-defined variable**\n\nValue: `' + (variable.expression || '') + '`';
                        if (variable.description) {
                            varDoc += '\n\n' + variable.description;
                        }
                        item.documentation = new vscode.MarkdownString(varDoc);
                        item.sortText = '0_' + variable.name; // Sort user-defined content first
                        completionItems.push(item);
                    }
                }

                // Add user-defined functions
                for (const func of definitions.functions) {
                    if (!word || func.name.toLowerCase().includes(word.toLowerCase())) {
                        const item = new vscode.CompletionItem(func.name, vscode.CompletionItemKind.Function);
                        const paramStr = func.parameters.join('; ');
                        item.detail = func.description || ('Function(' + paramStr + ')');
                        item.documentation = new vscode.MarkdownString(
                            this.buildParameterizedDoc('User-defined function', func.description, func.parameters, func.paramTypes, func.paramDescriptions, undefined, func.defaults)
                        );
                        item.insertText = new vscode.SnippetString(func.name + '(' + this.createParameterSnippet(func.parameters) + ')');
                        item.sortText = '0_' + func.name;
                        completionItems.push(item);
                    }
                }

                // Add user-defined macros
                for (const macro of definitions.macros) {
                    if (!word || macro.name.toLowerCase().includes(word.toLowerCase())) {
                        const item = new vscode.CompletionItem(macro.name, vscode.CompletionItemKind.Class);
                        const paramStr = macro.parameters.join('; ');
                        item.detail = macro.description || (macro.parameters.length > 0 ? 'Macro(' + paramStr + ')' : 'Macro');
                        item.documentation = new vscode.MarkdownString(
                            this.buildParameterizedDoc('User-defined macro', macro.description, macro.parameters, macro.paramTypes, macro.paramDescriptions, macro.source !== 'local' ? (macro.sourceFile || macro.source) : undefined, macro.defaults)
                        );

                        if (macro.parameters.length > 0) {
                            item.insertText = new vscode.SnippetString(macro.name + '(' + this.createParameterSnippet(macro.parameters) + ')');
                        } else {
                            item.insertText = macro.name;
                        }
                        item.sortText = '0_' + macro.name;
                        completionItems.push(item);
                    }
                }

                // Add custom units
                for (const unit of definitions.customUnits) {
                    if (!word || unit.name.toLowerCase().includes(word.toLowerCase())) {
                        const item = new vscode.CompletionItem(unit.name, vscode.CompletionItemKind.Unit);
                        item.detail = 'Custom Unit = ' + (unit.expression || '');
                        item.documentation = new vscode.MarkdownString('**Custom unit**\n\nDefinition: `' + (unit.expression || '') + '`');
                        item.sortText = '0_' + unit.name;
                        completionItems.push(item);
                    }
                }
            }

        } catch (error) {
            this.outputChannel.appendLine('[COMPLETION ERROR] ' + error);
        }

        // Add built-in content from insert manager — only snippets that define a keyword
        // (function, command, unit, constant, operator, setting, control keyword). UI-only
        // entries like HTML tags, markdown syntax, symbols, and block templates are excluded.
        const allInsertItems = this.insertManager.getAllItems().filter(item => !!item.keywordType);
        for (const insertItem of allInsertItems) {
            // Filter by search term if provided
            if (!word || this.matchesSearchTerm(insertItem, word)) {
                const completionItem = this.convertInsertItemToCompletionItem(insertItem);
                if (completionItem) {
                    // Sort built-ins after user-defined content
                    completionItem.sortText = `1_${completionItem.label}`;
                    completionItems.push(completionItem);
                }
            }
        }

        this.outputChannel.appendLine(`[COMPLETION] Returning ${completionItems.length} items`);
        return completionItems;
    }

    /**
     * Check if insert item matches the search term
     */
    private matchesSearchTerm(item: InsertItem, searchTerm: string): boolean {
        const term = searchTerm.toLowerCase();
        return (
            item.tag.toLowerCase().includes(term) ||
            (item.label?.toLowerCase().includes(term) ?? false) ||
            item.description.toLowerCase().includes(term) ||
            (item.categoryPath?.toLowerCase().includes(term) ?? false)
        );
    }

    /**
     * Convert an InsertItem to a CompletionItem
     */
    private convertInsertItemToCompletionItem(item: InsertItem): vscode.CompletionItem | null {
        // Determine completion item kind based on content
        let kind = vscode.CompletionItemKind.Text;
        let insertText = item.tag;
        
        // Categorize based on tag content and category path
        if (item.tag.includes('(') || item.categoryPath?.toLowerCase().includes('function')) {
            kind = vscode.CompletionItemKind.Function;
            // Extract function name and create snippet
            const funcMatch = item.tag.match(/^([a-zA-Z_][a-zA-Z0-9_]*)/);
            if (funcMatch) {
                const funcName = funcMatch[1];
                // Try to extract parameters from the tag
                const paramMatch = item.tag.match(/\\(([^)]*)\\)/);
                if (paramMatch && paramMatch[1].trim()) {
                    const params = paramMatch[1].split(/[;,]/).map(p => p.trim()).filter(p => p);
                    insertText = `${funcName}(${this.createParameterSnippet(params)})`;
                } else {
                    insertText = `${funcName}()`;
                }
            }
        } else if (item.categoryPath?.toLowerCase().includes('constant') || 
                   item.tag.match(/^[A-Za-z_][A-Za-z0-9_]*\\s*=/) ||
                   ['π', 'e', 'φ', 'γ'].includes(item.tag)) {
            kind = vscode.CompletionItemKind.Constant;
            // For constants with assignments, just use the name part
            const constMatch = item.tag.match(/^([A-Za-z_π][A-Za-z0-9_]*)/);
            if (constMatch) {
                insertText = constMatch[1];
            }
        } else if (item.categoryPath?.toLowerCase().includes('operator')) {
            kind = vscode.CompletionItemKind.Operator;
        }

        const completionItem = new vscode.CompletionItem(
            item.label || item.tag,
            kind
        );
        
        completionItem.detail = item.categoryPath || 'Built-in';
        completionItem.documentation = new vscode.MarkdownString(
            `**${item.categoryPath || 'Built-in'}**\\n\\n${item.description}`
        );
        
        // Use snippet string for functions, plain text for others
        if (kind === vscode.CompletionItemKind.Function && insertText.includes('${')) {
            completionItem.insertText = new vscode.SnippetString(insertText);
        } else {
            completionItem.insertText = insertText;
        }

        return completionItem;
    }

    /**
     * Build a markdown documentation string for a parameterized definition (function or macro).
     * Includes description, parameter types, and parameter descriptions when available.
     */
    private buildParameterizedDoc(
        heading: string,
        description?: string,
        params?: string[],
        paramTypes?: string[],
        paramDescriptions?: string[],
        sourceFile?: string,
        defaults?: (string | null)[]
    ): string {
        let doc = `**${heading}**`;

        if (sourceFile) {
            doc += `\n\nSource: \`${sourceFile}\``;
        }

        if (description) {
            doc += '\n\n' + description;
        }

        if (params && params.length > 0) {
            const hasTypes = paramTypes && paramTypes.length > 0;
            const hasDescs = paramDescriptions && paramDescriptions.length > 0;
            const hasDefaults = defaults && defaults.length > 0;

            if (hasTypes || hasDescs || hasDefaults) {
                doc += '\n\n**Parameters:**';
                for (let i = 0; i < params.length; i++) {
                    const name = params[i];
                    const type = hasTypes && i < paramTypes.length ? paramTypes[i] : undefined;
                    const desc = hasDescs && i < paramDescriptions.length ? paramDescriptions[i] : undefined;
                    const def = hasDefaults && i < defaults.length ? defaults[i] : undefined;
                    let line = `\n- \`${name}\``;
                    if (type) line += ` *(${type})*`;
                    if (desc) line += ` — ${desc}`;
                    if (def !== undefined && def !== null) {
                        line += ` *(default: ${def})*`;
                    } else if (hasDefaults) {
                        line += ` *(required)*`;
                    }
                    doc += line;
                }
            } else {
                doc += '\n\nParameters: `' + params.join('; ') + '`';
            }
        }

        return doc;
    }

    // ===== Metadata comment JSON autocomplete =====

    /** Valid paramType values for custom functions */
    private static readonly FUNCTION_PARAM_TYPES = ['value', 'vector', 'matrix', 'any'];

    /** Valid paramType values for macros (TokenType names) */
    private static readonly MACRO_PARAM_TYPES = [
        'None', 'Const', 'Operator', 'Bracket', 'LineContinuation',
        'Variable', 'LocalVariable', 'Function', 'Macro', 'MacroParameter',
        'Units', 'Setting',
        'Keyword', 'ControlBlockKeyword', 'EndKeyword', 'Command',
        'Include', 'FilePath', 'DataExchangeKeyword',
        'Comment', 'HtmlComment', 'Tag', 'HtmlContent', 'JavaScript', 'Css', 'Svg',
        'Input', 'Format',
        'StringVariable', 'StringFunction', 'StringTable'
    ];

    /**
     * Provide completions when inside a metadata comment JSON block.
     * Returns null if the cursor is not in a metadata comment context.
     */
    private provideMetadataJsonCompletions(beforeCursor: string, position: vscode.Position): vscode.CompletionItem[] | null {
        const trimmed = beforeCursor.trimStart();

        // Must be a comment line (starts with ' or ")
        if (!trimmed.startsWith("'") && !trimmed.startsWith('"')) {
            return null;
        }

        // Must contain the <!--  marker (HTML comment start)
        const markerIdx = trimmed.indexOf('<!--');
        if (markerIdx < 0) {
            return null;
        }

        // Must not already have --> before cursor (cursor would be outside the block)
        const afterMarker = trimmed.substring(markerIdx + 4);
        if (afterMarker.includes('-->')) {
            return null;
        }

        // We're inside a '<!--{...  block (no closing --> yet)
        // The insert range is from cursor to cursor (insert, don't replace)
        const insertRange = new vscode.Range(position, position);

        // Determine what to complete: property names or paramType values

        // Check if we're inside a "paramTypes" array value
        if (this.isInsideParamTypesArray(afterMarker)) {
            const replaceRange = this.getArrayValueReplaceRange(beforeCursor, position);
            return this.getParamTypeValueCompletions(replaceRange);
        }

        // Check if we're inside a "settings" object
        if (this.isInsideSettingsObject(afterMarker)) {
            return this.getSettingsKeyCompletions(insertRange);
        }

        // Check if we're at a property name position (after { or ,)
        if (this.isAtPropertyNamePosition(afterMarker)) {
            const replaceRange = this.getArrayValueReplaceRange(beforeCursor, position);
            return this.getPropertyNameCompletions(replaceRange);
        }

        return null;
    }

    /**
     * Check if cursor is inside a "paramTypes": [...] array value
     */
    private isInsideParamTypesArray(afterMarker: string): boolean {
        const ptIdx = afterMarker.indexOf('"paramTypes"');
        if (ptIdx < 0) return false;

        const afterPt = afterMarker.substring(ptIdx + '"paramTypes"'.length);
        const bracketOpen = afterPt.indexOf('[');
        if (bracketOpen < 0) return false;

        const bracketClose = afterPt.indexOf(']', bracketOpen);
        return bracketClose < 0;
    }

    /**
     * Check if cursor is inside a "settings": {...} object value
     */
    private isInsideSettingsObject(afterMarker: string): boolean {
        const sIdx = afterMarker.indexOf('"settings"');
        if (sIdx < 0) return false;

        const afterS = afterMarker.substring(sIdx + '"settings"'.length);
        const braceOpen = afterS.indexOf('{');
        if (braceOpen < 0) return false;

        // Count braces to see if we're still inside
        let depth = 0;
        for (let i = braceOpen; i < afterS.length; i++) {
            if (afterS[i] === '{') depth++;
            else if (afterS[i] === '}') depth--;
            if (depth === 0) return false; // Closed
        }
        return true; // Still inside
    }

    /**
     * Check if cursor is at a JSON property name position (after { or , at depth 0).
     * Uses forward scanning to correctly handle string quoting.
     */
    private isAtPropertyNamePosition(afterMarker: string): boolean {
        if (afterMarker.length === 0) return false;

        let inString = false;
        let arrayDepth = 0;
        let braceDepth = 0;
        let lastSignificant = '';

        for (let i = 0; i < afterMarker.length; i++) {
            const c = afterMarker[i];

            if (inString) {
                if (c === '"' && afterMarker[i - 1] !== '\\') {
                    inString = false;
                }
                continue;
            }

            if (c === '"') { inString = true; continue; }
            if (c === '{') { braceDepth++; lastSignificant = '{'; continue; }
            if (c === '}') { braceDepth--; lastSignificant = '}'; continue; }
            if (c === '[') { arrayDepth++; continue; }
            if (c === ']') { arrayDepth--; continue; }

            if (braceDepth === 1 && arrayDepth === 0) {
                if (c === ',') lastSignificant = ',';
                else if (c === ':') lastSignificant = ':';
            }
        }

        // At a property name position if we're inside the top-level object,
        // not inside an array, and the last token was { or ,
        // Also allow when inString is true (user started typing a quoted property name)
        return braceDepth >= 1 && arrayDepth === 0 && (lastSignificant === '{' || lastSignificant === ',');
    }

    /**
     * Helper to set filterText and range on a completion item so VS Code shows it
     * even when there's no word at the cursor position.
     */
    private applyInsertFix(item: vscode.CompletionItem, range: vscode.Range): void {
        item.filterText = ' ';
        item.range = range;
    }

    /**
     * Find the replacement range for the current value being typed inside a JSON context.
     * Scans backwards from the cursor to find the start of the current token
     * (after opening quote, comma, bracket, brace, or whitespace).
     */
    private getArrayValueReplaceRange(beforeCursor: string, position: vscode.Position): vscode.Range {
        let startCol = position.character;
        for (let i = beforeCursor.length - 1; i >= 0; i--) {
            const ch = beforeCursor[i];
            if (ch === '"') {
                // Include the opening quote since insertText starts with "
                startCol = i;
                break;
            }
            if (ch === '[' || ch === ',' || ch === '{') {
                startCol = i + 1;
                break;
            }
        }
        return new vscode.Range(position.line, startCol, position.line, position.character);
    }

    /**
     * Get completion items for top-level HTML comment JSON property names
     */
    private getPropertyNameCompletions(range: vscode.Range): vscode.CompletionItem[] {
        const items: vscode.CompletionItem[] = [];

        const props: Array<{label: string; detail: string; snippet: string; sort: string}> = [
            { label: '"desc"', detail: 'Description of the definition', snippet: '"desc": "${1:description}"', sort: '0_desc' },
            { label: '"paramTypes"', detail: 'Type hints per parameter', snippet: '"paramTypes": [${1}]', sort: '0_paramTypes' },
            { label: '"paramDesc"', detail: 'Descriptions per parameter', snippet: '"paramDesc": [${1}]', sort: '0_paramDesc' },
            { label: '"settings"', detail: 'File settings overrides', snippet: '"settings": {${1}}', sort: '0_settings' },
            { label: '"LintIgnore"', detail: 'Suppress linter diagnostics (start region)', snippet: '"LintIgnore": [${1}]', sort: '0_LintIgnore' },
            { label: '"EndLintIgnore"', detail: 'End lint suppression region', snippet: '"EndLintIgnore": []', sort: '0_EndLintIgnore' },
        ];

        for (const p of props) {
            const item = new vscode.CompletionItem(p.label, vscode.CompletionItemKind.Property);
            item.detail = p.detail;
            item.insertText = new vscode.SnippetString(p.snippet);
            item.sortText = p.sort;
            // Strip quotes for filterText so VS Code matches against typed text
            item.filterText = p.label.replace(/"/g, '');
            item.range = range;
            items.push(item);
        }

        return items;
    }

    /** Valid settings keys from FileSettingsExtractor */
    private static readonly SETTINGS_KEYS: Array<{key: string; detail: string; value: string}> = [
        { key: 'decimals', detail: 'Decimal places in output (0-15)', value: '${1:4}' },
        { key: 'degrees', detail: 'Angle unit: 0=radians, 1=degrees, 2=gradians', value: '${1:0}' },
        { key: 'complex', detail: 'Enable complex number mode', value: '${1|true,false|}' },
        { key: 'substitute', detail: 'Substitute variable values into expressions', value: '${1|true,false|}' },
        { key: 'formatEquations', detail: 'Format equations in output', value: '${1|true,false|}' },
        { key: 'zeroSmallMatrixElements', detail: 'Zero out near-zero matrix elements', value: '${1|true,false|}' },
        { key: 'maxOutputCount', detail: 'Maximum output rows (5-100)', value: '${1:20}' },
        { key: 'units', detail: 'Unit system string', value: '"${1}"' },
        { key: 'vectorGraphics', detail: 'Render plots as SVG', value: '${1|true,false|}' },
        { key: 'colorScale', detail: 'Plot color scale', value: '"${1|None,Gray,Rainbow,Terrain,VioletToYellow,GreenToYellow,Blues,BlueToYellow,BlueToRed,PurpleToYellow|}"' },
        { key: 'smoothScale', detail: 'Smooth color scale transitions', value: '${1|true,false|}' },
        { key: 'shadows', detail: 'Enable 3-D plot shadows', value: '${1|true,false|}' },
        { key: 'adaptivePlot', detail: 'Use adaptive sampling for plots', value: '${1|true,false|}' },
    ];

    /**
     * Get completion items for settings object keys
     */
    private getSettingsKeyCompletions(range: vscode.Range): vscode.CompletionItem[] {
        const items: vscode.CompletionItem[] = [];

        for (const s of CalcpadCompletionProvider.SETTINGS_KEYS) {
            const item = new vscode.CompletionItem('"' + s.key + '"', vscode.CompletionItemKind.Property);
            item.detail = s.detail;
            item.insertText = new vscode.SnippetString('"' + s.key + '": ' + s.value);
            item.sortText = '0_' + s.key;
            this.applyInsertFix(item, range);
            items.push(item);
        }

        return items;
    }

    /**
     * Get completion items for paramType values.
     * Offers both function types and macro types.
     */
    private getParamTypeValueCompletions(range: vscode.Range): vscode.CompletionItem[] {
        const items: vscode.CompletionItem[] = [];

        for (const type of CalcpadCompletionProvider.FUNCTION_PARAM_TYPES) {
            const item = new vscode.CompletionItem('"' + type + '"', vscode.CompletionItemKind.EnumMember);
            item.detail = 'Function param type';
            item.insertText = '"' + type + '"';
            item.sortText = '0_' + type;
            item.filterText = type;
            item.range = range;
            items.push(item);
        }

        for (const type of CalcpadCompletionProvider.MACRO_PARAM_TYPES) {
            if (CalcpadCompletionProvider.FUNCTION_PARAM_TYPES.includes(type.toLowerCase())) continue;
            const item = new vscode.CompletionItem('"' + type + '"', vscode.CompletionItemKind.EnumMember);
            item.detail = 'Macro param type (TokenType)';
            item.insertText = '"' + type + '"';
            item.sortText = '1_' + type;
            item.filterText = type;
            item.range = range;
            items.push(item);
        }

        return items;
    }

    /**
     * Create snippet parameters for function calls
     */
    private createParameterSnippet(params: string[]): string {
        return params.map((param, index) => {
            // Clean up parameter name for display
            let cleanParam = param.trim();
            // Remove $ suffix for display in snippet placeholder
            if (cleanParam.endsWith('$')) {
                cleanParam = cleanParam.slice(0, -1);
            }
            // Remove ? suffix for optional parameters
            if (cleanParam.endsWith('?')) {
                cleanParam = cleanParam.slice(0, -1);
            }
            // Remove type annotations if present (e.g., "x:number" -> "x")
            cleanParam = cleanParam.split(':')[0].trim();
            
            return `\${${index + 1}:${cleanParam}}`;
        }).join('; ');
    }

    /**
     * Register the completion provider
     */
    public static register(definitionsService: CalcpadDefinitionsService, insertManager: CalcpadInsertManager, outputChannel: vscode.OutputChannel): vscode.Disposable {
        const provider = new CalcpadCompletionProvider(definitionsService, insertManager, outputChannel);
        return vscode.languages.registerCompletionItemProvider(
            ['calcpad', 'plaintext'], // Language selector
            provider,
            '.', '(', '$', '{' // Trigger characters ('{' for '<!--{' metadata comments)
        );
    }
}