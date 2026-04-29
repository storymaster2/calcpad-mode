using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for built-in functions.
    /// </summary>
    public static class FunctionSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            // ============================================
            // TRIGONOMETRIC FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "sin(§)",
                Description = "Sine",
                Documentation = "Computes the sine of an angle. The angle is interpreted using the current `#deg` / `#rad` / `#gra` setting; pass an angle with explicit units (`deg`, `rad`, `gra`) to override.",
                Example = "sin(30*deg)",
                Category = "Functions/Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar in [-1, 1]",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Angle" }]
            },
            new SnippetItem
            {
                Insert = "cos(§)",
                Description = "Cosine",
                Documentation = "Computes the cosine of an angle. The angle is interpreted using the current `#deg` / `#rad` / `#gra` setting; pass an angle with explicit units to override.",
                Example = "cos(60*deg)",
                Category = "Functions/Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar in [-1, 1]",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Angle" }]
            },
            new SnippetItem
            {
                Insert = "tan(§)",
                Description = "Tangent",
                Documentation = "Computes the tangent (sin/cos) of an angle. Undefined at odd multiples of π/2 (90°, 270°, ...).",
                Example = "tan(45*deg)",
                Category = "Functions/Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Angle" }]
            },
            new SnippetItem
            {
                Insert = "csc(§)",
                Description = "Cosecant",
                Documentation = "Computes the cosecant (1/sin) of an angle. Undefined at integer multiples of π (0°, 180°, ...).",
                Category = "Functions/Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Angle" }]
            },
            new SnippetItem
            {
                Insert = "sec(§)",
                Description = "Secant",
                Documentation = "Computes the secant (1/cos) of an angle. Undefined at odd multiples of π/2 (90°, 270°, ...).",
                Category = "Functions/Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Angle" }]
            },
            new SnippetItem
            {
                Insert = "cot(§)",
                Description = "Cotangent",
                Documentation = "Computes the cotangent (cos/sin) of an angle. Undefined at integer multiples of π (0°, 180°, ...).",
                Category = "Functions/Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Angle" }]
            },

            // ============================================
            // INVERSE TRIGONOMETRIC FUNCTIONS
            // ============================================
            // Inverse trig functions return an angle in the unit selected by the current
            // #deg / #rad / #gra setting.
            new SnippetItem
            {
                Insert = "asin(§)",
                Description = "Inverse sine (arc sine)",
                Documentation = "Returns the angle whose sine is `x`. Result is in the range [-π/2, π/2] (or [-90°, 90°]). Errors if |x| > 1.",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value in [-1, 1]" }]
            },
            new SnippetItem
            {
                Insert = "acos(§)",
                Description = "Inverse cosine (arc cosine)",
                Documentation = "Returns the angle whose cosine is `x`. Result is in the range [0, π] (or [0°, 180°]). Errors if |x| > 1.",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value in [-1, 1]" }]
            },
            new SnippetItem
            {
                Insert = "atan(§)",
                Description = "Inverse tangent (arc tangent)",
                Documentation = "Returns the angle whose tangent is `x`. Result is in the range (-π/2, π/2). For full-quadrant resolution from `(x, y)` coordinates, use `atan2`.",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "atan2(§; §)",
                Description = "Two-argument arc tangent",
                Documentation = "Returns the angle of the vector `(x, y)` measured from the positive x-axis, with the correct quadrant. Result is in (-π, π] (or (-180°, 180°]).",
                Example = "atan2(1; 1)  ' returns 45°",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "X coordinate" },
                    new SnippetParameter { Name = "y", Type = ParameterType.Scalar, Description = "Y coordinate" }
                ]
            },
            new SnippetItem
            {
                Insert = "acsc(§)",
                Description = "Inverse cosecant",
                Documentation = "Returns the angle whose cosecant is `x`. Defined for |x| ≥ 1.",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "asec(§)",
                Description = "Inverse secant",
                Documentation = "Returns the angle whose secant is `x`. Defined for |x| ≥ 1.",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "acot(§)",
                Description = "Inverse cotangent",
                Documentation = "Returns the angle whose cotangent is `x`.",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },

            // ============================================
            // HYPERBOLIC FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "sinh(§)",
                Description = "Hyperbolic sine",
                Documentation = "Computes (e^x − e^-x) / 2. Argument is dimensionless.",
                Category = "Functions/Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "cosh(§)",
                Description = "Hyperbolic cosine",
                Documentation = "Computes (e^x + e^-x) / 2. Argument is dimensionless. Result is always ≥ 1.",
                Category = "Functions/Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar ≥ 1",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "tanh(§)",
                Description = "Hyperbolic tangent",
                Documentation = "Computes sinh(x) / cosh(x). Result is in (-1, 1) and approaches ±1 for large |x|.",
                Category = "Functions/Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar in (-1, 1)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "csch(§)",
                Description = "Hyperbolic cosecant",
                Documentation = "Computes 1 / sinh(x). Undefined at x = 0.",
                Category = "Functions/Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "sech(§)",
                Description = "Hyperbolic secant",
                Documentation = "Computes 1 / cosh(x). Result is in (0, 1].",
                Category = "Functions/Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar in (0, 1]",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "coth(§)",
                Description = "Hyperbolic cotangent",
                Documentation = "Computes cosh(x) / sinh(x). Undefined at x = 0.",
                Category = "Functions/Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },

            // ============================================
            // INVERSE HYPERBOLIC FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "asinh(§)",
                Description = "Inverse hyperbolic sine",
                Documentation = "Returns the value whose hyperbolic sine is `x`. Equivalent to `ln(x + sqrt(x² + 1))`. Defined for all real `x`.",
                Category = "Functions/Inverse Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "acosh(§)",
                Description = "Inverse hyperbolic cosine",
                Documentation = "Returns the non-negative value whose hyperbolic cosine is `x`. Equivalent to `ln(x + sqrt(x² − 1))`. Errors if `x < 1`.",
                Category = "Functions/Inverse Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar ≥ 0",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value ≥ 1" }]
            },
            new SnippetItem
            {
                Insert = "atanh(§)",
                Description = "Inverse hyperbolic tangent",
                Documentation = "Returns the value whose hyperbolic tangent is `x`. Equivalent to `0.5 · ln((1 + x) / (1 − x))`. Errors if `|x| ≥ 1`.",
                Category = "Functions/Inverse Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value in (-1, 1)" }]
            },
            new SnippetItem
            {
                Insert = "acsch(§)",
                Description = "Inverse hyperbolic cosecant",
                Documentation = "Returns the value whose hyperbolic cosecant is `x`. Equivalent to `asinh(1/x)`. Undefined at `x = 0`.",
                Category = "Functions/Inverse Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value ≠ 0" }]
            },
            new SnippetItem
            {
                Insert = "asech(§)",
                Description = "Inverse hyperbolic secant",
                Documentation = "Returns the value whose hyperbolic secant is `x`. Equivalent to `acosh(1/x)`. Defined for `x ∈ (0, 1]`.",
                Category = "Functions/Inverse Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar ≥ 0",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value in (0, 1]" }]
            },
            new SnippetItem
            {
                Insert = "acoth(§)",
                Description = "Inverse hyperbolic cotangent",
                Documentation = "Returns the value whose hyperbolic cotangent is `x`. Equivalent to `atanh(1/x)`. Defined for `|x| > 1`.",
                Category = "Functions/Inverse Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value with |x| > 1" }]
            },

            // ============================================
            // LOGARITHMIC, EXPONENTIAL AND ROOTS
            // ============================================
            new SnippetItem
            {
                Insert = "log(§)",
                Description = "Decimal (base-10) logarithm",
                Documentation = "Computes log₁₀(x). Errors when `x ≤ 0` (real mode). For complex mode, the principal branch is used.",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value > 0" }]
            },
            new SnippetItem
            {
                Insert = "ln(§)",
                Description = "Natural logarithm",
                Documentation = "Computes the natural logarithm (base e) of `x`. Errors when `x ≤ 0` (real mode).",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value > 0" }]
            },
            new SnippetItem
            {
                Insert = "log_2(§)",
                Description = "Binary (base-2) logarithm",
                Documentation = "Computes log₂(x). Errors when `x ≤ 0` (real mode).",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value > 0" }]
            },
            new SnippetItem
            {
                Insert = "exp(§)",
                Description = "Exponential function (e^x)",
                Documentation = "Computes Euler's number `e` raised to the power `x`. Inverse of `ln`.",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar > 0",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Exponent" }]
            },
            new SnippetItem
            {
                Insert = "sqr(§)",
                Description = "Square root",
                Documentation = "Returns the principal square root of `x`. Identical to `sqrt`. Errors for negative `x` in real mode.",
                Label = "sqr(x)",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar ≥ 0",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value ≥ 0" }]
            },
            new SnippetItem
            {
                Insert = "sqrt(§)",
                Description = "Square root",
                Documentation = "Returns the principal square root of `x`. Identical to `sqr`. Errors for negative `x` in real mode.",
                Label = "sqrt(x)",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar ≥ 0",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value ≥ 0" }]
            },
            new SnippetItem
            {
                Insert = "cbrt(§)",
                Description = "Cubic root",
                Documentation = "Returns the real cube root of `x`. Defined for all real `x`, including negatives.",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "root(§; §)",
                Description = "N-th root",
                Documentation = "Returns the principal n-th root of `x`, equivalent to `x^(1/n)`. Use a positive integer for `n`.",
                Example = "root(8; 3)  ' returns 2",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" },
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Root degree" }
                ]
            },

            // ============================================
            // ROUNDING FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "round(§)",
                Description = "Round to the nearest integer",
                Documentation = "Rounds `x` to the nearest integer using banker's rounding (ties go to even). Preserves units.",
                Example = "round(2.5)  ' returns 2",
                Category = "Functions/Rounding",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar (integer-valued)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "floor(§)",
                Description = "Round down (towards -∞)",
                Documentation = "Returns the largest integer not exceeding `x`. Preserves units. For negative numbers, rounds away from zero.",
                Example = "floor(-1.2)  ' returns -2",
                Category = "Functions/Rounding",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar (integer-valued)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "ceiling(§)",
                Description = "Round up (towards +∞)",
                Documentation = "Returns the smallest integer not less than `x`. Preserves units.",
                Example = "ceiling(1.2)  ' returns 2",
                Category = "Functions/Rounding",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar (integer-valued)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "trunc(§)",
                Description = "Truncate towards zero",
                Documentation = "Discards the fractional part of `x`, leaving only the integer portion. Preserves units. Same as `floor` for positive `x`, `ceiling` for negative `x`.",
                Category = "Functions/Rounding",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar (integer-valued)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },

            // ============================================
            // INTEGER FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "mod(§; §)",
                Description = "Remainder of integer division",
                Documentation = "Computes `x mod y`, i.e., the remainder after dividing `x` by `y`. Sign of the result follows `x`.",
                Example = "mod(10; 3)  ' returns 1",
                Category = "Functions/Integer",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar (integer-valued)",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Integer, Description = "Dividend" },
                    new SnippetParameter { Name = "y", Type = ParameterType.Integer, Description = "Divisor" }
                ]
            },
            new SnippetItem
            {
                Insert = "gcd(§; §)",
                Description = "Greatest common divisor",
                Documentation = "Returns the largest positive integer that divides every argument exactly. Accepts two or more arguments.",
                Example = "gcd(12; 18; 30)  ' returns 6",
                Category = "Functions/Integer",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Positive integer",
                Parameters =
                [
                    new SnippetParameter { Name = "values", Type = ParameterType.Integer, Description = "Two or more integers", IsVariadic = true }
                ]
            },
            new SnippetItem
            {
                Insert = "lcm(§; §)",
                Description = "Least common multiple",
                Documentation = "Returns the smallest positive integer that is a multiple of every argument. Accepts two or more arguments.",
                Example = "lcm(4; 6)  ' returns 12",
                Category = "Functions/Integer",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Positive integer",
                Parameters =
                [
                    new SnippetParameter { Name = "values", Type = ParameterType.Integer, Description = "Two or more integers", IsVariadic = true }
                ]
            },

            // ============================================
            // COMPLEX NUMBER FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "re(§)",
                Description = "Real part of a complex number",
                Documentation = "Returns the real part of `z`. For a real input, returns `z` unchanged.",
                Category = "Functions/Complex",
                KeywordType = "Function",
                ReturnTypeDescription = "Real scalar",
                Parameters = [new SnippetParameter { Name = "z", Type = ParameterType.Scalar, Description = "Complex number" }]
            },
            new SnippetItem
            {
                Insert = "im(§)",
                Description = "Imaginary part of a complex number",
                Documentation = "Returns the imaginary part of `z` as a real scalar. For a real input, returns 0.",
                Category = "Functions/Complex",
                KeywordType = "Function",
                ReturnTypeDescription = "Real scalar",
                Parameters = [new SnippetParameter { Name = "z", Type = ParameterType.Scalar, Description = "Complex number" }]
            },
            new SnippetItem
            {
                Insert = "abs(§)",
                Description = "Absolute value / magnitude",
                Documentation = "Returns |z|. For a real number, the unsigned magnitude. For a complex number, `sqrt(re² + im²)`. Element-wise on vectors and matrices.",
                Category = "Functions/Complex",
                KeywordType = "Function",
                IsElementWise = true,
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "z", Type = ParameterType.Any, Description = "Value or complex number" }]
            },
            new SnippetItem
            {
                Insert = "phase(§)",
                Description = "Phase angle of a complex number",
                Documentation = "Returns the argument (phase angle) of `z`, equivalent to `atan2(im(z); re(z))`. Result is in the current angle unit.",
                Category = "Functions/Complex",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "z", Type = ParameterType.Scalar, Description = "Complex number" }]
            },
            new SnippetItem
            {
                Insert = "conj(§)",
                Description = "Complex conjugate",
                Documentation = "Returns the complex conjugate of `z`, i.e., flips the sign of the imaginary part. For real input, returns `z` unchanged.",
                Category = "Functions/Complex",
                KeywordType = "Function",
                ReturnTypeDescription = "Complex scalar",
                Parameters = [new SnippetParameter { Name = "z", Type = ParameterType.Scalar, Description = "Complex number" }]
            },

            // ============================================
            // AGGREGATE AND INTERPOLATION FUNCTIONS
            // ============================================
            // min - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "min(v₁; v₂; ...)",
                Description = "Minimum of multiple values (scalars, vectors, or matrices)",
                Documentation = "Returns the smallest value among all arguments. Vector/matrix arguments are flattened element-wise before comparison.",
                Example = "min(3; 7; 1; 5)  ' returns 1",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // min - single vector
            new SnippetItem
            {
                Insert = "min(§)",
                Description = "Minimum element of a vector",
                Documentation = "Returns the smallest element of `v`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // min - single matrix
            new SnippetItem
            {
                Insert = "min(§)",
                Description = "Minimum element of a matrix",
                Documentation = "Returns the smallest element of `M` across all rows and columns.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // max - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "max(v₁; v₂; ...)",
                Description = "Maximum of multiple values (scalars, vectors, or matrices)",
                Documentation = "Returns the largest value among all arguments. Vector/matrix arguments are flattened element-wise before comparison.",
                Example = "max(3; 7; 1; 5)  ' returns 7",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // max - single vector
            new SnippetItem
            {
                Insert = "max(§)",
                Description = "Maximum element of a vector",
                Documentation = "Returns the largest element of `v`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // max - single matrix
            new SnippetItem
            {
                Insert = "max(§)",
                Description = "Maximum element of a matrix",
                Documentation = "Returns the largest element of `M` across all rows and columns.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // sum - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "sum(v₁; v₂; ...)",
                Description = "Sum of multiple values (scalars, vectors, or matrices)",
                Documentation = "Adds every element of every argument. Vector/matrix arguments are flattened first.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // sum - single vector
            new SnippetItem
            {
                Insert = "sum(§)",
                Description = "Sum of all elements in a vector",
                Documentation = "Adds every element of `v` and returns the result.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // sum - single matrix
            new SnippetItem
            {
                Insert = "sum(§)",
                Description = "Sum of all elements in a matrix",
                Documentation = "Adds every element of `M` (across rows and columns) and returns the result.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // sumsq - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "sumsq(v₁; v₂; ...)",
                Description = "Sum of squares of multiple values (scalars, vectors, or matrices)",
                Documentation = "Sums `xᵢ²` over all arguments. Useful for variance/energy calculations.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // sumsq - single vector
            new SnippetItem
            {
                Insert = "sumsq(§)",
                Description = "Sum of squares of all elements in a vector",
                Documentation = "Returns Σ vᵢ². Equivalent to `v · v` (dot product with itself).",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // sumsq - single matrix
            new SnippetItem
            {
                Insert = "sumsq(§)",
                Description = "Sum of squares of all elements in a matrix",
                Documentation = "Returns Σ Mᵢⱼ² over the entire matrix (Frobenius norm squared).",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // srss - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "srss(v₁; v₂; ...)",
                Description = "Square root of sum of squares of multiple values (scalars, vectors, or matrices)",
                Documentation = "Computes `sqrt(Σ xᵢ²)`. Common in engineering for combining orthogonal components (e.g., load cases, modal responses).",
                Example = "srss(3; 4)  ' returns 5",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // srss - single vector
            new SnippetItem
            {
                Insert = "srss(§)",
                Description = "Square root of sum of squares of all elements in a vector",
                Documentation = "Returns the Euclidean norm `sqrt(Σ vᵢ²)` of the vector.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // srss - single matrix
            new SnippetItem
            {
                Insert = "srss(§)",
                Description = "Square root of sum of squares of all elements in a matrix",
                Documentation = "Returns the Frobenius norm `sqrt(Σ Mᵢⱼ²)` of the matrix.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // average - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "average(v₁; v₂; ...)",
                Description = "Arithmetic mean of multiple values (scalars, vectors, or matrices)",
                Documentation = "Returns `(Σ xᵢ) / n`, where `n` is the total element count after flattening vector/matrix arguments.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // average - single vector
            new SnippetItem
            {
                Insert = "average(§)",
                Description = "Arithmetic mean of all elements in a vector",
                Documentation = "Returns `sum(v) / length(v)`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // average - single matrix
            new SnippetItem
            {
                Insert = "average(§)",
                Description = "Arithmetic mean of all elements in a matrix",
                Documentation = "Returns the sum of all elements divided by the total number of elements (rows × cols).",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // product - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "product(v₁; v₂; ...)",
                Description = "Product of multiple values (scalars, vectors, or matrices)",
                Documentation = "Multiplies every element of every argument together. Returns 1 for an empty input list.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // product - single vector
            new SnippetItem
            {
                Insert = "product(§)",
                Description = "Product of all elements in a vector",
                Documentation = "Returns Π vᵢ over the elements of `v`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // product - single matrix
            new SnippetItem
            {
                Insert = "product(§)",
                Description = "Product of all elements in a matrix",
                Documentation = "Returns Π Mᵢⱼ over the entire matrix.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // mean - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "mean(v₁; v₂; ...)",
                Description = "Geometric mean of multiple values (scalars, vectors, or matrices)",
                Documentation = "Returns the n-th root of the product of all `n` values: `(Π xᵢ)^(1/n)`. All arguments must be positive.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Positive scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // mean - single vector
            new SnippetItem
            {
                Insert = "mean(§)",
                Description = "Geometric mean of all elements in a vector",
                Documentation = "Returns `(Π vᵢ)^(1/n)` where `n = length(v)`. All elements must be positive.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Positive scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // mean - single matrix
            new SnippetItem
            {
                Insert = "mean(§)",
                Description = "Geometric mean of all elements in a matrix",
                Documentation = "Returns `(Π Mᵢⱼ)^(1/n)` over all `n = rows × cols` elements. All elements must be positive.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Positive scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // take - scalar variadic (n; v1; v2; v3; ...)
            new SnippetItem
            {
                Insert = "take(§; §)",
                Description = "Returns the n-th element from a list of scalars",
                Documentation = "Picks the `n`-th argument (1-based) from the variadic list. Errors if `n` is out of range.",
                Example = "take(2; 10; 20; 30)  ' returns 20",
                Category = "Functions/Aggregate and Interpolation",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Index (1-based)" },
                    new SnippetParameter { Name = "values", Type = ParameterType.Scalar, Description = "Scalar values", IsVariadic = true }
                ]
            },
            // take - from vector (n; vector)
            new SnippetItem
            {
                Insert = "take(§; §)",
                Description = "Returns the n-th element from a vector",
                Documentation = "Returns `v[n]` (1-based). Errors if `n` is out of range. To take multiple elements, use `extractRows`/`vec_slice`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Index (1-based)" },
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }
                ]
            },
            // line - scalar pairs (x; x1; y1; x2; y2; ...)
            new SnippetItem
            {
                Insert = "line(§; §)",
                Description = "Linear interpolation with scalar data pairs",
                Documentation = "Performs piecewise linear interpolation at `x` over the data pairs `(x1, y1), (x2, y2), …`. Pairs must be sorted by `x` ascending.",
                Example = "line(2.5; 0; 0; 1; 10; 5; 50)  ' interpolates between (1,10) and (5,50)",
                Category = "Functions/Aggregate and Interpolation",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Interpolation point" },
                    new SnippetParameter { Name = "data", Type = ParameterType.Scalar, Description = "Data pairs (x1; y1; x2; y2; ...)", IsVariadic = true }
                ]
            },
            // line - with vectors (x; xVector; yVector)
            new SnippetItem
            {
                Insert = "line(§; §; §)",
                Description = "Linear interpolation with x and y vectors",
                Documentation = "Performs piecewise linear interpolation at `x` using parallel `xData` and `yData` vectors. `xData` must be sorted ascending and have the same length as `yData`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Interpolation point" },
                    new SnippetParameter { Name = "xData", Type = ParameterType.Vector, Description = "X data points vector" },
                    new SnippetParameter { Name = "yData", Type = ParameterType.Vector, Description = "Y data points vector" }
                ]
            },
            // spline - scalar pairs (x; x1; y1; x2; y2; ...)
            new SnippetItem
            {
                Insert = "spline(§; §)",
                Description = "Hermite spline interpolation with scalar data pairs",
                Documentation = "Performs smooth Hermite cubic spline interpolation at `x` over the data pairs. Smoother than `line` but more expensive.",
                Category = "Functions/Aggregate and Interpolation",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Interpolation point" },
                    new SnippetParameter { Name = "data", Type = ParameterType.Scalar, Description = "Data pairs (x1; y1; x2; y2; ...)", IsVariadic = true }
                ]
            },
            // spline - with vectors (x; xVector; yVector)
            new SnippetItem
            {
                Insert = "spline(§; §; §)",
                Description = "Hermite spline interpolation with x and y vectors",
                Documentation = "Performs smooth Hermite cubic spline interpolation at `x` using parallel `xData` and `yData` vectors. `xData` must be sorted ascending.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Interpolation point" },
                    new SnippetParameter { Name = "xData", Type = ParameterType.Vector, Description = "X data points vector" },
                    new SnippetParameter { Name = "yData", Type = ParameterType.Vector, Description = "Y data points vector" }
                ]
            },

            // ============================================
            // CONDITIONAL AND LOGICAL FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "if(§; §; §)",
                Description = "Conditional evaluation",
                Documentation = "Returns `value-if-true` when `cond` is non-zero, otherwise `value-if-false`. Both branches are evaluated.",
                Example = "if(x > 0; x; -x)  ' absolute value",
                Category = "Functions/Conditional and Logical",
                KeywordType = "Function",
                ReturnTypeDescription = "Same type as the chosen branch",
                Parameters =
                [
                    new SnippetParameter { Name = "cond", Type = ParameterType.Boolean, Description = "Condition" },
                    new SnippetParameter { Name = "value-if-true", Type = ParameterType.Any, Description = "Value if condition is true" },
                    new SnippetParameter { Name = "value-if-false", Type = ParameterType.Any, Description = "Value if condition is false" }
                ]
            },
            new SnippetItem
            {
                Insert = "switch(c₁; v₁; c₂; v₂; …; def)",
                Description = "Selective evaluation (multiple conditions)",
                Documentation = "Evaluates conditions in order and returns the value paired with the first true condition. The final unpaired argument is the default returned when no condition matches.",
                Example = "switch(x < 0; -1; x > 0; 1; 0)  ' sign of x",
                Category = "Functions/Conditional and Logical",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Same type as the matched value",
                Parameters =
                [
                    new SnippetParameter { Name = "pairs", Type = ParameterType.Various, Description = "Condition/value pairs followed by default value", IsVariadic = true }
                ]
            },
            new SnippetItem
            {
                Insert = "not(§)",
                Description = "Logical NOT",
                Documentation = "Returns 1 if `x` is 0, otherwise 0. Use with comparisons and the boolean `and`/`or` family.",
                Category = "Functions/Conditional and Logical",
                KeywordType = "Function",
                ReturnTypeDescription = "Boolean (0 or 1)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Boolean, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "and(§; §)",
                Description = "Logical AND of multiple boolean values",
                Documentation = "Returns 1 if every argument is non-zero, otherwise 0. Accepts two or more arguments. Note: this is **not** short-circuit — all arguments are evaluated.",
                Category = "Functions/Conditional and Logical",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Boolean (0 or 1)",
                Parameters =
                [
                    new SnippetParameter { Name = "values", Type = ParameterType.Boolean, Description = "Boolean values", IsVariadic = true }
                ]
            },
            new SnippetItem
            {
                Insert = "or(§; §)",
                Description = "Logical OR of multiple boolean values",
                Documentation = "Returns 1 if any argument is non-zero, otherwise 0. Accepts two or more arguments. Not short-circuit — all arguments are evaluated.",
                Category = "Functions/Conditional and Logical",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Boolean (0 or 1)",
                Parameters =
                [
                    new SnippetParameter { Name = "values", Type = ParameterType.Boolean, Description = "Boolean values", IsVariadic = true }
                ]
            },
            new SnippetItem
            {
                Insert = "xor(§; §)",
                Description = "Logical XOR of multiple boolean values",
                Documentation = "Returns 1 if an odd number of arguments are non-zero, otherwise 0. Accepts two or more arguments.",
                Category = "Functions/Conditional and Logical",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Boolean (0 or 1)",
                Parameters =
                [
                    new SnippetParameter { Name = "values", Type = ParameterType.Boolean, Description = "Boolean values", IsVariadic = true }
                ]
            },

            // ============================================
            // OTHER FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "sign(§)",
                Description = "Sign of a number (-1, 0, or 1)",
                Documentation = "Returns -1 for negative `x`, 0 for `x = 0`, and 1 for positive `x`. Strips units.",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar in {-1, 0, 1}",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "random(§)",
                Description = "Random number between 0 and x",
                Documentation = "Returns a uniformly distributed random scalar in `[0, x)`. With no argument, returns a value in `[0, 1)`. Re-evaluating the document re-rolls the value.",
                Example = "random(100)  ' random integer-ish value in [0, 100)",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar in [0, x)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Upper bound", IsOptional = true }]
            },
            new SnippetItem
            {
                Insert = "getunits(§)",
                Description = "Gets the units of x (returns 1 if unitless)",
                Documentation = "Extracts the unit dimension of `x` as a value of magnitude 1. Useful for stripping the numeric part: `x / getunits(x)` gives the magnitude.",
                Example = "u = getunits(50*kN)  ' u = 1*kN",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar with the same units as x, magnitude 1",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Any, Description = "Value with units" }]
            },
            new SnippetItem
            {
                Insert = "setunits(§; §)",
                Description = "Sets the units to a scalar, vector, or matrix",
                Documentation = "Reinterprets `x` as having the units of `u` (without numerical conversion). To convert between compatible units, use the `|` operator instead.",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Same shape as x, with units of u",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Any, Description = "Value" },
                    new SnippetParameter { Name = "u", Type = ParameterType.Any, Description = "Units to set" }
                ]
            },
            new SnippetItem
            {
                Insert = "clrunits(§)",
                Description = "Clears the units from a scalar, vector, or matrix",
                Documentation = "Strips units from `x`, returning a dimensionless value with the same magnitude. Useful before passing to functions that require dimensionless input.",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Same shape as x, dimensionless",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Any, Description = "Value with units" }]
            },
            new SnippetItem
            {
                Insert = "hp(§)",
                Description = "Converts to high-performance type",
                Documentation = "Converts a vector or matrix to a high-performance internal representation (single-precision, contiguous storage). Use for large numerical workloads where memory and speed matter; precision is reduced.",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector or matrix (high-performance)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Any, Description = "Vector or matrix" }]
            },
            new SnippetItem
            {
                Insert = "ishp(§)",
                Description = "Checks if the type is high-performance",
                Documentation = "Returns 1 if `x` is stored in the high-performance representation (see `hp`), otherwise 0.",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Boolean (0 or 1)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Any, Description = "Value to check" }]
            },

            // ============================================
            // STRING FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "len$(§)",
                Description = "Returns the length of a string",
                Documentation = "Returns the number of characters in `s$` as a unitless scalar.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.Value,
                ReturnTypeDescription = "Non-negative integer",
                Parameters = [new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String" }]
            },
            new SnippetItem
            {
                Insert = "trim$(§)",
                Description = "Trims whitespace from both ends of a string",
                Documentation = "Returns `s$` with leading and trailing whitespace removed.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters = [new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String" }]
            },
            new SnippetItem
            {
                Insert = "ltrim$(§)",
                Description = "Trims whitespace from the start of a string",
                Documentation = "Returns `s$` with leading whitespace removed.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters = [new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String" }]
            },
            new SnippetItem
            {
                Insert = "rtrim$(§)",
                Description = "Trims whitespace from the end of a string",
                Documentation = "Returns `s$` with trailing whitespace removed.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters = [new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String" }]
            },
            new SnippetItem
            {
                Insert = "ucase$(§)",
                Description = "Converts a string to upper case",
                Documentation = "Returns `s$` with all letters converted to upper case using the invariant culture.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters = [new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String" }]
            },
            new SnippetItem
            {
                Insert = "lcase$(§)",
                Description = "Converts a string to lower case",
                Documentation = "Returns `s$` with all letters converted to lower case using the invariant culture.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters = [new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String" }]
            },
            new SnippetItem
            {
                Insert = "string$(§)",
                Description = "Converts a value to its string representation (without units)",
                Documentation = "Formats `x` as a string using the document's current decimal-places setting. Units are stripped — use `string$(x; 'true')` to include them.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Any, Description = "Value to convert" }]
            },
            new SnippetItem
            {
                Insert = "string$(§; 'true')",
                Description = "Converts a value to its string representation including units",
                Documentation = "Formats `x` as a string. When `includeUnits` is `'true'`, the unit suffix is appended.",
                Label = "string$ (with units)",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Any, Description = "Value to convert" },
                    new SnippetParameter { Name = "includeUnits", Type = ParameterType.String, Description = "'true' to include units, 'false' to exclude" }
                ]
            },
            new SnippetItem
            {
                Insert = "val$(§)",
                Description = "Parses a string to a numeric value",
                Documentation = "Parses `s$` as a number using invariant-culture decimal point. Errors if the string is not a valid number. Units are stripped — use `val$(s$; 'true')` to include them.",
                Example = "val$('3.14')  ' returns 3.14",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.Value,
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String to parse" }]
            },
            new SnippetItem
            {
                Insert = "val$(§; 'true')",
                Description = "Parses a string to a numeric value preserving units",
                Documentation = "Parses `s$` as a number. When `includeUnits` is `'true'`, the unit suffix is retained (e.g., `val$('3.5 kN'; 'true')` returns `3.5 kN`). For table variables, each cell's units are preserved in the resulting matrix literal.",
                Label = "val$ (with units)",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.Value,
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String or table variable to parse" },
                    new SnippetParameter { Name = "includeUnits", Type = ParameterType.String, Description = "'true' to preserve units, 'false' to strip" }
                ]
            },
            new SnippetItem
            {
                Insert = "left$(§; §)",
                Description = "Returns the leftmost characters of a string",
                Documentation = "Returns the first `count` characters of `s$`. If `count` exceeds the string length, returns the whole string.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters =
                [
                    new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String" },
                    new SnippetParameter { Name = "count", Type = ParameterType.Integer, Description = "Number of characters" }
                ]
            },
            new SnippetItem
            {
                Insert = "right$(§; §)",
                Description = "Returns the rightmost characters of a string",
                Documentation = "Returns the last `count` characters of `s$`. If `count` exceeds the string length, returns the whole string.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters =
                [
                    new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String" },
                    new SnippetParameter { Name = "count", Type = ParameterType.Integer, Description = "Number of characters" }
                ]
            },
            new SnippetItem
            {
                Insert = "compare$(§; §)",
                Description = "Compares two strings (-1, 0, or 1)",
                Documentation = "Lexicographic ordinal comparison. Returns -1 if `a$` precedes `b$`, 0 if they are equal, 1 if `a$` follows `b$`.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.Value,
                ReturnTypeDescription = "Scalar in {-1, 0, 1}",
                Parameters =
                [
                    new SnippetParameter { Name = "a$", Type = ParameterType.String, Description = "First string" },
                    new SnippetParameter { Name = "b$", Type = ParameterType.String, Description = "Second string" }
                ]
            },
            new SnippetItem
            {
                Insert = "space$(§)",
                Description = "Creates a string of spaces",
                Documentation = "Returns a string consisting of `count` space characters. Useful for fixed-width formatting.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters = [new SnippetParameter { Name = "count", Type = ParameterType.Integer, Description = "Number of spaces" }]
            },
            new SnippetItem
            {
                Insert = "chr$('newline')",
                Description = "Returns the newline character (\\n)",
                Documentation = "Returns the literal newline character. Calcpad string literals don't process backslash escapes, so use `chr$('newline')` to embed an actual newline — e.g. `join$(tbl$; chr$('newline'); ', ')` to render each table row on its own line.",
                Label = "chr$ (newline)",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters = [new SnippetParameter { Name = "name", Type = ParameterType.String, Description = "Character name: 'newline'" }]
            },
            new SnippetItem
            {
                Insert = "mid$(§; §; §)",
                Description = "Extracts a substring from a string",
                Documentation = "Returns the substring of `s$` starting at position `start` (1-based) with length `count`. Result is clipped if it would extend past the end of the string.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters =
                [
                    new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String" },
                    new SnippetParameter { Name = "start", Type = ParameterType.Integer, Description = "Start position (1-based)" },
                    new SnippetParameter { Name = "count", Type = ParameterType.Integer, Description = "Number of characters" }
                ]
            },
            new SnippetItem
            {
                Insert = "replace$(§; §; §)",
                Description = "Replaces all occurrences of a substring",
                Documentation = "Returns `s$` with every occurrence of `old$` replaced by `new$`. Case-sensitive, ordinal match.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters =
                [
                    new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "Source string" },
                    new SnippetParameter { Name = "old$", Type = ParameterType.String, Description = "String to find" },
                    new SnippetParameter { Name = "new$", Type = ParameterType.String, Description = "Replacement string" }
                ]
            },
            new SnippetItem
            {
                Insert = "instr$(§; §; §)",
                Description = "Finds the position of a substring (1-based, 0 if not found)",
                Documentation = "Searches `s$` from position `start` (1-based) for the first occurrence of `search$`. Returns the 1-based index, or 0 if not found.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.Value,
                ReturnTypeDescription = "Non-negative integer",
                Parameters =
                [
                    new SnippetParameter { Name = "start", Type = ParameterType.Integer, Description = "Start position (1-based)" },
                    new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String to search in" },
                    new SnippetParameter { Name = "search$", Type = ParameterType.String, Description = "String to find" }
                ]
            },
            new SnippetItem
            {
                Insert = "find$(§; §)",
                Description = "Finds all positions of a substring, returns a vector",
                Documentation = "Returns a vector of 1-based positions where `search$` occurs in `s$`. Empty vector if not found.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.Value,
                ReturnTypeDescription = "Vector of positions",
                Parameters =
                [
                    new SnippetParameter { Name = "search$", Type = ParameterType.String, Description = "String to find" },
                    new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String to search in" }
                ]
            },
            new SnippetItem
            {
                Insert = "concat$(§; §)",
                Description = "Concatenates multiple strings",
                Documentation = "Joins all arguments into a single string in order. Numeric arguments are converted to strings using the document's formatting.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters =
                [
                    new SnippetParameter { Name = "values", Type = ParameterType.Any, Description = "Strings to concatenate", IsVariadic = true }
                ]
            },

            // ============================================
            // JSON FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "parsejson$(§; §)",
                Description = "Parses a JSON string and extracts a value at the given path",
                Documentation = "Walks `path$` through `json$` and returns the value at that location as a string. Path uses dot notation for objects and `[i]` for arrays.",
                Example = "parsejson$('{\"a\":[10,20]}'; 'a[1]')  ' returns '20'",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters =
                [
                    new SnippetParameter { Name = "json$", Type = ParameterType.String, Description = "JSON string to parse" },
                    new SnippetParameter { Name = "path$", Type = ParameterType.String, Description = "Path to value (e.g., \"key\", \"nested.arr[0]\")" }
                ]
            },

            // ============================================
            // STRING TABLE FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "table$(§; §)",
                Description = "Creates an empty string table with the specified dimensions",
                Documentation = "Allocates a `rows × cols` table of empty strings. Cells can be assigned via index access.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringTable,
                ReturnTypeDescription = "String table",
                Parameters =
                [
                    new SnippetParameter { Name = "rows", Type = ParameterType.Integer, Description = "Number of rows" },
                    new SnippetParameter { Name = "cols", Type = ParameterType.Integer, Description = "Number of columns" }
                ]
            },
            new SnippetItem
            {
                Insert = "split$(§; §; §)",
                Description = "Splits a string into a table using row and column delimiters",
                Documentation = "First splits `s$` on `rowDelim$` to create rows, then splits each row on `colDelim$`. Useful for parsing CSV-like data.",
                Example = "split$('a,b\\nc,d'; '\\n'; ',')  ' 2x2 table",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringTable,
                ReturnTypeDescription = "String table",
                Parameters =
                [
                    new SnippetParameter { Name = "s$", Type = ParameterType.String, Description = "String to split" },
                    new SnippetParameter { Name = "rowDelim$", Type = ParameterType.String, Description = "Row delimiter" },
                    new SnippetParameter { Name = "colDelim$", Type = ParameterType.String, Description = "Column delimiter" }
                ]
            },
            new SnippetItem
            {
                Insert = "join$(§; §; §)",
                Description = "Joins a table into a string using row and column delimiters",
                Documentation = "Inverse of `split$`. Joins each row with `colDelim$`, then joins rows with `rowDelim$`. Defaults: `rowDelim$ = '\\n'`, `colDelim$ = ','`.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String",
                Parameters =
                [
                    new SnippetParameter { Name = "t$", Type = ParameterType.StringTable, Description = "Table to join" },
                    new SnippetParameter { Name = "rowDelim$", Type = ParameterType.String, Description = "Row delimiter", IsOptional = true },
                    new SnippetParameter { Name = "colDelim$", Type = ParameterType.String, Description = "Column delimiter", IsOptional = true }
                ]
            },
            new SnippetItem
            {
                Insert = "rowToStringArray$(§; §)",
                Description = "Extracts a row from a table as a JSON string array (e.g., [\"a\", \"b\", \"c\"])",
                Documentation = "Returns the `row`-th row of `t$` formatted as a JSON array of strings. Useful for round-tripping table rows through JSON-aware tools.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "JSON array string",
                Parameters =
                [
                    new SnippetParameter { Name = "t$", Type = ParameterType.StringTable, Description = "Table variable" },
                    new SnippetParameter { Name = "row", Type = ParameterType.Integer, Description = "Row index (1-based)" }
                ]
            },
            new SnippetItem
            {
                Insert = "colToStringArray$(§; §)",
                Description = "Extracts a column from a table as a JSON string array (e.g., [\"a\", \"b\", \"c\"])",
                Documentation = "Returns the `col`-th column of `t$` formatted as a JSON array of strings.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "JSON array string",
                Parameters =
                [
                    new SnippetParameter { Name = "t$", Type = ParameterType.StringTable, Description = "Table variable" },
                    new SnippetParameter { Name = "col", Type = ParameterType.Integer, Description = "Column index (1-based)" }
                ]
            },
            new SnippetItem
            {
                Insert = "tableToStringArray$(§)",
                Description = "Converts an entire table to a nested JSON string array (e.g., [[\"a\",\"b\"],[\"c\",\"d\"]])",
                Documentation = "Serializes the whole table as a nested JSON array (rows of strings).",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "JSON nested array string",
                Parameters =
                [
                    new SnippetParameter { Name = "t$", Type = ParameterType.StringTable, Description = "Table variable" }
                ]
            },
            new SnippetItem
            {
                Insert = "typeOf$(§)",
                Description = "Returns the type of an expression as a string (value, complex, vector, matrix, string, table, or undefined)",
                Documentation = "Returns one of `'value'`, `'complex'`, `'vector'`, `'matrix'`, `'string'`, `'table'`, or `'undefined'`. Useful for runtime branching.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringVariable,
                ReturnTypeDescription = "String type tag",
                Parameters =
                [
                    new SnippetParameter { Name = "expr", Type = ParameterType.Any, Description = "Expression or variable to check" }
                ]
            },

            // ============================================
            // STRING TABLE MANIPULATION FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "augmentT$(§; §)",
                Description = "Concatenates two or more tables horizontally (side by side)",
                Documentation = "Returns a new table with all input tables placed side-by-side. All inputs must have the same number of rows.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringTable,
                ReturnTypeDescription = "String table",
                Parameters =
                [
                    new SnippetParameter { Name = "t1$", Type = ParameterType.StringTable, Description = "First table" },
                    new SnippetParameter { Name = "t2$", Type = ParameterType.StringTable, Description = "Second table (more can follow)" }
                ]
            },
            new SnippetItem
            {
                Insert = "stackT$(§; §)",
                Description = "Concatenates two or more tables vertically (stacked)",
                Documentation = "Returns a new table with input tables stacked top to bottom. All inputs must have the same number of columns.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringTable,
                ReturnTypeDescription = "String table",
                Parameters =
                [
                    new SnippetParameter { Name = "t1$", Type = ParameterType.StringTable, Description = "First table" },
                    new SnippetParameter { Name = "t2$", Type = ParameterType.StringTable, Description = "Second table (more can follow)" }
                ]
            },
            new SnippetItem
            {
                Insert = "rowT$(§; §)",
                Description = "Extracts a single row from a table as a 1-row table",
                Documentation = "Returns a 1-row table containing the `row`-th row of `t$`. Use `rowToStringArray$` for a JSON-array view.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringTable,
                ReturnTypeDescription = "1-row string table",
                Parameters =
                [
                    new SnippetParameter { Name = "t$", Type = ParameterType.StringTable, Description = "Table variable" },
                    new SnippetParameter { Name = "row", Type = ParameterType.Integer, Description = "Row index (1-based)" }
                ]
            },
            new SnippetItem
            {
                Insert = "colT$(§; §)",
                Description = "Extracts a single column from a table as a 1-column table",
                Documentation = "Returns a 1-column table containing the `col`-th column of `t$`.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringTable,
                ReturnTypeDescription = "1-column string table",
                Parameters =
                [
                    new SnippetParameter { Name = "t$", Type = ParameterType.StringTable, Description = "Table variable" },
                    new SnippetParameter { Name = "col", Type = ParameterType.Integer, Description = "Column index (1-based)" }
                ]
            },
            new SnippetItem
            {
                Insert = "extractRowsT$(§; [§])",
                Description = "Extracts multiple rows from a table by index",
                Documentation = "Returns a new table with the specified rows of `t$`, in the order given by `indices`. Indices may repeat to duplicate rows.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringTable,
                ReturnTypeDescription = "String table",
                Parameters =
                [
                    new SnippetParameter { Name = "t$", Type = ParameterType.StringTable, Description = "Table variable" },
                    new SnippetParameter { Name = "indices", Type = ParameterType.Vector, Description = "Row indices (1-based, e.g., [1; 3; 5])" }
                ]
            },
            new SnippetItem
            {
                Insert = "extractColsT$(§; [§])",
                Description = "Extracts multiple columns from a table by index",
                Documentation = "Returns a new table with the specified columns of `t$`, in the order given by `indices`.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringTable,
                ReturnTypeDescription = "String table",
                Parameters =
                [
                    new SnippetParameter { Name = "t$", Type = ParameterType.StringTable, Description = "Table variable" },
                    new SnippetParameter { Name = "indices", Type = ParameterType.Vector, Description = "Column indices (1-based, e.g., [1; 3; 5])" }
                ]
            },
            new SnippetItem
            {
                Insert = "subTable$(§; §; §; §; §)",
                Description = "Extracts a rectangular sub-table by row and column bounds",
                Documentation = "Returns the rectangular block of `t$` from `(r1, c1)` to `(r2, c2)` inclusive (1-based, inclusive bounds).",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringTable,
                ReturnTypeDescription = "String table",
                Parameters =
                [
                    new SnippetParameter { Name = "t$", Type = ParameterType.StringTable, Description = "Table variable" },
                    new SnippetParameter { Name = "r1", Type = ParameterType.Integer, Description = "Starting row (1-based)" },
                    new SnippetParameter { Name = "c1", Type = ParameterType.Integer, Description = "Starting column (1-based)" },
                    new SnippetParameter { Name = "r2", Type = ParameterType.Integer, Description = "Ending row (1-based)" },
                    new SnippetParameter { Name = "c2", Type = ParameterType.Integer, Description = "Ending column (1-based)" }
                ]
            },
            new SnippetItem
            {
                Insert = "transposeT$(§)",
                Description = "Transposes a table (swaps rows and columns)",
                Documentation = "Returns a new table where rows and columns are swapped. A `m × n` table becomes `n × m`.",
                Category = "Functions/String",
                KeywordType = "Function",
                ReturnType = CalcpadType.StringTable,
                ReturnTypeDescription = "String table (transposed)",
                Parameters =
                [
                    new SnippetParameter { Name = "t$", Type = ParameterType.StringTable, Description = "Table variable" }
                ]
            }
        ];
    }
}