using System;
using System.IO;
using System.Text;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Tokenizer;

namespace Calcpad.Highlighter.Tests
{
    public class LinterTestRunner
    {
        private static StringBuilder _log;

        public static void Main(string[] args)
        {
            // Find Samples folder relative to the assembly location
            var assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var samplesPath = Path.Combine(assemblyDir, "Samples");
            string singleFile = null;
            string folder = null;

            // Parse arguments:
            //   --file filename.cpd         Run a single file from Samples/
            //   --folder comprehensive       Run all .cpd files in a folder (relative to assembly dir)
            //   --folder comprehensive/errors  Run only error test files
            //   (no args)                   Run all .cpd files in Samples/
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--file" && i + 1 < args.Length)
                {
                    singleFile = args[i + 1];
                    // Strip "Samples/" prefix if present
                    if (singleFile.StartsWith("Samples" + Path.DirectorySeparatorChar) ||
                        singleFile.StartsWith("Samples/"))
                    {
                        singleFile = Path.GetFileName(singleFile);
                    }
                    i++;
                }
                else if (args[i] == "--folder" && i + 1 < args.Length)
                {
                    folder = args[i + 1];
                    i++;
                }
            }

            var resolver = new ContentResolver();
            var linter = new CalcpadLinter();

            _log = new StringBuilder();

            Console.WriteLine("=== Calcpad Linter Test Runner ===\n");
            Log("=== Calcpad Linter Test Runner ===");
            Log("Run at: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Log("");

            if (folder != null)
            {
                // Run all .cpd files in the specified folder
                var folderPath = Path.Combine(assemblyDir, folder);
                if (!Directory.Exists(folderPath))
                {
                    Console.WriteLine("ERROR: Folder not found: " + folderPath);
                    return;
                }

                var provider = new TestFileProvider(folderPath);
                var includeFiles = provider.GetIncludeFiles();

                Console.WriteLine("Running all .cpd files in: " + folder + "\n");
                Log("Mode: Folder - " + folder);
                Log("");

                var cpdFiles = Directory.GetFiles(folderPath, "*.cpd", SearchOption.TopDirectoryOnly);
                Array.Sort(cpdFiles);
                foreach (var filePath in cpdFiles)
                {
                    var filename = Path.GetFileName(filePath);
                    TestFile(filename, provider, includeFiles, resolver, linter);
                }

                var logPath = Path.Combine(folderPath, "test-output.log");
                File.WriteAllText(logPath, _log.ToString());
                Console.WriteLine("Log written to: " + Path.GetFullPath(logPath));
            }
            else if (singleFile != null)
            {
                var provider = new TestFileProvider(samplesPath);
                var includeFiles = provider.GetIncludeFiles();

                // Check if file is in comprehensive folder
                if (singleFile.StartsWith("comprehensive" + Path.DirectorySeparatorChar) ||
                    singleFile.StartsWith("comprehensive/"))
                {
                    var comprehensivePath = Path.Combine(assemblyDir, "comprehensive");
                    provider = new TestFileProvider(comprehensivePath);
                    includeFiles = provider.GetIncludeFiles();
                    singleFile = Path.GetFileName(singleFile);
                }

                // Run single file
                Console.WriteLine("Running single file: " + singleFile + "\n");
                Log("Mode: Single file - " + singleFile);
                Log("");
                TestFile(singleFile, provider, includeFiles, resolver, linter);

                var logPath = Path.Combine(samplesPath, "..", "test-output.log");
                File.WriteAllText(logPath, _log.ToString());
                Console.WriteLine("Log written to: " + Path.GetFullPath(logPath));
            }
            else
            {
                var provider = new TestFileProvider(samplesPath);
                var includeFiles = provider.GetIncludeFiles();

                // Run all .cpd files in the samples folder
                Console.WriteLine("Running all .cpd files in: " + samplesPath + "\n");
                Log("Mode: All files in " + samplesPath);
                Log("");

                var cpdFiles = Directory.GetFiles(samplesPath, "*.cpd");
                foreach (var filePath in cpdFiles)
                {
                    var filename = Path.GetFileName(filePath);
                    TestFile(filename, provider, includeFiles, resolver, linter);
                }

                var logPath = Path.Combine(samplesPath, "..", "test-output.log");
                File.WriteAllText(logPath, _log.ToString());
                Console.WriteLine("Log written to: " + Path.GetFullPath(logPath));
            }
        }

        private static void Log(string message)
        {
            _log.AppendLine(message);
        }

        private static void TestFile(
            string filename,
            TestFileProvider provider,
            System.Collections.Generic.Dictionary<string, string> includeFiles,
            ContentResolver resolver,
            CalcpadLinter linter)
        {
            Console.WriteLine("--- Testing: " + filename + " ---\n");
            Log("========================================");
            Log("Testing: " + filename);
            Log("========================================");

            try
            {
                var content = provider.GetFileContent(filename);

                Log("");
                Log("=== ORIGINAL CONTENT ===");
                LogNumberedLines(content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None));

                var resolved = resolver.GetStagedContent(content, includeFiles);

                Log("");
                Log("=== STAGE 1: Line Continuations ===");
                Log("Lines: " + resolved.Stage1.Lines.Count);
                Log("Source Map (Stage1 line -> Original line):");
                foreach (var kvp in resolved.Stage1.SourceMap)
                {
                    Log("  Stage1[" + kvp.Key + "] -> Original[" + kvp.Value + "]");
                }
                Log("");
                Log("Line Continuation Segments:");
                if (resolved.Stage1.LineContinuationSegments != null)
                {
                    foreach (var kvp in resolved.Stage1.LineContinuationSegments)
                    {
                        Log("  Stage1[" + kvp.Key + "] has " + kvp.Value.Count + " segments:");
                        foreach (var seg in kvp.Value)
                        {
                            Log("    - OriginalLine=" + seg.OriginalLine + ", StartCol=" + seg.StartColumn + ", Length=" + seg.Length);
                        }
                    }
                }
                else
                {
                    Log("  (none)");
                }
                Log("");
                Log("Content:");
                LogNumberedLines(resolved.Stage1.Lines);

                Log("");
                Log("=== STAGE 2: Includes Resolved + Macro Collection ===");
                Log("Lines: " + resolved.Stage2.Lines.Count);
                Log("Macros Found: " + resolved.Stage2.MacroDefinitions.Count);
                foreach (var macro in resolved.Stage2.MacroDefinitions)
                {
                    var contentPreview = macro.Content.Count > 0 ? macro.Content[0] : "";
                    if (contentPreview.Length > 40) contentPreview = contentPreview.Substring(0, 40) + "...";
                    Log("  - " + macro.Name + " (params: " + (macro.Params?.Count ?? 0) + ") at line " + macro.LineNumber + " [" + macro.Source + "] = " + contentPreview);
                }
                Log("Duplicate Macros: " + resolved.Stage2.DuplicateMacros.Count);
                foreach (var dup in resolved.Stage2.DuplicateMacros)
                {
                    Log("  - " + dup.Name + " duplicate at line " + dup.DuplicateLineNumber + " (original: " + dup.OriginalLineNumber + ")");
                }
                Log("Macro Comment Parameters (for tokenization):");
                foreach (var kvp in resolved.Stage2.MacroCommentParameters)
                {
                    if (kvp.Value.Count > 0)
                    {
                        Log("  - " + kvp.Key + ": " + string.Join(", ", kvp.Value));
                    }
                }
                Log("");
                Log("Source Map (Stage2 line -> Stage1 line):");
                foreach (var kvp in resolved.Stage2.SourceMap)
                {
                    var sourceInfo = resolved.Stage2.IncludeMap.ContainsKey(kvp.Key) ? resolved.Stage2.IncludeMap[kvp.Key] : null;
                    var source = sourceInfo != null ? "[" + sourceInfo.Source + (sourceInfo.SourceFile != null ? ":" + sourceInfo.SourceFile : "") + "]" : "[local]";
                    Log("  " + kvp.Key + " -> " + kvp.Value + " " + source);
                }
                Log("");
                Log("Content:");
                LogNumberedLines(resolved.Stage2.Lines);

                Log("");
                Log("=== STAGE 3: Macro Expansion + Definitions ===");
                Log("Lines: " + resolved.Stage3.Lines.Count);
                Log("Variables: " + resolved.Stage3.DefinedVariables.Count);
                foreach (var v in resolved.Stage3.DefinedVariables)
                {
                    Log("  - " + v);
                }
                Log("Variables with definitions:");
                foreach (var v in resolved.Stage3.VariablesWithDefinitions)
                {
                    Log("  - " + v.Name + " = " + v.Definition + " at Stage3 line " + v.LineNumber);
                }
                Log("Functions: " + resolved.Stage3.UserDefinedFunctions.Count);
                foreach (var f in resolved.Stage3.FunctionsWithParams)
                {
                    Log("  - " + f.Name + "(" + string.Join("; ", f.Params) + ") at line " + f.LineNumber);
                }
                Log("User Macros: " + resolved.Stage3.UserDefinedMacros.Count);
                foreach (var m in resolved.Stage3.UserDefinedMacros)
                {
                    Log("  - " + m.Key + " (params: " + m.Value.ParamCount + ") at line " + m.Value.LineNumber);
                }
                Log("Custom Units: " + resolved.Stage3.CustomUnits.Count);
                foreach (var u in resolved.Stage3.CustomUnits)
                {
                    Log("  - ." + u.Name + " = " + u.Definition + " at line " + u.LineNumber);
                }
                Log("");
                Log("Source Map (Stage3 line -> Stage2 line):");
                foreach (var kvp in resolved.Stage3.SourceMap)
                {
                    Log("  Stage3[" + kvp.Key + "] -> Stage2[" + kvp.Value + "]");
                }
                Log("");
                Log("Content:");
                LogNumberedLines(resolved.Stage3.Lines);

                Console.WriteLine("ContentResolver Results:");
                Console.WriteLine("  Stage1 Lines: " + resolved.Stage1.Lines.Count);
                Console.WriteLine("  Stage2 Lines: " + resolved.Stage2.Lines.Count);
                Console.WriteLine("  Stage2 Macros: " + resolved.Stage2.MacroDefinitions.Count);
                Console.WriteLine("  Stage3 Lines: " + resolved.Stage3.Lines.Count);
                Console.WriteLine("  Stage3 Variables: " + resolved.Stage3.DefinedVariables.Count);
                Console.WriteLine("  Stage3 Functions: " + resolved.Stage3.UserDefinedFunctions.Count);
                Console.WriteLine();

                Log("");
                Log("=== TOKENIZER RESULTS ===");
                var tokenizer = new CalcpadTokenizer();
                // Pass Stage2 macro comment parameters to tokenizer for correct argument tokenization
                tokenizer.SetMacroCommentParameters(
                    resolved.Stage2.MacroCommentParameters,
                    resolved.Stage2.MacroParameterOrder,
                    resolved.Stage2.MacroBodies);
                var tokenResult = tokenizer.Tokenize(content);
                Log("Total Tokens: " + tokenResult.Tokens.Count);
                Console.WriteLine("Tokenizer Results:");
                Console.WriteLine("  Total Tokens: " + tokenResult.Tokens.Count);

                // Count special token types
                int localVarCount = 0;
                int filePathCount = 0;
                int macroParamCount = 0;
                foreach (var tok in tokenResult.Tokens)
                {
                    if (tok.Type == Tokenizer.Models.TokenType.LocalVariable)
                        localVarCount++;
                    else if (tok.Type == Tokenizer.Models.TokenType.FilePath)
                        filePathCount++;
                    else if (tok.Type == Tokenizer.Models.TokenType.MacroParameter)
                        macroParamCount++;
                }
                Log("LocalVariable Tokens: " + localVarCount);
                Log("FilePath Tokens: " + filePathCount);
                Log("MacroParameter Tokens: " + macroParamCount);
                Console.WriteLine("  LocalVariable Tokens: " + localVarCount);
                Console.WriteLine("  FilePath Tokens: " + filePathCount);
                Console.WriteLine("  MacroParameter Tokens: " + macroParamCount);

                // Show all tokens for each line
                for (int lineNum = 0; lineNum < tokenResult.TokensByLine.Count; lineNum++)
                {
                    var lineTokens = tokenResult.GetTokensForLine(lineNum);
                    if (lineTokens.Count > 0)
                    {
                        Log("  Line " + (lineNum + 1) + " tokens:");
                        foreach (var tok in lineTokens)
                        {
                            Log("    [" + tok.Column + "-" + tok.EndColumn + "] " + tok.Type + ": \"" + tok.Text + "\"");
                        }
                    }
                }

                // Show lines with LocalVariable, FilePath, or MacroParameter tokens
                Log("");
                Log("=== LINES WITH SPECIAL TOKENS (LocalVariable, FilePath, MacroParameter) ===");
                for (int lineNum = 0; lineNum < tokenResult.TokensByLine.Count; lineNum++)
                {
                    var lineTokens = tokenResult.GetTokensForLine(lineNum);
                    bool hasSpecial = false;
                    foreach (var tok in lineTokens)
                    {
                        if (tok.Type == Tokenizer.Models.TokenType.LocalVariable ||
                            tok.Type == Tokenizer.Models.TokenType.FilePath ||
                            tok.Type == Tokenizer.Models.TokenType.MacroParameter)
                        {
                            hasSpecial = true;
                            break;
                        }
                    }
                    if (hasSpecial)
                    {
                        var originalLines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                        var lineContent = lineNum < originalLines.Length ? originalLines[lineNum] : "";
                        Log("  Line " + (lineNum + 1) + ": " + lineContent);
                        foreach (var tok in lineTokens)
                        {
                            if (tok.Type == Tokenizer.Models.TokenType.LocalVariable ||
                                tok.Type == Tokenizer.Models.TokenType.FilePath ||
                                tok.Type == Tokenizer.Models.TokenType.MacroParameter)
                            {
                                Log("    -> [" + tok.Column + "-" + tok.EndColumn + "] " + tok.Type + ": \"" + tok.Text + "\"");
                            }
                        }
                    }
                }

                Log("");
                Log("=== LINTER RESULTS ===");
                var lintResult = linter.Lint(resolved);
                Log("Errors: " + lintResult.ErrorCount);
                Log("Warnings: " + lintResult.WarningCount);

                Console.WriteLine("Linter Results:");
                Console.WriteLine("  Errors: " + lintResult.ErrorCount);
                Console.WriteLine("  Warnings: " + lintResult.WarningCount);

                foreach (var diag in lintResult.Diagnostics)
                {
                    var severity = diag.Severity == Linter.Models.LinterSeverity.Error ? "ERROR"
                        : diag.Severity == Linter.Models.LinterSeverity.Information ? "INFO" : "WARN";
                    var msg = "  [" + diag.Code + "] " + severity + " Line " + (diag.Line + 1) + ": " + diag.Message;
                    Console.WriteLine(msg);
                    Log(msg);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  Error: " + ex.Message);
                Log("ERROR: " + ex.Message);
                Log(ex.StackTrace);
            }

            Log("");
            Console.WriteLine();
        }

        private static void LogNumberedLines(System.Collections.Generic.IList<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                Log(string.Format("{0,4}: {1}", i + 1, lines[i]));
            }
        }

        private static void LogNumberedLines(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                Log(string.Format("{0,4}: {1}", i + 1, lines[i]));
            }
        }
    }
}
