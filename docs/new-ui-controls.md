# Interactive UI Controls (`#UI`)

> Calcpad.Web only (web editor and VS Code extension). Not available in the WPF desktop application.

The `#UI` directive turns a variable assignment into an interactive control in the preview panel. When the user edits a value, the calculation re-runs automatically with the updated input. Five control types are supported: **entry**, **datagrid**, **dropdown**, **radio**, and **checkbox**.

## Entry fields (scalar inputs)

Place `#UI` before any scalar variable assignment to expose its value as an editable text input:

```text
#UI L = 10m
#UI W = 5m
A = L * W
```

`L` and `W` appear as editable text fields. Changing either value recalculates `A` in real time. The unit suffix (e.g. `m`) is preserved when the numeric value is overridden.

## Datagrid (matrix/vector editor)

When the right-hand side is a matrix or vector, `#UI` creates a spreadsheet-style grid editor:

```text
#UI v = [1; 2; 3]              ' 1×3 vector
#UI M = [1|2|3; 4|5|6]         ' 2×3 matrix
#UI Z = vector(5)              ' 1×5 grid of zeros
#UI G = matrix(3; 4)           ' 3×4 grid of zeros
```

Grid dimensions are auto-detected from the RHS. Pipe `|` separates rows, semicolon `;` separates cells.

## Dropdowns, radios, and checkboxes

Three additional control types are exposed via the `type` JSON property:

```text
#UI {"type": "dropdown", "keys": ["Low", "Med", "High"], "values": ["1", "2", "3"]} grade = 1
#UI {"type": "radio",    "keys": ["Steel", "Concrete"], "values": ["200GPa", "25GPa"]} E = 200GPa
#UI {"type": "checkbox"} flag = 1
```

- `keys` — display labels shown to the user
- `values` — underlying values substituted back into the calculation
- `keys` and `values` must have matching lengths
- Checkbox sends `1` when checked, `0` when unchecked

## JSON configuration properties

| Property | Type | Applies to | Description |
|----------|------|------------|-------------|
| `type` | string | all | `"entry"`, `"datagrid"`, `"dropdown"`, `"radio"`, `"checkbox"` (auto-detected if omitted) |
| `mode` | string | all | `"string"` or `"number"` (auto-detected from `$` suffix) |
| `style` | string | all | CSS class name(s) applied to the rendered control |
| `rows` | number | datagrid | Explicit row count (auto-detected if omitted) |
| `columns` | number | datagrid | Explicit column count (auto-detected if omitted) |
| `columnHeaders` | string[] | datagrid | Custom column labels |
| `rowHeaders` | string[] | datagrid | Custom row labels |
| `keys` | string[] | dropdown/radio | Display labels (**required**) |
| `values` | string[] | dropdown/radio | Underlying values (**required**) |

Unknown properties are silently ignored.

## String variable expansion for `#UI` config

JSON configuration can be stored in a string variable and referenced in the `#UI` directive:

```text
#string UIJSON$ = '{"type": "entry", "style": "highlight"}'
#UI UIJSON$ L = 10m
```

The string variable is expanded before JSON parsing, allowing shared UI configurations across many variables.

## Conditional UI fields

UI fields work inside `#if`, `#else if`, `#else`, `#end if` blocks. When the branch is inactive the UI control is not rendered:

```text
#UI material$ = 'Steel'
#if material$ ≡ 'Steel'
    #UI E = 200GPa
#else if material$ ≡ 'Concrete'
    #UI E = 25GPa
#end if
```

## Override persistence

The VS Code extension persists UI override values as an HTML comment block at the top of the file so state survives close/reopen:

```text
'<!--{"uiOverrides": {"L": "12", "W": "4"}}-->
```

## String mode (`#UI` as alternative to `#string`)

Because only one `#xxx` keyword can prefix a given line, `#UI` doubles as a replacement for `#string` whenever the LHS is a string variable (name ending with `$`). The `mode` JSON property accepts `"string"` or `"number"`; when omitted, the parser auto-detects string mode from a `$` suffix on the LHS or a string-shaped RHS.

```text
#UI greeting$ = 'hello'                          ' text entry, stored in greeting$
#UI t$ = ['a'; 'b' | 'c'; 'd']                   ' 2×2 editable string table
#UI {"type": "dropdown", "keys": ["Red","Blue"], "values": ["red","blue"]} color$ = 'red'
```

Behavior notes:

- Checkbox in string mode stores `'true'` or `'false'`; `"1"`/`"0"` overrides are coerced
- Dropdown/radio `values` are stored as strings (no numeric coercion)
- Datagrid in string mode populates `_tableVariables`, so `join$`, `rowT$`, `colT$` work on the result
- Explicit `"mode": "string"` with an LHS that does not end in `$` is an error; an LHS that ends in `$` cannot be forced into `"mode": "number"`
- Concatenation works because the RHS goes through string evaluation:

```text
#UI first$ = 'John'
#UI last$  = 'Doe'
#UI full$  = first$ + ' ' + last$    ' stored as 'John Doe'
```

## CSS classes for theming

Controls emit semantic class names so host themes can style them:

- `.calcpad-ui-input`
- `.calcpad-ui-dropdown`
- `.calcpad-ui-radio`, `.calcpad-ui-radio-label`
- `.calcpad-ui-checkbox`
- `.calcpad-ui-datagrid`

## Linter validation (CPD-3415)

The semantic validator emits warnings for:

- Missing closing `}`
- Invalid JSON payload
- `type: "dropdown"` or `"radio"` without `keys` or `values`
- `keys.length !== values.length` for dropdown/radio