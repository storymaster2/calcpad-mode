# PyCalcpad API Surface Reference

## Calculator Class

Main entry point for calculations:

```csharp
public class Calculator
{
    public string Eval(string expression);
    public string Run(string code);
    public void SetVariable(string name, double value);
    public double GetVariable(string name);
    public void Clear();
}
```

## Parser Class

Low-level expression parsing:

```csharp
public class Parser
{
    public ParseResult Parse(string expression);
    public bool IsValid(string expression);
    public IEnumerable<string> GetVariables();
    public IEnumerable<string> GetFunctions();
}
```

## Settings Classes

```csharp
public class Settings
{
    public MathSettings Math { get; set; }
    public PlotSettings Plot { get; set; }
    public string OutputFormat { get; set; }  // "text", "html", "latex"
}

public class MathSettings
{
    public int DecimalPlaces { get; set; } = 6;
    public bool ShowSubstitution { get; set; } = true;
    public AngleUnit AngleUnit { get; set; } = AngleUnit.Radians;
}

public class PlotSettings
{
    public int Width { get; set; } = 600;
    public int Height { get; set; } = 400;
    public bool UseSvg { get; set; } = true;
}
```

## Converter Class

```csharp
public static class Converter
{
    public static double ToDouble(object value);
    public static double[] ToArray(object vector);
    public static double[,] ToMatrix(object matrix);
    public static object FromArray(double[] array);
}
```

## Usage Examples

### Basic Calculation
```csharp
var calc = new Calculator();
var result = calc.Eval("2 + 2");  // "4"

calc.SetVariable("x", 5);
calc.SetVariable("y", 3);
result = calc.Eval("x^2 + y^2");  // "34"
```

### Running Calcpad Code
```csharp
var calc = new Calculator();
string code = @"
a = 5m
b = 3m
area = a * b
";
string output = calc.Run(code);
```

### With Settings
```csharp
var settings = new Settings
{
    Math = new MathSettings
    {
        DecimalPlaces = 4,
        AngleUnit = AngleUnit.Degrees
    },
    OutputFormat = "html"
};
var calc = new Calculator(settings);
var result = calc.Run(code);
```

### Vector/Matrix Operations
```csharp
var calc = new Calculator();
calc.Run("v = [1; 2; 3; 4; 5]");
var sum = calc.Eval("sum(v)");  // "15"
var array = Converter.ToArray(calc.GetValue("v"));
```

## Error Handling

```csharp
public class CalculationException : Exception
{
    public int Line { get; }
    public int Column { get; }
    public string Expression { get; }
}
```

## Integration with Core

The API wraps Calcpad.Core's `MathParser`:

```csharp
public class Calculator
{
    private readonly MathParser _parser;
    private readonly Settings _settings;

    public Calculator(Settings settings = null)
    {
        _settings = settings ?? new Settings();
        _parser = new MathParser();
        ApplySettings();
    }

    public string Eval(string expression)
    {
        try
        {
            _parser.Parse(expression);
            return _parser.Calculate();
        }
        catch (Exception ex)
        {
            throw new CalculationException(ex.Message);
        }
    }
}
```

## Testing

```csharp
[Fact]
public void Eval_SimpleExpression_ReturnsCorrectResult()
{
    var calc = new Calculator();
    var result = calc.Eval("2 + 2");
    Assert.Equal("4", result);
}

[Fact]
public void SetVariable_ThenEval_UsesVariable()
{
    var calc = new Calculator();
    calc.SetVariable("x", 5);
    var result = calc.Eval("x * 2");
    Assert.Equal("10", result);
}
```
