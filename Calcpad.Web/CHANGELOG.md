# Calcpad.Web Changelog

## calcpad-experimental Branch — Granular Feature Breakdown

This changelog documents all new features and changes introduced on the `calcpad-experimental` branch since diverging from `main`. It is organized as a granular, documentation-ready breakdown — each numbered sub-feature is intended to stand alone as a documentation topic with its own code examples. It covers the VS Code extension, web backend, Calcpad.Core parser, and Calcpad.Highlighter library.

---

## 1. Interactive UI Controls (`#UI` Directive)

A new `#UI` keyword creates interactive input controls in the preview panel. When a user edits a value, the calculation automatically re-runs with the updated input. Five control types are supported: **entry**, **datagrid**, **dropdown**, **radio**, and **checkbox**.

### 1.1 Entry Fields (Scalar Inputs)

Place `#UI` before any scalar variable assignment to turn its result into an editable text input:

```
#UI L = 10m
#UI W = 5m
A = L * W
```

In the preview, `L` and `W` appear as editable text fields. Changing either value recalculates `A` in real time. The unit suffix (e.g. `m`) is preserved when the numeric value is overridden.

### 1.2 Datagrid (Matrix/Vector Editor)

When the right-hand side is a matrix or vector, `#UI` automatically creates a spreadsheet-style grid editor (powered by jspreadsheet-ce):

```
#UI v = [1; 2; 3]              ' 1×3 vector
#UI M = [1|2|3; 4|5|6]          ' 2×3 matrix
#UI Z = vector(5)               ' 1×5 grid of zeros
#UI G = matrix(3; 4)            ' 3×4 grid of zeros
```

Grid dimensions are auto-detected from the RHS:

- `[1; 2; 3]` → 1 row × 3 columns (displayed horizontally)
- `[1;2;3 | 4;5;6]` → 2 rows × 3 columns (pipe `|` separates rows, semicolon `;` separates cells)
- `vector(n)` → 1×n grid initialized to zeros
- `matrix(m; n)` → m×n grid initialized to zeros

### 1.3 Dropdowns, Radio Groups, and Checkboxes

Three additional control types are exposed via the `type` JSON property:

```
#UI {"type": "dropdown", "keys": ["Low", "Med", "High"], "values": ["1", "2", "3"]} grade = 1
#UI {"type": "radio",    "keys": ["Steel", "Concrete"], "values": ["200GPa", "25GPa"]} E = 200GPa
#UI {"type": "checkbox"} flag = 1
```

- `keys` — display labels shown to the user
- `values` — underlying values substituted back into the calculation
- `keys` and `values` must have matching lengths
- Checkbox sends `1` when checked, `0` when unchecked

### 1.4 JSON Configuration Properties

The optional JSON block supports all of these properties:

| Property | Type | Applies to | Description |
|----------|------|------------|-------------|
| `type` | string | all | `"entry"`, `"datagrid"`, `"dropdown"`, `"radio"`, `"checkbox"` (auto-detected if omitted) |
| `style` | string | all | CSS class name(s) applied to the rendered control |
| `rows` | number | datagrid | Explicit row count (auto-detected if omitted) |
| `columns` | number | datagrid | Explicit column count (auto-detected if omitted) |
| `columnHeaders` | string[] | datagrid | Custom column labels |
| `rowHeaders` | string[] | datagrid | Custom row labels |
| `keys` | string[] | dropdown/radio | Display labels (**required** for dropdown/radio) |
| `values` | string[] | dropdown/radio | Underlying values (**required** for dropdown/radio) |

Unknown properties are silently ignored.

### 1.5 String Variable Expansion for UI Config

Store JSON configuration in a string variable and reference it in the `#UI` directive:

```
#string UIJSON$ = '{"type": "entry", "style": "highlight"}'
#UI UIJSON$ L = 10m
```

The string variable is expanded before JSON parsing, allowing shared UI configurations across many variables.

### 1.6 Conditional UI Fields

UI fields work inside `#if`, `#else if`, `#else`, `#end if` blocks. When the branch is inactive the UI control is not rendered:

```
#UI material$ = 'Steel'
#if material$ ≡ 'Steel'
    #UI E = 200GPa
#else if material$ ≡ 'Concrete'
    #UI E = 25GPa
#end if
```

### 1.7 UI Override Persistence

The VS Code extension persists current UI override values as an HTML comment block at the top of the file, so state survives file save/close/reopen:

```
'<!--{"uiOverrides": {"L": "12", "W": "4"}}-->
```

Handled by `Calcpad.Web/frontend/calcpad-frontend/src/services/ui-overrides.ts`. The `/api/calcpad/definitions` endpoint parses this block and returns it in the response so the extension can re-apply overrides on reload.

### 1.8 VS Code UI Preview Panel

- Opens from the third preview tab (**UI Preview**) in the `.cpd` webview
- Calls `POST /api/calcpad/convert-ui` with the current content and an `uiOverrides` dictionary
- On `uiValueChange` message from any control, the override is merged into the dictionary and the view is re-rendered
- Entry fields have a light yellow background (dark theme: dark gray) with a blue focus outline
- Entry: fires on blur or **Enter** key
- Dropdown/radio/checkbox: fires on `change` event
- Datagrid: fires on mouseup, MutationObserver changes, and cell blur

### 1.9 CDN Dependencies

Datagrid rendering requires the following CDN assets (loaded once per preview):

- `https://cdn.jsdelivr.net/npm/jspreadsheet-ce/dist/jspreadsheet.min.css`
- `https://cdn.jsdelivr.net/npm/jspreadsheet-ce/dist/index.min.js`
- `https://cdn.jsdelivr.net/npm/jsuites/dist/jsuites.min.css`
- `https://cdn.jsdelivr.net/npm/jsuites/dist/jsuites.min.js`

Loading is polled for up to 5 seconds. If the CDN is unreachable, a "Datagrid library failed to load" message is shown.

### 1.10 CSS Classes for Theming

All UI controls emit semantic class names so host themes can style them:

- `.calcpad-ui-input`
- `.calcpad-ui-dropdown`
- `.calcpad-ui-radio`, `.calcpad-ui-radio-label`
- `.calcpad-ui-checkbox`
- `.calcpad-ui-datagrid`

### 1.11 String Mode (`#UI` as Alternative to `#string` / `#table`)

Because only one `#xxx` keyword can prefix a given line, `#UI` now doubles as a replacement for `#string` and `#table` whenever the left-hand side is a string variable (name ending with `$`). A new `"mode"` JSON property accepts `"string"` or `"number"`; when omitted, the parser auto-detects string mode from a `$` suffix on the LHS or a string-shaped RHS. String-mode stores the evaluated value into `_stringVariables` (or `_tableVariables` for datagrid) so the variable is usable by name elsewhere in the document, and emits a UI control bound to that variable.

**Auto-detected (mode omitted):**

```
#UI greeting$ = 'hello'                          ' text entry, stored in greeting$
#UI t$ = ['a'; 'b' | 'c'; 'd']                   ' 2×2 editable string table, stored in t$
#UI x = 10                                       ' numeric (unchanged existing behavior)
```

**Explicit string mode:**

```
#UI {"type": "entry",    "mode": "string"} msg$ = 'hi'
#UI {"type": "dropdown", "mode": "string", "keys": ["Red","Blue"], "values": ["red","blue"]} color$ = 'red'
#UI {"type": "radio",    "mode": "string", "keys": ["Steel","Concrete"], "values": ["steel","concrete"]} material$ = 'steel'
#UI {"type": "checkbox", "mode": "string"} flag$ = 'false'
#UI {"type": "datagrid", "mode": "string"} t$ = ['name'; 'value' | 'length'; '10m']
```

**Behavior notes:**

- Checkbox in string mode stores the literal `'true'` or `'false'`. Overrides like `"1"` / `"0"` are coerced to `'true'` / `'false'`.
- Dropdown/radio `values` are the strings stored in the variable (no numeric coercion).
- Datagrid in string mode populates `_tableVariables`, so `join$(t$; ...)`, `rowT$`, `colT$`, etc. all work on the result.
- `Settings.UiOverrides["name$"]` replaces the evaluated value before storage.
- Rendered HTML adds `data-ui-mode="string"` to both the wrapping element and the child control so frontend renderers can distinguish string from numeric controls (e.g. text editors rather than numeric-only cells in a datagrid).
- Explicit `"mode": "string"` with an LHS that does not end in `$` produces an error. Likewise an LHS that ends in `$` cannot be forced into `"mode": "number"`.
- Concatenation is supported because the RHS goes through `EvaluateStringExpression`:

```
#UI first$ = 'John'
#UI last$  = 'Doe'
#UI full$  = first$ + ' ' + last$    ' stored as 'John Doe'
```

This supersedes the workaround of pairing `#UI` metadata with a separate `#string` definition line — a single `#UI` now owns both the value and the control.

The `style` JSON property is appended to the class list.

### 1.11 Linter Validation (CPD-3415)

The semantic validator checks `#UI` JSON syntax and emits warnings for:

- Missing closing `}`
- Invalid JSON payload
- `type: "dropdown"` or `"radio"` without `keys` or `values`
- `keys.length !== values.length` for dropdown/radio

Implemented in [SemanticValidator.cs](../Calcpad.Highlighter/Linter/Validators/Stage3/SemanticValidator.cs) (Stage 3).

---

## 2. String Variables

A new string type allows text values to be stored, manipulated, compared, and referenced throughout a Calcpad document. Three storage kinds are supported: **scalar string variables** (`#string`), **macros** (`#def`), and **2D string tables** (`#table`). All names must end with `$`.

### 2.1 Inline String Definition (`#string`)

```
#string title$ = 'Engineering Report'
#string author$ = 'John Doe'
```

- Defined and evaluated immediately (single-line)
- RHS is a string expression (literal, concatenation, string function call, variable reference)

### 2.2 String Literal Quoting

String literals are delimited by **single quotes (`'`)**. To include a single quote inside a literal, double it:

```
#string saying$ = 'She said ''No'''   ' value: She said 'No'
```

Double quotes (`"`) are not used for string literals in `#string` definitions.

### 2.3 Multiline Macro Definition (`#def`)

```
#def description$
    This is a multiline
    string variable
#end def
```

### 2.4 Inline Macro Shorthand

```
#def label$ = 'Section Header'
#def max$(a; b) = $If{a > b @ a : b}
```

### 2.5 Variable Expansion (`$` Suffix)

Reference a string variable with its `$` suffix anywhere in the source. Expansion is case-insensitive and happens before expression parsing:

```
#string name$ = 'Beam Analysis'
'Title: name$
```

Implemented by `ExpressionParser.Strings.ExpandStringVariables()` as part of the preprocessing pipeline.

### 2.6 String Tables (`#table`)

Tables are 2D string arrays accessed via `tbl$(row; col)` syntax. Three ways to create one:

```
' Literal (| separates rows, ; separates columns)
#table data$ = ['Name'; 'Age' | 'John'; '30' | 'Jane'; '28']

' Empty constructor
#table blank$ = table$(3; 4)

' From a delimited string
#table parsed$ = split$('a,b;c,d'; ';'; ',')
```

### 2.7 Table Element Access and Assignment

```
data$(1; 1)                 ' read: "Name"
data$(2; 2) = '31'          ' write
```

### 2.8 String Comparison Operators

| Operator | Meaning | Notes |
|----------|---------|-------|
| `≡` / `==` | Equal | Case-sensitive, ordinal |
| `≠` / `!=` | Not equal | Case-sensitive, ordinal |

```
#string status$ = 'OK'
#if status$ ≡ 'OK'
    'All checks passed'
#end if
```

Result is numeric `1` (true) or `0` (false), usable in any expression context.

### 2.9 Built-in String Functions

All names end with `$`. Arguments are separated by semicolons.

**Single-argument:** `len$(s)`, `trim$(s)`, `ltrim$(s)`, `rtrim$(s)`, `ucase$(s)`, `lcase$(s)`, `string$(x)`, `val$(s)`, `space$(n)`, `typeOf$(x)`, `tableToStringArray$(t)`

**Two-argument:** `left$(s; n)`, `right$(s; n)`, `compare$(s1; s2)`, `find$(needle; haystack)`, `parsejson$(json; path)`, `table$(rows; cols)`, `rowToStringArray$(t; row)`, `colToStringArray$(t; col)`

**Three-argument:** `mid$(s; start; len)`, `replace$(s; find; repl)`, `instr$(start; haystack; needle)`, `split$(s; rowDelim; colDelim)`, `join$(t; rowDelim; colDelim)`

**Variadic:** `concat$(s1; s2; ...; sN)`

**Numeric-returning** (return a number, not a string): `len$`, `val$`, `compare$`, `instr$`, `find$`.

### 2.10 Table Manipulation Functions

Dedicated table-shape functions live alongside the string functions:

`rowT$`, `colT$`, `extractRowsT$`, `extractColsT$`, `subTable$`, `transposeT$`, `augmentT$` (horizontal stack), `stackT$` (vertical stack).

### 2.11 Numeric Conversion

Because string variables cannot participate directly in arithmetic, use `val$()`:

```
#string age_str$ = '30'
age = val$(age_str$)
next = age + 1            ' 31
```

### 2.12 VS Code Integration

- Autocomplete suggests user-defined string variables after the `$` trigger character
- Snippets for `#string`, `#def`, `#table` appear in the keyword category
- Linter validates name/definition syntax via CPD-2201 through CPD-2213 (for macros) and CPD-3301/CPD-3309 (for usage)

---

## 3. Keyword Arguments in Functions and Macros

Both custom functions and macros now support optional parameters with default values, plus call-site keyword arguments of the form `name=value`.

### 3.1 Function Optional Parameters

Define default values with `=` in the parameter list. Default expressions are stored verbatim and evaluated at **call time** in global scope:

```
f(x; y = 0; z = 1kg) = x * y + z
```

Required parameters must appear before optional ones (see 3.7).

### 3.2 Function Keyword Argument Calls

Call functions using `paramName=value`:

```
f(x; y = 0; z = 1) = x * y + z

f(5; 3; 2)            ' positional only
f(x = 5; z = 2; y = 3) ' all keyword, any order
f(5; z = 2)            ' x=5 (positional), y=0 (default), z=2 (keyword)
```

Rewriting happens before tokenization: `f(5; z = 2)` becomes `f(5; 0; 2)` for the evaluator.

### 3.3 Macro Optional Parameters

Macros use the `$` suffix on names and parameters. Defaults are raw text substituted into the macro body:

```
#def calc$(a$; b$ = 10; c$ = 5)
    result = a$ + b$ + c$
#end def
```

### 3.4 Macro Keyword Argument Calls

```
calc$(1; 2; 3)             ' positional
calc$(a$ = 1; c$ = 7; b$ = 2) ' keyword, any order
calc$(1; c$ = 7)           ' positional then keyword
```

### 3.5 Default Expression Evaluation

- **Functions:** defaults are expression source text; parsed and evaluated at each call in the global variable scope
- **Macros:** defaults are raw text substituted before parsing; evaluated in the context of the expansion
- **Defaults cannot reference other parameters** — each default is resolved independently

### 3.6 Rules for Mixing Positional and Keyword Arguments

1. Required parameters must appear before optional parameters **in the definition**
2. Positional arguments must appear before keyword arguments **in a call**
3. Each parameter can only be supplied once (duplicate → error)
4. Missing required arguments → error
5. Missing optional arguments → default is used

### 3.7 Linter Error Codes

| Code | Applies to | Condition |
|------|------------|-----------|
| `CPD-2213` | Macro definition | Required parameter after optional parameter |
| `CPD-3215` | Function definition | Required parameter after optional parameter |
| `CPD-3314` | Macro call | Unknown keyword argument name |
| `CPD-3315` | Function call | Unknown keyword argument name |

### 3.8 VS Code Autocomplete

Autocomplete inserts function/macro calls with parameter-name snippet placeholders (e.g. `f(${1:x}; ${2:y})`) and renders default values in the hover documentation:

```
param y: Value *(default: 0)*
param z: Value *(default: 1kg)*
```

Required parameters display `*(required)*` when the function also has optional parameters.

### 3.9 Limitations

- Built-in functions (`sin`, `cos`, `sqrt`, …) **do not** support keyword arguments or optional parameters
- Defaults cannot reference other parameters
- String variable expansion is not applied to default expressions
- Command-block functions (`$Inline`, `$Block`, `$While`) accept optional params in their definition but receive only positional arguments internally

---

## 4. Language Server Features (VS Code)

Full IDE-grade symbol support for variables, functions, macros, and custom units, implemented via standard VS Code language providers.

### 4.1 Go to Definition

- Trigger: **Ctrl+Click** or **F12**
- Covers variables, functions, macros, custom units
- Works across `#include` files (the backend resolves source file + line from the symbol index)
- Jumps to the first assignment; reassignments are not targeted
- Provider: [calcpadDefinitionProvider.ts](frontend/vscode-calcpad/src/calcpadDefinitionProvider.ts)
- Backend: `POST /api/calcpad/find-references`, filtered to `isAssignment === true`

### 4.2 Find All References

- Trigger: **Shift+Alt+F12** or right-click → *Find All References*
- Covers the same symbol types as Go to Definition
- Works across `#include` files
- Each location includes exact column and length for precise selection
- `context.includeDeclaration` toggles whether the definition is included in results

### 4.3 Rename Symbol

- Trigger: **F2**
- Renames only `source: 'local'` occurrences
- **Does not** rename across `#include` files — attempting to rename an imported symbol returns an error "`'name'` is defined in an include file and cannot be renamed here"
- Macro renames are supported for local macros only

### 4.4 Path Completion for `#include`, `#read`, `#write`, `#append`

- `#include`: `.cpd`, `.txt`
- `#read` / `#write` / `#append`: `.cpd`, `.txt`, `.csv`, `.tsv`, `.xlsx`, `.xlsm`, `.xls`
- Supports `/` and `\` path separators and drills into subdirectories
- Expands `%VAR%` (Windows) and `$VAR` (POSIX) environment variables
- Honors `calcpad.libraryPath` setting — library files appear alongside workspace paths
- Strips `@sheet`, `type=`, `sep=` options before resolving the path
- Remote URL completion is not provided

### 4.5 Quick-Type Symbol Insertion

Type `~` followed by a symbol key and press **space** to replace with the symbol:

```
~a   → α         ~A   → Α
~b   → β         ~p   → π
~g   → γ         ~s   → σ
~t   → θ         ~S   → Σ
```

Controlled by the `calcpad.enableQuickTyping` setting (default `true`). The full mapping comes from [SymbolSnippets.cs](../Calcpad.Highlighter/Snippets/Data/SymbolSnippets.cs).

### 4.6 Snippet Categories

Snippets are organized hierarchically and supplied by the `/api/calcpad/snippets` endpoint. Source data files:

| Source file | Category |
|-------------|----------|
| `ConstantSnippets.cs` | Mathematical constants |
| `OperatorSnippets.cs` | Operators |
| `FunctionSnippets.cs` | Built-in scalar functions (Trigonometric, Inverse Trig, Hyperbolic, …) |
| `VectorFunctionSnippets.cs` | Vector functions |
| `MatrixFunctionSnippets.cs` | Matrix functions |
| `KeywordSnippets.cs` | Keywords (`#if`, `#def`, `#string`, `#UI`, …) |
| `CommandSnippets.cs` | Commands (`$plot`, `$find`, `$sum`, …) |
| `SettingSnippets.cs` | Settings (`Precision`, `Tol`, `PlotHeight`, …) |
| `UnitSnippets.cs` | Physical units |
| `HtmlSnippets.cs` | HTML tags |
| `MarkdownSnippets.cs` | Markdown syntax |
| `SymbolSnippets.cs` | Greek letters and symbols |

### 4.7 Hover Provider

Rich hover tooltips for macros, functions, variables, and custom units. Reads from the cached `/api/calcpad/definitions` result (no extra network round-trip). Content shown:

- **Macros:** signature, source file, description, parameter types & defaults
- **Functions:** signature, source file, description, return type, per-parameter docs
- **Variables:** assignment expression, inferred type, source file
- **Custom units:** definition expression, source file

Word matching includes `$` suffix and Unicode characters.

### 4.8 Semantic Tokens (Syntax Highlighting)

Server-backed semantic highlighting via `/api/calcpad/highlight`. 31 token types spanning Const, Units, Operator, Variable, Function, Keyword, Command, Bracket, Comment, Tag, Input, Include, Macro, HtmlComment, Format, LocalVariable, FilePath, DataExchangeKeyword, and more. Supports incremental per-line updates via `/api/calcpad/highlight-line`.

### 4.9 Completion Provider

- User-defined symbols prioritized above built-ins (sort order 0 vs 1)
- Metadata JSON completion inside HTML comment blocks above definitions
- Function/macro invocation snippets with `${N:param}` placeholders
- Settings key completion (`decimals`, `degrees`, `complex`, `units`, `colorScale`, …)
- `paramType` value completion for doc comments

### 4.10 Diagnostics Integration

- CPD error codes surfaced as standard VS Code diagnostics
- Severities: Error (0), Warning (1), Information (2)
- Minimum severity filtered via `calcpad.linter.minimumSeverity` (default `information`)
- Messages formatted as `[CODE] Description: details`
- `'<!--{"LintIgnore": ["<CODE>"]}-->` … `'<!--{"EndLintIgnore": []}-->` regions suppress specific codes for a range of lines (empty array suppresses all codes)

### 4.11 Providers Not Yet Implemented

Signature help (parameter hints on `(`), code actions/quick fixes, document formatting, folding ranges, and document links are not currently provided.

---

## 5. Recursive Includes and API Router

`#include` and `#read` now support recursive resolution, remote URLs, and a structured `<service:endpoint>` syntax for API calls.

### 5.1 Recursive `#include` Resolution

Included files can themselves include other files. The resolver passes a shared `visited` set through the recursion:

```
' top.cpd
#include 'shared/constants.cpd'
#include 'shared/helpers.cpd'
```

Handled by [ContentResolver.Stage2.cs](../Calcpad.Highlighter/ContentResolution/ContentResolver.Stage2.cs) (for linting) and [IncludeResolver.cs](backend/Services/IncludeResolver.cs) (for conversion).

### 5.2 Depth Limit (20 Levels)

Recursion depth is capped at **20**. When exceeded, the include is replaced with:

```
' Error: Include file not provided: <filename>
```

### 5.3 Circular Reference Detection

A case-insensitive `HashSet<string>` tracks every filename already expanded on the current path. A second attempt to include the same file is skipped — preventing infinite loops on self-referential or mutually-referential files.

### 5.4 Remote URL Support (HTTP/HTTPS)

Include content directly from the web:

```
#include "https://example.com/shared-calcs.cpd"
```

- Only `http://` and `https://` are recognized
- Default timeout: **10 seconds** (configurable per request via `apiTimeoutMs`)
- User-Agent: `Calcpad/1.0` (static fetch) or `Calcpad-Server/1.0` (routed API calls)
- Non-2xx responses throw an error with status code and reason phrase

### 5.5 `<service:endpoint>` Routing Syntax

Structured remote calls are written as:

```
#include "<weather_api:forecast>{\"city\":\"Seattle\"}"
```

Parsed as `serviceName:endpointName` plus a trailing request body. The body determines the HTTP method:

- Body starts with `{` or `[` → **POST** with `Content-Type: application/json`
- Otherwise → **GET**

### 5.6 RoutingConfig Structure

```json
{
  "weather_api": {
    "base_url": "https://api.weather.com",
    "auth": "jwt",
    "endpoints": {
      "current":  "/v1/current",
      "forecast": "/v1/forecast"
    }
  }
}
```

- Keys use snake_case (enforced by `JsonNamingPolicy.SnakeCaseLower`)
- `auth: "jwt"` causes `Authorization: Bearer <token>` to be added from the request's `AuthSettings`
- Final URL is `base_url + endpoint_template`

### 5.7 Remote Content Pre-Fetching and Caching

Before the main conversion runs, all remote `#include` and `#read` targets are fetched asynchronously in parallel.

- **Global cache** (`ConcurrentDictionary<string, byte[]>`) — shared across all requests, keyed by URL or `<service:endpoint>` token
- **Per-request `ClientFileCache`** — base64-encoded file contents supplied by the client
- **Disk cache** — files over ~1 MB are offloaded to `{AppContext.BaseDirectory}/cache/` as SHA-256-keyed `.cache` files
- Cache cleared manually via `POST /api/calcpad/refresh-cache` (single key, multiple keys, or full flush)

### 5.8 Source Mapping and Error Attribution

Every expanded line tracks its origin through three maps:

- `Stage1Result.SourceMap` — line continuation merges
- `Stage2Result.IncludeMap` — `{ Source, SourceFile, OriginalLine }` for each expanded line
- `Stage3Result.MacroExpansions` — which macros were expanded on each line

Diagnostics use these maps to trace errors back to the original file and line.

### 5.9 `#include` vs `#read`

| Aspect | `#include` | `#read` |
|--------|-----------|---------|
| When processed | Parse time (substituted) | Runtime (evaluated) |
| Content | Calcpad source code | CSV, TSV, Excel, JSON data |
| Scope filtering | `#local` blocks stripped | n/a |
| Output | Nested source inlined | Directive preserved; produces a matrix/vector variable |
| Pre-fetching | Yes | Yes |

---

## 6. Table of Contents

Generate a navigable TOC from document headings.

### 6.1 Heading Comment Syntax

Markdown-style heading comments use a leading `'` plus one to six `#` characters:

```
'# Section 1
'Some content
'## Subsection 1.1
'More content
'### Deeper subsection
'# Section 2
```

These render as `<h1>`–`<h6>` HTML headings in the output.

### 6.2 Automatic ID Generation

`toc.js` walks all `<h1>`–`<h6>` elements on page load. For each heading without an `id`:

1. Snake-cases the heading text (lowercase, spaces → underscores)
2. Detects collisions and appends `_<n>` when necessary
3. Assigns the generated ID to the element

### 6.3 Nested List Rendering

`makeList({ target, parent })` builds a nested `<ul>` tree matching the heading levels:

```js
window.addEventListener('load', () => {
  makeList({ target: '#toc', parent: 'article' });
});
```

- Deeper heading levels become nested `<ul>` elements
- Same or shallower levels pop the stack to the correct parent
- Each entry becomes `<li><a href="#heading_id">Heading Text</a></li>`

### 6.4 Example File

[Calcpad.Cli/Examples/Demos/toc.cpd](../Calcpad.Cli/Examples/Demos/toc.cpd) demonstrates the full pattern including `#md on`, custom CSS, and the JS invocation.

### 6.5 Platform Support

- **Web / VS Code:** fully supported (JavaScript runs in the preview)
- **WPF:** passive — renders TOC HTML if included, but does not auto-generate it

---

## 7. PDF Generation (PuppeteerSharp + PDFsharp)

The PDF pipeline has been replaced with **PuppeteerSharp** (headless Chromium) for HTML rendering, plus **PDFsharp** for post-processing headers, footers, and background overlays.

### 7.1 Rendering Pipeline

1. Launch headless browser (singleton, locked with a semaphore)
2. Create page and call `SetContentAsync(html, { WaitUntil: Networkidle0 })`
3. Inject print-specific CSS to force color-accurate output and fit datagrid tables
4. Transform DOM via JavaScript (convert inputs to underlined text, etc.)
5. Generate PDF bytes with Puppeteer `PdfDataAsync`
6. Post-process with PDFsharp (headers, footers, background) if enabled

### 7.2 Supported Paper Formats

Letter, Legal, Tabloid, Ledger, **A0, A1, A2, A3, A4, A5, A6**. Default: A4. Unrecognized values fall back to A4.

### 7.3 Margins and Orientation

- Margins accept CSS unit strings: `"2cm"`, `"1.5cm"`, `"0.5in"`
- `orientation: "portrait"` (default) or `"landscape"`
- Independent per-edge margin control: `marginTop`, `marginRight`, `marginBottom`, `marginLeft`

### 7.4 Headers (when `enableHeader: true`)

Laid out by PDFsharp:

- Document **title** (top-left, bold 12pt)
- Document **subtitle** (below title, gray)
- **Center text** from `headerCenter`
- **Timestamp** (top-right, 8pt, light gray) formatted per `dateTimeFormat` (defaults to `"g"`)
- 1 px gray separator line below the header

### 7.5 Footers (when `enableFooter: true`)

- **Left:** author and company
- **Center:** custom `footerCenter` text
- **Right:** page numbers (`Page N of Total`) and project field
- 1 px gray separator line above the footer

### 7.6 Background PDF Overlay

```
options.backgroundPdf = "C:/templates/letterhead.pdf";
```

PDFsharp loads the background via `XPdfForm.FromFile` and draws it **behind** each page using `XGraphicsPdfPageOptions.Prepend`, stretching to fit page dimensions.

### 7.7 Image Base64 Embedding

The VS Code extension scans generated HTML for `<img src="…">` and, for each local file path (not `http://`, `https://`, or `data:`), embeds the file as a base64 data URI before sending to `/api/calcpad/pdf`. Supported MIME types: `png`, `jpg`/`jpeg`, `gif`, `webp`, `svg`. Required because headless Chromium cannot read from the client's disk.

### 7.8 Browser Detection Order

1. Explicit `browserPath` (request field, env var, or `appsettings.json`)
2. **Microsoft Edge** (Windows: Program Files, Program Files (x86), LocalAppData)
3. **Google Chrome** (same three locations)
4. **Linux:** `chromium`, `chromium-browser`, `google-chrome`, `google-chrome-stable`, `/snap/bin/chromium`
5. **macOS:** Chrome, Edge, Chromium in `/Applications`
6. **Fallback:** auto-download `ChromeHeadlessShell` via `BrowserFetcher` into `{AppContext.BaseDirectory}/chromium`

### 7.9 Complete `PdfOptions` Reference

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `format` | string | `"A4"` | Paper format |
| `orientation` | string | `"portrait"` | `portrait` or `landscape` |
| `printBackground` | bool | `true` | Render background colors/images |
| `scale` | float | `1.0` | Zoom factor (0.1 – 2.0) |
| `marginTop` | string | `"2cm"` | Top margin |
| `marginRight` | string | `"1.5cm"` | Right margin |
| `marginBottom` | string | `"2cm"` | Bottom margin |
| `marginLeft` | string | `"1.5cm"` | Left margin |
| `enableHeader` | bool | `false` | Render header |
| `enableFooter` | bool | `false` | Render footer |
| `documentTitle` | string? | `null` | Title (header, bold) |
| `documentSubtitle` | string? | `null` | Subtitle (header, gray) |
| `author` | string? | `null` | Author (footer left) |
| `company` | string? | `null` | Company (footer left) |
| `project` | string? | `null` | Project (footer right) |
| `headerCenter` | string? | `null` | Custom center header text |
| `footerCenter` | string? | `null` | Custom center footer text |
| `dateTimeFormat` | string? | `null` | .NET format string (null → `"g"`) |
| `backgroundPdf` | string? | `null` | Path to background PDF |

### 7.10 VS Code Export Flow

Command `vscode-calcpad.exportToPdf`:

1. Read `calcpad.pdf.*` settings
2. Build `ClientFileCache` from the document's `#include`/`#read` references
3. `POST /api/calcpad/convert` → HTML
4. Embed local images as base64
5. `POST /api/calcpad/pdf` with 60-second timeout
6. Show save dialog and write the PDF
7. Offer to open the saved file

### 7.11 Health Check

`GET /api/calcpad/pdf/health` returns:

```json
{ "status": "ok", "service": "calcpad-pdf", "version": "2.0.0" }
```

---

## 8. Web Backend Architecture

The web backend (`Calcpad.Web/backend`) was refactored from the former `Calcpad.Server` project with clearer separation of concerns.

### 8.1 Endpoint Catalog

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/calcpad/convert` | POST | HTML conversion with theme + settings |
| `/api/calcpad/convert-unwrapped` | POST | Raw code HTML for error navigation |
| `/api/calcpad/convert-ui` | POST | HTML with interactive `#UI` overrides applied |
| `/api/calcpad/debug-raw-code` | POST | Macro-expanded source (debugging aid) |
| `/api/calcpad/sample` | GET | Retrieve a sample document |
| `/api/calcpad/pdf` | POST | Generate a PDF |
| `/api/calcpad/pdf/health` | GET | PDF service health check |
| `/api/calcpad/resolve-content` | POST | Three-stage content resolution, all stages returned |
| `/api/calcpad/highlight` | POST | Tokenize full document for syntax highlighting |
| `/api/calcpad/highlight-line` | POST | Tokenize a single line (incremental) |
| `/api/calcpad/lint` | POST | Semantic diagnostics with CPD codes |
| `/api/calcpad/definitions` | POST | Macros, functions, variables, units, UI overrides |
| `/api/calcpad/find-references` | POST | Full symbol occurrence index |
| `/api/calcpad/snippets` | GET | Snippets by category |
| `/api/calcpad/refresh-cache` | POST | Invalidate remote content cache |

### 8.2 Common Request Fields (`CalcpadRequest`)

- `content` — Calcpad source code
- `settings` — math/plot/units configuration
- `theme` — `"light"` or `"dark"`
- `clientFileCache` — base64-encoded file contents keyed by path
- `authSettings` — JWT token and routing config
- `apiTimeoutMs` — remote fetch timeout (default 10000)
- `sourceFilePath` — client-side file path for resolving relative `#include`

### 8.3 UI Request (`CalcpadUiRequest`)

Adds `uiOverrides: Dictionary<string, string>` to `CalcpadRequest`. Forces `settings.EnableUi = true` server-side.

### 8.4 Lint Response Shape

```typescript
{
  errorCount: number,
  warningCount: number,
  diagnostics: Array<{
    line: number, column: number, endColumn: number,
    code: string,        // "CPD-XXXX"
    message: string,
    severity: "error" | "warning" | "information",
    severityId: 0 | 1 | 2,
    source: "Calcpad Linter"
  }>
}
```

### 8.5 Definitions Response Shape

Returns four parallel arrays plus persisted UI overrides:

- `macros[]` — name, parameters, isMultiline, content, source, description, paramTypes, paramDescriptions, defaults
- `functions[]` — name, parameters, expression, returnType, returnTypeId, hasCommandBlock, commandBlockType, commandBlockStatements, defaults
- `variables[]` — name, expression, type, typeId
- `customUnits[]` — name, expression
- `uiOverrides?` — `{ overrides: Dictionary<string,string>, commentLine: number }`

`typeId` values: 0 Unknown, 1 Value, 2 Vector, 3 Matrix, 4 StringVariable, 5 Various, 6 Function, 7 InlineMacro, 8 MultilineMacro, 9 CustomUnit.

### 8.6 Find-References Response

Three dictionaries (`variables`, `functions`, `macros`), each mapping symbol name to an array of:

```typescript
{ line, column, length, source, sourceFile?, isAssignment }
```

`isAssignment: true` indicates a definition or reassignment.

### 8.7 Highlight Response

Returns an array of `{ line, column, length, type, typeId, text? }`. Passing `includeText: false` (default) omits the `text` field to reduce payload.

### 8.8 Snippets Response

```typescript
{
  count: number,
  snippets: Array<{
    insert: string,        // § marks cursor placement
    description: string,
    label?: string,
    category: string,      // e.g. "Functions/Trigonometric"
    quickType?: string,    // e.g. "a" for ~a → α
    parameters?: Array<{ name, description? }>
  }>
}
```

Optional query string `?category=Functions/Trigonometric` filters results.

### 8.9 Refresh-Cache Request

```typescript
{ key?: string, keys?: string[] }  // null/empty → clear entire cache
```

### 8.10 API Router Service

API routing moved from `Calcpad.Core` to `Calcpad.Web/backend/Services/Router.cs`:

- Parses `<service:endpoint>` syntax
- Supports JWT-authenticated requests via `AuthSettings`
- Configurable endpoint mappings via `RoutingConfig` (snake_case JSON)
- Auto-selects GET vs POST based on body contents

### 8.11 API Schema Documentation

A machine-readable API schema lives at [backend/API_SCHEMA.md](backend/API_SCHEMA.md). The shared TypeScript types are in [frontend/calcpad-frontend/src/types/api.ts](frontend/calcpad-frontend/src/types/api.ts).

---

## 9. Content Resolution Pipeline

A new three-stage pipeline in `Calcpad.Highlighter` processes files uniformly for the linter, definitions endpoint, and highlight endpoint. Each stage emits an artifact plus a source map pointing back to the previous stage.

### 9.1 Stage 1 — Line Continuation

- **Explicit:** line ends with ` _` (space + underscore) → merged with next line
- **Implicit:** line ends with `;|&@:({[` **and** has unbalanced delimiters → continues
- Delimiter tracking ignores content inside comments (quoted text)
- Produces `LineContinuationMap` (merged line → list of original lines) and `LineContinuationSegments` (start column + length per segment) to support accurate error positions

### 9.2 Stage 2 — Include Resolution & Macro Collection

- Two passes: recursive `#include` substitution, then macro definition tokenization
- `#read` directives pass through unchanged (resolved at runtime)
- Computes `MacroDefinitions`, `DuplicateMacros`, `MacroParameterOrder` (for keyword-arg validation), `MacroBodies`, `UserDefinedMacros`
- `MacroCommentParameters` — fixed-point iteration to compute which macro parameters are actually referenced inside quoted/comment content (used by the tokenizer to colorize them correctly)

### 9.3 Stage 3 — Macro Expansion & Type Tracking

- Removes macro definitions from the output, expands every call
- Collects `DefinedVariables`, `VariablesWithDefinitions`, `FunctionsWithParams`, `CustomUnits`, `CommandBlockFunctions`
- Builds three symbol indices used by all IDE features: `VariableIndex`, `FunctionIndex`, `MacroIndex`
- Tracks `VariableReassignments` and `OuterScopeAssignments` (← operator) separately from first assignment
- Runs the `TypeTracker` to infer every symbol's `CalcpadType`

### 9.4 Source Mapping Back to the Original File

`SourceMapper.MapLineBack()` chains the three `SourceMap` dictionaries plus the include map and macro-expansion map to resolve any Stage 3 line/column back to the original file, original line, and source kind (`local` vs `include`).

---

## 10. Disk Caching and Performance

### 10.1 In-Memory Remote Cache

- `ConcurrentDictionary<string, byte[]>` shared across requests
- Keyed by URL or `<service:endpoint>` token
- Thread-safe, no explicit locking required

### 10.2 Disk Cache Offload

- Files larger than ~1 MB are written to `{AppContext.BaseDirectory}/cache/`
- Filename: first 32 hex characters of SHA-256 of the cache key, `.cache` extension
- Hit on the in-memory entry touches `LastWriteTimeUtc` to refresh the TTL

### 10.3 Cleanup Service

- `DiskCacheCleanupService` runs every hour (`CleanupInterval = 1h`)
- Deletes any `.cache` file older than 24 hours (`MaxAge = 24h`)
- Permission errors are silently skipped

### 10.4 Base64 Truncation for Lint

When lint-stage content includes embedded base64 blobs above a size threshold, they are truncated before analysis to prevent memory bloat. The original content is preserved for rendering.

### 10.5 Remote Pre-Fetching

All remote `#include` / `#read` targets (URLs and API routes) are fetched **in parallel** before the main conversion. Results populate the in-memory cache. Used by `/api/calcpad/convert`, `/convert-unwrapped`, `/convert-ui`, `/lint`, `/definitions`, `/find-references`, `/resolve-content`.

### 10.6 Manual Cache Invalidation

`POST /api/calcpad/refresh-cache` — clear a single key, a list of keys, or everything. The VS Code extension exposes a command that triggers a full flush.

---

## 11. Linter and Diagnostics

### 11.1 Error Code Conventions

All codes follow the pattern `CPD-SCNN`:

- `S` — pipeline stage (1 pre-include, 2 macro, 3 post-include)
- `C` — category within the stage
- `NN` — sequence number

### 11.2 Stage 1: Include Validation (CPD-11xx)

| Code | Severity | Description |
|------|----------|-------------|
| CPD-1101 | Error | Malformed `#include` statement |
| CPD-1102 | Error | Missing `#include` filename |

### 11.3 Stage 2: Macro Definitions (CPD-22xx)

| Code | Severity | Description |
|------|----------|-------------|
| CPD-2201 | Error | Duplicate macro definition |
| CPD-2202 | Error | Macro name must end with `$` |
| CPD-2203 | Error | Macro parameter must end with `$` |
| CPD-2204 | Error | Invalid macro name (must start with a letter) |
| CPD-2205 | Error | Malformed `#def` syntax |
| CPD-2206 | Error | Unmatched `#def` or `#end def` |
| CPD-2207 | Error | Nested macro definition not allowed |
| CPD-2208 | Error | Macro parameter must start with a letter |
| CPD-2209 | Warning | Macro definition inside control block has no effect |
| CPD-2210 | Error | Invalid character in macro name |
| CPD-2211 | Error | Invalid character in macro parameter |
| CPD-2212 | Error | Duplicate macro parameter |
| CPD-2213 | Error | Required parameter after optional parameter (macro) |

### 11.4 Stage 3: Balance (CPD-31xx)

| Code | Severity | Description |
|------|----------|-------------|
| CPD-3101 | Error | Unmatched opening parenthesis |
| CPD-3102 | Error | Unmatched closing parenthesis |
| CPD-3103 | Error | Unmatched opening square bracket |
| CPD-3104 | Error | Unmatched closing square bracket |
| CPD-3105 | Error | Unmatched opening curly brace or control block |
| CPD-3106 | Error | Unmatched closing curly brace |

### 11.5 Stage 3: Naming (CPD-32xx)

| Code | Severity | Description |
|------|----------|-------------|
| CPD-3201 | Error | Invalid variable name (must start with a letter) |
| CPD-3203 | Error | Invalid function name |
| CPD-3204 | Error | Function name conflicts with built-in function |
| CPD-3205 | Error | Variable name conflicts with a keyword |
| CPD-3207 | Error | Variable name conflicts with a built-in constant |
| CPD-3208 | Error | Function must have at least one parameter |
| CPD-3215 | Error | Required parameter after optional parameter (function) |

### 11.6 Stage 3: Usage (CPD-33xx)

| Code | Severity | Description |
|------|----------|-------------|
| CPD-3301 | Error | Undefined variable |
| CPD-3302 | Error | Function called with incorrect parameter count |
| CPD-3303 | Error | Undefined macro |
| CPD-3304 | Error | Macro called with incorrect parameter count |
| CPD-3305 | Error | Undefined function |
| CPD-3306 | Warning | Invalid element access |
| CPD-3307 | Error | Too few parameters |
| CPD-3308 | Error | Too many parameters |
| CPD-3309 | Warning | Parameter type mismatch |
| CPD-3310 | Error | Undefined unit |
| CPD-3311 | Error | Empty parameter in function call |
| CPD-3312 | Information | Unused variable |
| CPD-3314 | Error | Unknown keyword argument in macro call |
| CPD-3315 | Error | Unknown keyword argument in function call |

### 11.7 Stage 3: Semantic (CPD-34xx)

| Code | Severity | Description |
|------|----------|-------------|
| CPD-3401 | Error | Invalid operator usage |
| CPD-3404 | Error | Unknown command name |
| CPD-3406 | Error | Unknown directive |
| CPD-3407 | Warning | Invalid assignment |
| CPD-3409 | Error | `#` directive not allowed inside command block |
| CPD-3410 | Error | Invalid command syntax |
| CPD-3411 | Error | Incomplete expression |
| CPD-3412 | Error | Command variable mismatch |
| CPD-3413 | Error | Reassignment of a constant |
| CPD-3414 | Error | Outer scope assignment (`←`) to undefined variable |
| CPD-3415 | Warning | Invalid `#UI` JSON format |
| CPD-3416 | Warning | Invalid `paramType` value in metadata comment |
| CPD-3417 | Warning | Invalid metadata comment JSON |

### 11.8 Stage 3: Format (CPD-36xx)

| Code | Severity | Description |
|------|----------|-------------|
| CPD-3601 | Warning | Invalid format specifier |

### 11.9 Lint-Ignore Regions

Suppress specific diagnostics within a region:

```
'<!--{"LintIgnore": ["CPD-3301"]}-->
prototype_var = 5
'<!--{"EndLintIgnore": []}-->
```

Parsed in [backend/Services/LintIgnoreRegionParser.cs](backend/Services/LintIgnoreRegionParser.cs). Applied after mapping diagnostics back to original source lines.

### 11.10 Severity Filtering (VS Code)

Setting `calcpad.linter.minimumSeverity` (`error` | `warning` | `information`, default `information`) filters lower-severity diagnostics before they reach the editor.

---

## 12. Parsing Modes and Display Directives

Calcpad now supports explicit mode switching for how a document section is rendered, plus a set of visibility directives for controlling what appears in preview vs. calculation output. Implemented in [ExpressionParser.Keywords.cs](../Calcpad.Core/Parsers/ExpressionParser/ExpressionParser.Keywords.cs).

### 12.1 `#cpd` — Calcpad Mode (default)

Switches the parser into standard Calcpad mode, where lines are calculations by default and any line prefixed with `'` is treated as HTML/text. This is the initial mode; use `#cpd` to return to default after a `#html` or `#markdown` section:

```
#html
<h1>Report</h1>
<p>This entire section is passed through verbatim.</p>

#cpd
A = 5m
B = 3m
'Area = A * B
```

### 12.2 `#html` — HTML Mode

Every line in the block is treated as raw HTML and emitted directly, with no leading `'` required. Useful for inlining large HTML sections without the `'` prefix boilerplate:

```
#html
<section class="intro">
  <h2>Introduction</h2>
  <p>Pure HTML here.</p>
</section>

#cpd
```

### 12.3 `#markdown` — Markdown Mode

Every line is treated as Markdown and converted to HTML:

```
#markdown
# Report Title
## Section 1
- Item 1
- Item 2

#cpd
```

### 12.4 `#md on` / `#md off`

A lighter-weight toggle: inside Calcpad mode, `#md on` (or just `#md`) enables Markdown processing on subsequent `'`-prefixed comment lines; `#md off` disables it. Implemented by `ParseKeywordMd()`:

```
#md on
'# This renders as a heading
'**bold text**

#md off
'# This stays literal
```

### 12.5 Visibility Directives

Seven directives control what appears in each render pass. All are blocked inside `#html` and `#markdown` mode (see `IsBlockedInNonCpdMode()`):

| Directive | Effect |
|-----------|--------|
| `#hide`   | Hide all subsequent output from rendering |
| `#show`   | Show all subsequent output (cancels `#hide`) |
| `#pre`    | Show only in preview mode, not calculated output |
| `#post`   | Show only in calculation mode, not preview |
| `#val`    | Show **values only**, not equations |
| `#equ`    | Show **equations** (default, cancels `#val`) |
| `#noc`    | Suppress all output (no calculation, no rendering) |

Example — typical usage in a report template:

```
#hide
helper = some_intermediate_value

#show
#val
final_result = helper * 2     ' renders as "final_result = 42" only
```

### 12.6 Text vs HTML Token Classification

Within Calcpad mode, a `'`-prefixed line is auto-classified:

- Line starts with `<` → tokenized as **Html** (emitted without `<p>` wrapping)
- Otherwise → tokenized as **Text** (wrapped in `<p>…</p>`)

This means `'<div>…</div>` is inlined as HTML, while `'My comment` becomes `<p>My comment</p>`.

---

## 13. HTML Tokenization and Embedded Language Support

The Highlighter tokenizer recognizes embedded HTML, JavaScript, CSS, and SVG inside comment lines, enabling accurate syntax highlighting across mixed-content `.cpd` files. Implemented in [CalcpadTokenizer.Comments.cs](../Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.Comments.cs).

### 13.1 Embedded Content Token Types

| Token type | ID | Triggered by |
|------------|----|--------------|
| `HtmlComment` | 20 | `<!-- … -->` blocks (may span multiple lines) |
| `Tag` | 21 | `<tag …>` and `</tag>` markers inside comments |
| `HtmlContent` | 22 | Text between HTML tags |
| `JavaScript` | 23 | Content inside `<script>…</script>` |
| `Css` | 24 | Content inside `<style>…</style>` |
| `Svg` | 25 | Content inside `<svg>…</svg>` |

### 13.2 Special-Content Tracking

The tokenizer keeps state for `<script>`, `<style>`, and `<svg>` blocks so their inner text is colored as JavaScript/CSS/SVG rather than plain comment content. Nested tags inside those elements are validated before breaking out of the special mode.

### 13.3 Multi-Line HTML Comments

`<!-- … -->` comments can span multiple `'`-prefixed lines. The tokenizer sets `_inHtmlComment` and keeps subsequent lines in the `HtmlComment` token class until the closing `-->` is found.

### 13.4 Line Continuation Inside Comments

`_continueTextComment` and `_continueTagComment` flags preserve quote/tag context across lines ending with the ` _` continuation marker, so highlighting stays coherent across merged lines.

### 13.5 Known Limitations

- Full HTML/JS/CSS validation is not yet provided — the tokenizer recognizes embedded languages for highlighting but does not lint them (planned: wire a JS library via the Node instance VS Code ships with)
- Base64 `data:` URIs inside `<img src="…">` attributes can overrun syntax highlighting for very long lines; see the To-Do for "Fix base64 syntax highlighting by sending the last characters that close the img tag"
- `'` (apostrophe) inside embedded JS/CSS/SVG can confuse tokenization — a known edge case with `<script>` blocks

---

## 14. VS Code Quality-of-Life Features

A wide range of editor affordances beyond the core language server features (Section 4). All commands, keybindings, settings, and menus are declared in [vscode-calcpad/package.json](frontend/vscode-calcpad/package.json).

### 14.1 Formatting Hotkeys

Content-aware formatting that inserts HTML or Markdown based on the `calcpad.commentFormat` setting (`auto` | `html` | `markdown`). In `auto`, the extension inspects the current document and picks HTML or Markdown syntax. Toggle the whole feature with `calcpad.enableFormattingHotkeys` (default `true`).

| Keybinding | Command | Effect |
|------------|---------|--------|
| **Ctrl+B** | `formatBold` | `**bold**` or `<b>bold</b>` |
| **Ctrl+I** | `formatItalic` | `*italic*` or `<i>italic</i>` |
| **Ctrl+U** | `formatUnderline` | `<u>underline</u>` |
| **Ctrl+=** | `formatSubscript` | `<sub>x</sub>` |
| **Ctrl+Shift+=** | `formatSuperscript` | `<sup>x</sup>` |
| **Ctrl+1** – **Ctrl+6** | `formatHeading1` – `formatHeading6` | `# …` / `<h1>…</h1>` |
| **Ctrl+L** | `formatParagraph` | `<p>…</p>` |
| **Ctrl+R** | `formatLineBreak` | `<br>` |
| **Ctrl+Shift+L** | `formatBulletedList` | Bulleted list |
| **Ctrl+Shift+N** | `formatNumberedList` | Numbered list |
| **Ctrl+Q** | `toggleComment` | Add/remove `'` prefix |

(Each binding uses `Cmd` instead of `Ctrl` on macOS.)

### 14.2 Preview Panels

Three distinct webview previews, each opening in a new editor column:

| Panel | Command | Endpoint |
|-------|---------|----------|
| **HTML Preview** | `previewHtml` | `/api/calcpad/convert` |
| **Unwrapped Preview** | `previewUnwrapped` | `/api/calcpad/convert-unwrapped` |
| **UI Preview** | `previewUi` | `/api/calcpad/convert-ui` |

All three panels:
- Re-render automatically on document change
- Apply the theme from `calcpad.previewTheme` (`light` / `dark` / `system`)
- Use `calcpad.darkBackground` (default `#1e1e1e`) for dark-mode backgrounds
- Embed local images as base64 (needed for PDF/print fidelity)
- Support the `View Webview Source` context-menu item for debugging

### 14.3 Export and Print Commands

| Command | Description |
|---------|-------------|
| `exportToPdf` | Full-fidelity PDF export via `/api/calcpad/pdf` with save dialog and 60-second timeout |
| `printToPdf` | Print-style PDF generated from the live webview |
| `viewWebviewSource` | Opens the HTML being rendered in a scratch editor for debugging |
| `insertImage` | File picker to insert `<img>` tag with relative path |

### 14.4 CalcPad Sidebar (Activity Bar)

A dedicated activity-bar view (`calcpadVueUI`) displays the document's:

- Macros (with parameters and defaults)
- User-defined functions (with return type and signature)
- Variables (with inferred type)
- Custom units

Powered by `/api/calcpad/definitions`. Includes **Refresh Document** and **Stop Server** buttons in the view title bar.

### 14.5 Server Lifecycle Management

The extension bundles a local `Calcpad.Server` instance and manages its lifecycle automatically:

- **Modes:** `calcpad.server.mode` = `auto` (default) | `local` | `remote`
- **URL:** `calcpad.server.url` (default `http://localhost:9420`)
- **dotnet path:** `calcpad.server.dotnetPath` (default `dotnet`)
- **Auto-start:** Local server launches in the background on activation
- **Auto-restart:** Up to 3 crash retries before requiring manual refresh
- **Health fallback:** If local start fails, the extension falls back to the configured remote URL
- **Clean shutdown:** Server process is terminated when the window closes (fix for orphaned `dotnet.exe` processes)
- **Manual control:** `calcpad.stopServer` command

### 14.6 Output Channels (Debug Logging)

Four independent VS Code output channels for troubleshooting:

| Channel | Purpose |
|---------|---------|
| **CalcPad Extension** | Extension lifecycle, command execution, errors |
| **Calcpad Output HTML** | Rendered HTML diagnostics |
| **Calcpad Webview Console** | `console.log` intercepted from preview webviews |
| **CalcPad Server Debug** | stdout/stderr from the spawned `Calcpad.Server` process |

### 14.7 Calculation & Plot Settings

All pass-through Calcpad settings are surfaced in VS Code configuration under `calcpad.settings`:

**Math:** `decimals` (0–15), `degrees` (0 radians / 1 degrees / 2 gradians), `isComplex`, `substitute`, `formatEquations`, `zeroSmallMatrixElements`, `maxOutputCount`, `formatString`.

**Plot:** `isAdaptive`, `screenScaleFactor`, `imagePath`, `imageUri`, `vectorGraphics`, `colorScale`, `smoothScale`, `shadows`, `lightDirection`.

These are forwarded to the server in every conversion request.

### 14.8 Library Path for Shared Files

`calcpad.libraryPath` points at a directory of reusable `.cpd`/`.txt` files. Files under that directory show up alongside workspace files in `#include` / `#read` path completion, letting teams share a common library without copying into each project.

### 14.9 Paste as Comment

`pasteAsComment` pastes clipboard content with each line prefixed by `'`, useful when copying text from docs or emails into a Calcpad document.

### 14.10 Editor Title and Context Menus

Quick-access icons in the editor title bar (top-right of a `.cpd` tab):

- Preview
- Unwrapped Preview
- UI Preview
- Insert
- Export PDF
- Insert Image

Webview title bar: **Print to PDF**, **View Webview Source**.

### 14.11 Editor Right-Click Menu

- **Print to PDF** (`calcpad` group, position 1)
- **View Webview Source** (`calcpad` group, position 2)

### 14.12 Built-in Color Themes

Two themes ship with the extension:

- **CalcPad Light**
- **CalcPad Dark**

Plus a debug theme (`CalcPad Token Test`) for semantic-token scope inspection.

### 14.13 Semantic Token Scope Mapping

`package.json` defines detailed `semanticTokenScopes` so the 31 Calcpad token types map cleanly onto standard VS Code highlighting roles — meaning third-party themes color `.cpd` files reasonably without additional configuration.

### 14.14 Operator Replacer and Auto-Indenter

Typing standard ASCII operators triggers automatic replacement with Unicode equivalents where appropriate (e.g. `<=` → `≤`, `>=` → `≥`, `!=` → `≠`). The auto-indenter handles `#if`/`#else`/`#end if`, `#for`/`#end for`, and `#def`/`#end def` block indentation.

### 14.15 Quick-Typer

The `~<key>` → symbol shortcut (Section 4.5) is driven by the same snippet registry that feeds autocomplete, so the mapping stays in sync across both features. Toggle with `calcpad.enableQuickTyping` (default `true`).

---

## 15. Bug Fixes

- **Include path stability** — fixed crashes from self-referential or deeply nested includes
- **Recursive include support** — includes now resolve recursively with proper cycle detection
- **Orphaned server cleanup** — fixed orphaned `dotnet.exe` processes not being terminated when VS Code closes
- **Tokenizer stability** — fixed multiple tokenizer crashes and edge cases
- **Macro parameter tokenization** — fixed tokenization of macro parameters across included files
- **API call UI freezing** — fixed long-running API calls blocking the VS Code UI (now properly async)
- **PDF plotting on Windows** — fixed plotting issues due to PuppeteerSharp and Edge limitations
- **Global variable scoping** — improved handling of global variables inside macro `#def` blocks
- **Large file reading stability** — improved stability when reading large files
- **Linter rebalancing** — fixed linter bugs around macro parameters; tokenizer is now the source of truth for the linter
