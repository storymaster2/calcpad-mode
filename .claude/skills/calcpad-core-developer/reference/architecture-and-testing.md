# Calcpad.Core Architecture & Testing Reference

## Directory Structure

```
Calcpad.Core/
├── BaseTypes/
│   ├── Complex.cs         - Complex number type
│   ├── Real.cs            - Real number wrapper
│   ├── Parameter.cs       - Function parameters
│   ├── Variable.cs        - Variable storage
│   └── Unit.cs            - Unit of measurement
├── Calculator/
│   ├── Calculator.cs      - Abstract base calculator
│   ├── RealCalculator.cs  - Real number operations
│   ├── ComplexCalculator.cs - Complex operations
│   ├── MatrixCalculator.cs  - Matrix operations
│   └── VectorCalculator.cs  - Vector operations
├── Matrix/
│   ├── Matrix.cs          - Base matrix type
│   ├── ColumnMatrix.cs    - Column vector as matrix
│   ├── DiagonalMatrix.cs  - Diagonal matrix
│   ├── SymmetricMatrix.cs - Symmetric matrix
│   ├── LowerTriangularMatrix.cs
│   └── UpperTriangularMatrix.cs
├── Vector/
│   └── Vector.cs          - Vector operations
├── Parsers/
│   ├── MathParser/        - Main expression parser
│   └── ExpressionParser/  - Sub-expression handling
├── Plotter/
│   ├── Plotter.cs         - 2D plotting engine
│   ├── MapPlotter.cs      - 2D color map plotting
│   └── ChartPlotter.cs    - Chart generation
├── Output/
│   └── OutputWriter.cs    - Result formatting
├── Networking/
│   └── (Network utilities)
├── Solver/
│   └── Solver.cs          - Equation solving
└── Validator/
    └── Validator.cs       - Input validation
```

## Key Classes

### MathParser
The core expression parser. Handles:
- Tokenizing mathematical expressions
- Building expression trees
- Evaluating with proper operator precedence
- Managing variables and functions
- Unit conversions

### Calculator Classes
```csharp
// Abstract base
public abstract class Calculator
{
    public abstract Value Evaluate(string expression);
    public abstract void SetVariable(string name, Value value);
}

// RealCalculator - sin, cos, tan, log, exp, sqrt, etc.
// ComplexCalculator - re(), im(), phase(), conj(), etc.
// MatrixCalculator - det(), inverse(), transp(), eigenvals(), etc.
// VectorCalculator - norm(), dot(), cross(), sort(), etc.
```

### Unit System
Units are defined with conversion factors to base SI units:
```csharp
public class Unit
{
    public string Name { get; }
    public double Factor { get; }      // Conversion to base
    public int[] Dimensions { get; }   // [length, mass, time, current, temp, amount, luminosity]
}
```

## Matrix Type Hierarchy

```
Matrix (base)
├── ColumnMatrix      - m×1 matrix (column vector)
├── DiagonalMatrix    - Only diagonal elements stored
├── SymmetricMatrix   - A = A^T, stores upper triangle
├── LowerTriangularMatrix
├── UpperTriangularMatrix
└── (Regular dense matrix)
```

## Plotting System

```csharp
Plotter.Plot(function, xMin, xMax, options);
Plotter.PlotParametric(xFunc, yFunc, tMin, tMax, options);
MapPlotter.Plot(function, xMin, xMax, yMin, yMax, options);
```

Uses SkiaSharp for rendering to PNG/SVG.

## External Dependencies

- **Markdig.Signed** (0.43.0) - Markdown processing in comments
- **SkiaSharp** (3.119.1) - Graphics rendering for plots
- **System.IO.Packaging** (10.0.0) - Package handling

## Calcpad Syntax Reference

### Operators (precedence high to low)
1. `!` - Factorial
2. `^` - Exponentiation
3. `*`, `/`, `\` (integer div), `%%` (modulo)
4. `+`, `-`
5. Comparison: `==`, `!=`, `<`, `>`, `<=`, `>=`
6. Logical: `&&`, `||`, `^^`

### Built-in Functions (partial list)
**Trigonometric:** sin, cos, tan, csc, sec, cot, asin, acos, atan, atan2
**Hyperbolic:** sinh, cosh, tanh, asinh, acosh, atanh
**Logarithmic:** log, ln, log_2, exp
**Roots:** sqr/sqrt, cbrt, root(x,n)
**Rounding:** round, floor, ceiling, trunc
**Complex:** re, im, abs, phase, conj
**Aggregate:** min, max, sum, average, product
**Vector:** vector(n), range(a,b,step), len, sort, reverse, dot, cross, norm
**Matrix:** matrix(m,n), identity(n), det, inverse, transp, eigenvals, lsolve

### Commands (iterative/numerical methods)
`$Root`, `$Find`, `$Sup`, `$Inf` - Optimization
`$Area`, `$Integral`, `$Slope` - Calculus
`$Sum`, `$Product`, `$Repeat` - Iteration
`$Plot`, `$Map` - Visualization

## Testing

### Unit Tests
```bash
cd Calcpad.Tests
dotnet test
```

Tests cover: expression parsing, function evaluation, unit conversions, matrix operations.

### Integration Testing via Linux Dev Server
```bash
# Start the dev server (runs on port 9420)
./scripts/Calcpad.Server/restart-dev-server.sh
```

See [Calcpad.Server/API_SCHEMA.md](../../Calcpad.Server/API_SCHEMA.md) for full API documentation.

**POST /api/calcpad/convert** - Test calculations:
```bash
curl -X POST http://localhost:9420/api/calcpad/convert \
  -H "Content-Type: application/json" \
  -d '{"content": "x = 5*m\ny = sin(45°)", "theme": "light"}'
```

**Check for errors:**
```bash
curl -s -X POST http://localhost:9420/api/calcpad/convert \
  -H "Content-Type: application/json" \
  -d '{"content": "your test code"}' | grep -i "class=\"err"
```
