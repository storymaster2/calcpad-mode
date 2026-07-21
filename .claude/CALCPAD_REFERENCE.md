# Calcpad Language Reference

This document provides a comprehensive reference for the Calcpad programming language, designed for mathematical and engineering calculations.

## Overview

Calcpad is free software for mathematical and engineering calculations. It represents a flexible and modern programmable calculator with HTML report generator.

### Key Features

- Real and complex numbers (rectangular and polar-phasor formats)
- Units of measurement (SI, Imperial and USCS)
- Vectors and matrices: rectangular, symmetric, column, diagonal, upper/lower triangular
- Custom variables and units
- Built-in library with common math functions
- Custom functions of multiple parameters `f(x; y; z; ...)`
- Powerful numerical methods for root and extremum finding, numerical integration and differentiation
- Finite sum, product and iteration procedures, Fourier series and FFT
- Modules, macros and string variables
- Reading and writing data from/to text, CSV and Excel files
- Program flow control with conditions and loops
- Comments with HTML, CSS, JS and SVG support
- Function plotting, images, tables, parametric SVG drawings
- Automatic generation of HTML forms for data input
- Export to Word (*.docx) and PDF documents
- Variable substitution and smart rounding of numbers

---

## Basic Syntax

### How It Works

1. Enter formulas and text (in quotes) into the "Code" box
2. Press F5 to calculate - results appear in the "Output" box
3. Export to HTML, PDF, or Word document

---

## Data Types

### Real Numbers
- Digits `0-9` and decimal point `.`
- Example: `3.14159`

### Complex Numbers
- Format: `re ± im*i`
- Example: `3 - 2i`

### Vectors
- Format: `[v1; v2; v3; ...; vn]`
- Example: `[1; 2; 3; 4; 5]`

### Matrices
- Format: `[M11; M12; ... ; M1n | M21; M22; ... ; M2n | ... | Mm1; Mm2; ... ; Mmn]`
- Rows separated by `|`, elements separated by `;`
- Example: `[1; 2; 3 | 4; 5; 6 | 7; 8; 9]`

---

## Variables

### Naming Rules
- Must start with a letter
- Names are case sensitive
- Can include:
  - All Unicode letters
  - Digits: `0-9`
  - Comma: `,`
  - Special symbols: `′`, `″`, `‴`, `⁗`, `‾`, `ø`, `Ø`, `°`, `∡`
  - Superscripts: `⁰`, `¹`, `²`, `³`, `⁴`, `⁵`, `⁶`, `⁷`, `⁸`, `⁹`, `ⁿ`, `⁺`, `⁻`
  - Subscripts: `₀`, `₁`, `₂`, `₃`, `₄`, `₅`, `₆`, `₇`, `₈`, `₉`, `₊`, `₋`, `₌`, `₍`, `₎`
  - Underscore `_` for subscript

### Built-in Constants
| Symbol | Description |
|--------|-------------|
| `π` | Pi (3.14159...) |
| `e` | Euler's number (2.71828...) |
| `φ` | Golden ratio |
| `γ` | Euler-Mascheroni constant |
| `g` | Standard gravity |
| `G` | Gravitational constant |
| `ME` | Earth mass |
| `MS` | Sun mass |
| `c` | Speed of light |
| `h` | Planck constant |
| `μ0` | Vacuum permeability |
| `ε0` | Vacuum permittivity |
| `ke` | Coulomb constant |
| `me` | Electron mass |
| `mp` | Proton mass |
| `mn` | Neutron mass |
| `NA` | Avogadro constant |
| `σ` | Stefan-Boltzmann constant |
| `kB` | Boltzmann constant |
| `R` | Gas constant |
| `F` | Faraday constant |
| `γc`, `γs`, `γa`, `γg`, `γw` | Material safety factors |

---

## Operators

| Operator | Description | Alternative |
|----------|-------------|-------------|
| `!` | Factorial | |
| `^` | Exponent | |
| `/` | Division | |
| `÷` | Force division bar (inline) / slash (pro mode) | `//` |
| `\` | Integer division | |
| `⦼` | Modulo (remainder) | `%%` |
| `*` | Multiplication | |
| `-` | Minus | |
| `+` | Plus | |
| `≡` | Equal to | `==` |
| `≠` | Not equal to | `!=` |
| `<` | Less than | |
| `>` | Greater than | |
| `≤` | Less or equal | `<=` |
| `≥` | Greater or equal | `>=` |
| `∧` | Logical AND | `&&` |
| `∨` | Logical OR | `||` |
| `⊕` | Logical XOR | `^^` |
| `∠` | Phasor A∠φ | `<<` |
| `=` | Assignment | |

---

## Built-in Functions

### Trigonometric Functions
| Function | Description |
|----------|-------------|
| `sin(x)` | Sine |
| `cos(x)` | Cosine |
| `tan(x)` | Tangent |
| `csc(x)` | Cosecant |
| `sec(x)` | Secant |
| `cot(x)` | Cotangent |

### Hyperbolic Functions
| Function | Description |
|----------|-------------|
| `sinh(x)` | Hyperbolic sine |
| `cosh(x)` | Hyperbolic cosine |
| `tanh(x)` | Hyperbolic tangent |
| `csch(x)` | Hyperbolic cosecant |
| `sech(x)` | Hyperbolic secant |
| `coth(x)` | Hyperbolic cotangent |

### Inverse Trigonometric Functions
| Function | Description |
|----------|-------------|
| `asin(x)` | Inverse sine |
| `acos(x)` | Inverse cosine |
| `atan(x)` | Inverse tangent |
| `atan2(x; y)` | Angle whose tangent is y/x |
| `acsc(x)` | Inverse cosecant |
| `asec(x)` | Inverse secant |
| `acot(x)` | Inverse cotangent |

### Inverse Hyperbolic Functions
| Function | Description |
|----------|-------------|
| `asinh(x)` | Inverse hyperbolic sine |
| `acosh(x)` | Inverse hyperbolic cosine |
| `atanh(x)` | Inverse hyperbolic tangent |
| `acsch(x)` | Inverse hyperbolic cosecant |
| `asech(x)` | Inverse hyperbolic secant |
| `acoth(x)` | Inverse hyperbolic cotangent |

### Logarithmic, Exponential and Roots
| Function | Description |
|----------|-------------|
| `log(x)` | Decimal logarithm (base 10) |
| `ln(x)` | Natural logarithm |
| `log_2(x)` | Binary logarithm |
| `exp(x)` | Natural exponent (e^x) |
| `sqr(x)` or `sqrt(x)` | Square root |
| `cbrt(x)` | Cubic root |
| `root(x; n)` | N-th root |

### Rounding Functions
| Function | Description |
|----------|-------------|
| `round(x)` | Round to nearest integer |
| `floor(x)` | Round down (towards -∞) |
| `ceiling(x)` | Round up (towards +∞) |
| `trunc(x)` | Round towards zero |

### Integer Functions
| Function | Description |
|----------|-------------|
| `mod(x; y)` | Remainder of integer division |
| `gcd(x; y; z...)` | Greatest common divisor |
| `lcm(x; y; z...)` | Least common multiple |

### Complex Number Functions
| Function | Description |
|----------|-------------|
| `re(z)` | Real part |
| `im(z)` | Imaginary part |
| `abs(z)` | Absolute value/magnitude |
| `phase(z)` | Phase angle |
| `conj(z)` | Complex conjugate |

### Aggregate and Interpolation Functions
| Function | Description |
|----------|-------------|
| `min(x; y; z...)` | Minimum value |
| `max(x; y; z...)` | Maximum value |
| `sum(x; y; z...)` | Sum of values |
| `sumsq(x; y; z...)` | Sum of squares |
| `srss(x; y; z...)` | Square root of sum of squares |
| `average(x; y; z...)` | Average (mean) |
| `product(x; y; z...)` | Product of values |
| `mean(x; y; z...)` | Geometric mean |
| `take(n; a; b; c...)` | Returns n-th element from list |
| `line(x; a; b; c...)` | Linear interpolation |
| `spline(x; a; b; c...)` | Hermite spline interpolation |

### Conditional and Logical Functions
| Function | Description |
|----------|-------------|
| `if(cond; value-if-true; value-if-false)` | Conditional evaluation |
| `switch(cond1; val1; cond2; val2; ...; default)` | Selective evaluation |
| `not(x)` | Logical NOT |
| `and(x; y; z...)` | Logical AND |
| `or(x; y; z...)` | Logical OR |
| `xor(x; y; z...)` | Logical XOR |

### Other Functions
| Function | Description |
|----------|-------------|
| `sign(x)` | Sign of number (-1, 0, or 1) |
| `random(x)` | Random number between 0 and x |
| `getunits(x)` | Get units without value (returns 1 if unitless) |
| `setunits(x; u)` | Set units u to x |
| `clrunits(x)` | Clear units from x |
| `hp(x)` | Convert to high-performance type |
| `ishp(x)` | Check if high-performance type |

---

## Vector Functions

### Creational
| Function | Description |
|----------|-------------|
| `vector(n)` | Create empty vector with length n |
| `vector_hp(n)` | Create high-performance empty vector |
| `range(x1; xn; s)` | Create vector from x1 to xn with step s |
| `range_hp(x1; xn; s)` | Create high-performance range vector |

### Structural
| Function | Description |
|----------|-------------|
| `len(v)` | Length of vector |
| `size(v)` | Index of last non-zero element |
| `resize(v; n)` | Set new length n |
| `fill(v; x)` | Fill vector with value x |
| `join(A; b; c...)` | Join matrices, vectors, and scalars |
| `slice(v; i1; i2)` | Extract portion between indexes i1 and i2 |
| `first(v; n)` | First n elements |
| `last(v; n)` | Last n elements |
| `extract(v; i)` | Extract elements at indexes in i |

### Data Operations
| Function | Description |
|----------|-------------|
| `sort(v)` | Sort ascending |
| `rsort(v)` | Sort descending |
| `order(v)` | Indexes in ascending order |
| `revorder(v)` | Indexes in descending order |
| `reverse(v)` | Reverse element order |
| `count(v; x; i)` | Count elements equal to x after index i |
| `search(v; x; i)` | Index of first element equal to x after i |

### Find Functions (return indexes)
| Function | Description |
|----------|-------------|
| `find(v; x; i)` or `find_eq(v; x; i)` | Elements equal to x |
| `find_ne(v; x; i)` | Elements not equal to x |
| `find_lt(v; x; i)` | Elements less than x |
| `find_le(v; x; i)` | Elements less than or equal to x |
| `find_gt(v; x; i)` | Elements greater than x |
| `find_ge(v; x; i)` | Elements greater than or equal to x |

### Lookup Functions (return values)
| Function | Description |
|----------|-------------|
| `lookup(a; b; x)` or `lookup_eq(a; b; x)` | Elements of a where b = x |
| `lookup_ne(a; b; x)` | Elements of a where b ≠ x |
| `lookup_lt(a; b; x)` | Elements of a where b < x |
| `lookup_le(a; b; x)` | Elements of a where b ≤ x |
| `lookup_gt(a; b; x)` | Elements of a where b > x |
| `lookup_ge(a; b; x)` | Elements of a where b ≥ x |

### Vector Math
| Function | Description |
|----------|-------------|
| `norm_1(v)` | L1 (Manhattan) norm |
| `norm(v)` or `norm_2(v)` or `norm_e(v)` | L2 (Euclidean) norm |
| `norm_p(v; p)` | Lp norm |
| `norm_i(v)` | L∞ (infinity) norm |
| `unit(v)` | Normalized vector (L2 norm = 1) |
| `dot(a; b)` | Scalar (dot) product |
| `cross(a; b)` | Cross product (2D or 3D vectors) |

---

## Matrix Functions

### Creational
| Function | Description |
|----------|-------------|
| `matrix(m; n)` | Empty m×n matrix |
| `identity(n)` | n×n identity matrix |
| `diagonal(n; d)` | n×n diagonal matrix filled with d |
| `column(m; c)` | m×1 column matrix filled with c |
| `utriang(n)` | n×n upper triangular matrix |
| `ltriang(n)` | n×n lower triangular matrix |
| `symmetric(n)` | n×n symmetric matrix |

### High-Performance Creational
| Function | Description |
|----------|-------------|
| `matrix_hp(m; n)` | High-performance m×n matrix |
| `identity_hp(n)` | High-performance identity matrix |
| `diagonal_hp(n; d)` | High-performance diagonal matrix |
| `column_hp(m; c)` | High-performance column matrix |
| `utriang_hp(n)` | High-performance upper triangular |
| `ltriang_hp(n)` | High-performance lower triangular |
| `symmetric_hp(n)` | High-performance symmetric matrix |

### Vector to Matrix Conversion
| Function | Description |
|----------|-------------|
| `vec2diag(v)` | Diagonal matrix from vector |
| `vec2row(v)` | Row matrix from vector |
| `vec2col(v)` | Column matrix from vector |
| `join_cols(c1; c2; c3...)` | Matrix from column vectors |
| `join_rows(r1; r2; r3...)` | Matrix from row vectors |
| `augment(A; B; C...)` | Append matrices side by side |
| `stack(A; B; C...)` | Stack matrices vertically |

### Structural
| Function | Description |
|----------|-------------|
| `n_rows(M)` | Number of rows |
| `n_cols(M)` | Number of columns |
| `mresize(M; m; n)` | Set new dimensions |
| `mfill(M; x)` | Fill matrix with value |
| `fill_row(M; i; x)` | Fill row i with value |
| `fill_col(M; j; x)` | Fill column j with value |
| `copy(A; B; i; j)` | Copy A to B starting at (i,j) |
| `add(A; B; i; j)` | Add A to B starting at (i,j) |
| `row(M; i)` | Extract row i as vector |
| `col(M; j)` | Extract column j as vector |
| `extract_rows(M; i)` | Extract rows at indexes in i |
| `extract_cols(M; j)` | Extract columns at indexes in j |
| `diag2vec(M)` | Extract diagonal as vector |
| `submatrix(M; i1; i2; j1; j2)` | Extract submatrix |

### Matrix Data Operations
| Function | Description |
|----------|-------------|
| `sort_cols(M; i)` | Sort columns by row i (ascending) |
| `rsort_cols(M; i)` | Sort columns by row i (descending) |
| `sort_rows(M; j)` | Sort rows by column j (ascending) |
| `rsort_rows(M; j)` | Sort rows by column j (descending) |
| `order_cols(M; i)` | Column indexes ordered by row i |
| `revorder_cols(M; i)` | Column indexes reverse ordered |
| `order_rows(M; j)` | Row indexes ordered by column j |
| `revorder_rows(M; j)` | Row indexes reverse ordered |
| `mcount(M; x)` | Count occurrences of x |
| `msearch(M; x; i; j)` | Find first occurrence of x |

### Matrix Find Functions
| Function | Description |
|----------|-------------|
| `mfind(M; x)` or `mfind_eq(M; x)` | Indexes where M = x |
| `mfind_ne(M; x)` | Indexes where M ≠ x |
| `mfind_lt(M; x)` | Indexes where M < x |
| `mfind_le(M; x)` | Indexes where M ≤ x |
| `mfind_gt(M; x)` | Indexes where M > x |
| `mfind_ge(M; x)` | Indexes where M ≥ x |

### Horizontal Lookup (hlookup)
| Function | Description |
|----------|-------------|
| `hlookup(M; x; i1; i2)` | Values from row i2 where row i1 = x |
| `hlookup_eq(M; x; i1; i2)` | Values from row i2 where row i1 = x |
| `hlookup_ne(M; x; i1; i2)` | Values from row i2 where row i1 ≠ x |
| `hlookup_lt(M; x; i1; i2)` | Values from row i2 where row i1 < x |
| `hlookup_le(M; x; i1; i2)` | Values from row i2 where row i1 ≤ x |
| `hlookup_gt(M; x; i1; i2)` | Values from row i2 where row i1 > x |
| `hlookup_ge(M; x; i1; i2)` | Values from row i2 where row i1 ≥ x |

### Vertical Lookup (vlookup)
| Function | Description |
|----------|-------------|
| `vlookup(M; x; j1; j2)` | Values from col j2 where col j1 = x |
| `vlookup_eq(M; x; j1; j2)` | Values from col j2 where col j1 = x |
| `vlookup_ne(M; x; j1; j2)` | Values from col j2 where col j1 ≠ x |
| `vlookup_lt(M; x; j1; j2)` | Values from col j2 where col j1 < x |
| `vlookup_le(M; x; j1; j2)` | Values from col j2 where col j1 ≤ x |
| `vlookup_gt(M; x; j1; j2)` | Values from col j2 where col j1 > x |
| `vlookup_ge(M; x; j1; j2)` | Values from col j2 where col j1 ≥ x |

### Matrix Math
| Function | Description |
|----------|-------------|
| `hprod(A; B)` | Hadamard (element-wise) product |
| `fprod(A; B)` | Frobenius product |
| `kprod(A; B)` | Kronecker product |
| `mnorm(M)` or `mnorm_2(M)` | L2 norm |
| `mnorm_1(M)` | L1 norm |
| `mnorm_e(M)` | Frobenius norm |
| `mnorm_i(M)` | L∞ norm |
| `cond(M)` or `cond_e(M)` | Condition number (Euclidean) |
| `cond_1(M)` | Condition number (L1) |
| `cond_2(M)` | Condition number (L2) |
| `cond_i(M)` | Condition number (L∞) |
| `det(M)` | Determinant |
| `rank(M)` | Rank |
| `trace(M)` | Trace |
| `transp(M)` | Transpose |
| `adj(M)` | Adjugate |
| `cofactor(M)` | Cofactor matrix |

### Eigenvalues and Decomposition
| Function | Description |
|----------|-------------|
| `eigenvals(M; n)` | First n eigenvalues (or all) |
| `eigenvecs(M; n)` | First n eigenvectors (or all) |
| `eigen(M; n)` | First n eigenvalues and eigenvectors |
| `cholesky(M)` | Cholesky decomposition |
| `lu(M)` | LU decomposition |
| `qr(M)` | QR decomposition |
| `svd(M)` | Singular value decomposition |
| `inverse(M)` | Matrix inverse |

### Linear System Solvers
| Function | Description |
|----------|-------------|
| `lsolve(A; b)` | Solve Ax = b (LDLT/LU) |
| `clsolve(A; b)` | Solve Ax = b (Cholesky) |
| `slsolve(A; b)` | Solve Ax = b (PCG method, hp matrices) |
| `msolve(A; B)` | Solve AX = B (LDLT/LU) |
| `cmsolve(A; B)` | Solve AX = B (Cholesky) |
| `smsolve(A; B)` | Solve AX = B (PCG method, hp matrices) |

### Fourier Transform
| Function | Description |
|----------|-------------|
| `fft(M)` | Fast Fourier transform |
| `ift(M)` | Inverse Fourier transform |

### Double Interpolation (Matrix)
| Function | Description |
|----------|-------------|
| `take(x; y; M)` | Element at indexes x, y |
| `line(x; y; M)` | Double linear interpolation |
| `spline(x; y; M)` | Double Hermite spline interpolation |

### Solver Configuration
- `Tol` - Target tolerance for iterative PCG solver

---

## Comments

- **Title comments**: Use double quotes `"Title"`
- **Text comments**: Use single quotes `'text'`
- HTML, CSS, JS, and SVG are allowed within comments

---

## Graphing and Plotting

### Plot Commands
| Syntax | Description |
|--------|-------------|
| `$Plot { f(x) @ x = a : b }` | Simple function plot |
| `$Plot { x(t) | y(t) @ t = a : b }` | Parametric plot |
| `$Plot { f1(x) & f2(x) & ... @ x = a : b }` | Multiple functions |
| `$Plot { x1(t) | y1(t) & x2(t) | y2(t) & ... @ t = a : b }` | Multiple parametric |
| `$Map { f(x; y) @ x = a : b & y = c : d }` | 2D color map of 3D surface |

### Plot Settings
| Variable | Description |
|----------|-------------|
| `PlotHeight` | Height in pixels |
| `PlotWidth` | Width in pixels |
| `PlotSVG` | 1 = SVG (vector), 0 = PNG (raster) |
| `PlotAdaptive` | 1 = adaptive mesh, 0 = uniform |
| `PlotStep` | Mesh size for map plotting |
| `PlotPalette` | Color palette (0-9) |
| `PlotShadows` | Enable shadows |
| `PlotSmooth` | 1 = smooth gradient, 0 = isobands |
| `PlotLightDir` | Light direction (0-7, clockwise) |

---

## Iterative and Numerical Methods

| Syntax | Description |
|--------|-------------|
| `$Root { f(x) = const @ x = a : b }` | Root finding for f(x) = const |
| `$Root { f(x) @ x = a : b }` | Root finding for f(x) = 0 |
| `$Find { f(x) @ x = a : b }` | Find approximate solution |
| `$Sup { f(x) @ x = a : b }` | Local maximum |
| `$Inf { f(x) @ x = a : b }` | Local minimum |
| `$Area { f(x) @ x = a : b }` | Gauss-Lobatto integration |
| `$Integral { f(x) @ x = a : b }` | Tanh-Sinh integration |
| `$Slope { f(x) @ x = a }` | Numerical differentiation |
| `$Sum { f(k) @ k = a : b }` | Iterative sum |
| `$Product { f(k) @ k = a : b }` | Iterative product |
| `$Repeat { f(k) @ k = a : b }` | Iterative expression block |
| `$While { condition; expressions }` | Conditional iteration |
| `$Block { expressions }` | Multiline expression block |
| `$Inline { expressions }` | Inline expression block |

### Precision Setting
- `Precision` - Relative precision for numerical methods [10^-2 to 10^-16], default is 10^-12

---

## Program Flow Control

### Simple Conditional
```
#if condition
    your code goes here
#end if
```

### Alternative Conditional
```
#if condition
    your code goes here
#else
    alternative code
#end if
```

### Complete Conditional
```
#if condition1
    code for condition1
#else if condition2
    code for condition2
#else
    default code
#end if
```

---

## Iteration Blocks

### Simple Repeat
```
#repeat number_of_repetitions
    your code goes here
#loop
```

### Repeat with Break/Continue
```
#repeat number_of_repetitions
    your code goes here
    #if condition
        #break
    #end if
    more code
#loop
```

### For Loop (Counter)
```
#for counter = start : end
    your code goes here
#loop
```

### While Loop
```
#while condition
    your code goes here
#loop
```

---

## Modules and Macros

### Module Commands
| Command | Description |
|---------|-------------|
| `#include filename` | Include external module |
| `#local` | Start local section (not included) |
| `#global` | Start global section (to be included) |

### String Variables
**Inline:**
```
#def variable_name$ = content
```

**Multiline:**
```
#def variable_name$
    content line 1
    content line 2
#end def
```

### Macros (with parameters)
**Inline:**
```
#def macro_name$(param1$; param2$; ...) = content
```

**Multiline:**
```
#def macro_name$(param1$; param2$; ...)
    content line 1
    content line 2
#end def
```

---

## Import/Export of External Data

### Text/CSV Files
```
#read M from filename.txt@R1C1:R2C2 TYPE=R SEP=','
#write M to filename.txt@R1C1:R2C2 TYPE=N SEP=','
#append M to filename.txt@R1C1:R2C2 TYPE=N SEP=','
```

### Excel Files (xlsx, xlsm)
```
#read M from filename.xlsx@Sheet1!A1:B2 TYPE=R
#write M to filename.xlsx@Sheet1!A1:B2 TYPE=N
#append M to filename.xlsx@Sheet1!A1:B2 TYPE=N
```

### Type Options
- For `#read`: `R`, `D`, `C`, `S`, `U`, `L`, `V` (add `_HP` for hp matrices)
- For `#write`/`#append`: `Y` or `N`
- Sheet, range, TYPE, and SEP can be omitted

---

## Output Control Commands

| Command | Description |
|---------|-------------|
| `#hide` | Hide report contents |
| `#show` | Show contents (default) |
| `#pre` | Show only before calculations |
| `#post` | Show only after calculations |
| `#val` | Show only result, no equation |
| `#equ` | Show complete equations and results (default) |
| `#noc` | Show equations without results |
| `#nosub` | Do not substitute variables |
| `#novar` | Show only substituted values |
| `#varsub` | Show variables and substituted values (default) |
| `#split` | Split equations that don't fit |
| `#wrap` | Wrap equations (default) |
| `#round n` | Round to n decimal places |
| `#round default` | Restore default rounding |
| `#format FFFF` | Custom format string |
| `#format default` | Restore default format |
| `#md on` | Enable markdown in comments |
| `#md off` | Disable markdown in comments |
| `#phasor` | Complex output as A∠φ |
| `#complex` | Complex output as a + bi |

---

## Breakpoints

| Command | Description |
|---------|-------------|
| `#pause` | Calculate to line and wait for resume |
| `#input` | Render input form and wait for user |

---

## Angle Units

| Command | Description |
|---------|-------------|
| `#deg` | Degrees |
| `#rad` | Radians |
| `#gra` | Gradians |

- Separator for target units: `|`
- Example: `3ft + 12in|cm` outputs `121.92 cm`
- `ReturnAngleUnits = 1` - Return angles with units

---

## Units of Measurement

### Dimensionless
`%`, `‰`, `‱`, `pcm`, `ppm`, `ppb`, `ppt`, `ppq`

### Angle Units
`°`, `′`, `″`, `deg`, `rad`, `grad`, `rev`

### Metric Units (SI and Compatible)

| Category | Units |
|----------|-------|
| **Mass** | `g`, `hg`, `kg`, `t`, `kt`, `Mt`, `Gt`, `dg`, `cg`, `mg`, `μg`, `Da`, `u` |
| **Length** | `m`, `km`, `dm`, `cm`, `mm`, `μm`, `nm`, `pm`, `AU`, `ly` |
| **Time** | `s`, `ms`, `μs`, `ns`, `ps`, `min`, `h`, `d`, `w`, `y` |
| **Frequency** | `Hz`, `kHz`, `MHz`, `GHz`, `THz`, `mHz`, `μHz`, `nHz`, `pHz`, `rpm` |
| **Speed** | `kmh` |
| **Electric Current** | `A`, `kA`, `MA`, `GA`, `TA`, `mA`, `μA`, `nA`, `pA` |
| **Temperature** | `°C`, `Δ°C`, `K` |
| **Amount of Substance** | `mol` |
| **Luminous Intensity** | `cd` |
| **Area** | `a`, `daa`, `ha` |
| **Volume** | `L`, `daL`, `hL`, `dL`, `cL`, `mL`, `μL`, `nL`, `pL` |
| **Force** | `N`, `daN`, `hN`, `kN`, `MN`, `GN`, `TN`, `gf`, `kgf`, `tf`, `dyn` |
| **Moment** | `Nm`, `kNm` |
| **Pressure** | `Pa`, `daPa`, `hPa`, `kPa`, `MPa`, `GPa`, `TPa`, `dPa`, `cPa`, `mPa`, `μPa`, `nPa`, `pPa`, `bar`, `mbar`, `μbar`, `atm`, `at`, `Torr`, `mmHg` |
| **Viscosity** | `P`, `cP`, `St`, `cSt` |
| **Energy/Work** | `J`, `kJ`, `MJ`, `GJ`, `TJ`, `mJ`, `μJ`, `nJ`, `pJ`, `Wh`, `kWh`, `MWh`, `GWh`, `TWh`, `mWh`, `μWh`, `nWh`, `pWh`, `eV`, `keV`, `MeV`, `GeV`, `TeV`, `PeV`, `EeV`, `cal`, `kcal`, `erg` |
| **Power** | `W`, `kW`, `MW`, `GW`, `TW`, `mW`, `μW`, `nW`, `pW`, `hpM`, `ks`, `VA`, `kVA`, `MVA`, `GVA`, `TVA`, `mVA`, `μVA`, `nVA`, `pVA`, `VAR`, `kVAR`, `MVAR`, `GVAR`, `TVAR`, `mVAR`, `μVAR`, `nVAR`, `pVAR` |
| **Electric Charge** | `C`, `kC`, `MC`, `GC`, `TC`, `mC`, `μC`, `nC`, `pC`, `Ah`, `mAh` |
| **Potential** | `V`, `kV`, `MV`, `GV`, `TV`, `mV`, `μV`, `nV`, `pV` |
| **Capacitance** | `F`, `kF`, `MF`, `GF`, `TF`, `mF`, `μF`, `nF`, `pF` |
| **Resistance** | `Ω`, `kΩ`, `MΩ`, `GΩ`, `TΩ`, `mΩ`, `μΩ`, `nΩ`, `pΩ` |
| **Conductance** | `S`, `kS`, `MS`, `GS`, `TS`, `mS`, `μS`, `nS`, `pS`, `℧`, `k℧`, `M℧`, `G℧`, `T℧`, `m℧`, `μ℧`, `n℧`, `p℧` |
| **Magnetic Flux** | `Wb`, `kWb`, `MWb`, `GWb`, `TWb`, `mWb`, `μWb`, `nWb`, `pWb` |
| **Magnetic Flux Density** | `T`, `kT`, `MT`, `GT`, `TT`, `mT`, `μT`, `nT`, `pT` |
| **Inductance** | `H`, `kH`, `MH`, `GH`, `TH`, `mH`, `μH`, `nH`, `pH` |
| **Luminous Flux** | `lm` |
| **Illuminance** | `lx` |
| **Radioactivity** | `Bq`, `kBq`, `MBq`, `GBq`, `TBq`, `mBq`, `μBq`, `nBq`, `pBq`, `Ci`, `Rd` |
| **Absorbed Dose** | `Gy`, `kGy`, `MGy`, `GGy`, `TGy`, `mGy`, `μGy`, `nGy`, `pGy` |
| **Equivalent Dose** | `Sv`, `kSv`, `MSv`, `GSv`, `TSv`, `mSv`, `μSv`, `nSv`, `pSv` |
| **Catalytic Activity** | `kat` |

### Non-Metric Units (Imperial/US)

| Category | Units |
|----------|-------|
| **Mass** | `gr`, `dr`, `oz`, `lb` (or `lbm`, `lb_m`), `klb`, `kipm` (or `kip_m`), `st`, `qr`, `cwt` (or `cwt_UK`, `cwt_US`), `ton` (or `ton_UK`, `ton_US`), `slug` |
| **Length** | `th`, `in`, `ft`, `yd`, `ch`, `fur`, `mi`, `ftm` (or `ftm_UK`, `ftm_US`), `cable` (or `cable_UK`, `cable_US`), `nmi`, `li`, `rod`, `pole`, `perch`, `lea` |
| **Speed** | `mph`, `knot` |
| **Temperature** | `°F`, `Δ°F`, `°R` |
| **Area** | `rood`, `ac` |
| **Volume (Fluid)** | `fl_oz`, `gi`, `pt`, `qt`, `gal`, `bbl` (with UK/US variants: `fl_oz_UK`, `gi_UK`, `pt_UK`, `qt_UK`, `gal_UK`, `bbl_UK`, `fl_oz_US`, `gi_US`, `pt_US`, `qt_US`, `gal_US`, `bbl_US`) |
| **Volume (Dry)** | `pt_dry`, `qt_dry`, `gal_dry`, `bbl_dry`, `pk` (or `pk_UK`, `pk_US`), `bu` (or `bu_UK`, `bu_US`) |
| **Force** | `ozf` (or `oz_f`), `lbf` (or `lb_f`), `kip` (or `kipf`, `kip_f`), `tonf` (or `ton_f`), `pdl` |
| **Pressure** | `osi`, `osf`, `psi`, `psf`, `ksi`, `ksf`, `tsi`, `tsf`, `inHg` |
| **Energy/Work** | `BTU`, `therm` (or `therm_UK`, `therm_US`), `quad` |
| **Power** | `hp`, `hpE`, `hpS` |

### Custom Units
- Syntax: `.Name = expression`
- Names can include currency symbols: `€`, `£`, `₤`, `¥`, `¢`, `₽`, `₹`, `₩`, `₪`

---

## Custom Functions

Define custom functions with multiple parameters:
```
f(x; y; z) = x^2 + y^2 + z^2
```

Call custom functions:
```
result = f(3; 4; 5)
```

---

## Examples

### Basic Calculation
```
a = 5m
b = 3m
area = a*b
```

### Using Vectors
```
v = [1; 2; 3; 4; 5]
sum_v = sum(v)
mean_v = average(v)
```

### Matrix Operations
```
A = [1; 2 | 3; 4]
B = [5; 6 | 7; 8]
C = A*B
det_A = det(A)
```

### Conditional Logic
```
x = 10
#if x > 5
    'x is greater than 5'
    result = x^2
#else
    'x is 5 or less'
    result = x
#end if
```

### Numerical Integration
```
f(x) = sin(x)
integral = $Area { f(x) @ x = 0 : π }
```

### Root Finding
```
g(x) = x^3 - 2*x - 5
root = $Root { g(x) @ x = 1 : 3 }
```

### Plotting
```
PlotWidth = 600
PlotHeight = 400
$Plot { sin(x) & cos(x) @ x = 0 : 2*π }
```

---

## File Formats

- Plain text: `.txt`, `.cpd`
- Binary: `.cpdz`
- Export: HTML, PDF, Word (`.docx`)

---

## Project Information

- **Website**: https://calcpad.eu
- **Online IDE**: https://calcpad.eu/Ide
- **License**: MIT License
- **Copyright**: PROEKTSOFT EOOD
