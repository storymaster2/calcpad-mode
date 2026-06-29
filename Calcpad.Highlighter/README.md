# Calcpad.Highlighter

A .NET library that provides syntax analysis, linting, content resolution, and autocomplete data for Calcpad source code. Used by Calcpad.Web's backend to power syntax highlighting, diagnostics, go-to-definition, find-references, and code completion.

**Zero external dependencies** — uses only the .NET BCL.

## Architecture

```
Calcpad.Highlighter/
├── ContentResolution/   Three-stage content pipeline (the core of the library)
├── Tokenizer/           Syntax tokenization for highlighting and analysis
├── Linter/              Error detection with 10 validators across 3 stages
├── Snippets/            Autocomplete/IntelliSense data (12 categories)
├── Parsing/             Low-level parsing utilities (CharClassifier, LineEnumerator)
└── Tests/               Unit and integration tests
```

---

## Content Resolution Pipeline

The `ContentResolver` processes raw Calcpad source code through three stages, each building on the previous. Every stage maintains a source map so that errors can be traced back to the original source lines.

```
Original Source
    │
    ▼
┌─────────────────────────────────┐
│  Stage 1: Line Continuations    │  Merge multi-line expressions
└─────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────┐
│  Stage 2: Includes & Macros     │  Expand #include, collect macro definitions
└─────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────┐
│  Stage 3: Expansion & Defs      │  Expand macro calls, collect all definitions
└─────────────────────────────────┘
    │
    ▼
StagedResolvedContent (used by Linter, API endpoints, IDE features)
```

### Usage

```csharp
var resolver = new ContentResolver();
var staged = resolver.GetStagedContent(
    content: sourceCode,
    includeFiles: includeDict,         // filename -> content (plain text)
    clientFileCache: cacheDict,        // filename -> raw bytes (base64-decoded)
    sourceFilePath: "/path/to/file.cpd"
);

// staged.Stage1 — line continuations resolved
// staged.Stage2 — includes expanded, macros collected
// staged.Stage3 — macros expanded, all definitions extracted
```

### Stage 1: Line Continuations

Merges multi-line expressions into single lines. Calcpad supports two continuation styles:

- **Explicit:** a trailing ` _` (space + underscore) joins the next line
- **Implicit:** lines ending with certain characters (`;`, `|`, `&`, `@`, `:`, `(`, `{`, `[`) automatically continue

**Output (`Stage1Result`):**
- `Lines` — content with continuations merged
- `SourceMap` — maps merged line index back to original line index
- `LineContinuationSegments` — tracks where each original line's content starts within a merged line (used for accurate column mapping in diagnostics)

### Stage 2: Include Resolution & Macro Collection

Two passes over the Stage 1 output:

**Pass 1 — Include expansion:**
- Replaces `#include` lines with the referenced file's content (recursive, up to 20 levels deep)
- Detects circular includes
- File resolution order: filesystem path, then `includeFiles` dictionary, then `clientFileCache` dictionary
- Tracks source origin (`local` vs `include`) and original file line numbers for each expanded line

**Pass 2 — Macro collection:**
- Tokenizes content in **Macro mode** to extract all `#def` definitions
- Records each macro's name, parameters, defaults, content lines, and source location
- Computes "comment parameters" — parameters that appear inside comments (with transitive closure for nested macro calls). This tells the tokenizer to highlight call-site arguments as comments instead of expressions
- Builds macro bodies for type inference at call sites

**Output (`Stage2Result`):**
- `Lines` — content with includes expanded (macros still unexpanded)
- `SourceMap` — maps to Stage 1 lines
- `IncludeMap` — source file info per line (`local` or include path + original line)
- `MacroDefinitions` — full metadata for every macro (name, params, defaults, content, description)
- `MacroCommentParameters` — which parameters are "comment params" per macro
- `MacroParameterOrder` — parameter ordering for positional argument matching
- `MacroBodies` — inline bodies for type resolution
- `UserDefinedMacros` — param counts for linting
- `DuplicateMacros` — redefinition tracking

### Stage 3: Macro Expansion & Definition Collection

Processes the Stage 2 output in two phases:

**Phase 1 — Macro expansion:**
- Builds a lookup from macro name to its definition (params, content)
- Skips over `#def`...`#end def` blocks (they've already been collected)
- Expands macro calls by substituting arguments into the macro body
- Tracks expansion metadata so errors inside expanded content map back to the call site

**Phase 2 — Definition extraction:**
- Tokenizes the expanded content in **Lint mode** to extract all definitions:
  - **Variables:** name, initial expression, type inference, line number, source
  - **Functions:** name, parameters, defaults, expression body or command block, return type
  - **Custom units:** name (`.unitName`), definition expression
  - **Command block functions:** functions using `$Inline{}`, `$Block{}`, or `$While{}`
- Builds the `TypeTracker` with inferred types (scalar, vector, matrix, string, etc.)
- Builds symbol indices mapping every symbol name to all its occurrences (definition, reassignment, usage) with source file info — used by go-to-definition and find-all-references

**Output (`Stage3Result`):**
- `Lines` — fully expanded content (ready for linting)
- `SourceMap` — maps to Stage 2 lines
- `MacroExpansions` — maps expanded lines back to their call sites
- `UserDefinedFunctions` / `FunctionsWithParams` — function definitions with param info
- `VariablesWithDefinitions` — variable definitions with expressions and types
- `CustomUnits` — custom unit definitions
- `TypeTracker` — complete type inference for all symbols
- `CommandBlockFunctions` — functions containing `$Inline`/`$Block`/`$While` blocks
- `VariableAssignments` / `VariableUsages` — for unused variable detection
- `VariableIndex` / `FunctionIndex` / `MacroIndex` — symbol occurrence maps for navigation

### How Content Is Fetched

The `ContentResolver` itself does not perform any I/O. The caller (Calcpad.Web backend) is responsible for pre-fetching all file contents and passing them in:

1. **`includeFiles`** — a `Dictionary<string, string>` mapping filenames to plain-text content. The caller populates this from the filesystem, remote URLs, or API routes before calling `GetStagedContent`.

2. **`clientFileCache`** — a `Dictionary<string, byte[]>` mapping filenames to raw bytes. Used when the client (e.g., a VS Code extension) has files in memory that the server can't access directly. Decoded as UTF-8.

3. **Filesystem fallback** — Stage 2 also tries `Path.GetFullPath()` with environment variable expansion for local file resolution.

Resolution priority: filesystem > `includeFiles` > `clientFileCache`. If a file can't be found, an error comment is injected into the content and processing continues.

For remote content (URLs and API routes), Calcpad.Web's backend pre-fetches into a global cache via `CalcpadService.PreFetchRemoteContentAsync()` before calling the resolver.

### Source Mapping

All three stages maintain source maps that chain together for accurate error reporting:

```
Stage 3 line → Stage3.SourceMap → Stage 2 line
Stage 2 line → Stage2.SourceMap → Stage 1 line
Stage 1 line → Stage1.SourceMap → Original line
```

The `SourceMapper` utility class handles the full chain, including line continuation segment mapping for accurate column positions within merged lines.

---

## Tokenizer

The `CalcpadTokenizer` converts source code into a stream of typed tokens. It operates in three modes:

| Mode | Purpose | Used By |
|------|---------|---------|
| **Highlight** | Produce tokens for syntax coloring | `/highlight` endpoint, IDE highlighting |
| **Macro** | Extract macro definitions and comment parameters | Stage 2 of ContentResolver |
| **Lint** | Full definition extraction with type tracking | Stage 3 of ContentResolver |

### Token Types

| ID | Type | Category | Description |
|----|------|----------|-------------|
| 0 | None | Core | Whitespace or unknown content |
| 1 | Const | Core | Numeric constants (123, 3.14, 1e-5) |
| 2 | Operator | Core | Operators (+, -, *, /, =, ≤, ≥) |
| 3 | Bracket | Core | Brackets: (), [], {} |
| 4 | LineContinuation | Core | Continuation marker (_ at end of line) |
| 5 | Variable | Identifiers | Variable identifiers |
| 6 | LocalVariable | Identifiers | Scoped variables (function params, loop vars, command scope) |
| 7 | Function | Identifiers | Function names (built-in or user-defined) |
| 8 | Macro | Identifiers | Macro names (ending with $) |
| 9 | MacroParameter | Identifiers | Macro parameters in #def statements |
| 10 | Units | Identifiers | Unit identifiers (m, kg, N/m^2) |
| 11 | Setting | Identifiers | Backend setting variables (PlotHeight, Precision, etc.) |
| 12 | Keyword | Keywords | Keywords starting with # (#if, #else, #def) |
| 13 | ControlBlockKeyword | Keywords | Block-starting keywords (#if, #for, #while, #def, #else) |
| 14 | EndKeyword | Keywords | Block-ending keywords (#end if, #end def, #loop) |
| 15 | Command | Keywords | Commands starting with $ ($Plot, $Sum, $Root) |
| 16 | Include | File/Data | Include file paths |
| 17 | FilePath | File/Data | File paths in #read/#write/#append |
| 18 | DataExchangeKeyword | File/Data | Sub-keywords (from, to, sep, type) |
| 19 | Comment | Comments | Plain text comments in ' or " |
| 20 | HtmlComment | Comments | HTML comments (<!-- ... -->) |
| 21 | Tag | Comments | HTML tags within comments |
| 22 | HtmlContent | Comments | Text between HTML tags |
| 23 | JavaScript | Comments | JavaScript in `<script>` tags |
| 24 | Css | Comments | CSS in `<style>` tags |
| 25 | Svg | Comments | SVG in `<svg>` tags |
| 26 | Input | Special | Input markers (? or #{...}) |
| 27 | Format | Special | Format specifiers (:f2, :e3) |
| 28 | FutureReserved28 | Reserved | _Reserved ordinal_ |
| 29 | FutureReserved29 | Reserved | _Reserved ordinal_ |
| 30 | FutureReserved30 | Reserved | _Reserved ordinal_ |

The tokenizer is split across 8 partial class files for maintainability: core logic, comment parsing, macro handling, bracket/operator parsing, type resolution, helpers, definition extraction, and macro collection.

### Performance

- Zero-allocation parsing with `ReadOnlySpan<char>` and `StringBuilder`
- Pre-computed ASCII character classification table (`CharClassifier`) for O(1) lookups
- Zero-allocation line splitting (`LineEnumerator` ref struct)

---

## Linter

The `CalcpadLinter` validates `StagedResolvedContent` and produces diagnostics with error codes, severity levels, and precise source positions.

```csharp
var linter = new CalcpadLinter();
var result = linter.Lint(staged, ignoreRegions);
// result.ErrorCount, result.WarningCount, result.Diagnostics
```

### Validators

10 validators organized by pipeline stage:

**Stage 1:**
- `IncludeValidator` — validates `#include` syntax and file resolution

**Stage 2:**
- `MacroValidator` — duplicate macros, parameter syntax, naming rules, nesting

**Stage 3 (8 validators):**
- `BalanceValidator` — bracket/parenthesis matching
- `NamingValidator` — variable naming, conflicts with built-ins, undefined variables
- `UsageValidator` — unused variables, unreachable code, scoping
- `SemanticValidator` — operator sequences, command syntax, assignments
- `FunctionTypeValidator` — parameter counts, return types, overload resolution
- `CommandBlockValidator` — `$Inline`/`$Block`/`$While` statement syntax
- `FormatValidator` — format specifier syntax
- `HtmlCommentValidator` — HTML structure and tag nesting

### Error Codes

| Range | Stage | Category |
|-------|-------|----------|
| CPD-11xx | 1 | Include directives |
| CPD-22xx | 2 | Macro definitions |
| CPD-31xx | 3 | Bracket/brace balance |
| CPD-32xx | 3 | Naming and undefined variables |
| CPD-33xx | 3 | Usage (param counts, keyword args) |
| CPD-34xx | 3 | Semantic (operators, commands, assignments) |

### Lint Ignore Regions

Diagnostics can be suppressed in specific regions using `LintIgnoreRegion`:

```csharp
var ignoreRegions = new LintIgnoreRegionParser().ExtractRegions(sourceCode);
var result = linter.Lint(staged, ignoreRegions);
```

Each region specifies a line range and optionally a list of error codes to suppress.

---

## Snippet System

The `SnippetRegistry` provides autocomplete data for all Calcpad language constructs. It aggregates snippets from 12 category-specific data classes into frozen (immutable, optimized) collections.

### How Snippets Are Defined

Each category file (e.g., `FunctionSnippets.cs`, `KeywordSnippets.cs`) defines a static array of `SnippetItem` objects:

```csharp
public static class FunctionSnippets
{
    public static readonly SnippetItem[] Items = [
        new("sin(§)", "Sine of angle in radians", "Functions/Trigonometric",
            parameters: [new("x", ParameterType.Scalar, description: "Angle in radians")],
            returnType: CalcpadType.Value),
        // ...
    ];
}
```

Each `SnippetItem` includes:
- **Insert text** — what gets inserted, with `§` marking cursor position
- **Description** — tooltip text
- **Category** — hierarchical path (e.g., `"Functions/Trigonometric"`)
- **Parameters** — name, type (`Scalar`/`Vector`/`Matrix`/`Integer`/etc.), optional/variadic flags, description
- **Return type** — for functions (`Value`, `Vector`, `Matrix`, etc.)
- **Quick type** — shortcut string for `~`-prefix insertion (e.g., `"a"` means `~a` inserts `α`)

### Snippet Categories

| Category | File | Description |
|----------|------|-------------|
| Constants | `ConstantSnippets.cs` | pi, e, and assignment patterns |
| Operators | `OperatorSnippets.cs` | +, -, *, /, ^, ≤, ≥, etc. |
| Functions/* | `FunctionSnippets.cs` | ~200 built-in math functions |
| Functions/Vector | `VectorFunctionSnippets.cs` | sum, max, rms, len, join, etc. |
| Functions/Matrix | `MatrixFunctionSnippets.cs` | transpose, det, solve, identity, etc. |
| Keywords | `KeywordSnippets.cs` | #if, #for, #while, #def, #include, #read, #write, etc. |
| Commands | `CommandSnippets.cs` | $Plot, $Sum, $Find, $Root, $Table, etc. |
| Settings | `SettingSnippets.cs` | PlotHeight, PlotWidth, Precision, Tol, etc. |
| Units | `UnitSnippets.cs` | m, kg, s, A, K, mol, rad, and all derived units |
| HTML | `HtmlSnippets.cs` | HTML tags for comment documentation |
| Markdown | `MarkdownSnippets.cs` | Markdown syntax for documentation |
| Symbols | `SymbolSnippets.cs` | Greek letters and special math symbols |

### How Snippets Are Used

**1. Autocomplete (API):**
The `/api/calcpad/snippets` endpoint queries `SnippetRegistry` and returns filtered results. The frontend uses these to populate autocomplete dropdowns with insert text, descriptions, and parameter documentation.

```csharp
// Get all snippets
SnippetItem[] all = SnippetRegistry.GetAllSnippetsArray();

// Filter by category prefix
SnippetItem[] trig = SnippetRegistry.GetSnippetsByCategory("Functions/Trigonometric");
```

**2. Built-in name validation (Linter):**
The `SnippetRegistry` provides frozen sets of known names used by the linter to distinguish built-in identifiers from user-defined ones:

```csharp
SnippetRegistry.GetFunctionNames()    // FrozenSet<string> of all built-in function names
SnippetRegistry.GetKeywordNames()     // FrozenSet<string> of all keywords (with # prefix)
SnippetRegistry.GetCommandNames()     // FrozenSet<string> of all commands (with $ prefix)
SnippetRegistry.GetUnitNames()        // FrozenSet<string> of all unit identifiers
SnippetRegistry.GetConstantNames()    // FrozenSet<string> (pi, e)
SnippetRegistry.GetOperators()        // FrozenSet<char> of operator characters
```

These are used by `NamingValidator` to detect conflicts (e.g., a variable named `sin`) and by the tokenizer to classify identifiers.

**3. Function signature validation (Linter):**
The `FunctionTypeValidator` uses snippet parameter metadata to check call-site argument counts and types:

```csharp
// Get the most permissive overload for each function
SnippetRegistry.GetFunctionSnippetsByName()    // Dict<string, SnippetItem>

// Get all overloads for a function
SnippetRegistry.GetFunctionOverloads()         // Dict<string, SnippetItem[]>

// Check return types for type inference
SnippetRegistry.GetVectorReturningFunctions()  // FrozenSet<string>
SnippetRegistry.GetMatrixReturningFunctions()  // FrozenSet<string>
```

**4. Tokenizer classification:**
During tokenization, the tokenizer checks snippet registries to determine if an identifier is a built-in function, keyword, command, unit, or setting — which determines its token type and syntax coloring.

---

## Type Tracking

The `TypeTracker` infers and records types for all definitions discovered in Stage 3:

| Type | ID | Description |
|------|----|-------------|
| Unknown | 0 | Type could not be determined |
| Value | 1 | Scalar numeric value |
| Vector | 2 | 1D array |
| Matrix | 3 | 2D array |
| Various | 5 | Multiple types assigned in different places |

Type inference works by analyzing expressions:
- `[1; 2; 3]` — Vector (semicolons)
- `[1|2; 3|4]` — Matrix (pipes + semicolons)
- `sin(x)` — Value (known function return type)
- `take(v; 3)` — Vector (known vector-returning function)
- Reassignment to a different type marks the variable as `Various`

---

## Integration

Calcpad.Highlighter is consumed by **Calcpad.Web's backend** (Calcpad.Server), which uses it to power the `/highlight`, `/lint`, `/definitions`, `/find-references`, and `/snippets` REST endpoints.

The library has no dependencies on any consumers. It receives content as strings and dictionaries, and returns structured results.
