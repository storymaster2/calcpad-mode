using System.Collections.Generic;

namespace Calcpad.Tests
{
    public class UiStringModeTests
    {
        private static ExpressionParser NewParser(Dictionary<string, string> overrides = null)
        {
            var p = new ExpressionParser();
            p.Settings.EnableUi = true;
            if (overrides != null)
                p.Settings.UiOverrides = overrides;
            return p;
        }

        [Fact]
        public void AutoDetect_StringEntry_FromDollarSuffix()
        {
            var p = NewParser();
            p.Parse("#UI greeting$ = 'hello'\ngreeting$");
            var html = p.HtmlResult;
            Assert.Contains("data-ui-type=\"entry\"", html);
            Assert.Contains("data-ui-mode=\"string\"", html);
            Assert.Contains("data-ui-var=\"greeting$\"", html);
            Assert.Contains("value=\"hello\"", html);
            // Referencing greeting$ on the next line expands to the stored value.
            Assert.Contains("hello", html);
        }

        [Fact]
        public void ExplicitMode_String_EntryForm()
        {
            var p = NewParser();
            p.Parse("#UI {\"type\": \"entry\", \"mode\": \"string\"} msg$ = 'hi'");
            var html = p.HtmlResult;
            Assert.Contains("data-ui-type=\"entry\"", html);
            Assert.Contains("data-ui-mode=\"string\"", html);
            Assert.Contains("value=\"hi\"", html);
        }

        [Fact]
        public void ExplicitMode_String_MissingDollarSuffix_IsError()
        {
            var p = NewParser();
            p.Parse("#UI {\"mode\": \"string\"} x = 'y'");
            var html = p.HtmlResult;
            Assert.Contains("class=\"err", html);
        }

        [Fact]
        public void Dropdown_String_SelectedOptionMatches()
        {
            var p = NewParser();
            var code = "#UI {\"type\": \"dropdown\", \"mode\": \"string\", " +
                       "\"keys\": [\"A\", \"B\"], \"values\": [\"a\", \"b\"]} c$ = 'a'";
            p.Parse(code);
            var html = p.HtmlResult;
            Assert.Contains("<select", html);
            Assert.Contains("data-ui-type=\"dropdown\"", html);
            Assert.Contains("data-ui-mode=\"string\"", html);
            Assert.Contains("<option value=\"a\" selected>A</option>", html);
            Assert.Contains("<option value=\"b\">B</option>", html);
        }

        [Fact]
        public void Radio_String_CheckedMatches()
        {
            var p = NewParser();
            var code = "#UI {\"type\": \"radio\", \"mode\": \"string\", " +
                       "\"keys\": [\"Red\", \"Blue\"], \"values\": [\"red\", \"blue\"]} color$ = 'blue'";
            p.Parse(code);
            var html = p.HtmlResult;
            Assert.Contains("data-ui-type=\"radio\"", html);
            Assert.Contains("data-ui-mode=\"string\"", html);
            Assert.Contains("value=\"blue\" checked", html);
        }

        [Fact]
        public void Checkbox_String_StoresTrueFalseLiteral()
        {
            var p = NewParser();
            p.Parse("#UI {\"type\": \"checkbox\", \"mode\": \"string\"} flag$ = 'false'\nflag$");
            var html = p.HtmlResult;
            Assert.Contains("data-ui-type=\"checkbox\"", html);
            Assert.Contains("data-ui-mode=\"string\"", html);
            // "checked" attribute should not be present for 'false'
            Assert.DoesNotContain("checkbox\" data-ui-var=\"flag$\" data-ui-line=\"0\" data-ui-mode=\"string\" checked", html);
        }

        [Fact]
        public void Checkbox_String_Checked_ForTrue()
        {
            var p = NewParser();
            p.Parse("#UI {\"type\": \"checkbox\", \"mode\": \"string\"} flag$ = 'true'");
            var html = p.HtmlResult;
            Assert.Contains("data-ui-type=\"checkbox\"", html);
            Assert.Contains(" checked>", html);
        }

        [Fact]
        public void Datagrid_String_StoresStringTableAndEmitsDiv()
        {
            var p = NewParser();
            p.Parse("#UI {\"type\": \"datagrid\", \"mode\": \"string\"} t$ = ['a'; 'b' | 'c'; 'd']");
            var html = p.HtmlResult;
            Assert.Contains("calcpad-ui-datagrid", html);
            Assert.Contains("data-ui-mode=\"string\"", html);
            Assert.Contains("data-ui-rows=\"2\"", html);
            Assert.Contains("data-ui-columns=\"2\"", html);
            Assert.Contains("data-ui-values=\"a;b|c;d\"", html);
        }

        [Fact]
        public void UiOverride_ReplacesEntryValue()
        {
            var overrides = new Dictionary<string, string> { ["name$"] = "world" };
            var p = NewParser(overrides);
            p.Parse("#UI name$ = 'hello'\nname$");
            var html = p.HtmlResult;
            Assert.Contains("value=\"world\"", html);
            // Reference below picks up the overridden value.
            Assert.Contains("world", html);
            Assert.DoesNotContain("value=\"hello\"", html);
        }

        [Fact]
        public void UiOverride_CheckboxCoercesToTrueFalse()
        {
            var overrides = new Dictionary<string, string> { ["flag$"] = "1" };
            var p = NewParser(overrides);
            p.Parse("#UI {\"type\": \"checkbox\", \"mode\": \"string\"} flag$ = 'false'\nflag$");
            var html = p.HtmlResult;
            Assert.Contains(" checked>", html);
            Assert.Contains("true", html);
        }

        [Fact]
        public void NumericUiStillWorks_NoRegression()
        {
            var p = NewParser();
            p.Parse("#UI x = 5");
            var html = p.HtmlResult;
            // Numeric #UI emits the UI-bound <input> as a child of the equation;
            // data-ui-var identifies the target variable. data-ui-mode must NOT be "string".
            Assert.Contains("data-ui-var=\"x\"", html);
            Assert.DoesNotContain("data-ui-mode=\"string\"", html);
        }

        [Fact]
        public void AutoDetect_Numeric_WhenNoDollarOrStringRhs()
        {
            var p = NewParser();
            p.Parse("#UI x = 10");
            var html = p.HtmlResult;
            // Should not mark as string mode.
            Assert.DoesNotContain("data-ui-mode=\"string\"", html);
        }

        [Fact]
        public void Concatenation_RhsIsEvaluated()
        {
            var p = NewParser();
            p.Parse("#UI first$ = 'John'\n#UI last$ = 'Doe'\n#UI full$ = first$ + ' ' + last$\nfull$");
            var html = p.HtmlResult;
            // The stored full$ should render as 'John Doe' somewhere in the output.
            Assert.Contains("John Doe", html);
        }

        [Fact]
        public void ConditionalSkip_DoesNotStoreVariable()
        {
            var p = NewParser();
            p.Parse("#if 0\n#UI name$ = 'skipped'\n#end if\n#UI name$ = 'kept'\nname$");
            var html = p.HtmlResult;
            Assert.Contains("kept", html);
            Assert.DoesNotContain("skipped", html);
        }
    }
}
