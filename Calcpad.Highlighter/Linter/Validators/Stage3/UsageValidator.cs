using System;
using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    public class UsageValidator
    {
        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            ValidateCommandSyntax(stage3, result, tokenProvider);
            ValidateCommandVariables(stage3, result, tokenProvider);
            ValidateUndefinedIdentifiers(stage3, result, tokenProvider);
            ValidateUndefinedUnits(stage3, result, tokenProvider);
            ValidateFunctionCalls(stage3, result, tokenProvider);
            ValidateMacroCalls(stage3, result, tokenProvider);
            ValidateUnusedDefinitions(stage3, result, tokenProvider);
        }

        /// <summary>
        /// Validates command syntax, specifically the @ separator pattern in commands like:
        /// $Repeat{expression @ variable = start : end}
        /// Checks that:
        /// 1. There's exactly one variable (LocalVariable or Variable token) between @ and =
        /// 2. No numbers or problematic tokens appear between @ and the variable
        /// Example valid: $Sum{k*2 @ k = a : b}
        /// Example invalid: $Repeat{i*j @ 90 i = 1 : 10} (has "90" between @ and variable)
        /// Special case: $Map allows multiple counters with & separator
        /// </summary>
        private void ValidateCommandSyntax(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var trimmed = line.Trim();

                if (LineParser.IsDirectiveLine(trimmed))
                    continue;

                // Check if line contains a command with @ separator
                var atIndex = line.IndexOf('@');
                if (atIndex < 0)
                    continue;

                // Check if this is within a command block and determine which command
                var hasCommand = false;
                var commandName = string.Empty;
                foreach (var cmd in CalcpadBuiltIns.CommandsExcludingCommandBlocks)
                {
                    var cmdIndex = line.IndexOf(cmd + "{", StringComparison.OrdinalIgnoreCase);
                    if (cmdIndex >= 0 && cmdIndex < atIndex)
                    {
                        hasCommand = true;
                        commandName = cmd;
                        break;
                    }
                }

                if (!hasCommand)
                    continue;

                // Special case: $Plot allows | and & operators before @
                // Skip validation for $Plot commands as they have complex expression syntax
                if (commandName.Equals("$Plot", StringComparison.OrdinalIgnoreCase))
                {
                    var beforeAt = line.Substring(0, atIndex);
                    // Check if the expression contains | (parametric) or & (multiple functions)
                    if (beforeAt.Contains('|') || beforeAt.Contains('&'))
                    {
                        // These are valid $Plot syntaxes, skip validation
                        continue;
                    }
                }

                // Extract the part after @ (e.g., "x = 0 : 5}" or "90 i=1:10")
                var afterAt = line.Substring(atIndex + 1);

                // Find the = sign (not ==, <=, >=, !=)
                var equalsIndex = -1;
                for (int j = 0; j < afterAt.Length; j++)
                {
                    if (afterAt[j] == '=')
                    {
                        // Check it's not ==, <=, >=, or !=
                        var isComparison = (j > 0 && (afterAt[j - 1] == '<' || afterAt[j - 1] == '>' || afterAt[j - 1] == '!' || afterAt[j - 1] == '=')) ||
                                         (j + 1 < afterAt.Length && afterAt[j + 1] == '=');
                        if (!isComparison)
                        {
                            equalsIndex = j;
                            break;
                        }
                    }
                }

                if (equalsIndex < 0)
                {
                    // No = sign found after @
                    result.AddError(i, atIndex, atIndex + 1, "CPD-3410",
                        "Invalid command syntax: expected 'variable = start : end' after '@'");
                    continue;
                }

                // Get tokens for this line to check what's between @ and =
                var tokens = tokenProvider.GetTokensForLine(i);
                var variableTokens = new List<Token>();
                var numberTokens = new List<Token>();

                // Find all tokens between @ and =
                var atAbsolutePos = atIndex;
                var equalsAbsolutePos = atIndex + 1 + equalsIndex;

                foreach (var token in tokens)
                {
                    // Check if token is between @ and =
                    if (token.Column > atAbsolutePos && token.Column < equalsAbsolutePos)
                    {
                        if (token.Type == TokenType.LocalVariable || token.Type == TokenType.Variable)
                        {
                            variableTokens.Add(token);
                        }
                        else if (token.Type == TokenType.Const)
                        {
                            // Found a number token - this is problematic
                            numberTokens.Add(token);
                        }
                        // Ignore whitespace, operators, and other tokens
                    }
                }

                // Validation: should have exactly 1 variable token between @ and =
                if (variableTokens.Count == 0)
                {
                    result.AddError(i, atAbsolutePos + 1, equalsAbsolutePos, "CPD-3410",
                        "Invalid command syntax: expected variable name after '@'");
                }
                else if (variableTokens.Count > 1)
                {
                    result.AddError(i, atAbsolutePos + 1, equalsAbsolutePos, "CPD-3410",
                        "Invalid command syntax: expected single variable name after '@'");
                }
                else if (numberTokens.Count > 0)
                {
                    // There are number tokens before the variable (like "@ 90 i")
                    var firstNumber = numberTokens[0];
                    result.AddError(i, firstNumber.Column, firstNumber.Column + firstNumber.Length, "CPD-3410",
                        "Invalid command syntax: unexpected number '" + firstNumber.Text + "' between '@' and variable name");
                }
            }
        }

        /// <summary>
        /// Validates that variables used in command expressions match the declared loop variable.
        /// Example valid: $Root{f(x) @ x = 0 : 5} - expression uses 'x', loop variable is 'x'
        /// Example invalid: $Root{f(y) @ x = 0 : 5} - expression uses 'y', but loop variable is 'x'
        /// </summary>
        private void ValidateCommandVariables(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var trimmed = line.Trim();

                if (LineParser.IsDirectiveLine(trimmed))
                    continue;

                // Check if line contains a command with @ separator
                var atIndex = line.IndexOf('@');
                if (atIndex < 0)
                    continue;

                // Check if this is within a command block
                var hasCommand = false;
                var commandName = string.Empty;
                var cmdStartIndex = -1;
                foreach (var cmd in CalcpadBuiltIns.CommandsExcludingCommandBlocks)
                {
                    var cmdIndex = line.IndexOf(cmd + "{", StringComparison.OrdinalIgnoreCase);
                    if (cmdIndex >= 0 && cmdIndex < atIndex)
                    {
                        hasCommand = true;
                        commandName = cmd;
                        cmdStartIndex = cmdIndex + cmd.Length + 1; // Position after "{"
                        break;
                    }
                }

                if (!hasCommand)
                    continue;

                // Skip $Plot and $Map commands - they have special syntax
                if (commandName.Equals("$Plot", StringComparison.OrdinalIgnoreCase) ||
                    commandName.Equals("$Map", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Extract the declared loop variable (after @, before =)
                var afterAt = line.Substring(atIndex + 1);
                var equalsIndex = afterAt.IndexOf('=');
                if (equalsIndex < 0)
                    continue; // Already handled by ValidateCommandSyntax

                var declaredVarSection = afterAt.Substring(0, equalsIndex).Trim();

                // Get tokens to find the declared variable
                var tokens = tokenProvider.GetTokensForLine(i);
                var atAbsolutePos = atIndex;
                var equalsAbsolutePos = atIndex + 1 + equalsIndex;

                string declaredVar = null;
                foreach (var token in tokens)
                {
                    if (token.Column > atAbsolutePos && token.Column < equalsAbsolutePos &&
                        (token.Type == TokenType.LocalVariable || token.Type == TokenType.Variable))
                    {
                        declaredVar = token.Text;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(declaredVar))
                    continue; // No declared variable found

                // Now check variables used in the expression (before @)
                var usedVariables = new HashSet<string>(StringComparer.Ordinal);
                foreach (var token in tokens)
                {
                    // Only look at tokens in the expression (between { and @)
                    if (token.Column >= cmdStartIndex && token.Column < atAbsolutePos &&
                        (token.Type == TokenType.Variable || token.Type == TokenType.LocalVariable))
                    {
                        usedVariables.Add(token.Text);
                    }
                }

                // Check if the declared variable is used in the expression.
                // The loop variable defined after @ (e.g., $Repeat{expr @ i = 1 : n})
                // may appear directly or as an element access suffix (e.g., v1_ind.i)
                // when the tokenizer doesn't split because v1_ind isn't known as a vector.
                var usesLoopVar = usedVariables.Contains(declaredVar);
                if (!usesLoopVar)
                {
                    var dotSuffix = "." + declaredVar;
                    foreach (var uv in usedVariables)
                    {
                        if (uv.EndsWith(dotSuffix, StringComparison.Ordinal))
                        {
                            usesLoopVar = true;
                            break;
                        }
                    }
                }

                if (usedVariables.Count > 0 && !usesLoopVar)
                {
                    // Expression uses variables but not the declared one
                    var usedVarsList = string.Join(", ", usedVariables.Select(v => "'" + v + "'"));
                    result.AddError(i, cmdStartIndex, atAbsolutePos, "CPD-3412",
                        "Expression uses " + usedVarsList + " but loop variable is '" + declaredVar + "'");
                }
            }
        }

        private void ValidateUndefinedIdentifiers(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var trimmed = line.Trim();

                if (LineParser.IsDirectiveLine(trimmed))
                    continue;

                // Skip undefined variable checks for function definitions with command blocks
                // These have their own local scope where any variable can be assigned and used
                // Errors will be caught when the function is actually called
                if (IsCommandBlockFunctionDefinition(trimmed))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                // Check if this line is a function definition and extract its parameters
                var functionParams = ParsingHelpers.GetFunctionParamsFromLine(line);

                // Check for command-scope variables (e.g., x in $Root{f(x) @ x = 0 : 5})
                var commandScopeVars = GetCommandScopeVariables(line);

                foreach (var token in tokens)
                {
                    // Skip LocalVariable tokens - these are function params, loop vars, command scope vars
                    // The tokenizer has already identified them as locally scoped
                    if (token.Type == TokenType.LocalVariable)
                        continue;

                    // Skip FilePath tokens - these are file paths in data exchange keywords
                    if (token.Type == TokenType.FilePath)
                        continue;

                    // Only check Variable, StringVariable, and StringTable tokens (Function tokens are checked in ValidateFunctionCalls)
                    if (token.Type != TokenType.Variable && token.Type != TokenType.StringVariable && token.Type != TokenType.StringTable)
                        continue;

                    var identifier = token.Text;

                    // Handle element access syntax: v.(index) - the identifier ends with .
                    var isElementAccess = identifier.EndsWith(".");
                    var baseName = isElementAccess ? identifier.Substring(0, identifier.Length - 1) : identifier;

                    // Check if it's on the left side of an assignment (definition, not usage)
                    if (IsBeingDefined(line, token.Column, baseName))
                        continue;

                    // Check if it's a function parameter on this line (fallback for edge cases)
                    if (functionParams.Contains(baseName))
                        continue;

                    // Check if it's a command-scope variable (fallback for edge cases)
                    if (commandScopeVars.Contains(baseName))
                        continue;

                    // Check if defined
                    var isDefined = stage3.DefinedVariables.Contains(baseName) ||
                                   stage3.DefinedFunctions.ContainsKey(baseName) ||
                                   CalcpadBuiltIns.Functions.Contains(baseName) ||
                                   CalcpadBuiltIns.CommonConstants.Contains(baseName) ||
                                   CalcpadBuiltIns.Units.Contains(baseName) ||
                                   stage3.CustomUnits.Contains(baseName);

                    if (!isDefined)
                    {
                        var dotIdx = baseName.IndexOf('.');
                        if (dotIdx > 0)
                        {
                            var beforeDot = baseName.Substring(0, dotIdx);

                            // Check if this looks like param.x where param is a function parameter.
                            // Function params don't support .i syntax - must use .(i).
                            if (functionParams.Contains(beforeDot) || commandScopeVars.Contains(beforeDot))
                            {
                                var afterDot = baseName.Substring(dotIdx + 1);
                                result.AddError(i, token.Column, token.Column + token.Length, "CPD-3301",
                                    "'" + baseName + "'. Function parameters require .()" +
                                    " syntax for element access: " + beforeDot + ".(" + afterDot + ")");
                                continue;
                            }

                            // If the base variable before the dot is defined and its type
                            // supports element access (Unknown, Vector, Matrix, Various),
                            // suppress the false positive. The tokenizer didn't split the dot
                            // because it didn't know the variable was a vector/matrix during
                            // tokenization, but element access is still plausible.
                            if (stage3.DefinedVariables.Contains(beforeDot) && stage3.TypeTracker != null)
                            {
                                var varInfo = stage3.TypeTracker.GetVariableInfo(beforeDot);
                                if (varInfo == null || varInfo.SupportsElementAccess)
                                    continue;
                            }
                        }
                        // Line continuation mapping is handled automatically by LinterResult
                        result.AddError(i, token.Column, token.Column + token.Length, "CPD-3301",
                            "'" + baseName + "'");
                        continue;
                    }

                    // If using element access, validate that the variable supports it
                    if (isElementAccess && stage3.TypeTracker != null)
                    {
                        var varInfo = stage3.TypeTracker.GetVariableInfo(baseName);
                        if (varInfo != null && !varInfo.SupportsElementAccess)
                        {
                            result.AddWarning(i, token.Column, token.Column + token.Length, "CPD-3306",
                                "'" + baseName + "' is a " + varInfo.Type.ToString().ToLower() + ", element access .() is only valid for vectors and matrices");
                        }
                    }
                }
            }
        }

        private void ValidateUndefinedUnits(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var trimmed = line.Trim();

                if (LineParser.IsDirectiveLine(trimmed))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                foreach (var token in tokens)
                {
                    // Only check Units tokens
                    if (token.Type != TokenType.Units)
                        continue;

                    var unitName = token.Text;

                    // Check if it's a valid built-in unit or custom unit
                    var isValidUnit = CalcpadBuiltIns.Units.Contains(unitName) ||
                                     stage3.CustomUnits.Contains(unitName);

                    if (!isValidUnit)
                    {
                        result.AddError(i, token.Column, token.Column + token.Length, "CPD-3310",
                            "'" + unitName + "'");
                    }
                }
            }
        }

        /// <summary>
        /// Extracts command-scope variables from numerical method commands.
        /// For example: "$Root{f(x) @ x = 0 : 5}" returns {"x"}
        /// Also handles: "$Sum{k^2 @ k = 1 : 5}", "$Plot{sin(x) @ x = 0 : 2*π}"
        /// Also handles command block local variables ($Inline, $Block, $While).
        /// </summary>
        private static HashSet<string> GetCommandScopeVariables(string line)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);

            // Check for command blocks - variables assigned inside are local to the block
            foreach (var cmdBlock in CalcpadBuiltIns.CommandBlocks)
            {
                var cmdIndex = line.IndexOf(cmdBlock + "{", StringComparison.OrdinalIgnoreCase);
                if (cmdIndex >= 0)
                {
                    var braceStart = line.IndexOf('{', cmdIndex);
                    if (braceStart >= 0)
                    {
                        // Extract content inside the braces
                        var blockContent = ParsingHelpers.ExtractBlockContent(line, braceStart);

                        // Find all variable assignments inside the block
                        // Assignments are separated by ; and pattern is: varName = ...
                        ExtractBlockLocalVariables(blockContent, result);
                    }
                    break;
                }
            }

            // Look for @ symbol which indicates command variable definition
            var atIndex = line.IndexOf('@');
            if (atIndex < 0)
                return result;

            // Extract the part after @ (e.g., "x = 0 : 5}")
            var afterAt = line.Substring(atIndex + 1);

            // Find the variable name before the = sign
            // Pattern: @ variable = start : end
            var equalsIndex = afterAt.IndexOf('=');
            if (equalsIndex < 0)
                return result;

            var varPart = afterAt.Substring(0, equalsIndex).Trim();

            // The variable name should be a valid identifier
            if (!string.IsNullOrEmpty(varPart) && CalcpadCharacterHelpers.IsIdentifierStartCharWithUnderscore(varPart[0]))
            {
                // Extract just the identifier (stop at non-identifier chars)
                var varName = CalcpadCharacterHelpers.ExtractIdentifier(varPart);
                if (!string.IsNullOrEmpty(varName))
                {
                    result.Add(varName);
                }
            }

            return result;
        }


        /// <summary>
        /// Extracts variable names that are assigned inside a block.
        /// Block statements are separated by ; and assignments use = (not ==).
        /// </summary>
        private static void ExtractBlockLocalVariables(string blockContent, HashSet<string> result)
        {
            // Split by semicolons (statement separator in $Inline blocks)
            var statements = blockContent.Split(';');

            foreach (var stmt in statements)
            {
                var trimmed = stmt.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                // Look for assignment pattern: identifier = value (not ==)
                var equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex <= 0)
                    continue;

                // Make sure it's not == (comparison)
                if (equalsIndex + 1 < trimmed.Length && trimmed[equalsIndex + 1] == '=')
                    continue;

                // Make sure there's no operator before = (like +=, -=, etc.)
                if (equalsIndex > 0)
                {
                    var charBefore = trimmed[equalsIndex - 1];
                    if (charBefore == '+' || charBefore == '-' || charBefore == '*' ||
                        charBefore == '/' || charBefore == '!' || charBefore == '<' ||
                        charBefore == '>' || charBefore == '≠' || charBefore == '≤' ||
                        charBefore == '≥')
                        continue;
                }

                var leftSide = trimmed.Substring(0, equalsIndex).Trim();

                // Handle element access like helperV.i = val (extract helperV)
                var dotIndex = leftSide.IndexOf('.');
                if (dotIndex > 0)
                {
                    leftSide = leftSide.Substring(0, dotIndex).Trim();
                }

                // Extract the identifier
                if (!string.IsNullOrEmpty(leftSide) && CalcpadCharacterHelpers.IsIdentifierStartCharWithUnderscore(leftSide[0]))
                {
                    var varName = CalcpadCharacterHelpers.ExtractIdentifier(leftSide);
                    if (!string.IsNullOrEmpty(varName))
                    {
                        result.Add(varName);
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a line is a function definition that uses $Inline, $Block, or $While command blocks.
        /// These command blocks have their own local scope, so we skip undefined variable checks.
        /// Line continuations are already processed by Stage 1 before the linter runs.
        /// </summary>
        private static bool IsCommandBlockFunctionDefinition(string trimmedLine)
        {
            return CalcpadPatterns.FunctionWithCommandBlock.IsMatch(trimmedLine);
        }

        private void ValidateFunctionCalls(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var trimmed = line.Trim();

                if (LineParser.IsDirectiveLine(trimmed))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                foreach (var token in tokens)
                {
                    // Only check Function tokens
                    if (token.Type != TokenType.Function)
                        continue;

                    var funcName = token.Text;

                    // Skip built-in functions (they have variable param counts)
                    if (CalcpadBuiltIns.Functions.Contains(funcName))
                        continue;

                    // Check user-defined functions
                    if (stage3.DefinedFunctions.TryGetValue(funcName, out FunctionInfo funcInfo))
                    {
                        var (found, paramsStr) = ParsingHelpers.ExtractParamsString(line, token.Column + token.Length);
                        if (found)
                        {
                            var argList = ParameterParser.ParseParameters(paramsStr);
                            // Filter out empty-only result (zero args case)
                            if (argList.Count == 1 && argList[0].Length == 0)
                                argList.Clear();

                            var keywordArgNames = new List<string>();
                            foreach (var rawArg in argList)
                            {
                                if (IsFunctionKeywordArg(rawArg, funcInfo, out var kwName))
                                    keywordArgNames.Add(kwName);
                            }

                            int totalActual = argList.Count;
                            if (totalActual < funcInfo.RequiredParamCount || totalActual > funcInfo.ParamCount)
                            {
                                var endCol = ParsingHelpers.FindClosingParen(line, token.Column + token.Length);
                                if (endCol <= token.Column + token.Length) endCol = token.Column + token.Length;
                                var expectRange = funcInfo.RequiredParamCount == funcInfo.ParamCount
                                    ? funcInfo.ParamCount.ToString()
                                    : funcInfo.RequiredParamCount + "-" + funcInfo.ParamCount;
                                result.AddError(i, token.Column, endCol, "CPD-3302",
                                    "'" + funcName + "' expects " + expectRange + " parameter(s) but got " + totalActual);
                            }

                            foreach (var kwName in keywordArgNames)
                            {
                                if (!funcInfo.ParamNames.Contains(kwName, StringComparer.OrdinalIgnoreCase))
                                {
                                    var endCol = ParsingHelpers.FindClosingParen(line, token.Column + token.Length);
                                    if (endCol <= token.Column + token.Length) endCol = token.Column + token.Length;
                                    result.AddError(i, token.Column, endCol, "CPD-3315",
                                        "'" + funcName + "' has no parameter named '" + kwName + "'");
                                }
                            }
                        }
                    }
                    else
                    {
                        // Function not defined - the content resolver handles included files
                        result.AddError(i, token.Column, token.Column + token.Length, "CPD-3305",
                            "'" + funcName + "' is not defined");
                    }
                }
            }
        }

        private void ValidateMacroCalls(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var trimmed = line.Trim();

                // Skip #def lines (macro definitions)
                if (LineParser.IsDefStatement(trimmed))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                foreach (var token in tokens)
                {
                    // Only check Macro tokens
                    if (token.Type != TokenType.Macro)
                        continue;

                    var macroName = token.Text;

                    // Check if macro is defined
                    if (!stage3.DefinedMacros.ContainsKey(macroName))
                    {
                        // Pass Stage3 line index - diagnostic extensions handle mapping
                        result.AddError(i, token.Column, token.Column + token.Length, "CPD-3303",
                            "'" + macroName + "'");
                        continue;
                    }

                    // Check parameter count and keyword arguments
                    if (stage3.DefinedMacros.TryGetValue(macroName, out MacroInfo macroInfo))
                    {
                        var rawArgs = ParseMacroCallArgStrings(line, token.Column + token.Length);
                        if (rawArgs == null) rawArgs = new List<string>();

                        int positionalCount = 0;
                        var keywordArgNames = new List<string>();

                        foreach (var rawArg in rawArgs)
                        {
                            if (IsKeywordArg(rawArg.Trim(), out var kwName))
                                keywordArgNames.Add(kwName);
                            else
                                positionalCount++;
                        }

                        int totalActual = rawArgs.Count;
                        var endCol = ParsingHelpers.FindClosingParen(line, token.Column + token.Length);
                        if (endCol <= token.Column + token.Length)
                            endCol = token.Column + token.Length;

                        if (totalActual < macroInfo.RequiredParamCount || totalActual > macroInfo.ParamCount)
                        {
                            var expectedStr = macroInfo.RequiredParamCount == macroInfo.ParamCount
                                ? macroInfo.ParamCount.ToString()
                                : macroInfo.RequiredParamCount + "-" + macroInfo.ParamCount;
                            result.AddError(i, token.Column, endCol, "CPD-3304",
                                "'" + macroName + "' expects " + expectedStr + " parameter(s) but got " + totalActual);
                        }

                        // Validate keyword argument names
                        foreach (var kwName in keywordArgNames)
                        {
                            if (!macroInfo.ParamNames.Contains(kwName, StringComparer.Ordinal))
                            {
                                result.AddError(i, token.Column, endCol, "CPD-3314",
                                    "'" + macroName + "' has no parameter named '" + kwName + "'");
                            }
                        }
                    }
                }
            }
        }

        private static bool IsBeingDefined(string line, int identifierStart, string identifier)
        {
            // Check if identifier is on the left side of = (being assigned/defined)
            var beforeIdentifier = line.Substring(0, identifierStart).TrimEnd();

            // If there's nothing before, check if = comes after the identifier
            if (string.IsNullOrEmpty(beforeIdentifier))
            {
                var afterPos = identifierStart + identifier.Length;
                if (afterPos < line.Length)
                {
                    var afterIdentifier = line.Substring(afterPos).TrimStart();
                    if (afterIdentifier.StartsWith("=") && !afterIdentifier.StartsWith("=="))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Count the number of parameters in a function call.
        /// Returns -1 if no opening parenthesis is found.
        /// </summary>
        private static int CountFunctionParams(string line, int afterFuncName)
        {
            var (found, paramsStr) = ParsingHelpers.ExtractParamsString(line, afterFuncName);
            if (!found)
                return -1;

            return ParameterParser.CountParameters(paramsStr);
        }

        private static bool IsFunctionKeywordArg(string arg, FunctionInfo funcInfo, out string kwName)
        {
            // Detect: "identifier = expr" where identifier is a parameter name
            // Handles optional whitespace: "x=5", "x = 5"
            var trimmed = arg.AsSpan().Trim();
            int i = 0;
            while (i < trimmed.Length && (char.IsLetterOrDigit(trimmed[i]) || trimmed[i] == '_')) i++;
            if (i > 0)
            {
                var potentialName = trimmed[..i];
                var rest = trimmed[i..].TrimStart();
                if (!rest.IsEmpty && rest[0] == '=')
                {
                    var nameStr = potentialName.ToString();
                    if (funcInfo.ParamNames.Contains(nameStr, StringComparer.OrdinalIgnoreCase))
                    {
                        kwName = nameStr;
                        return true;
                    }
                }
            }
            kwName = null;
            return false;
        }



        /// <summary>
        /// Returns the raw argument strings for a macro call, or null if no opening paren found.
        /// </summary>
        private static List<string> ParseMacroCallArgStrings(string line, int afterMacroName)
        {
            var (found, paramsStr) = ParsingHelpers.ExtractParamsString(line, afterMacroName);
            if (!found) return null;
            return ParameterParser.ParseMacroParameters(paramsStr);
        }

        /// <summary>
        /// Returns true if arg is a keyword argument (name$=value pattern).
        /// Sets kwName to the parameter name including the $ suffix.
        /// </summary>
        private static bool IsKeywordArg(string arg, out string kwName)
        {
            int idx = 0;
            while (idx < arg.Length && (char.IsLetterOrDigit(arg[idx]) || arg[idx] == '_')) idx++;
            if (idx > 0 && idx + 1 < arg.Length && arg[idx] == '$' && arg[idx + 1] == '=')
            {
                kwName = arg[..(idx + 1)]; // name including $
                return true;
            }
            kwName = null;
            return false;
        }

        /// <summary>
        /// Validates that defined variables are actually used after their last assignment.
        /// Reports warnings for unused definitions to help identify dead code.
        ///
        /// Uses pre-collected variable assignment and usage data from the content resolver's
        /// Lint-mode tokenization, which correctly identifies Variable tokens even inside
        /// function call arguments and macro call arguments.
        ///
        /// Examples:
        /// - var = 1 / var / var = 2       -> warns on line 3 (var = 2 is never used after)
        /// - var = 1 / var / var = 2 / var -> no warning (var is used after last assignment)
        /// - var = 1 / var / var = 2 / 'text'var' -> no warning (var used in comment after last assignment)
        ///
        /// Note: Functions are not checked because they may be called from included files.
        /// </summary>
        private void ValidateUnusedDefinitions(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            // Build last assignment for each variable from pre-collected assignment data
            var lastAssignment = new Dictionary<string, (int Line, int Column, int Length)>(StringComparer.Ordinal);

            foreach (var (name, line, column, length) in stage3.VariableAssignments)
            {
                // Keep the LAST assignment (highest line number)
                if (!lastAssignment.TryGetValue(name, out var existing) || line > existing.Line)
                {
                    lastAssignment[name] = (line, column, length);
                }
            }

            // Check which variables have usage after their last assignment
            var usedAfterLastAssignment = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (name, line, column) in stage3.VariableUsages)
            {
                if (!lastAssignment.TryGetValue(name, out var assignInfo))
                    continue;

                // Usage after the last assignment line
                if (line > assignInfo.Line)
                {
                    usedAfterLastAssignment.Add(name);
                }
                // Usage on the same line as last assignment (right side of =, or in comment interpolation)
                else if (line == assignInfo.Line)
                {
                    usedAfterLastAssignment.Add(name);
                }
            }

            // Report unused variables (those with no usage after their last assignment)
            foreach (var kvp in lastAssignment)
            {
                var varName = kvp.Key;
                var (line, column, length) = kvp.Value;

                if (!usedAfterLastAssignment.Contains(varName))
                {
                    result.AddInformation(line, column, column + length, "CPD-3312",
                        "Variable '" + varName + "' is defined but never used");
                }
            }

            // Note: Unused function warnings (CPD-3313) are not reported
            // Functions may be called from included files
        }

    }
}
