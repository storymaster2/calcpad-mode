# Keyword Arguments in Functions and Macros

> Calcpad.Web only (web editor and VS Code extension). Not available in the WPF desktop application.

Both custom functions and macros support optional parameters with default values, plus call-site keyword arguments of the form `name=value`.

## Function optional parameters

Define default values with `=` in the parameter list. Default expressions are stored verbatim and evaluated at **call time** in the global scope:

```text
f(x; y = 0; z = 1kg) = x * y + z
```

Required parameters must appear before optional ones.

## Function keyword argument calls

Functions can be called with `paramName=value`:

```text
f(x; y = 0; z = 1) = x * y + z

f(5; 3; 2)              ' positional only
f(x = 5; z = 2; y = 3)  ' all keyword, any order
f(5; z = 2)             ' x=5 (positional), y=0 (default), z=2 (keyword)
```

Rewriting happens before tokenization: `f(5; z = 2)` becomes `f(5; 0; 2)` for the evaluator.

## Macro optional parameters

Macros use the `$` suffix on names and parameters. Defaults are raw text substituted into the macro body:

```text
#def calc$(a$; b$ = 10; c$ = 5)
    result = a$ + b$ + c$
#end def
```

## Macro keyword argument calls

```text
calc$(1; 2; 3)                ' positional
calc$(a$ = 1; c$ = 7; b$ = 2) ' keyword, any order
calc$(1; c$ = 7)              ' positional then keyword
```

## Default expression evaluation

- **Functions** — defaults are expression source text; parsed and evaluated at each call in the global variable scope
- **Macros** — defaults are raw text substituted before parsing; evaluated in the context of the expansion
- **Defaults cannot reference other parameters** — each default is resolved independently

## Rules for mixing positional and keyword arguments

1. Required parameters must appear before optional parameters **in the definition**
2. Positional arguments must appear before keyword arguments **in a call**
3. Each parameter can only be supplied once (duplicate → error)
4. Missing required arguments → error
5. Missing optional arguments → default is used

## Linter error codes

| Code | Applies to | Condition |
|------|------------|-----------|
| `CPD-2213` | Macro definition | Required parameter after optional parameter |
| `CPD-3215` | Function definition | Required parameter after optional parameter |
| `CPD-3314` | Macro call | Unknown keyword argument name |
| `CPD-3315` | Function call | Unknown keyword argument name |

## VS Code autocomplete

Autocomplete inserts function/macro calls with parameter-name snippet placeholders (e.g. `f(${1:x}; ${2:y})`) and renders default values in the hover documentation:

```text
param y: Value (default: 0)
param z: Value (default: 1kg)
```

Required parameters display `(required)` when the function also has optional parameters.

## Limitations

- Built-in functions (`sin`, `cos`, `sqrt`, …) **do not** support keyword arguments or optional parameters
- Defaults cannot reference other parameters
- String variable expansion is not applied to default expressions
- Command-block functions (`$Inline`, `$Block`, `$While`) accept optional params in their definition but receive only positional arguments internally