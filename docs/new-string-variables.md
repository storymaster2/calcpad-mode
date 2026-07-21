# String Variables

> Calcpad.Web only (web editor and VS Code extension). Not available in the WPF desktop application.

A string type allows text values to be stored, manipulated, compared, and referenced throughout a Calcpad document. Two storage kinds are supported: **scalar string variables** and **2D string tables**, both defined via `#string` (or multi-line `#def`). All string names must end with `$`.

The previously separate `#table` keyword has been retired — the RHS shape now decides whether a `#string` line creates a scalar, a vector, or a table, mirroring how numeric assignments route their storage.

## Inline string definition (`#string`)

```text
#string title$  = 'Engineering Report'           ' scalar
#string author$ = 'John Doe'                     ' scalar
#string matrix$ = ['a'; 'b' | 'c'; 'd']          ' 2x2 string table (bracket literal)
#string empty$  = table$(3; 4)                   ' 3x4 empty string table
#string parsed$ = split$('a,b;c,d'; ';'; ',')    ' table from delimited string
```

The RHS may be a string expression (literal, concatenation, function call, variable reference), a bracket literal of string cells, or a table-returning function (`table$`, `split$`, `augmentT$`, `stackT$`, `rowT$`, `colT$`, `extractRowsT$`, `extractColsT$`, `subTable$`, `transposeT$`).

## String literal quoting

String literals are delimited by **single quotes (`'`)**. To include a single quote inside a literal, double it:

```text
#string saying$ = 'She said ''No'''   ' value: She said 'No'
```

Double quotes (`"`) are not used for string literals.

## Multiline macro definition (`#def`)

```text
#def description$
    This is a multiline
    string variable
#end def
```

## Inline macro shorthand

```text
#def label$ = 'Section Header'
#def max$(a; b) = $If{a > b @ a : b}
```

## Variable expansion (`$` suffix)

Reference a string variable with its `$` suffix anywhere in the source. Expansion is case-insensitive and happens before expression parsing:

```text
#string name$ = 'Beam Analysis'
'Title: name$
```

## String tables

Tables are 2D string arrays accessed with `tbl$(row; col)` syntax. `#string` creates a table whenever its RHS is a bracket literal or a table-returning function:

```text
' Literal (| separates rows, ; separates columns)
#string data$ = ['Name'; 'Age' | 'John'; '30' | 'Jane'; '28']

' Empty constructor
#string blank$ = table$(3; 4)

' From a delimited string
#string parsed$ = split$('a,b;c,d'; ';'; ',')
```

### Element access and assignment

```text
data$(1; 1)                 ' read: "Name"
data$(2; 2) = '31'          ' write
```

## String comparison operators

| Operator | Meaning | Notes |
|----------|---------|-------|
| `≡` / `==` | Equal | Case-sensitive, ordinal |
| `≠` / `!=` | Not equal | Case-sensitive, ordinal |

```text
#string status$ = 'OK'
#if status$ ≡ 'OK'
    'All checks passed'
#end if
```

The result is numeric `1` (true) or `0` (false), usable in any expression context.

## Built-in string functions

All names end with `$`. Arguments are separated by semicolons.

**Single-argument:** `len$(s)`, `trim$(s)`, `ltrim$(s)`, `rtrim$(s)`, `ucase$(s)`, `lcase$(s)`, `string$(x)`, `val$(s)`, `space$(n)`, `typeOf$(x)`, `tableToStringArray$(t)`

**Two-argument:** `left$(s; n)`, `right$(s; n)`, `compare$(s1; s2)`, `find$(needle; haystack)`, `parsejson$(json; path)`, `table$(rows; cols)`, `rowToStringArray$(t; row)`, `colToStringArray$(t; col)`

**Three-argument:** `mid$(s; start; len)`, `replace$(s; find; repl)`, `instr$(start; haystack; needle)`, `split$(s; rowDelim; colDelim)`, `join$(t; rowDelim; colDelim)`

**Variadic:** `concat$(s1; s2; ...; sN)`

**Numeric-returning** (return a number, not a string): `len$`, `val$`, `compare$`, `instr$`, `find$`.

## Table manipulation functions

Dedicated table-shape functions live alongside the string functions:

`rowT$`, `colT$`, `extractRowsT$`, `extractColsT$`, `subTable$`, `transposeT$`, `augmentT$` (horizontal stack), `stackT$` (vertical stack).

## `chr$('name')` — named-character function

Calcpad string literals do not process backslash escapes (`'\n'` is the literal two characters `\` + `n`). `chr$` returns characters that cannot be written as literals, looked up by name:

```text
#string out$ = join$(tbl$; chr$('newline'); ', ')
```

Unknown names raise a parse error listing the known names.

## Numeric conversion

Because string variables cannot participate directly in arithmetic, use `val$()`:

```text
#string age_str$ = '30'
age = val$(age_str$)
next = age + 1            ' 31
```

If the string does not parse to a number, `#read` and `#str` return `0/0` (NaN) rather than raising an error.

## VS Code integration

- Autocomplete suggests user-defined string variables after the `$` trigger character
- Snippets for `#string` (scalar and table forms) and `#def` appear in the keyword category
- Linter validates name/definition syntax via CPD-2201 through CPD-2213 (macros) and CPD-3301/CPD-3309 (usage)