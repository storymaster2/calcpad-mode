using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Parsing;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.ContentResolution
{
    /// <summary>
    /// Stage 3: Macro expansion and definition collection.
    /// </summary>
    public partial class ContentResolver
    {
        /// <summary>
        /// STAGE 3: Expand macros, collect all definitions
        /// Removes macro definitions and substitutes macro calls with their expanded content
        /// </summary>
        private Stage3Result ProcessStage3(Stage2Result stage2, Stage1Result stage1)
        {
            var lines = new List<string>();
            var sourceMap = new Dictionary<int, int>();
            var macroExpansions = new Dictionary<int, MacroExpansionInfo>();

            // Build macro map from stage2 definitions (skip duplicates, use first definition)
            var macros = new Dictionary<string, (List<string> Params, List<string> Defaults, List<string> Content)>(StringComparer.OrdinalIgnoreCase);
            foreach (var macroDef in stage2.MacroDefinitions)
            {
                if (!macros.ContainsKey(macroDef.Name))
                {
                    macros[macroDef.Name] = (macroDef.Params, macroDef.Defaults, macroDef.Content);
                }
            }

            // Process lines: skip macro definitions, expand macro calls
            bool inMultilineMacro = false;

            for (int i = 0; i < stage2.Lines.Count; i++)
            {
                var line = stage2.Lines[i];
                var trimmedSpan = line.AsSpan().Trim();

                // Check for multiline macro end
                if (inMultilineMacro)
                {
                    if (trimmedSpan.Equals("#end def", StringComparison.OrdinalIgnoreCase))
                    {
                        inMultilineMacro = false;
                    }
                    // Skip all lines inside macro definition
                    continue;
                }

                // Check for macro definition start (char-based, no regex)
                if (trimmedSpan.StartsWith("#def ", StringComparison.OrdinalIgnoreCase))
                {
                    if (HasEqualsOutsideParens(trimmedSpan, 5))
                    {
                        // Inline macro (#def name$(params) = content) - skip this single line
                        continue;
                    }

                    // Multiline macro start (#def name$(params)) - skip until #end def
                    inMultilineMacro = true;
                    continue;
                }

                // Regular line - expand macro calls, tracking which macros were expanded
                var originalLine = line;
                var expandedMacroNames = new List<string>();
                var expandedLine = ExpandMacros(line, macros, expandedMacroNames);
                var isFromMacroExpansion = expandedLine != originalLine;

                // Handle multiline expansions (macro content can have multiple lines)
                var expandedSubLines = expandedLine.Split('\n');
                var totalContentLines = expandedSubLines.Length;

                for (int subIdx = 0; subIdx < totalContentLines; subIdx++)
                {
                    lines.Add(expandedSubLines[subIdx]);
                    sourceMap[lines.Count - 1] = i;
                    if (isFromMacroExpansion)
                    {
                        macroExpansions[lines.Count - 1] = new MacroExpansionInfo
                        {
                            MacroNames = expandedMacroNames,
                            CallSiteLine = originalLine,
                            CallSiteStage2Line = i,
                            ContentLineIndex = subIdx,
                            TotalContentLines = totalContentLines
                        };
                    }
                }
            }

            // Build new includeMap for the filtered lines
            var filteredIncludeMap = new Dictionary<int, SourceInfo>();
            foreach (var kvp in sourceMap)
            {
                if (stage2.IncludeMap.TryGetValue(kvp.Value, out var sourceInfo))
                {
                    filteredIncludeMap[kvp.Key] = sourceInfo;
                }
            }

            // Tokenize in Lint mode: extracts full definition metadata
            var tokenizer = new CalcpadTokenizer();
            tokenizer.SetIncludeMap(filteredIncludeMap);
            var source = JoinLines(lines);
            var tokenizerResult = tokenizer.Tokenize(source, TokenizerMode.Lint);

            // All definitions come from tokenizer Lint mode
            var variablesWithDefs = tokenizerResult.VariableDefinitions;
            var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in variablesWithDefs) variables.Add(v.Name);
            // Also include variables from basic tracking (e.g., inside command blocks)
            foreach (var name in tokenizerResult.DefinedVariables.Keys) variables.Add(name);

            var functionsWithParams = tokenizerResult.FunctionDefinitions;
            var functions = new Dictionary<string, FunctionInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in functionsWithParams)
            {
                int requiredCount;
                if (f.Defaults != null)
                {
                    requiredCount = 0;
                    foreach (var d in f.Defaults)
                        if (d is null) requiredCount++;
                }
                else
                {
                    requiredCount = f.Params?.Count ?? 0;
                }
                functions[f.Name] = new FunctionInfo
                {
                    LineNumber = f.LineNumber,
                    ParamCount = f.Params?.Count ?? 0,
                    RequiredParamCount = requiredCount,
                    ParamNames = f.Params ?? new List<string>()
                };
            }

            var userDefinedMacros = stage2.UserDefinedMacros
                ?? new Dictionary<string, MacroInfo>(StringComparer.OrdinalIgnoreCase);
            var customUnits = tokenizerResult.CustomUnitDefinitions;
            var commandBlockFunctions = tokenizerResult.CommandBlockFunctions;

            // Build TypeTracker from collected definitions
            var typeTracker = new TypeTracker();
            typeTracker.SetTokensByLine(tokenizerResult.TokensByLine);
            PopulateTypeTracker(typeTracker, variablesWithDefs, functionsWithParams, stage2.MacroDefinitions, customUnits);

            // Collect variable assignments and usages from Lint-mode tokens for usage analysis
            var (variableAssignments, variableUsages) = CollectVariableAssignmentsAndUsages(tokenizerResult, lines);

            // Build variable index with source file info and original line mapping
            var variableIndex = BuildVariableIndex(
                variableAssignments, variableUsages, sourceMap, filteredIncludeMap,
                stage2.SourceMap, stage1.SourceMap, stage1.LineContinuationSegments);

            // Build function index from Lint-mode Function tokens
            var functionIndex = BuildFunctionIndex(
                tokenizerResult, functions, sourceMap, filteredIncludeMap,
                stage2.SourceMap, stage1.SourceMap, stage1.LineContinuationSegments);

            // Build macro index from definitions and expansion tracking
            var macroIndex = BuildMacroIndex(
                stage2.MacroDefinitions, macroExpansions, stage2.Lines,
                sourceMap, filteredIncludeMap, stage2.SourceMap, stage1.SourceMap,
                stage2.IncludeMap, stage1.LineContinuationSegments);

            return new Stage3Result
            {
                Lines = lines,
                SourceMap = sourceMap,
                MacroExpansions = macroExpansions,
                UserDefinedFunctions = functions,
                FunctionsWithParams = functionsWithParams,
                UserDefinedMacros = userDefinedMacros,
                DefinedVariables = variables,
                VariablesWithDefinitions = variablesWithDefs,
                CustomUnits = customUnits,
                TypeTracker = typeTracker,
                CommandBlockFunctions = commandBlockFunctions,
                VariableReassignments = tokenizerResult.VariableReassignments,
                OuterScopeAssignments = tokenizerResult.OuterScopeAssignments,
                VariableAssignments = variableAssignments,
                VariableUsages = variableUsages,
                VariableIndex = variableIndex,
                FunctionIndex = functionIndex,
                MacroIndex = macroIndex
            };
        }

        /// <summary>
        /// Populates the TypeTracker with all definitions in order of appearance.
        /// </summary>
        private void PopulateTypeTracker(
            TypeTracker typeTracker,
            List<VariableDefinition> variables,
            List<FunctionDefinition> functions,
            List<MacroDefinition> macros,
            List<CustomUnitDefinition> customUnits)
        {
            // Register custom units first (they can be used in expressions)
            foreach (var unit in customUnits)
            {
                typeTracker.RegisterCustomUnit(unit.Name, unit.Definition, unit.LineNumber, 0, unit.Source);
            }

            // Register functions
            foreach (var func in functions)
            {
                VariableInfo info;
                if (func.CommandBlock != null)
                {
                    // Command block function - use RegisterCommandBlockFunction for proper return type inference
                    info = typeTracker.RegisterCommandBlockFunction(
                        func.Name,
                        func.Params,
                        func.CommandBlock.Statements,
                        func.LineNumber,
                        0,
                        func.Source,
                        func.IsConst,
                        func.Defaults);
                }
                else
                {
                    // Normal function - use the expression for return type inference
                    info = typeTracker.RegisterFunction(func.Name, func.Params, func.Expression ?? "", func.LineNumber, 0, func.Source, func.IsConst, func.Defaults);
                }
                // Copy metadata from definition comment
                info.Description = func.Description;
                info.ParamTypes = func.ParamTypes;
                info.ParamDescriptions = func.ParamDescriptions;
            }

            // Register macros
            foreach (var macro in macros)
            {
                var isInline = macro.Content.Count == 1 && !string.IsNullOrWhiteSpace(macro.Content[0]);
                VariableInfo info;
                if (isInline)
                {
                    info = typeTracker.RegisterInlineMacro(macro.Name, macro.Params ?? new List<string>(), macro.Content[0], macro.LineNumber, 0, macro.Source, macro.Defaults);
                }
                else
                {
                    info = typeTracker.RegisterMultilineMacro(macro.Name, macro.Params ?? new List<string>(), macro.LineNumber, 0, macro.Source, macro.Defaults);
                }
                // Copy metadata from definition comment
                info.Description = macro.Description;
                info.ParamTypes = macro.ParamTypes;
                info.ParamDescriptions = macro.ParamDescriptions;
            }

            // Register variables (in order - this handles type changes correctly)
            foreach (var variable in variables)
            {
                VariableInfo info;
                // #read variables have known types: matrix (default) or vector (TYPE=V)
                if (variable.Definition == "#read matrix")
                {
                    info = typeTracker.RegisterReadVariable(variable.Name, isVector: false, variable.LineNumber, 0, variable.Source);
                }
                else if (variable.Definition == "#read vector")
                {
                    info = typeTracker.RegisterReadVariable(variable.Name, isVector: true, variable.LineNumber, 0, variable.Source);
                }
                else
                {
                    info = typeTracker.RegisterVariable(variable.Name, variable.Definition, variable.LineNumber, 0, variable.Source, variable.IsConst);
                }
                // Copy metadata from definition comment
                info.Description = variable.Description;
            }
        }


        /// <summary>
        /// Scans Lint-mode tokens to collect all variable assignment positions and usage positions.
        /// Assignments are Variable tokens on the left side of = (not comparison operators).
        /// Usages are all other Variable tokens.
        /// </summary>
        private static (List<(string Name, int Line, int Column, int Length)> Assignments,
                         List<(string Name, int Line, int Column)> Usages)
            CollectVariableAssignmentsAndUsages(TokenizerResult tokenizerResult, List<string> lines)
        {
            var assignments = new List<(string, int, int, int)>();
            var usages = new List<(string, int, int)>();
            var assignmentPositions = new HashSet<(int Line, int Column)>();

            // First pass: identify assignment targets per line
            foreach (var lineEntry in tokenizerResult.TokensByLine)
            {
                var lineNumber = lineEntry.Key;
                var tokens = lineEntry.Value;

                // Skip empty/comment/directive lines (same filtering as the linter)
                if (lineNumber < 0 || lineNumber >= lines.Count)
                    continue;
                var line = lines[lineNumber];
                if (LineParser.ShouldSkipLine(line) || LineParser.IsDirectiveLine(line.Trim()))
                    continue;

                // Find first Variable token on the line
                Token? firstVar = null;

                for (int t = 0; t < tokens.Count; t++)
                {
                    var token = tokens[t];

                    if (firstVar == null && (token.Type == TokenType.Variable || token.Type == TokenType.StringVariable || token.Type == TokenType.StringTable))
                        firstVar = token;

                    if (token.Type != TokenType.Operator || token.Text != "=")
                        continue;

                    // Check it's not a comparison operator (==, <=, >=, !=)
                    bool isComparison = false;
                    if (t > 0)
                    {
                        var prev = tokens[t - 1];
                        if (prev.Type == TokenType.Operator &&
                            prev.Column + prev.Length == token.Column &&
                            (prev.Text == "<" || prev.Text == ">" || prev.Text == "!" || prev.Text == "="))
                            isComparison = true;
                    }
                    if (t + 1 < tokens.Count)
                    {
                        var next = tokens[t + 1];
                        if (next.Type == TokenType.Operator &&
                            token.Column + token.Length == next.Column &&
                            next.Text == "=")
                            isComparison = true;
                    }

                    if (!isComparison && firstVar.HasValue)
                    {
                        var v = firstVar.Value;
                        assignments.Add((v.Text, v.Line, v.Column, v.Length));
                        assignmentPositions.Add((v.Line, v.Column));
                    }
                    break;
                }
            }

            // Second pass: collect all Variable tokens that are NOT assignment targets
            foreach (var token in tokenizerResult.Tokens)
            {
                if (token.Type != TokenType.Variable && token.Type != TokenType.StringVariable && token.Type != TokenType.StringTable)
                    continue;

                if (assignmentPositions.Contains((token.Line, token.Column)))
                    continue;

                usages.Add((token.Text, token.Line, token.Column));
            }

            return (assignments, usages);
        }

        /// <summary>
        /// Builds a variable index mapping each variable name to all its occurrences (assignments and usages),
        /// with positions mapped back to original source lines and annotated with include file info.
        /// </summary>
        private static Dictionary<string, List<SymbolLocation>> BuildVariableIndex(
            List<(string Name, int Line, int Column, int Length)> assignments,
            List<(string Name, int Line, int Column)> usages,
            Dictionary<int, int> stage3ToStage2Map,
            Dictionary<int, SourceInfo> includeMap,
            Dictionary<int, int> stage2ToStage1Map,
            Dictionary<int, int> stage1ToOriginalMap,
            Dictionary<int, List<LineContinuationSegment>> lineContinuationSegments)
        {
            var index = new Dictionary<string, List<SymbolLocation>>(StringComparer.Ordinal);

            // Map a stage3 position to original line + adjusted column, using continuation segments
            (int OriginalLine, int AdjustedColumn) MapPositionToOriginal(int stage3Line, int column)
            {
                var stage2Line = stage3ToStage2Map.TryGetValue(stage3Line, out var s2) ? s2 : stage3Line;
                var stage1Line = stage2ToStage1Map.TryGetValue(stage2Line, out var s1) ? s1 : stage2Line;
                return MapColumnThroughContinuation(stage1Line, column, lineContinuationSegments, stage1ToOriginalMap);
            }

            (string Source, string SourceFile, int OriginalLine) GetSourceInfo(int stage3Line)
            {
                if (includeMap.TryGetValue(stage3Line, out var info))
                    return (info.Source, info.SourceFile, info.OriginalLine);
                return ("local", null, -1);
            }

            // Add assignments
            foreach (var (name, line, column, length) in assignments)
            {
                var (source, sourceFile, includeOriginalLine) = GetSourceInfo(line);
                int originalLine, adjustedColumn;
                if (source == "include" && includeOriginalLine >= 0)
                {
                    originalLine = includeOriginalLine;
                    adjustedColumn = column;
                }
                else
                {
                    (originalLine, adjustedColumn) = MapPositionToOriginal(line, column);
                }

                if (!index.TryGetValue(name, out var locations))
                {
                    locations = new List<SymbolLocation>();
                    index[name] = locations;
                }

                locations.Add(new SymbolLocation
                {
                    Line = originalLine,
                    Column = adjustedColumn,
                    Length = length,
                    Source = source,
                    SourceFile = sourceFile,
                    IsAssignment = true
                });
            }

            // Add usages
            foreach (var (name, line, column) in usages)
            {
                var (source, sourceFile, includeOriginalLine) = GetSourceInfo(line);
                int originalLine, adjustedColumn;
                if (source == "include" && includeOriginalLine >= 0)
                {
                    originalLine = includeOriginalLine;
                    adjustedColumn = column;
                }
                else
                {
                    (originalLine, adjustedColumn) = MapPositionToOriginal(line, column);
                }

                if (!index.TryGetValue(name, out var locations))
                {
                    locations = new List<SymbolLocation>();
                    index[name] = locations;
                }

                locations.Add(new SymbolLocation
                {
                    Line = originalLine,
                    Column = adjustedColumn,
                    Length = name.Length,
                    Source = source,
                    SourceFile = sourceFile,
                    IsAssignment = false
                });
            }

            return index;
        }

        /// <summary>
        /// Builds a function index mapping each user-defined function name to all its occurrences
        /// (definition and call sites), with positions mapped back to original source lines.
        /// </summary>
        private static Dictionary<string, List<SymbolLocation>> BuildFunctionIndex(
            TokenizerResult tokenizerResult,
            Dictionary<string, FunctionInfo> userDefinedFunctions,
            Dictionary<int, int> stage3ToStage2Map,
            Dictionary<int, SourceInfo> includeMap,
            Dictionary<int, int> stage2ToStage1Map,
            Dictionary<int, int> stage1ToOriginalMap,
            Dictionary<int, List<LineContinuationSegment>> lineContinuationSegments)
        {
            var index = new Dictionary<string, List<SymbolLocation>>(StringComparer.OrdinalIgnoreCase);

            if (userDefinedFunctions.Count == 0)
                return index;

            // Map a stage3 position to original line + adjusted column, using continuation segments
            (int OriginalLine, int AdjustedColumn) MapPositionToOriginal(int stage3Line, int column)
            {
                var stage2Line = stage3ToStage2Map.TryGetValue(stage3Line, out var s2) ? s2 : stage3Line;
                var stage1Line = stage2ToStage1Map.TryGetValue(stage2Line, out var s1) ? s1 : stage2Line;
                return MapColumnThroughContinuation(stage1Line, column, lineContinuationSegments, stage1ToOriginalMap);
            }

            (string Source, string SourceFile, int OriginalLine) GetSourceInfo(int stage3Line)
            {
                if (includeMap.TryGetValue(stage3Line, out var info))
                    return (info.Source, info.SourceFile, info.OriginalLine);
                return ("local", null, -1);
            }

            // Track definition positions so we can mark them as IsAssignment=true
            var definitionPositions = new HashSet<(int Line, int Column)>();
            foreach (var funcDef in tokenizerResult.FunctionDefinitions)
            {
                if (userDefinedFunctions.ContainsKey(funcDef.Name))
                {
                    definitionPositions.Add((funcDef.LineNumber, 0));
                }
            }

            // Scan all Function tokens, only index user-defined functions
            foreach (var token in tokenizerResult.Tokens)
            {
                if (token.Type != TokenType.Function)
                    continue;

                if (!userDefinedFunctions.ContainsKey(token.Text))
                    continue;

                var (source, sourceFile, includeOriginalLine) = GetSourceInfo(token.Line);
                int originalLine, adjustedColumn;
                if (source == "include" && includeOriginalLine >= 0)
                {
                    originalLine = includeOriginalLine;
                    adjustedColumn = token.Column;
                }
                else
                {
                    (originalLine, adjustedColumn) = MapPositionToOriginal(token.Line, token.Column);
                }
                var isDefinition = definitionPositions.Contains((token.Line, token.Column));

                if (!index.TryGetValue(token.Text, out var locations))
                {
                    locations = new List<SymbolLocation>();
                    index[token.Text] = locations;
                }

                locations.Add(new SymbolLocation
                {
                    Line = originalLine,
                    Column = adjustedColumn,
                    Length = token.Length,
                    Source = source,
                    SourceFile = sourceFile,
                    IsAssignment = isDefinition
                });
            }

            return index;
        }

        /// <summary>
        /// Builds a macro index mapping each macro name to all its occurrences (definitions and call sites).
        /// Definitions come from Stage 2 macro collection; call sites from macro expansion tracking.
        /// </summary>
        private static Dictionary<string, List<SymbolLocation>> BuildMacroIndex(
            List<MacroDefinition> macroDefinitions,
            Dictionary<int, MacroExpansionInfo> macroExpansions,
            List<string> stage2Lines,
            Dictionary<int, int> stage3ToStage2Map,
            Dictionary<int, SourceInfo> stage3IncludeMap,
            Dictionary<int, int> stage2ToStage1Map,
            Dictionary<int, int> stage1ToOriginalMap,
            Dictionary<int, SourceInfo> stage2IncludeMap,
            Dictionary<int, List<LineContinuationSegment>> lineContinuationSegments)
        {
            var index = new Dictionary<string, List<SymbolLocation>>(StringComparer.OrdinalIgnoreCase);

            if (macroDefinitions.Count == 0)
                return index;

            // Map a stage2 line + column to original line + adjusted column, using continuation segments
            (int OriginalLine, int AdjustedColumn) MapStage2PositionToOriginal(int stage2Line, int column)
            {
                var stage1Line = stage2ToStage1Map.TryGetValue(stage2Line, out var s1) ? s1 : stage2Line;
                return MapColumnThroughContinuation(stage1Line, column, lineContinuationSegments, stage1ToOriginalMap);
            }

            (string Source, string SourceFile, int OriginalLine) GetStage2SourceInfo(int stage2Line)
            {
                if (stage2IncludeMap.TryGetValue(stage2Line, out var info))
                    return (info.Source, info.SourceFile, info.OriginalLine);
                return ("local", null, -1);
            }

            // Add definitions
            foreach (var macroDef in macroDefinitions)
            {
                var (source, sourceFile, includeOriginalLine) = GetStage2SourceInfo(macroDef.LineNumber);

                // Find column of macro name in the merged stage2 line
                var mergedColumn = 0;
                if (macroDef.LineNumber >= 0 && macroDef.LineNumber < stage2Lines.Count)
                {
                    var defLine = stage2Lines[macroDef.LineNumber];
                    var nameIdx = defLine.AsSpan().IndexOf(macroDef.Name.AsSpan(), StringComparison.OrdinalIgnoreCase);
                    if (nameIdx >= 0) mergedColumn = nameIdx;
                }

                int originalLine, adjustedColumn;
                if (source == "include" && includeOriginalLine >= 0)
                {
                    originalLine = includeOriginalLine;
                    adjustedColumn = mergedColumn;
                }
                else
                {
                    (originalLine, adjustedColumn) = MapStage2PositionToOriginal(macroDef.LineNumber, mergedColumn);
                }

                if (!index.TryGetValue(macroDef.Name, out var locations))
                {
                    locations = new List<SymbolLocation>();
                    index[macroDef.Name] = locations;
                }

                locations.Add(new SymbolLocation
                {
                    Line = originalLine,
                    Column = adjustedColumn,
                    Length = macroDef.Name.Length,
                    Source = source ?? "local",
                    SourceFile = sourceFile,
                    IsAssignment = true
                });
            }

            // Add call sites from macro expansion tracking
            // Each unique (macroName, callSiteStage2Line) pair represents one call site
            var seenCallSites = new HashSet<(string Name, int Stage2Line)>(
                new CallSiteComparer());

            foreach (var kvp in macroExpansions)
            {
                var expansion = kvp.Value;
                var stage2Line = expansion.CallSiteStage2Line;

                foreach (var macroName in expansion.MacroNames)
                {
                    if (!seenCallSites.Add((macroName, stage2Line)))
                        continue; // Already tracked this call site

                    var (source, sourceFile, includeOriginalLine) = GetStage2SourceInfo(stage2Line);

                    // Find column of macro call in the merged Stage 2 line
                    var mergedColumn = 0;
                    if (stage2Line >= 0 && stage2Line < stage2Lines.Count)
                    {
                        var callLine = stage2Lines[stage2Line];
                        var nameIdx = callLine.AsSpan().IndexOf(macroName.AsSpan(), StringComparison.OrdinalIgnoreCase);
                        if (nameIdx >= 0) mergedColumn = nameIdx;
                    }

                    int originalLine, adjustedColumn;
                    if (source == "include" && includeOriginalLine >= 0)
                    {
                        originalLine = includeOriginalLine;
                        adjustedColumn = mergedColumn;
                    }
                    else
                    {
                        (originalLine, adjustedColumn) = MapStage2PositionToOriginal(stage2Line, mergedColumn);
                    }

                    if (!index.TryGetValue(macroName, out var locations))
                    {
                        locations = new List<SymbolLocation>();
                        index[macroName] = locations;
                    }

                    locations.Add(new SymbolLocation
                    {
                        Line = originalLine,
                        Column = adjustedColumn,
                        Length = macroName.Length,
                        Source = source ?? "local",
                        SourceFile = sourceFile,
                        IsAssignment = false
                    });
                }
            }

            // Add macro calls found inside macro definition bodies.
            // Multiline macro bodies (between #def and #end def) are skipped in Stage 3,
            // so their macro references aren't captured by the expansion-based tracking above.
            // Inline macro content (after '=') is also scanned.
            var allMacroNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var md in macroDefinitions)
                allMacroNameSet.Add(md.Name);

            foreach (var macroDef in macroDefinitions)
            {
                if (macroDef.Content == null || macroDef.Content.Count == 0)
                    continue;

                // Parameter names should not be treated as macro calls
                var paramNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (macroDef.Params != null)
                    foreach (var p in macroDef.Params)
                        paramNames.Add(p);

                // Determine if inline macro (#def name$ = content) vs multiline (#def name$ ... #end def)
                bool isInlineMacro = false;
                int inlineContentStartCol = -1;
                if (macroDef.LineNumber >= 0 && macroDef.LineNumber < stage2Lines.Count)
                {
                    var defLine = stage2Lines[macroDef.LineNumber];
                    var trimmedDefSpan = defLine.AsSpan().TrimStart();
                    if (trimmedDefSpan.StartsWith("#def ", StringComparison.OrdinalIgnoreCase))
                        isInlineMacro = HasEqualsOutsideParens(trimmedDefSpan, 5);

                    if (isInlineMacro)
                    {
                        // Find '=' position in the actual (untrimmed) line to mark content start
                        int leadingSpaces = defLine.Length - trimmedDefSpan.Length;
                        int depth = 0;
                        for (int ci = leadingSpaces + 5; ci < defLine.Length; ci++)
                        {
                            if (defLine[ci] == '(') depth++;
                            else if (defLine[ci] == ')') depth--;
                            else if (defLine[ci] == '=' && depth == 0)
                            {
                                inlineContentStartCol = ci + 1;
                                break;
                            }
                        }
                    }
                }

                // Determine which stage2 lines to scan
                int bodyStartLine, bodyEndLine;
                if (isInlineMacro)
                {
                    bodyStartLine = macroDef.LineNumber;
                    bodyEndLine = macroDef.LineNumber;
                }
                else
                {
                    bodyStartLine = macroDef.LineNumber + 1;
                    bodyEndLine = macroDef.LineNumber + macroDef.Content.Count;
                }

                for (int s2Line = bodyStartLine; s2Line <= bodyEndLine && s2Line >= 0 && s2Line < stage2Lines.Count; s2Line++)
                {
                    var lineText = stage2Lines[s2Line];
                    if (string.IsNullOrEmpty(lineText) || !lineText.AsSpan().Contains('$'))
                        continue;

                    // For inline macros, only scan after the '=' to skip the definition name
                    int scanStart = (isInlineMacro && s2Line == macroDef.LineNumber && inlineContentStartCol >= 0)
                        ? inlineContentStartCol : 0;

                    for (int ci = scanStart; ci < lineText.Length; ci++)
                    {
                        if (lineText[ci] != '$') continue;

                        // Work backwards from $ to find the start of the macro name
                        int nameStart = ci;
                        while (nameStart > 0 && CalcpadCharacterHelpers.IsMacroLetter(lineText[nameStart - 1], ci - nameStart))
                            nameStart--;

                        if (nameStart >= ci) continue; // No name characters before $

                        var candidateName = lineText.Substring(nameStart, ci - nameStart + 1);

                        // Skip parameters and unknown names
                        if (paramNames.Contains(candidateName)) continue;
                        if (!allMacroNameSet.Contains(candidateName)) continue;

                        var (source, sourceFile, includeOriginalLine) = GetStage2SourceInfo(s2Line);
                        int originalLine, adjustedColumn;
                        if (source == "include" && includeOriginalLine >= 0)
                        {
                            originalLine = includeOriginalLine;
                            adjustedColumn = nameStart;
                        }
                        else
                        {
                            (originalLine, adjustedColumn) = MapStage2PositionToOriginal(s2Line, nameStart);
                        }

                        if (!index.TryGetValue(candidateName, out var locations))
                        {
                            locations = new List<SymbolLocation>();
                            index[candidateName] = locations;
                        }

                        locations.Add(new SymbolLocation
                        {
                            Line = originalLine,
                            Column = adjustedColumn,
                            Length = candidateName.Length,
                            Source = source ?? "local",
                            SourceFile = sourceFile,
                            IsAssignment = false
                        });
                    }
                }
            }

            return index;
        }

        /// <summary>
        /// Maps a column position in a merged stage1 line back to the correct original line and adjusted column,
        /// using LineContinuationSegments. If the stage1 line has no continuation segments, falls back to the
        /// simple stage1→original map with the raw column.
        /// </summary>
        private static (int OriginalLine, int AdjustedColumn) MapColumnThroughContinuation(
            int stage1Line, int column,
            Dictionary<int, List<LineContinuationSegment>> lineContinuationSegments,
            Dictionary<int, int> stage1ToOriginalMap)
        {
            if (lineContinuationSegments != null &&
                lineContinuationSegments.TryGetValue(stage1Line, out var segments))
            {
                foreach (var seg in segments)
                {
                    if (column >= seg.StartColumn && column < seg.StartColumn + seg.Length)
                        return (seg.OriginalLine, column - seg.StartColumn);
                }
                // Column past all segments — use last segment
                if (segments.Count > 0)
                {
                    var lastSeg = segments[segments.Count - 1];
                    if (column >= lastSeg.StartColumn)
                        return (lastSeg.OriginalLine, column - lastSeg.StartColumn);
                }
            }

            var originalLine = stage1ToOriginalMap.TryGetValue(stage1Line, out var orig) ? orig : stage1Line;
            return (originalLine, column);
        }

        /// <summary>Comparer for deduplicating macro call sites by name and stage2 line.</summary>
        private class CallSiteComparer : IEqualityComparer<(string Name, int Stage2Line)>
        {
            public bool Equals((string Name, int Stage2Line) x, (string Name, int Stage2Line) y)
                => string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) && x.Stage2Line == y.Stage2Line;

            public int GetHashCode((string Name, int Stage2Line) obj)
                => StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name) ^ obj.Stage2Line.GetHashCode();
        }

        /// <summary>
        /// Expands macros in a line, handling nested macro names by matching the longest name first.
        /// This matches Calcpad.Core's MacroParser behavior where longer macro names take precedence.
        /// For example, with macros "string$" and "ng$", "gstring$" should expand "string$", not "ng$".
        /// Optionally tracks which macros were expanded for source mapping.
        /// </summary>
        private string ExpandMacros(string line, Dictionary<string, (List<string> Params, List<string> Defaults, List<string> Content)> macros, List<string> expandedMacroNames = null)
        {
            if (macros.Count == 0 || !line.AsSpan().Contains('$'))
                return line;

            // Sort macros by name length descending so longer names are matched first
            // This ensures "string$" is matched before "ng$" in "gstring$"
            var sortedMacros = macros.OrderByDescending(m => m.Key.Length).ToList();

            var result = new System.Text.StringBuilder(line.Length * 2);
            var textBuffer = new System.Text.StringBuilder();
            int i = 0;

            while (i < line.Length)
            {
                var c = line[i];

                // When we hit a $, check if the accumulated text ends with a macro name
                if (c == '$')
                {
                    textBuffer.Append(c);

                    // Try to find the longest matching macro name that ends at this position
                    // Compare StringBuilder chars directly to avoid textBuffer.ToString() allocation
                    (string macroName, (List<string> Params, List<string> Defaults, List<string> Content) macro)? matchedMacro = null;
                    int macroStartInBuffer = -1;

                    foreach (var kvp in sortedMacros)
                    {
                        var name = kvp.Key;
                        if (textBuffer.Length >= name.Length &&
                            StringBuilderEndsWith(textBuffer, name))
                        {
                            matchedMacro = (name, kvp.Value);
                            macroStartInBuffer = textBuffer.Length - name.Length;
                            break; // First match is the longest due to sorted order
                        }
                    }

                    if (matchedMacro.HasValue)
                    {
                        var (macroName, macro) = matchedMacro.Value;

                        // Output text before the macro name
                        if (macroStartInBuffer > 0)
                        {
                            result.Append(textBuffer.ToString(0, macroStartInBuffer));
                        }
                        textBuffer.Clear();

                        // Check for arguments after the macro name
                        int pos = i + 1;

                        // Skip whitespace
                        while (pos < line.Length && char.IsWhiteSpace(line[pos]))
                            pos++;

                        List<string> argList;
                        int replacementEnd;

                        // Check for opening parenthesis (macro with arguments)
                        if (pos < line.Length && line[pos] == '(')
                        {
                            var argsStart = pos + 1;
                            var depth = 1;
                            pos++;
                            while (pos < line.Length && depth > 0)
                            {
                                if (line[pos] == '(') depth++;
                                else if (line[pos] == ')') depth--;
                                pos++;
                            }

                            if (depth == 0)
                            {
                                var argsEnd = pos - 1;
                                var argsStr = line.Substring(argsStart, argsEnd - argsStart);
                                // Use ParseMacroParameters for macro calls - only parentheses count for nesting
                                argList = ParameterParser.ParseMacroParameters(argsStr);
                                replacementEnd = pos;
                            }
                            else
                            {
                                // Unbalanced parens - output as-is and continue
                                result.Append(macroName);
                                i++;
                                continue;
                            }
                        }
                        else
                        {
                            // No parenthesis - macro with no arguments
                            argList = new List<string>();
                            replacementEnd = i + 1;
                        }

                        // Resolve keyword args and fill defaults
                        var resolvedArgs = ResolveMacroArgs(macro.Params, macro.Defaults, argList);
                        if (resolvedArgs != null)
                        {
                            expandedMacroNames?.Add(macroName);
                            var macroContent = JoinLines(macro.Content);

                            // Substitute parameters - sort by length descending to handle nested param names
                            if (macro.Params.Count > 0)
                            {
                                var sortedParams = macro.Params
                                    .Select((p, idx) => (Param: p, Arg: resolvedArgs[idx]))
                                    .OrderByDescending(x => x.Param.Length)
                                    .ToList();

                                foreach (var (param, arg) in sortedParams)
                                {
                                    macroContent = macroContent.Replace(param, arg);
                                }
                            }

                            // Recursively expand any macros in the result (nested expansions also tracked)
                            macroContent = ExpandMacros(macroContent, macros, expandedMacroNames);

                            result.Append(macroContent);
                            i = replacementEnd;
                        }
                        else
                        {
                            // Parameter count mismatch - preserve full call text so linter can report accurate diagnostics
                            result.Append(macroName);
                            result.Append(line, i + 1, replacementEnd - (i + 1));
                            i = replacementEnd;
                        }
                    }
                    else
                    {
                        // No macro matched, continue accumulating
                        i++;
                    }
                }
                else if (CalcpadCharacterHelpers.IsMacroLetter(c, textBuffer.Length))
                {
                    // Accumulate potential macro name characters
                    textBuffer.Append(c);
                    i++;
                }
                else
                {
                    // Non-macro character - flush buffer and output character
                    if (textBuffer.Length > 0)
                    {
                        result.Append(textBuffer);
                        textBuffer.Clear();
                    }
                    result.Append(c);
                    i++;
                }
            }

            // Flush any remaining buffer
            if (textBuffer.Length > 0)
            {
                result.Append(textBuffer);
            }

            return result.ToString();
        }

        /// <summary>
        /// Resolves positional and keyword arguments against macro parameters with optional defaults.
        /// Returns a resolved argument array parallel to params, or null if the call is invalid.
        /// </summary>
        private static List<string> ResolveMacroArgs(List<string> paramNames, List<string> defaults, List<string> argList)
        {
            if (paramNames == null || paramNames.Count == 0)
                return argList.Count == 0 ? new List<string>() : null;

            // Separate positional and keyword arguments
            var positional = new List<string>();
            var keywords = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var rawArg in argList)
            {
                var trimmed = rawArg.Trim();
                if (trimmed.Length == 0 && argList.Count == 1)
                    continue; // Empty parens: macro$()

                if (TryParseMacroKeywordArg(trimmed, paramNames, out var kwName, out var kwValue))
                    keywords[kwName] = kwValue;
                else
                    positional.Add(rawArg);
            }

            int totalProvided = positional.Count + keywords.Count;
            if (totalProvided > paramNames.Count)
                return null;

            // Compute required count
            int requiredCount = paramNames.Count;
            if (defaults != null)
            {
                requiredCount = 0;
                foreach (var d in defaults)
                    if (d == null) requiredCount++;
            }

            if (totalProvided < requiredCount)
                return null;

            // Build resolved args
            var resolved = new List<string>(paramNames.Count);
            int posIdx = 0;
            for (int j = 0; j < paramNames.Count; j++)
            {
                if (keywords.TryGetValue(paramNames[j], out var kwVal))
                    resolved.Add(kwVal);
                else if (posIdx < positional.Count)
                    resolved.Add(positional[posIdx++]);
                else if (defaults != null && j < defaults.Count && defaults[j] != null)
                    resolved.Add(defaults[j]);
                else
                    return null; // Required param not satisfied
            }
            return resolved;
        }

        /// <summary>
        /// Tries to parse a macro keyword argument in the form "name$=value".
        /// </summary>
        private static bool TryParseMacroKeywordArg(ReadOnlySpan<char> arg, List<string> paramNames, out string kwName, out string kwValue)
        {
            int idx = 0;
            while (idx < arg.Length && (char.IsLetterOrDigit(arg[idx]) || arg[idx] == '_')) idx++;
            if (idx > 0 && idx + 1 < arg.Length && arg[idx] == '$' && arg[idx + 1] == '=')
            {
                var name = arg[..(idx + 1)].ToString();
                if (paramNames.Contains(name))
                {
                    kwName = name;
                    kwValue = arg[(idx + 2)..].Trim().ToString();
                    return true;
                }
            }
            kwName = null;
            kwValue = null;
            return false;
        }

        /// <summary>
        /// Checks if a #def line has an '=' at parenthesis depth 0, indicating an inline macro definition.
        /// Scans from startPos (after "#def ") to avoid false positives from '=' inside parentheses.
        /// </summary>
        private static bool HasEqualsOutsideParens(ReadOnlySpan<char> line, int startPos)
        {
            int depth = 0;
            for (int i = startPos; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == '=' && depth == 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a StringBuilder ends with the given string (case-insensitive).
        /// Avoids textBuffer.ToString() allocation in the ExpandMacros hot loop.
        /// </summary>
        private static bool StringBuilderEndsWith(StringBuilder sb, string value)
        {
            var len = value.Length;
            if (sb.Length < len) return false;
            var offset = sb.Length - len;
            for (int i = 0; i < len; i++)
            {
                if (char.ToUpperInvariant(sb[offset + i]) != char.ToUpperInvariant(value[i]))
                    return false;
            }
            return true;
        }
    }
}
