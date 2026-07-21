namespace Calcpad.Tests
{
    public class StringFunctionTests
    {
        private static string RunExpression(string code)
        {
            var parser = new ExpressionParser();
            parser.Parse(code, calculate: true, getXml: false);
            return parser.HtmlResult;
        }

        private static bool OutputContains(string html, string expected) =>
            html.Contains(expected, StringComparison.Ordinal);

        #region Declaration and Output

        [Fact]
        [Trait("Category", "String")]
        public void StringDeclaration()
        {
            var html = RunExpression("#string s$ = 'hello'\ns$");
            Assert.True(OutputContains(html, "hello"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void StringReassignment()
        {
            var html = RunExpression("#string s$ = 'hello'\ns$ = 'world'\ns$");
            Assert.True(OutputContains(html, "world"));
            Assert.False(OutputContains(html, "hello</p>"));
        }

        #endregion

        #region 1-arg functions

        [Fact]
        [Trait("Category", "String")]
        public void Len()
        {
            var html = RunExpression("#string s$ = 'hello'\n#val\nlen$(s$)");
            Assert.True(OutputContains(html, "5"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void Trim()
        {
            var html = RunExpression("#string s$ = '  hello  '\ntrim$(s$)");
            Assert.True(OutputContains(html, "hello"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void LTrim()
        {
            var html = RunExpression("#string s$ = '  hello'\nltrim$(s$)");
            Assert.True(OutputContains(html, "hello"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void RTrim()
        {
            var html = RunExpression("#string s$ = 'hello  '\nrtrim$(s$)");
            Assert.True(OutputContains(html, "hello"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void UCase()
        {
            var html = RunExpression("#string s$ = 'hello'\nucase$(s$)");
            Assert.True(OutputContains(html, "HELLO"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void LCase()
        {
            var html = RunExpression("#string s$ = 'HELLO'\nlcase$(s$)");
            Assert.True(OutputContains(html, "hello"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void Space()
        {
            var html = RunExpression("#string s$ = space$(3)\n#val\nlen$(s$)");
            Assert.True(OutputContains(html, "3"));
        }

        #endregion

        #region 2-arg functions

        [Fact]
        [Trait("Category", "String")]
        public void Left()
        {
            var html = RunExpression("#string s$ = 'hello world'\nleft$(s$; 5)");
            Assert.True(OutputContains(html, "hello"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void Right()
        {
            var html = RunExpression("#string s$ = 'hello world'\nright$(s$; 5)");
            Assert.True(OutputContains(html, "world"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void Compare_Equal()
        {
            var html = RunExpression("#string s$ = 'abc'\n#val\ncompare$(s$; 'abc')");
            Assert.True(OutputContains(html, "0"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void Compare_Before()
        {
            var html = RunExpression("#val\ncompare$('Apple'; 'Banana')");
            Assert.True(OutputContains(html, "-1"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void Find()
        {
            var html = RunExpression("#val\nfind$('ab'; 'abcab')");
            // find$ returns [1; 4] vector, MathParser formats with comma separator
            Assert.True(OutputContains(html, "1") && OutputContains(html, "4"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void Find_NotFound()
        {
            var html = RunExpression("#val\nfind$('xyz'; 'abcab')");
            Assert.True(OutputContains(html, "0"));
        }

        #endregion

        #region 3-arg functions

        [Fact]
        [Trait("Category", "String")]
        public void Mid()
        {
            var html = RunExpression("#string s$ = 'hello world'\nmid$(s$; 7; 5)");
            Assert.True(OutputContains(html, "world"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void Replace()
        {
            var html = RunExpression("#string s$ = 'hello world'\nreplace$(s$; 'world'; 'there')");
            Assert.True(OutputContains(html, "hello there"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void InStr()
        {
            var html = RunExpression("#string s$ = 'hello world'\n#val\ninstr$(1; s$; 'world')");
            Assert.True(OutputContains(html, "7"));
        }

        #endregion

        #region Variadic functions

        [Fact]
        [Trait("Category", "String")]
        public void Concat()
        {
            var html = RunExpression("#string a$ = 'hello'\n#string b$ = ' world'\nconcat$(a$; b$)");
            Assert.True(OutputContains(html, "hello world"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void Concat_WithLiterals()
        {
            var html = RunExpression("concat$('hello'; ' '; 'world')");
            Assert.True(OutputContains(html, "hello world"));
        }

        #endregion

        #region Numeric arg evaluation via MathParser

        [Fact]
        [Trait("Category", "String")]
        public void Left_WithExpression()
        {
            var html = RunExpression("#string s$ = 'hello world'\nn = 2 + 3\nleft$(s$; n)");
            Assert.True(OutputContains(html, "hello"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void Mid_WithExpressions()
        {
            var html = RunExpression("#string s$ = 'hello world'\nstart = 7\ncount = 5\nmid$(s$; start; count)");
            Assert.True(OutputContains(html, "world"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void String_FromScalar()
        {
            var html = RunExpression("#string s$ = string$(42)\ns$");
            Assert.True(OutputContains(html, "42"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void String_FromExpression()
        {
            var html = RunExpression("x = 10\n#string s$ = string$(x * 2 + 1)\ns$");
            Assert.True(OutputContains(html, "21"));
        }

        #endregion

        #region val$ in expressions

        [Fact]
        [Trait("Category", "String")]
        public void Val_InExpression()
        {
            var html = RunExpression("#string s$ = '123'\n#val\nval$(s$) + 2");
            Assert.True(OutputContains(html, "125"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void Val_InvalidString()
        {
            var html = RunExpression("#string s$ = 'abc'\n#val\nval$(s$)");
            Assert.True(OutputContains(html, "NaN"));
        }

        #endregion

        #region String variables in loops

        [Fact]
        [Trait("Category", "String")]
        public void StringInForLoop()
        {
            var html = RunExpression(
                "#string s$ = 'a'\n" +
                "#for i = 1 : 3\n" +
                "  s$ = concat$(s$; string$(i))\n" +
                "#loop\n" +
                "s$");
            Assert.True(OutputContains(html, "a123"));
        }

        #endregion

        #region String expansion in #string definitions

        [Fact]
        [Trait("Category", "String")]
        public void StringDefinitionWithFunction()
        {
            var html = RunExpression("#string s$ = 'hello world'\n#string left2$ = left$(s$; 5)\nleft2$");
            Assert.True(OutputContains(html, "hello"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void StringDefinitionWithVariableRef()
        {
            var html = RunExpression("#string a$ = 'test'\n#string b$ = a$\nb$");
            Assert.True(OutputContains(html, "test"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void StringDefinitionWithPlusOperator()
        {
            var html = RunExpression(
                "#string s$ = 'hello world'\n" +
                "#string left2$ = left$(s$; 5)\n" +
                "#string right2$ = right$(s$; 5)\n" +
                "#string concatenated$ = left2$ + right2$\n" +
                "concatenated$");
            Assert.True(OutputContains(html, "helloworld"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void StringPlusLiteral()
        {
            var html = RunExpression(
                "#string s$ = 'hello'\n" +
                "#string r$ = s$ + ' world'\n" +
                "r$");
            Assert.True(OutputContains(html, "hello world"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void StringPlusInFunctionArg()
        {
            var html = RunExpression(
                "#string a$ = 'hello'\n" +
                "#string b$ = ' world'\n" +
                "ucase$(a$ + b$)");
            Assert.True(OutputContains(html, "HELLO WORLD"));
        }

        #endregion

        #region String comparison

        [Fact]
        [Trait("Category", "String")]
        public void StringEquality_True()
        {
            var html = RunExpression("#string s$ = 'hello world'\n#val\ns$ == 'hello world'");
            Assert.True(OutputContains(html, "1"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void StringEquality_False()
        {
            var html = RunExpression("#string s$ = 'hello'\n#val\ns$ == 'world'");
            Assert.True(OutputContains(html, "0"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void StringInequality_True()
        {
            var html = RunExpression("#string s$ = 'hello'\n#val\ns$ != 'world'");
            Assert.True(OutputContains(html, "1"));
        }

        [Fact]
        [Trait("Category", "String")]
        public void StringEquality_Unicode()
        {
            var html = RunExpression("#string s$ = 'hello'\n#val\ns$ ≡ 'hello'");
            Assert.True(OutputContains(html, "1"));
        }

        #endregion

        #region Nested functions

        [Fact]
        [Trait("Category", "String")]
        public void NestedStringFunctions()
        {
            var html = RunExpression("ucase$(left$('hello world'; 5))");
            Assert.True(OutputContains(html, "HELLO"));
        }

        #endregion

        #region Table declarations

        [Fact]
        [Trait("Category", "Table")]
        public void TableDeclaration_Constructor()
        {
            var html = RunExpression("#string t$ = table$(2; 3)\nt$(1; 1) = 'hello'\nt$(1; 1)");
            Assert.True(OutputContains(html, "hello"));
        }

        [Fact]
        [Trait("Category", "Table")]
        public void TableDeclaration_Literal()
        {
            var html = RunExpression("#string t$ = ['a'; 'b' | 'c'; 'd']\nt$");
            Assert.True(OutputContains(html, "<table"));
            Assert.True(OutputContains(html, "bordered"));
            Assert.True(OutputContains(html, "<td>a</td>"));
            Assert.True(OutputContains(html, "<td>d</td>"));
        }

        [Fact]
        [Trait("Category", "Table")]
        public void TableElementRead()
        {
            var html = RunExpression("#string t$ = ['hello'; 'world' | 'foo'; 'bar']\nt$(1; 2)");
            Assert.True(OutputContains(html, "world"));
        }

        [Fact]
        [Trait("Category", "Table")]
        public void TableElementWrite()
        {
            var html = RunExpression("#string t$ = ['a'; 'b' | 'c'; 'd']\nt$(2; 1) = 'changed'\nt$(2; 1)");
            Assert.True(OutputContains(html, "changed"));
        }

        [Fact]
        [Trait("Category", "Table")]
        public void TableElementWriteWithExpression()
        {
            var html = RunExpression("#string t$ = table$(2; 2)\nn = 1\nt$(n; n + 1) = 'test'\nt$(1; 2)");
            Assert.True(OutputContains(html, "test"));
        }

        #endregion

        #region split$ and join$

        [Fact]
        [Trait("Category", "Table")]
        public void SplitAndJoin_RoundTrip()
        {
            var html = RunExpression(
                "#string csv$ = 'a,b,c|d,e,f'\n" +
                "#string data$ = split$(csv$; '|'; ',')\n" +
                "#string back$ = join$(data$; '|'; ',')\n" +
                "back$");
            Assert.True(OutputContains(html, "a,b,c|d,e,f"));
        }

        [Fact]
        [Trait("Category", "Table")]
        public void Split_SingleRow()
        {
            var html = RunExpression(
                "#string t$ = split$('a,b,c'; ''; ',')\n" +
                "t$(1; 2)");
            Assert.True(OutputContains(html, "b"));
        }

        [Fact]
        [Trait("Category", "Table")]
        public void Split_SingleColumn()
        {
            var html = RunExpression(
                "#string t$ = split$('a|b|c'; '|'; '')\n" +
                "t$(2; 1)");
            Assert.True(OutputContains(html, "b"));
        }

        [Fact]
        [Trait("Category", "Table")]
        public void Join_Flatten()
        {
            var html = RunExpression(
                "#string t$ = ['a'; 'b' | 'c'; 'd']\n" +
                "#string flat$ = join$(t$; ',')\n" +
                "flat$");
            Assert.True(OutputContains(html, "a,b,c,d"));
        }

        #endregion

        #region Cross-type conversions

        [Fact]
        [Trait("Category", "Table")]
        public void ValOfTable_SingleCell()
        {
            var html = RunExpression("#string t$ = ['42']\n#val\nval$(t$) + 1");
            Assert.True(OutputContains(html, "43"));
        }

        [Fact]
        [Trait("Category", "Table")]
        public void StringOfMatrix()
        {
            var html = RunExpression("#string t$ = string$([1; 2 | 3; 4])\nt$(2; 2)");
            Assert.True(OutputContains(html, "4"));
        }

        #endregion

        #region Table in loops

        [Fact]
        [Trait("Category", "Table")]
        public void TableInLoop()
        {
            var html = RunExpression(
                "#string t$ = table$(3; 1)\n" +
                "#for i = 1 : 3\n" +
                "  t$(i; 1) = string$(i)\n" +
                "#loop\n" +
                "#string result$ = join$(t$; ','; '')\n" +
                "result$");
            Assert.True(OutputContains(html, "1,2,3"));
        }

        #endregion

        #region Name collision

        [Fact]
        [Trait("Category", "Table")]
        public void NameCollision_TableOverridesString()
        {
            var html = RunExpression(
                "#string x$ = 'hello'\n" +
                "#string x$ = ['a'; 'b']\n" +
                "x$(1; 2)");
            Assert.True(OutputContains(html, "b"));
        }

        [Fact]
        [Trait("Category", "Table")]
        public void NameCollision_StringOverridesTable()
        {
            var html = RunExpression(
                "#string x$ = ['a'; 'b']\n" +
                "#string x$ = 'hello'\n" +
                "x$");
            Assert.True(OutputContains(html, "hello"));
        }

        #endregion
    }
}
