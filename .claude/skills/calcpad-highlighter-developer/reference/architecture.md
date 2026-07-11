# Highlighter Architecture Reference

## Key Directories

```
Calcpad.Highlighter/
├── ContentResolution/
│   ├── ContentResolver.cs          - 3-stage content processing
│   └── ContentResolverResult.cs    - Result structures
├── Tokenizer/
│   ├── CalcpadTokenizer.cs         - Tokenization engine
│   └── Models/
│       ├── Token.cs                - Token structure
│       ├── TokenType.cs            - 15 token types
│       └── TokenizerResult.cs
├── Linter/
│   ├── CalcpadLinter.cs            - Main orchestrator
│   ├── Constants/
│   │   ├── CalcpadBuiltIns.cs      - Built-in functions, keywords, units
│   │   ├── CalcpadPatterns.cs      - Regex patterns
│   │   ├── ErrorCodes.cs           - CPD-xxxx error codes
│   │   └── FunctionSignatures.cs   - Function registry with types
│   ├── Models/
│   │   ├── CalcpadType.cs          - Type enum (Value, Vector, Matrix, etc.)
│   │   ├── FunctionSignature.cs    - Function metadata
│   │   ├── VariableInfo.cs         - Variable/function metadata
│   │   ├── Stage1Context.cs        - Stage 1 linter context
│   │   ├── Stage2Context.cs        - Stage 2 linter context
│   │   └── Stage3Context.cs        - Stage 3 linter context
│   ├── Helpers/
│   │   ├── TypeTracker.cs          - Type inference engine
│   │   ├── TokenizedLineProvider.cs - Token caching
│   │   ├── LineParser.cs           - Line parsing utilities
│   │   ├── SourceMapper.cs         - Maps lines across stages
│   │   └── ControlBlockHelper.cs   - Control flow validation
│   └── Validators/
│       ├── Stage1/IncludeValidator.cs
│       ├── Stage2/MacroValidator.cs
│       └── Stage3/
│           ├── BalanceValidator.cs      - Brackets/blocks
│           ├── NamingValidator.cs       - Variable names
│           ├── UsageValidator.cs        - Undefined checks
│           ├── SemanticValidator.cs     - Operators/commands
│           └── FunctionTypeValidator.cs - Parameter types
└── Tests/
    ├── LinterTestRunner.cs         - Test harness
    └── Samples/                    - Test .cpd files
```

## Type System

### CalcpadType Enum
```csharp
public enum CalcpadType
{
    Unknown,         // Unresolved type
    Value,           // Scalar numeric (real or complex)
    Vector,          // 1D array [v1; v2; ...]
    Matrix,          // 2D array [r1 | r2 | ...]
    CustomUnit,      // Unit definition (.unitName = expr)
    Function,        // User-defined function
    InlineMacro,     // Single-line macro (#def name$ = expr)
    MultilineMacro,  // Multi-line macro (#def ... #end def)
    Various          // Type changed during execution
}
```

### ParameterType Enum (Function Constraints)
```csharp
public enum ParameterType
{
    Any, Scalar, Vector, Matrix, Integer, String, Boolean, Expression, Various
}
```

### VariableInfo Structure
```csharp
public class VariableInfo
{
    string Name;                    // Variable/function/macro name
    CalcpadType Type;               // Inferred type
    int LineNumber;                 // Definition line (0-based)
    int Column;                     // Definition column
    List<string> Parameters;        // For functions/macros
    string Expression;              // Right-side expression
    string UnitName;                // For custom units
    bool SupportsElementAccess;     // Vector/matrix indexing
    bool IsDollarSuffixed;          // Ends with $
    string Source;                  // "local" or included file
}
```

### FunctionSignature Structure
```csharp
public class FunctionSignature
{
    string Name;
    int MinParams, MaxParams;       // -1 for variadic
    ParameterType[] ParameterTypes;
    CalcpadType ReturnType;
    bool IsElementWise;
    bool AcceptsAnyCount;
    string Description;
}
```

## Error Codes

Error codes follow the pattern CPD-XXYY where XX is the stage/category:

```
CPD-11xx: Stage 1 - Include validation
CPD-22xx: Stage 2 - Macro definitions
CPD-31xx: Stage 3 - Balance (brackets/blocks)
CPD-32xx: Stage 3 - Naming (variable/function names)
CPD-33xx: Stage 3 - Usage (undefined identifiers)
CPD-34xx: Stage 3 - Semantic (operators, commands)
CPD-35xx: Stage 3 - Function type checking
```

## TokenType Enum

```csharp
public enum TokenType
{
    Const, Units, Operator, Variable, Function, Keyword, Command,
    Bracket, Comment, Tag, Input, Include, Macro, HtmlComment, Format
}
```

The tokenizer uses two passes:
1. First pass collects definitions (functions, macros)
2. Second pass tokenizes with definition awareness

## Source Mapping

Lines are mapped across stages for accurate diagnostics:

```csharp
int originalLine = SourceMapper.MapStage3ToOriginal(
    stage3Line, stage3Context, stage2Context, stage1Context);
```

## Calcpad Syntax Reference

### Data Types
- **Scalars**: `3.14`, `1e-5`, `3 - 2i` (complex)
- **Vectors**: `[1; 2; 3; 4; 5]` - semicolon separated
- **Matrices**: `[1; 2 | 3; 4]` - pipe separates rows

### Keywords (start with #)
`#if`, `#else`, `#else if`, `#end if`, `#for`, `#while`, `#repeat`, `#loop`, `#break`, `#continue`, `#def`, `#end def`, `#include`, `#read`, `#write`, `#hide`, `#show`, `#val`, `#equ`, `#round`, `#deg`, `#rad`, `#gra`

### Commands (start with $)
`$Plot`, `$Map`, `$Root`, `$Find`, `$Sup`, `$Inf`, `$Area`, `$Integral`, `$Slope`, `$Sum`, `$Product`, `$Repeat`, `$While`, `$Block`, `$Inline`
