using System;
using Calcpad.Highlighter.Tokenizer;

public class QuickTest
{
    public static void Main()
    {
        var tokenizer = new CalcpadTokenizer();

        var lines = new[]
        {
            "x = 5 + _ + 5",
            "badLine(a; b; c) = a * b * c + _ + e",
            "multiply(a; b; c) = a * b * c _' Three parameters",
            "'<h5>LRFD (Section 2.3.1)</h5>'",
            "'Case 1 -> 1.4D<br> _",
            "'Case 3a -> 1.2D + 1.6S + 0.5W<br> _",
            "'Case 3b -> 1.2D + 1.6S - 0.5W"
        };

        foreach (var line in lines)
        {
            Console.WriteLine("Line: " + line);
            var result = tokenizer.Tokenize(line);
            Console.WriteLine("  Tokens: " + result.Tokens.Count);
            foreach (var token in result.Tokens)
            {
                Console.WriteLine("    [{0}-{1}] {2}: \"{3}\"", token.Column, token.EndColumn, token.Type, token.Text);
            }
            Console.WriteLine();
        }
    }
}
