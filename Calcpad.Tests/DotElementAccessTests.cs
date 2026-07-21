using Xunit;

namespace Calcpad.Tests
{
    public class DotElementAccessTests
    {
        [Fact]
        public void VectorElementAccessByLiteral()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "v = [10; 20; 30]",
                "v.2"
            ]);
            Assert.Equal(20, result);
        }

        [Fact]
        public void VectorElementAccessByVariable()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "v = [10; 20; 30]",
                "k = 3",
                "v.k"
            ]);
            Assert.Equal(30, result);
        }

        [Fact]
        public void VectorElementAccessByExpression()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "v = [10; 20; 30]",
                "v.(1 + 1)"
            ]);
            Assert.Equal(20, result);
        }

        [Fact]
        public void MatrixElementAccess()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "M = [1; 2 | 3; 4]",
                "M.(2; 1)"
            ]);
            Assert.Equal(3, result);
        }

        [Fact]
        public void VectorAssignmentByElement()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "v = vector(3)",
                "v.1 = 5",
                "v.2 = 7",
                "v.1 + v.2"
            ]);
            Assert.Equal(12, result);
        }

        [Fact]
        public void DottedScalarNameNoLongerCreatesNewVariable()
        {
            var calc = new TestCalc(new());
            // Under the new behavior, k is scalar so k.1 should error
            // (cannot do element access on a scalar). This test asserts
            // that the parser refuses to silently create "k.1" as a name.
            Assert.ThrowsAny<System.Exception>(() => calc.Run([
                "k = 5",
                "k.1 = 10"
            ]));
        }

        [Fact]
        public void CommaInVariableNameStillAllowed()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "k,1 = 7",
                "k,2 = 11",
                "k,1 + k,2"
            ]);
            Assert.Equal(18, result);
        }

        [Fact]
        public void FunctionParameterElementAccessByDot_DefOnly()
        {
            var calc = new TestCalc(new());
            calc.Run([
                "myFirst(v) = v.1"
            ]);
        }

        [Fact]
        public void UnknownVariableElementAccessRejected()
        {
            var calc = new TestCalc(new());
            // b is undefined - element access should fail at evaluation
            // ("Index target must be vector"), proving that tokenization
            // never silently creates a "b.1" scalar.
            var ex = Assert.ThrowsAny<Calcpad.Core.MathParserException>(
                () => calc.Run(["a = b.1"]));
            Assert.Contains("Index target must be vector", ex.Message);
        }

        [Fact]
        public void FunctionDefWithoutDot_Sanity()
        {
            // A simple function definition with no element access - sanity check
            var calc = new TestCalc(new());
            var result = calc.Run([
                "g(x) = x + 1",
                "g(5)"
            ]);
            Assert.Equal(6, result);
        }

        [Fact]
        public void FunctionDefWithKnownVectorElementAccess()
        {
            // A function that does element access on a GLOBAL vector (not parameter)
            var calc = new TestCalc(new());
            var result = calc.Run([
                "v = [10; 20; 30]",
                "h(x) = v.1 + x",
                "h(5)"
            ]);
            Assert.Equal(15, result);
        }

        [Fact]
        public void FunctionParameterElementAccessByDot()
        {
            var calc = new TestCalc(new());
            // Function parameters used to require .(i) syntax; now .i also works
            // because dots are unconditionally element access.
            var result = calc.Run([
                "myFirst(v) = v.1",
                "myFirst([42; 43; 44])"
            ]);
            Assert.Equal(42, result);
        }

        [Fact]
        public void FunctionParameterElementAccessByParenIndex()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "second(v) = v.(2)",
                "second([42; 43; 44])"
            ]);
            Assert.Equal(43, result);
        }

        [Fact]
        public void ValidatorRejectsDottedNames()
        {
            // Validator.IsVariable should never accept a name containing '.'
            Assert.False(Calcpad.Core.Validator.IsVariable("k.1"));
            Assert.False(Calcpad.Core.Validator.IsVariable("data.set"));
            Assert.True(Calcpad.Core.Validator.IsVariable("k_1"));
            Assert.True(Calcpad.Core.Validator.IsVariable("k,1"));
        }
    }
}
