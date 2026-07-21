# Calcpad.Server API Schema

This document describes the API endpoints for the Calcpad Server. The server provides endpoints for code conversion, syntax highlighting, linting, definitions, find-references, snippets, PDF generation, content resolution, caching, and optional authentication/user management.

## Base URLs

```
http://localhost:9420/api/calcpad   — Calcpad conversion, highlighting, linting, definitions, etc.
http://localhost:9420/api/auth      — Authentication (login, register, profile)
http://localhost:9420/api/user      — User management (admin only)
```

The default port is `9420` (configurable via the `CALCPAD_PORT` environment variable).

---

## Table of Contents

- [Calcpad Endpoints](#calcpad-endpoints)
  - [POST /convert](#post-convert)
  - [POST /convert-unwrapped](#post-convert-unwrapped)
  - [POST /convert-ui](#post-convert-ui)
  - [POST /debug-raw-code](#post-debug-raw-code)
  - [GET /sample](#get-sample)
  - [POST /highlight](#post-highlight)
  - [POST /highlight-line](#post-highlight-line)
  - [POST /lint](#post-lint)
  - [POST /definitions](#post-definitions)
  - [POST /find-references](#post-find-references)
  - [GET /snippets](#get-snippets)
  - [POST /pdf](#post-pdf)
  - [GET /pdf/health](#get-pdfhealth)
  - [POST /refresh-cache](#post-refresh-cache)
  - [POST /resolve-content](#post-resolve-content)
- [Auth Endpoints](#auth-endpoints)
  - [POST /login](#post-login)
  - [POST /register](#post-register)
  - [GET /profile](#get-profile)
- [User Endpoints](#user-endpoints-admin-only)
  - [GET /](#get-all-users)
  - [GET /{userId}](#get-user-by-id)
  - [PUT /{userId}](#update-user)
  - [DELETE /{userId}](#delete-user)
- [Shared Types](#shared-types)
- [Usage Notes](#usage-notes)

---

## Calcpad Endpoints

Base: `api/calcpad`

### POST /convert

Convert Calcpad source code to HTML. Processes macros, includes, and calculations.

**Request:**
```typescript
interface CalcpadRequest {
  content: string;                           // The Calcpad source code to convert
  settings?: Settings;                       // Optional Calcpad settings (math, plot, units, auth)
  forceUnwrappedCode?: boolean;              // If true, return code without calculation (default: false)
  theme?: string;                            // "light" or "dark" (default: "light")
  clientFileCache?: Record<string, string>;  // Optional client file cache (filename -> base64-encoded content)
  authSettings?: AuthSettings;               // Authentication settings for API routing
  apiTimeoutMs?: number;                     // Timeout for remote fetches in ms (default: 10000)
  sourceFilePath?: string;                   // Full path of source file on client (for resolving relative paths)
}
```

**Response:** HTML content (`text/html`)

**Example Request with Client File Cache:**
```json
{
  "content": "#include helper.cpd\na = helperFunc(5)",
  "clientFileCache": {
    "helper.cpd": "aGVscGVyRnVuYyh4KSA9IHggKiAy"
  }
}
```

The base64 content above decodes to: `helperFunc(x) = x * 2`

---

### POST /convert-unwrapped

Convert Calcpad source code to HTML without calculation (shows raw code with syntax highlighting). Automatically processes `data-text` links to make them functional.

**Request:** Same as `/convert` (uses `CalcpadRequest`)

**Response:** HTML content (`text/html`)

---

### POST /convert-ui

Convert Calcpad source code to HTML with UI input support. Enables interactive variable override inputs in the preview.

**Request:**
```typescript
interface CalcpadUiRequest extends CalcpadRequest {
  uiOverrides?: Record<string, string>;  // Maps variable names to override values for UI input fields
}
```

**Response:** HTML content (`text/html`)

---

### POST /debug-raw-code

Get the raw code output from the macro parser (for debugging macro expansion).

**Request:** Same as `/convert` (uses `CalcpadRequest`, only `content` is used)

**Response:** Plain text (`text/plain`)

---

### GET /sample

Get a sample Calcpad source code document.

**Response:**
```typescript
interface CalcpadRequest {
  content: string;  // Sample Calcpad source code
  // ... other fields at defaults
}
```

---

## Syntax Highlighter Endpoints

### POST /highlight

Tokenize Calcpad source code for syntax highlighting. Supports include file resolution for accurate macro-aware tokenization.

**Request:**
```typescript
interface HighlightRequest {
  content: string;                           // The Calcpad source code to tokenize
  includeText?: boolean;                     // Whether to include token text in response (default: false)
  includeFiles?: Record<string, string>;     // Optional dictionary of include file contents (filename -> content)
  clientFileCache?: Record<string, string>;  // Optional client file cache (filename -> base64-encoded content)
  sourceFilePath?: string;                   // Full path of source file on client (for resolving relative paths)
}
```

**Response:**
```typescript
interface HighlightResponse {
  tokens: HighlightToken[];
}

interface HighlightToken {
  line: number;      // Zero-based line number
  column: number;    // Zero-based column (character offset from start of line)
  length: number;    // Length of the token in characters
  type: string;      // Token type name for display/debugging
  typeId: number;    // Token type ID for efficient processing
  text?: string;     // Actual token text (only if includeText is true)
}
```

**Token Types (typeId):**
| ID | Type | Description |
|----|------|-------------|
| 0 | None | Whitespace or unknown content |
| 1 | Const | Numeric constants (e.g., 123, 3.14, 1e-5) |
| 2 | Units | Unit identifiers (e.g., m, kg, N/m^2) |
| 3 | Operator | Operators (e.g., +, -, *, /, =) |
| 4 | Variable | Variable identifiers |
| 5 | Function | Function names (built-in or user-defined) |
| 6 | Keyword | Keywords starting with # (e.g., #if, #else, #def) |
| 7 | Command | Commands starting with $ (e.g., $Plot, $Root, $Sum) |
| 8 | Bracket | Brackets: (), [], {} |
| 9 | Comment | Comments enclosed in ' or " |
| 10 | Tag | HTML tags within comments |
| 11 | Input | Input markers (? or #{...}) |
| 12 | Include | Include file paths |
| 13 | Macro | Macro names and parameters (ending with $) |
| 14 | HtmlComment | HTML comments |
| 15 | Format | Format specifiers (e.g., :f2, :e3) |
| 16 | LocalVariable | Local variables scoped to expressions (function params, #for vars, command scope vars) |
| 17 | FilePath | File paths in data exchange keywords (#read, #write, #append) |
| 18 | DataExchangeKeyword | Sub-keywords in data exchange statements (from, to, sep, type) |

**Example Request:**
```json
{
  "content": "a = 5*m\nb = sin(45)",
  "includeText": true
}
```

**Example Response:**
```json
{
  "tokens": [
    { "line": 0, "column": 0, "length": 1, "type": "Variable", "typeId": 4, "text": "a" },
    { "line": 0, "column": 2, "length": 1, "type": "Operator", "typeId": 3, "text": "=" },
    { "line": 0, "column": 4, "length": 1, "type": "Const", "typeId": 1, "text": "5" },
    { "line": 0, "column": 5, "length": 1, "type": "Operator", "typeId": 3, "text": "*" },
    { "line": 0, "column": 6, "length": 1, "type": "Units", "typeId": 2, "text": "m" },
    { "line": 1, "column": 0, "length": 1, "type": "Variable", "typeId": 4, "text": "b" },
    { "line": 1, "column": 2, "length": 1, "type": "Operator", "typeId": 3, "text": "=" },
    { "line": 1, "column": 4, "length": 3, "type": "Function", "typeId": 5, "text": "sin" },
    { "line": 1, "column": 7, "length": 1, "type": "Bracket", "typeId": 8, "text": "(" },
    { "line": 1, "column": 8, "length": 2, "type": "Const", "typeId": 1, "text": "45" },
    { "line": 1, "column": 10, "length": 1, "type": "Bracket", "typeId": 8, "text": ")" }
  ]
}
```

---

### POST /highlight-line

Tokenize a single line of Calcpad source code (for incremental updates).

**Request:**
```typescript
interface HighlightLineRequest {
  line: string;          // The line content to tokenize
  lineNumber?: number;   // Zero-based line number (default: 0)
  includeText?: boolean; // Whether to include token text (default: false)
}
```

**Response:** Same as `/highlight`

---

## Linter Endpoint

### POST /lint

Lint Calcpad source code and return diagnostics (errors, warnings, and informational messages). Supports lint-ignore regions via comments.

**Request:**
```typescript
interface LintRequest {
  content: string;                           // The Calcpad source code to lint
  includeFiles?: Record<string, string>;     // Optional dictionary of include file contents (filename -> content)
  clientFileCache?: Record<string, string>;  // Optional client file cache (filename -> base64-encoded content)
  authSettings?: AuthSettings;               // Authentication settings for API routing
  apiTimeoutMs?: number;                     // Timeout for remote fetches in ms (default: 10000)
  sourceFilePath?: string;                   // Full path of source file on client
}
```

**Response:**
```typescript
interface LintResponse {
  errorCount: number;           // Total number of errors
  warningCount: number;         // Total number of warnings
  diagnostics: LintDiagnostic[];
}

interface LintDiagnostic {
  line: number;        // Zero-based line number
  column: number;      // Zero-based column (start position)
  endColumn: number;   // Zero-based end column position
  code: string;        // Error code (e.g., "CPD-3301")
  message: string;     // Human-readable error/warning message
  severity: string;    // Severity name: "error", "warning", or "information"
  severityId: number;  // Severity ID: 0=Error, 1=Warning, 2=Information
  source: string;      // Source of the diagnostic (default: "Calcpad Linter")
}
```

**Error Codes:**

| Code | Category | Description |
|------|----------|-------------|
| **Stage 1: Pre-include validation (CPD-11xx)** |||
| CPD-1101 | Include | Malformed #include statement |
| CPD-1102 | Include | Invalid #include file path |
| CPD-1103 | Include | Missing #include filename |
| **Stage 2: Macro definitions (CPD-22xx)** |||
| CPD-2201 | Macro | Duplicate macro definition |
| CPD-2202 | Macro | Macro name must end with '$' |
| CPD-2203 | Macro | Macro parameter must end with '$' |
| CPD-2204 | Macro | Invalid macro name |
| CPD-2205 | Macro | Malformed #def syntax |
| CPD-2206 | Macro | Unmatched #def or #end def |
| CPD-2207 | Macro | Nested macro definition not allowed |
| CPD-2208 | Macro | Macro parameter must start with a letter |
| CPD-2209 | Macro | Macro definition inside control block |
| **Stage 3: Balance (CPD-31xx)** |||
| CPD-3101 | Balance | Unmatched opening parenthesis |
| CPD-3102 | Balance | Unmatched closing parenthesis |
| CPD-3103 | Balance | Unmatched opening square bracket |
| CPD-3104 | Balance | Unmatched closing square bracket |
| CPD-3105 | Balance | Unmatched opening curly brace/control block |
| CPD-3106 | Balance | Unmatched closing curly brace |
| **Stage 3: Naming (CPD-32xx)** |||
| CPD-3201 | Naming | Invalid variable name |
| CPD-3202 | Naming | Variable name conflicts with built-in function |
| CPD-3203 | Naming | Invalid function name |
| CPD-3204 | Naming | Function name conflicts with built-in function |
| CPD-3205 | Naming | Variable name conflicts with keyword |
| **Stage 3: Usage (CPD-33xx)** |||
| CPD-3301 | Usage | Undefined variable |
| CPD-3302 | Usage | Function called with incorrect parameter count |
| CPD-3303 | Usage | Undefined macro |
| CPD-3304 | Usage | Macro called with incorrect parameter count |
| CPD-3305 | Usage | Undefined function |
| CPD-3306 | Usage | Invalid element access |
| CPD-3307 | Usage | Too few parameters |
| CPD-3308 | Usage | Too many parameters |
| CPD-3309 | Usage | Parameter type mismatch (warning) |
| **Stage 3: Semantic (CPD-34xx)** |||
| CPD-3401 | Semantic | Invalid operator usage |
| CPD-3402 | Semantic | Mismatched operator |
| CPD-3403 | Semantic | Command must be at start of statement |
| CPD-3404 | Semantic | Invalid command syntax |
| CPD-3405 | Semantic | Invalid control structure syntax |
| CPD-3406 | Semantic | Unknown directive |
| CPD-3407 | Semantic | Invalid assignment |
| CPD-3408 | Semantic | Invalid CustomUnit syntax |

**Example Request:**
```json
{
  "content": "a = 5\nb = unknownVar\nc = sin()"
}
```

**Example Response:**
```json
{
  "errorCount": 2,
  "warningCount": 0,
  "diagnostics": [
    {
      "line": 1,
      "column": 4,
      "endColumn": 14,
      "code": "CPD-3301",
      "message": "Undefined variable: 'unknownVar'",
      "severity": "error",
      "severityId": 0,
      "source": "Calcpad Linter"
    },
    {
      "line": 2,
      "column": 4,
      "endColumn": 9,
      "code": "CPD-3307",
      "message": "Too few parameters: 'sin' requires at least 1 parameter(s), got 0",
      "severity": "error",
      "severityId": 0,
      "source": "Calcpad Linter"
    }
  ]
}
```

---

## Definitions Endpoint

### POST /definitions

Get detailed definitions (macros, functions, variables, custom units) from Calcpad source code. Returns type information, parameters, return types, source locations, and metadata from doc comments.

**Request:**
```typescript
interface DefinitionsRequest {
  content: string;                           // The Calcpad source code to analyze
  includeFiles?: Record<string, string>;     // Optional dictionary of include file contents (filename -> content)
  clientFileCache?: Record<string, string>;  // Optional client file cache (filename -> base64-encoded content)
  authSettings?: AuthSettings;               // Authentication settings for API routing
  apiTimeoutMs?: number;                     // Timeout for remote fetches in ms (default: 10000)
  sourceFilePath?: string;                   // Full path of source file on client
}
```

**Response:**
```typescript
interface DefinitionsResponse {
  macros: MacroDefinitionDto[];
  functions: FunctionDefinitionDto[];
  variables: VariableDefinitionDto[];
  customUnits: CustomUnitDefinitionDto[];
  uiOverrides?: PersistedUiOverridesDto;     // Persisted UI overrides from HTML comment blocks
}

interface MacroDefinitionDto {
  name: string;                    // Macro name (including $ suffix)
  parameters: string[];            // Parameter names
  isMultiline: boolean;            // True if multiline macro (#def...#end def), false if inline
  content: string[];               // Macro content lines
  lineNumber: number;              // Zero-based line number where defined
  source: string;                  // "local" or "include"
  sourceFile?: string;             // Source file path if from include
  description?: string;            // User-provided description from a metadata comment
  paramTypes?: string[];           // User-provided type hints per parameter
  paramDescriptions?: string[];    // User-provided descriptions per parameter
  defaults?: (string | null)[];    // Default values parallel to parameters (null = required)
}

interface FunctionDefinitionDto {
  name: string;                       // Function name
  parameters: string[];               // Parameter names
  expression?: string;                // Function body expression (right side of =)
  returnType: string;                 // Inferred return type name
  returnTypeId: number;               // Return type ID for efficient processing
  hasCommandBlock: boolean;           // True if uses $Inline, $Block, or $While
  commandBlockType?: string;          // "Inline", "Block", or "While" (if applicable)
  commandBlockStatements?: string[];  // Statements inside command block (if applicable)
  lineNumber: number;                 // Zero-based line number where defined
  source: string;                     // "local" or "include"
  sourceFile?: string;                // Source file path if from include
  description?: string;               // User-provided description from a metadata comment
  paramTypes?: string[];              // User-provided type hints per parameter
  paramDescriptions?: string[];       // User-provided descriptions per parameter
  defaults?: (string | null)[];       // Default values parallel to parameters (null = required)
}

interface VariableDefinitionDto {
  name: string;          // Variable name
  expression?: string;   // Initial expression (right side of first assignment)
  type: string;          // Inferred type name
  typeId: number;        // Type ID for efficient processing
  lineNumber: number;    // Zero-based line number where first defined
  source: string;        // "local" or "include"
  sourceFile?: string;   // Source file path if from include
  description?: string;  // User-provided description from a metadata comment
}

interface CustomUnitDefinitionDto {
  name: string;          // Unit name (without leading dot)
  expression?: string;   // Unit definition expression
  lineNumber: number;    // Zero-based line number where defined
  source: string;        // "local" or "include"
  sourceFile?: string;   // Source file path if from include
}

interface PersistedUiOverridesDto {
  overrides: Record<string, string>;  // Variable name to override value mapping
  commentLine: number;                // Zero-based line number of the HTML comment block
}
```

**Type IDs (typeId / returnTypeId):**
| ID | Type | Description |
|----|------|-------------|
| 0 | Unknown | Type could not be determined |
| 1 | Value | Scalar numeric value |
| 2 | Vector | Vector (1D array) |
| 3 | Matrix | Matrix (2D array) |
| 4 | StringVariable | String value |
| 5 | Various | Type varies (assigned different types in different places) |
| 6 | Function | Function type |
| 7 | InlineMacro | Inline macro |
| 8 | MultilineMacro | Multiline macro |
| 9 | CustomUnit | Custom unit definition |

**Example Request:**
```json
{
  "content": "#def double$(x$) = 2*x$\nmyFunc(a; b) = a + b\nvec = [1; 2; 3]\n.ksi = 1000*psi"
}
```

**Example Response:**
```json
{
  "macros": [
    {
      "name": "double$",
      "parameters": ["x$"],
      "isMultiline": false,
      "content": ["2*x$"],
      "lineNumber": 0,
      "source": "local",
      "sourceFile": null,
      "description": null,
      "paramTypes": null,
      "paramDescriptions": null,
      "defaults": null
    }
  ],
  "functions": [
    {
      "name": "myFunc",
      "parameters": ["a", "b"],
      "expression": "a + b",
      "returnType": "Value",
      "returnTypeId": 1,
      "hasCommandBlock": false,
      "commandBlockType": null,
      "commandBlockStatements": null,
      "lineNumber": 1,
      "source": "local",
      "sourceFile": null,
      "description": null,
      "paramTypes": null,
      "paramDescriptions": null,
      "defaults": null
    }
  ],
  "variables": [
    {
      "name": "vec",
      "expression": "[1; 2; 3]",
      "type": "Vector",
      "typeId": 2,
      "lineNumber": 2,
      "source": "local",
      "sourceFile": null,
      "description": null
    }
  ],
  "customUnits": [
    {
      "name": "ksi",
      "expression": "1000*psi",
      "lineNumber": 3,
      "source": "local",
      "sourceFile": null
    }
  ],
  "uiOverrides": null
}
```

**Command Block Functions Example:**

Note: Command blocks use function syntax like `if()`, `$Repeat{}`, etc. instead of `#if`, `#for` directives.

```json
{
  "content": "filterVec(v; val) = $Inline{result = vector(0); $Repeat{result = if(v.(i) > val; join(result; v.(i)); result) @ i = 1 : len(v)}; result}"
}
```

Response includes:
```json
{
  "functions": [
    {
      "name": "filterVec",
      "parameters": ["v", "val"],
      "expression": "$Inline{result = vector(0); ...}",
      "returnType": "Vector",
      "returnTypeId": 2,
      "hasCommandBlock": true,
      "commandBlockType": "Inline",
      "commandBlockStatements": [
        "result = vector(0)",
        "$Repeat{result = if(v.(i) > val; join(result; v.(i)); result) @ i = 1 : len(v)}",
        "result"
      ],
      "lineNumber": 0,
      "source": "local",
      "sourceFile": null,
      "description": null,
      "paramTypes": null,
      "paramDescriptions": null,
      "defaults": null
    }
  ]
}
```

---

## Find References Endpoint

### POST /find-references

Get all symbol occurrence locations (definitions, reassignments, and usages) for go-to-definition and find-all-references features. Returns dictionaries mapping symbol names to all their occurrences with original source line positions.

**Request:** Same as `/definitions` (uses `DefinitionsRequest`)

**Response:**
```typescript
interface FindReferencesResponse {
  variables: Record<string, SymbolLocationDto[]>;  // Variable name -> occurrences
  functions: Record<string, SymbolLocationDto[]>;  // Function name -> occurrences
  macros: Record<string, SymbolLocationDto[]>;     // Macro name -> occurrences
}

interface SymbolLocationDto {
  line: number;          // Zero-based line number (mapped back through all pipeline stages)
  column: number;        // Zero-based column
  length: number;        // Token length in characters
  source: string;        // "local" or "include"
  sourceFile?: string;   // File path if from an #include, null otherwise
  isAssignment: boolean; // True for definitions and reassignments, false for read-only usages
}
```

**Example Request:**
```json
{
  "content": "a = 5\nb = a + 1\nc = a * b"
}
```

**Example Response:**
```json
{
  "variables": {
    "a": [
      { "line": 0, "column": 0, "length": 1, "source": "local", "sourceFile": null, "isAssignment": true },
      { "line": 1, "column": 4, "length": 1, "source": "local", "sourceFile": null, "isAssignment": false },
      { "line": 2, "column": 4, "length": 1, "source": "local", "sourceFile": null, "isAssignment": false }
    ],
    "b": [
      { "line": 1, "column": 0, "length": 1, "source": "local", "sourceFile": null, "isAssignment": true },
      { "line": 2, "column": 8, "length": 1, "source": "local", "sourceFile": null, "isAssignment": false }
    ],
    "c": [
      { "line": 2, "column": 0, "length": 1, "source": "local", "sourceFile": null, "isAssignment": true }
    ]
  },
  "functions": {},
  "macros": {}
}
```

---

## Snippets Endpoint

### GET /snippets

Get all available snippets for autocomplete/intellisense. Returns snippet definitions with insert text, descriptions, categories, and parameter info.

**Query Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| category | string | Optional. Filter snippets by category prefix (e.g., "Functions", "Functions/Trigonometric") |

**Response:**
```typescript
interface SnippetsResponse {
  count: number;           // Total number of snippets returned
  snippets: SnippetDto[];  // Array of snippet definitions
}

interface SnippetDto {
  insert: string;                  // Text to insert (use '§' as cursor placeholder)
  description: string;             // Description shown in tooltips
  label?: string;                  // Optional display label (defaults to description)
  category: string;                // Category path (e.g., "Functions/Trigonometric")
  quickType?: string;              // Quick typing shortcut without ~ prefix (e.g., "a" means ~a -> insert)
  parameters?: SnippetParameterDto[]; // Parameter info for functions (null for non-functions)
}

interface SnippetParameterDto {
  name: string;         // Parameter name (e.g., "x", "M", "v")
  description?: string; // Description of the parameter's purpose
}
```

**Snippet Categories:**
| Category | Description |
|----------|-------------|
| Constants | Mathematical constants (e, pi, etc.) |
| Operators | Arithmetic and comparison operators |
| Functions/Trigonometric | sin, cos, tan, etc. |
| Functions/Hyperbolic | sinh, cosh, tanh, etc. |
| Functions/Exponential | exp, ln, log, etc. |
| Functions/Rounding | round, floor, ceil, trunc |
| Functions/Aggregate | min, max, sum, average, etc. |
| Functions/Conditional | if, switch, and, or, not |
| Functions/Other | abs, sign, random, etc. |
| Functions/Vector | len, range, join, fill, etc. |
| Functions/Matrix | matrix, identity, transpose, etc. |
| Program Flow Control | #if, #else, #for, #while, etc. |
| Modules and Macros | #include, #def, #local |
| Commands | $Plot, $Root, $Sum, etc. |
| Units | Length, mass, time units |

**Example Request:**
```
GET /api/calcpad/snippets
GET /api/calcpad/snippets?category=Functions/Trigonometric
```

**Example Response:**
```json
{
  "count": 3,
  "snippets": [
    {
      "insert": "sin(§)",
      "description": "Sine of angle in radians",
      "label": null,
      "category": "Functions/Trigonometric",
      "quickType": null,
      "parameters": [
        { "name": "x", "description": "Angle in radians" }
      ]
    },
    {
      "insert": "min(§; §)",
      "description": "Minimum of multiple scalar values",
      "label": null,
      "category": "Functions/Aggregate",
      "quickType": null,
      "parameters": [
        { "name": "values", "description": "Scalar values" }
      ]
    },
    {
      "insert": "#if",
      "description": "Conditional block",
      "label": null,
      "category": "Program Flow Control",
      "quickType": null,
      "parameters": null
    }
  ]
}
```

---

## PDF Endpoints

### POST /pdf

Generate a PDF from HTML content using Playwright browser automation and PDFsharp.

**Request:**
```typescript
interface PdfGenerateRequest {
  html: string;              // HTML content to convert to PDF (required)
  browserPath?: string;      // Custom browser executable path
  options?: PdfOptions;      // PDF generation settings
}

interface PdfOptions {
  // Page settings
  format?: string;           // Page format (default: "A4")
  orientation?: string;      // "portrait" or "landscape" (default: "portrait")
  printBackground?: boolean; // Print background graphics (default: true)
  scale?: number;            // Scale factor (default: 1.0)

  // Margins
  marginTop?: string;        // Top margin (default: "2cm")
  marginRight?: string;      // Right margin (default: "1.5cm")
  marginBottom?: string;     // Bottom margin (default: "2cm")
  marginLeft?: string;       // Left margin (default: "1.5cm")

  // Headers and footers
  enableHeader?: boolean;    // Enable page header
  enableFooter?: boolean;    // Enable page footer

  // Document metadata
  documentTitle?: string;    // Document title
  documentSubtitle?: string; // Document subtitle
  author?: string;           // Author name
  company?: string;          // Company name
  project?: string;          // Project name

  // Custom content
  headerCenter?: string;     // Custom header center content
  footerCenter?: string;     // Custom footer center content

  // Timestamp format (null/empty uses system default)
  dateTimeFormat?: string;

  // Background PDF (base64-encoded or file path)
  backgroundPdf?: string;
}
```

**Response:** PDF binary (`application/pdf`, filename: `document.pdf`)

---

### GET /pdf/health

Health check for the PDF generation service.

**Response:**
```json
{
  "status": "ok",
  "service": "calcpad-pdf",
  "version": "2.0.0"
}
```

---

## Cache Endpoint

### POST /refresh-cache

Clear or selectively invalidate the server-side remote content cache (used for API responses and URL fetches).

**Request:**
```typescript
interface RefreshCacheRequest {
  key?: string;       // Single cache key (URL or API route) to invalidate
  keys?: string[];    // Multiple cache keys to invalidate (takes precedence over key)
  // If both null/empty, clears entire cache
}
```

**Response:**
```json
{ "status": "ok" }
```

---

## Content Resolution Endpoint

### POST /resolve-content

Pre-fetch remote content and resolve includes/macros for Calcpad source code. Returns the staged content resolution result.

**Request:**
```typescript
interface ContentResolverRequest {
  content: string;                           // The Calcpad source code to resolve
  authSettings?: AuthSettings;               // Authentication settings for API routing
  apiTimeoutMs?: number;                     // Timeout for remote fetches in ms (default: 10000)
  staged?: boolean;                          // Whether to return staged content (default: false)
  includeFiles?: Record<string, string>;     // Optional dictionary of include file contents
  clientFileCache?: Record<string, string>;  // Optional client file cache (base64-encoded)
  sourceFilePath?: string;                   // Full path of source file on client
}
```

**Response:** Content resolution result (JSON)

---

## Auth Endpoints

Base: `api/auth`

Authentication is optional and must be enabled via the `Auth:Enabled=true` configuration. When disabled, all auth endpoints return `404`.

### POST /login

Authenticate a user and receive a JWT token.

**Request:**
```typescript
interface LoginRequest {
  username: string;  // Required
  password: string;  // Required
}
```

**Response:**
```typescript
interface AuthResponse {
  token: string;      // JWT bearer token
  user: UserDto;      // User profile
  expiresAt: string;  // Token expiry (ISO 8601)
}

interface UserDto {
  id: string;             // GUID
  username: string;
  email: string;
  role: UserRole;         // 1=Viewer, 2=Contributor, 3=Admin
  createdAt: string;      // ISO 8601
  lastLoginAt?: string;   // ISO 8601 or null
  isActive: boolean;
}
```

**Status Codes:**
| Code | Description |
|------|-------------|
| 200 | Successful login |
| 400 | Username and password are required |
| 401 | Invalid username or password |
| 404 | Auth is not enabled |

---

### POST /register

Register a new user. **Requires Admin role.**

**Request:**
```typescript
interface RegisterRequest {
  username: string;     // Required, 3-30 characters
  email: string;        // Required, must contain @
  password: string;     // Required, minimum 6 characters
  role?: UserRole;      // Optional (1=Viewer, 2=Contributor, 3=Admin)
}
```

**Response:** `AuthResponse` (201 Created)

**Status Codes:**
| Code | Description |
|------|-------------|
| 201 | User registered successfully |
| 400 | Validation error (username, email, password, or role) |
| 401 | Not authenticated |
| 403 | Not authorized (non-admin) |
| 404 | Auth is not enabled |
| 409 | Username or email already exists |

---

### GET /profile

Get the current authenticated user's profile. **Requires authentication.**

**Response:** `UserDto`

**Status Codes:**
| Code | Description |
|------|-------------|
| 200 | Profile returned |
| 401 | Not authenticated |
| 404 | Auth not enabled or user not found |

---

## User Endpoints (Admin Only)

Base: `api/user`

All user management endpoints require Admin role authentication.

### GET / (All Users)

Get all registered users.

**Response:** `UserDto[]`

---

### GET /{userId}

Get a specific user by ID.

**Response:** `UserDto`

**Status Codes:**
| Code | Description |
|------|-------------|
| 200 | User returned |
| 404 | Auth not enabled or user not found |

---

### PUT /{userId}

Update a user's role or active status.

**Request:**
```typescript
interface UpdateUserRequest {
  role?: UserRole;     // Optional new role (1=Viewer, 2=Contributor, 3=Admin)
  isActive?: boolean;  // Optional active status
}
```

**Response:**
```json
{ "message": "User updated" }
```

**Status Codes:**
| Code | Description |
|------|-------------|
| 200 | User updated |
| 400 | Invalid role |
| 404 | Auth not enabled or user not found |

---

### DELETE /{userId}

Delete a user.

**Response:**
```json
{ "message": "User deleted" }
```

**Status Codes:**
| Code | Description |
|------|-------------|
| 200 | User deleted |
| 404 | Auth not enabled or user not found |

---

## Shared Types

### AuthSettings

Used across multiple endpoints for API routing authentication.

```typescript
interface AuthSettings {
  jwt?: string;                  // JWT token for authenticated API calls
  routingConfig?: RoutingConfig; // API routing configuration
}

// RoutingConfig is a dictionary mapping service names to their config
type RoutingConfig = Record<string, ServiceConfig>;

interface ServiceConfig {
  baseUrl?: string;                       // Base URL for the service
  auth?: string;                          // Auth type ("jwt" or null)
  endpoints?: Record<string, string>;     // Endpoint name -> URL template
}
```

### UserRole

```typescript
enum UserRole {
  Viewer = 1,
  Contributor = 2,
  Admin = 3
}
```

---

## Usage Notes

1. **Line and column numbers are zero-based** - The first line is line 0, and the first character is column 0.

2. **Include files** - For the linter, highlighter, and definitions endpoints to properly validate code with `#include` statements, pass the include file contents in the `includeFiles` dictionary. The key should match the filename used in the `#include` statement.

3. **Client file cache** - An alternative to `includeFiles` for resolving `#include` and `#read` directives. Files are passed as base64-encoded content, which is useful when the client has files cached in memory that the server cannot access directly. The cache is checked after `includeFiles` - if a file exists in both, `includeFiles` takes precedence.

4. **Source file path** - Pass `sourceFilePath` when the client knows the full path of the source file. This is used to resolve relative `#include` and `#read` paths so they match the client's cache keys.

5. **Token positions** - For syntax highlighting, use `column` and `length` to determine the exact span of each token for colorization.

6. **Error ranges** - For the linter, use `column` and `endColumn` to underline or highlight the problematic code region.

7. **Incremental updates** - Use `/highlight-line` for real-time syntax highlighting as the user types, then periodically call `/lint` for full validation.

8. **Authentication** - Auth is optional. When enabled, pass a JWT Bearer token in the `Authorization` header for protected endpoints. The `/api/calcpad/*` endpoints do not require authentication. For remote content fetching, pass credentials via `authSettings` in the request body.

9. **PDF generation** - Use `/convert` to get HTML first, then pass it to `/pdf` to generate a PDF. Check `/pdf/health` to verify the service is available.

10. **Content caching** - The server caches remote content fetched via URLs and API routes. Use `/refresh-cache` to invalidate stale entries.

---

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `CALCPAD_PORT` | `9420` | Server port |
| `CALCPAD_HOST` | `0.0.0.0` | Server host |
| `CALCPAD_ENABLE_HTTPS` | (unset) | Set to `true` to enable HTTPS |
| `Auth:Enabled` | (unset) | Set to `true` to enable authentication |
| `Auth:DatabasePath` | `data/users.db` | Path to SQLite user database |
