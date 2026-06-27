# Deep Investigation: Calcpad.Highlighter

## Context

The user has refactored `Calcpad.Highlighter` many times and wants a clear-eyed view of the rot that has accumulated: where code is duplicated, where past refactors have left mismatched approaches side-by-side, and where the hot path leaks performance. The project tokenizes and lints Calcpad source on every keystroke in editor scenarios (VS Code extension, web editor), so allocations in the per-line / per-token path compound quickly.

This is an investigation report, not an implementation plan. Each finding cites exact file/line locations so it can be acted on independently. A prioritized roadmap is provided at the end.

Scope: `/home/isaiahm/repos/CalcpadCE/Calcpad.Highlighter/` — Tokenizer/, Linter/ (Helpers + Validators Stage1/2/3), ContentResolution/, Parsing/, Prettifier/, Snippets/. ~76 C# files.

---

## Already Completed

- **D1** — Whitespace-skip loops consolidated behind `ParsingHelpers.SkipWhitespace(ReadOnlySpan<char>, ref int)`. 7 sites routed through it.
- **D2** — Bracket-matching unified behind `ParsingHelpers.FindMatchingClose(ReadOnlySpan<char>, openPos, open, close)`. 6 depth-tracking loops collapsed. `HasEqualsOutsideParens` left alone — different semantics.
- **D3** — Keyword-args / parameter-defaults feature **removed entirely** (was experimental, not intended for this branch). Strips `IsFunctionKeywordArg`, `IsKeywordArg`, `TryParseMacroKeywordArg`, `ExtractFunctionParamDefaults`, `Defaults` props on `MacroDefinition`/`FunctionDefinition`, `RequiredParamCount` on `MacroInfo`/`FunctionInfo`, `ParameterDefaults` on `VariableInfo`, `defaults` params on 4 `TypeTracker.Register*` methods, error codes `CPD-3314`/`CPD-3315`, and the matching DTO properties in `CalcpadController.cs`. To be re-imported alongside the feature from the experimental branch.

---

## Findings — Code Duplication

### D4. Character-classification split between two utility classes
- [CalcpadCharacterHelpers.cs:135-141](Calcpad.Highlighter/Linter/Helpers/CalcpadCharacterHelpers.cs#L135-L141) defines `IsMacroLetter`
- [CalcpadTokenizer.Helpers.cs:13-28](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.Helpers.cs#L13-L28) re-declares local `IsDigit`, `IsMacroLetter`, `IsLatinLetter`

`CalcpadTokenizer.Helpers.cs` should delegate to `CalcpadCharacterHelpers` rather than carry its own.

### D5. `DiagnosticExtensions` exists but is half-used
[DiagnosticExtensions.cs:20-217](Calcpad.Highlighter/Linter/Helpers/DiagnosticExtensions.cs#L20-L217) provides 9 overloads (AddDiagnostic ×3, AddError ×3, AddWarning ×3, AddInformation ×3) but several validators still build `new LinterDiagnostic { ... }` literals directly, duplicating the column-mapping boilerplate. Audit the Stage3 validators and route them all through the extensions.

### D6. Stage3 validator loop boilerplate
Every Stage3 validator repeats:
```
for (int i = 0; i < stage3.Lines.Count; i++) {
    if (!tokenProvider.IsCpdMode(i)) continue;
    ...
}
```
in [BalanceValidator.cs:17-97](Calcpad.Highlighter/Linter/Validators/Stage3/BalanceValidator.cs), [UsageValidator.cs:279-398](Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs#L279-L398), [SemanticValidator.cs](Calcpad.Highlighter/Linter/Validators/Stage3/SemanticValidator.cs), and others. A `foreach (var (line, idx) in stage3.CpdLines())` enumerator would remove the boilerplate AND let multiple validators share a single tokenized-line pass (see P3 below).

### D7. Two overlapping line/quote state machines
- [LineParser.cs:32-151](Calcpad.Highlighter/Linter/Helpers/LineParser.cs#L32-L151) tracks `inSingleQuote`/`inDoubleQuote` to extract code vs string segments
- [CalcpadTokenizer.Comments.cs](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.Comments.cs) tracks comment/quote state in the tokenizer

The linter sometimes re-parses lines via `LineParser.ParseLine` after the tokenizer has already produced authoritative `Token` lists. The validators should consume `TokenizerResult.TokensByLine` rather than re-segmenting strings.

### D8. `ValidateCommandSyntax` and `ValidateCommandVariables` largely duplicated
[UsageValidator.cs:35-160](Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs#L35-L160) and [UsageValidator.cs:167-277](Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs#L167-L277) both scan for `@`, both filter to lines containing a known `Command{`, both find the trailing `=`. The first ~30 lines of each are near-identical. Merge into one pass that emits both diagnostic categories.

---

## Findings — Mismatched / Inconsistent Approaches

### M1. `string` vs `ReadOnlySpan<char>` inconsistency at API boundaries
- `LineParser` exposes both `IsDirectiveLine(string)` and `IsDirectiveLine(ReadOnlySpan<char>)`.
- [SemanticValidator.cs:161](Calcpad.Highlighter/Linter/Validators/Stage3/SemanticValidator.cs#L161) calls the `string` overload via `.Trim()` (allocating).
- [CommandBlockValidator.cs:72-76](Calcpad.Highlighter/Linter/Validators/Stage3/CommandBlockValidator.cs#L72-L76) calls the span overload via `.AsSpan().Trim()` (no alloc), but then immediately calls `.ToString()` anyway — defeating the savings.

There's no consistent rule about which to use. Pick one: span throughout, and only materialize strings when storing in result objects.

### M2. Line iteration: three different strategies inside the same class
Inside `ContentResolver`:
- Stage1/Stage2 use `LineEnumerator` (zero-alloc ref struct) at [ContentResolver.cs](Calcpad.Highlighter/ContentResolution/ContentResolver.cs)
- Stage3 uses `string.Split('\n')` at [ContentResolver.Stage3.cs:78](Calcpad.Highlighter/ContentResolution/ContentResolver.Stage3.cs#L78)
- Tests use a different `string.Split(new[] { "\r\n", "\r", "\n" }, …)` pattern

Same class, three approaches. Stage3 should adopt `LineEnumerator`.

### M3. Stage context injection inconsistency
- Stage1 validators receive `Stage1Context` directly.
- Stage2 validators receive `Stage2Context`.
- Stage3 validators receive `Stage3Context` **plus** a `TokenizedLineProvider` helper.

Stages 1 and 2 have no analogous helper-provider. Either all stages should get an injected provider for cross-cutting utilities (tokens, line classification, parsed segments) or none should. The current shape suggests Stage3 was retrofitted while 1/2 were left behind.

### M4. `LinterResult.HasErrors` / `ErrorCount` use LINQ over the diagnostics list
[LinterResult.cs:10-13](Calcpad.Highlighter/Linter/Models/LinterResult.cs#L10-L13):
```csharp
public bool HasErrors  => Diagnostics.Any(d => d.Severity == LinterSeverity.Error);
public int  ErrorCount => Diagnostics.Count(d => d.Severity == LinterSeverity.Error);
```
Every call scans the full list. Each `LinterResult` is a one-shot object — these should be cached counters incremented in `AddDiagnostic`. See P2.

### M5. Tokenizer round-trips spans to strings at the line boundary
[CalcpadTokenizer.cs:117-119](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.cs#L117-L119):
```csharp
var sourceSpan = source.AsSpan();
foreach (var lineSpan in new LineEnumerator(sourceSpan))
    TokenizeLineInternal(lineSpan.ToString(), lineNum++);
```
The `LineEnumerator` produces zero-alloc spans, then we materialize a string per line and use `text[i]` indexing throughout `TokenizeLineInternal`. For a 10K-line file that's 10K avoidable allocations. See P1.

### M6. Result-type shapes diverge
[TokenizerResult.cs](Calcpad.Highlighter/Tokenizer/Models/TokenizerResult.cs), [LinterResult.cs](Calcpad.Highlighter/Linter/Models/LinterResult.cs), and [ContentResolverResult.cs](Calcpad.Highlighter/ContentResolution/ContentResolverResult.cs) each define their own surface with no common shape. Not a bug; a sign these were written at different times.

### M7. `SourceMapper` and `LinterResult` private boundary
`SourceMapper` methods in [SourceMapper.cs:31-46](Calcpad.Highlighter/Linter/Helpers/SourceMapper.cs#L31-L46) are `public static` but only consumed by `LinterResult.MapDiagnosticsToOriginal`. They should be `internal`.

### M8. Tokenizer partial-class boundaries are blurry
`CalcpadTokenizer.Helpers.cs` carries methods that are tightly bound to tokenizer state (e.g., `IsClosingSpecialContentTag`) and used in exactly one site — they're not really helpers, they're inlined state-machine fragments split across files. Reorganization: keep static, pure helpers in `Helpers.cs`; move state-coupled fragments to the file that owns the state.

---

## Findings — Performance (impact-ranked)

### P1. Tokenizer materializes a fresh string per line  **(highest impact)**
[CalcpadTokenizer.cs:119](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.cs#L119) — `lineSpan.ToString()` per line. The entire `TokenizeLineInternal` uses `text[i]`/`text.Length` which work identically on `ReadOnlySpan<char>`. Migrating the signature to `ReadOnlySpan<char>` and only allocating `string text` inside `Append()` (where a `Token` is actually built) saves N allocations per file, where N is line count. On a 10K-line document, eliminates ~10K string allocs and ~10K associated GC pressure events.

Knock-on: `_state.Text` ([CalcpadTokenizer.cs:37](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.cs#L37)) is `string`; would need to become `ReadOnlyMemory<char>` (struct can't hold span). Some call sites use `text.AsSpan(i+1).IsWhiteSpace()` ([line 160](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.cs#L160)), confirming the loop is already span-friendly.

### P2. `LinterResult` LINQ properties are O(n) per call
[LinterResult.cs:10-13](Calcpad.Highlighter/Linter/Models/LinterResult.cs#L10-L13). UI typically reads `HasErrors` / `ErrorCount` per render. Replace with cached `int _errorCount; int _warningCount;` fields incremented inside `AddDiagnostic`. O(1) per access.

### P3. Stage3 validators each do a full pass over `stage3.Lines`
Seven validator methods in `UsageValidator` alone, plus `BalanceValidator`, `SemanticValidator`, `FormatValidator`, `NamingValidator`, `FunctionTypeValidator`, `HtmlCommentValidator`, `CommandBlockValidator` — each iterates every line in `stage3.Lines`. Many calls into `tokenProvider.IsCpdMode(i)`, `LineParser.ShouldSkipLine`, etc. are repeated.

Restructure: one pass over lines, dispatch to validators per line. Or batch into 2-3 phases (e.g., "needs tokens", "needs cpd-only", "needs raw text"). Estimated reduction: ~3-5× fewer per-line operations on large files.

### P4. `cmd + "{"` concatenation per command per line  **(high impact, easy)**
[UsageValidator.cs:61](Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs#L61):
```csharp
var cmdIndex = line.IndexOf(cmd + "{", StringComparison.OrdinalIgnoreCase);
```
Inside a `foreach (var cmd in CommandsExcludingCommandBlocks)` — for every line containing `@`, this allocates `cmd.Length + 2`-char strings ~25 times. Same pattern in `ValidateCommandVariables`.

Fix: in `CalcpadBuiltIns`, pre-compute `static FrozenDictionary<string, string> CommandWithBrace` where values are `"$Sum{"`, `"$Repeat{"`, etc. Or single-pass: find `{` after `$`, look up the command name by the preceding identifier — one O(1) lookup per `{`, not 25 IndexOf calls per line.

### P5. `string.Substring` in command syntax validator
[UsageValidator.cs:77,87](Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs#L77) — `line.Substring(0, atIndex)` and `line.Substring(atIndex + 1)` allocate per line with `@`. Replace with `line.AsSpan(0, atIndex)` and `line.AsSpan(atIndex + 1)`; `Contains('|')` works on spans.

### P6. Macro expansion allocates LIST + STRING per param replacement
[ContentResolver.Stage3.cs:855-961](Calcpad.Highlighter/ContentResolution/ContentResolver.Stage3.cs#L855-L961) — `sortedMacros = macros.OrderByDescending(…).ToList()` runs once per macro expansion (could be once per resolve), and `macroContent.Replace(param, arg)` allocates a new string per parameter. For a macro with 5 params expanded 100 times: 500 string allocations just for parameter substitution.

Fix: sort macros once at the top of Stage3, not per call. Use `StringBuilder` for param substitution, or build the replacement in a single pass with `Span<char>` scratch buffer.

### P7. `Split('\n')` after macro expansion
[ContentResolver.Stage3.cs:78](Calcpad.Highlighter/ContentResolution/ContentResolver.Stage3.cs#L78) — `expandedLine.Split('\n')` allocates a `string[]` per macro-expanding line. Use the existing `LineEnumerator` (already used elsewhere in this class).

### P8. `TypeTracker` substring allocations during type inference
[TypeTracker.cs:203,298,332,354,439,495,715](Calcpad.Highlighter/Linter/Helpers/TypeTracker.cs) — 7+ `Substring()` calls per type-inference operation. With heavy type-tracked code, each variable use can trigger several. Convert to span slicing + index tracking.

### P9. `ParameterParser.ParseParameters` allocates `List<List<Token>>` per call
[FunctionTypeValidator.cs:69](Calcpad.Highlighter/Linter/Validators/Stage3/FunctionTypeValidator.cs#L69) — per function call site. For a line with 10 function calls: 10+ nested lists. Cache a pooled `List<List<Token>>` and `Clear()` between uses, or use `ArrayPool<Token>` for the inner lists.

### Lower-priority items (P11-P14)
- `string.Join(", ", usedVariables.Select(…))` in error paths — only matters if errors are common; not hot.
- `FormatRegex` — verified static + `RegexOptions.Compiled` in [CalcpadPatterns.cs](Calcpad.Highlighter/Linter/Constants/CalcpadPatterns.cs). OK.
- `CalcpadBuiltIns.CommandsExcludingCommandBlocks` lazy property — already cached after first hit ([CalcpadBuiltIns.cs:171](Calcpad.Highlighter/Linter/Constants/CalcpadBuiltIns.cs#L171)). Fine as-is.
- `CharClassifier` — well-designed already; precomputed ASCII table + Unicode fallback. No changes needed.

---

## Prioritized Roadmap

Order by ROI (impact ÷ effort). Items are independent and can land separately.

**Tier 1 — significant perf, contained scope**
1. **P2** — Cache `_errorCount`/`_warningCount` in `LinterResult` (1 file, ~10 lines)
2. **P4** — Precompute `cmd + "{"` strings or restructure to single-pass command lookup (1 file)
3. **P5** — Replace `Substring` with `AsSpan` in `UsageValidator` (~6 sites)
4. **P7** — Replace `Split('\n')` with `LineEnumerator` in `Stage3.cs:78`

**Tier 2 — bigger perf wins, larger refactor**
5. **P1** — Tokenizer span migration (touches `TokenizerState`, every partial file using `_state.Text`). Largest perf win; needs care.
6. **P3** — Unify Stage3 validator passes (validator interface change, but eliminates redundant traversals)
7. **P6** — Macro expansion: sort once, replace via StringBuilder

**Tier 3 — cleanup / consistency**
8. **D5 + M1** — Audit all `new LinterDiagnostic` sites; route through `DiagnosticExtensions`; pick span-or-string and apply consistently
9. **D8** — Merge the two `ValidateCommandSyntax`/`ValidateCommandVariables` methods (first ~30 lines are near-identical)
10. **D4** — Consolidate char-class helpers (delegate `CalcpadTokenizer.Helpers.cs` to `CalcpadCharacterHelpers`)
11. **M2** — Standardize line iteration on `LineEnumerator` everywhere
12. **M3, M7, M8** — Stage-context symmetry, visibility cleanup, partial-class re-organization

---

## Critical Files

Most-touched in any tier-1/2 fix:
- [Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.cs](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.cs) — main loop, state struct
- [Calcpad.Highlighter/Linter/Models/LinterResult.cs](Calcpad.Highlighter/Linter/Models/LinterResult.cs) — counter caching
- [Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs](Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs) — biggest per-line allocator
- [Calcpad.Highlighter/Linter/Helpers/DiagnosticExtensions.cs](Calcpad.Highlighter/Linter/Helpers/DiagnosticExtensions.cs) — diagnostic factory
- [Calcpad.Highlighter/ContentResolution/ContentResolver.Stage3.cs](Calcpad.Highlighter/ContentResolution/ContentResolver.Stage3.cs) — macro expansion + Split
- [Calcpad.Highlighter/Linter/Helpers/TypeTracker.cs](Calcpad.Highlighter/Linter/Helpers/TypeTracker.cs) — Substring hotspots

Reusable existing utilities (prefer over new code):
- `Parsing/LineEnumerator.cs` — already zero-alloc; use it from Stage3 and tokenizer
- `Parsing/CharClassifier.cs` — keep using; well-designed precomputed table
- `Linter/Helpers/CalcpadCharacterHelpers.cs` — central identifier/Greek/unicode helpers
- `Linter/Helpers/DiagnosticExtensions.cs` — central diagnostic factory
- `Linter/Helpers/SourceMapper.cs` — central line/column mapping

---

## Verification

For each tier-1 change:
1. **Correctness**: run the test runner in [Calcpad.Highlighter/Tests/](Calcpad.Highlighter/Tests/) — `LinterTestRunner.cs` and `QuickTest.cs`. The repo already has a corpus of fixture files.
2. **Perf**: pick a representative large `.cpd` (100+ lines, with macros and command blocks). Wrap `Tokenize` and `Lint` in a `Stopwatch`/`BenchmarkDotNet` micro-benchmark; record allocations with `[MemoryDiagnoser]`. Before/after diff per change.
3. **Regression**: run the wider build — Highlighter is consumed by `Calcpad.Server`, `Calcpad.Web`, and the VS Code extension. Build the solution; run any server-side highlighter tests.
4. **UI smoke test**: load a representative file in the web editor; confirm syntax highlighting and linter squiggles are unchanged.
