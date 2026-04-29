# Linter and Diagnostics

> Calcpad.Web only (web editor and VS Code extension). Not available in the WPF desktop application.

The web backend exposes a semantic linter that emits diagnostics with stable `CPD-XXXX` codes. Diagnostics surface in VS Code as standard editor warnings/errors and in the web editor as inline marks.

## Error code conventions

All codes follow the pattern `CPD-SCNN`:

- `S` — pipeline stage (1 pre-include, 2 macro, 3 post-include)
- `C` — category within the stage
- `NN` — sequence number

## Stage 1: Include validation (CPD-11xx)

| Code | Severity | Description |
|------|----------|-------------|
| CPD-1101 | Error | Malformed `#include` statement |
| CPD-1102 | Error | Missing `#include` filename |

## Stage 2: Macro definitions (CPD-22xx)

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

## Stage 3: Balance (CPD-31xx)

| Code | Severity | Description |
|------|----------|-------------|
| CPD-3101 | Error | Unmatched opening parenthesis |
| CPD-3102 | Error | Unmatched closing parenthesis |
| CPD-3103 | Error | Unmatched opening square bracket |
| CPD-3104 | Error | Unmatched closing square bracket |
| CPD-3105 | Error | Unmatched opening curly brace or control block |
| CPD-3106 | Error | Unmatched closing curly brace |

## Stage 3: Naming (CPD-32xx)

| Code | Severity | Description |
|------|----------|-------------|
| CPD-3201 | Error | Invalid variable name (must start with a letter) |
| CPD-3203 | Error | Invalid function name |
| CPD-3204 | Error | Function name conflicts with built-in function |
| CPD-3205 | Error | Variable name conflicts with a keyword |
| CPD-3207 | Error | Variable name conflicts with a built-in constant |
| CPD-3208 | Error | Function must have at least one parameter |
| CPD-3215 | Error | Required parameter after optional parameter (function) |

## Stage 3: Usage (CPD-33xx)

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

## Stage 3: Semantic (CPD-34xx)

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

## Stage 3: Format (CPD-36xx)

| Code | Severity | Description |
|------|----------|-------------|
| CPD-3601 | Warning | Invalid format specifier |

## Lint-ignore regions

Suppress specific diagnostics within a region using paired HTML-comment markers:

```text
'<!--{"LintIgnore": ["CPD-3301"]}-->
prototype_var = 5
'<!--{"EndLintIgnore": []}-->
```

An empty `LintIgnore` array suppresses all codes within the region.

## Severity filtering (VS Code)

The setting `calcpad.linter.minimumSeverity` (`error` | `warning` | `information`, default `information`) filters lower-severity diagnostics before they reach the editor.

## Lint response shape

The `/api/calcpad/lint` endpoint returns:

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