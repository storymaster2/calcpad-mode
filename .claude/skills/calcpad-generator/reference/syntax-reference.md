# Calcpad Syntax Reference

## Data Types
- **Real Numbers**: Digits 0-9 and decimal point (e.g., `3.14159`)
- **Complex Numbers**: Format `re + im*i` (e.g., `3 - 2i`)
- **Vectors**: Format `[v1; v2; v3; ...; vn]` (e.g., `[1; 2; 3; 4; 5]`)
- **Matrices**: Rows separated by `|`, elements by `;` (e.g., `[1; 2; 3 | 4; 5; 6]`)

## Variable Naming Rules
- Must start with a letter
- Names are case sensitive
- Can include Unicode letters, digits, special symbols
- Use `_` for subscripts (renders as subscript in output)

## Built-in Constants
`pi`, `e`, `phi` (golden ratio), `gamma` (Euler-Mascheroni), `g` (gravity), `c` (speed of light), and more.

## Operators
| Operator | Description |
|----------|-------------|
| `!` | Factorial |
| `^` | Exponent |
| `/` | Division |
| `*` | Multiplication |
| `-` | Minus |
| `+` | Plus |
| `==` | Equal to |
| `!=` | Not equal to |
| `<` | Less than |
| `>` | Greater than |
| `<=` | Less or equal |
| `>=` | Greater or equal |
| `&&` | Logical AND |
| `||` | Logical OR |
| `=` | Assignment |

## Comments and Documentation
- **Title comments**: Double quotes `"Title"` - renders as heading
- **Text comments**: Single quotes `'text'` - renders as paragraph
- HTML, CSS, and SVG are allowed within comments

## Core Functions
**Trigonometric**: `sin(x)`, `cos(x)`, `tan(x)`, `asin(x)`, `acos(x)`, `atan(x)`, `atan2(x; y)`
**Hyperbolic**: `sinh(x)`, `cosh(x)`, `tanh(x)`, `asinh(x)`, `acosh(x)`, `atanh(x)`
**Logarithmic**: `log(x)` (base 10), `ln(x)` (natural), `log_2(x)` (binary), `exp(x)`
**Roots**: `sqr(x)`/`sqrt(x)`, `cbrt(x)`, `root(x; n)`
**Rounding**: `round(x)`, `floor(x)`, `ceiling(x)`, `trunc(x)`
**Aggregate**: `min(...)`, `max(...)`, `sum(...)`, `average(...)`, `product(...)`
**Conditional**: `if(cond; value-if-true; value-if-false)`, `switch(cond1; val1; ...; default)`
**Complex**: `re(z)`, `im(z)`, `abs(z)`, `phase(z)`, `conj(z)`

## Vector Functions
**Creation**: `vector(n)`, `range(x1; xn; s)`
**Structure**: `len(v)`, `resize(v; n)`, `join(...)`, `slice(v; i1; i2)`
**Math**: `norm(v)`, `unit(v)`, `dot(a; b)`, `cross(a; b)`
**Data**: `sort(v)`, `rsort(v)`, `reverse(v)`, `search(v; x; i)`

## Matrix Functions
**Creation**: `matrix(m; n)`, `identity(n)`, `diagonal(n; d)`
**Structure**: `n_rows(M)`, `n_cols(M)`, `row(M; i)`, `col(M; j)`, `transp(M)`
**Math**: `det(M)`, `inverse(M)`, `trace(M)`, `rank(M)`
**Solvers**: `lsolve(A; b)` - solve Ax = b
**Decomposition**: `eigenvals(M)`, `eigenvecs(M)`, `lu(M)`, `qr(M)`, `svd(M)`

## Control Flow

```
#if condition
    code
#else if condition2
    code
#else
    code
#end if
```

```
#for counter = start : end
    code
#loop
```

```
#while condition
    code
#loop
```

```
#repeat n
    code
#loop
```

## Numerical Methods
| Syntax | Description |
|--------|-------------|
| `$Root { f(x) @ x = a : b }` | Root finding for f(x) = 0 |
| `$Sup { f(x) @ x = a : b }` | Local maximum |
| `$Inf { f(x) @ x = a : b }` | Local minimum |
| `$Area { f(x) @ x = a : b }` | Numerical integration |
| `$Slope { f(x) @ x = a }` | Numerical differentiation |
| `$Sum { f(k) @ k = a : b }` | Iterative sum |
| `$Product { f(k) @ k = a : b }` | Iterative product |

## Plotting
```
$Plot { f(x) @ x = a : b }
$Plot { f1(x) & f2(x) @ x = a : b }
$Plot { x(t) | y(t) @ t = a : b }
$Map { f(x; y) @ x = a : b & y = c : d }
```

Plot settings: `PlotWidth`, `PlotHeight`, `PlotSVG` (1=vector, 0=raster)

## Custom Functions
```
f(x; y; z) = x^2 + y^2 + z^2
result = f(3; 4; 5)
```

## Units of Measurement

**SI Units**:
- Mass: `g`, `kg`, `t`, `mg`
- Length: `m`, `km`, `cm`, `mm`, `um`, `nm`
- Time: `s`, `ms`, `min`, `h`, `d`
- Force: `N`, `kN`, `MN`
- Pressure: `Pa`, `kPa`, `MPa`, `GPa`, `bar`
- Energy: `J`, `kJ`, `MJ`, `Wh`, `kWh`
- Power: `W`, `kW`, `MW`
- Temperature: `degC`, `K`
- Electric: `A`, `V`, `ohm`, `F`, `H`

**Imperial/US Units**:
- Mass: `lb`, `oz`, `ton`, `slug`
- Length: `in`, `ft`, `yd`, `mi`
- Force: `lbf`, `kip`
- Pressure: `psi`, `ksi`, `psf`
- Energy: `BTU`
- Temperature: `degF`

**Unit Conversion**: Use `|` to specify output units
```
length = 3ft + 12in|cm  ' outputs 121.92 cm
```

**Custom Units**:
```
.USD = 1
price = 100*.USD
```

## Output Control
| Command | Description |
|---------|-------------|
| `#hide` | Hide from output |
| `#show` | Show in output (default) |
| `#val` | Show only result value |
| `#equ` | Show full equations (default) |
| `#round n` | Round to n decimal places |
| `#deg` / `#rad` / `#gra` | Angle units |

## Modules and Macros
```
#include filename
#def variable$ = content
#def macro$(param1$; param2$) = expression
```
