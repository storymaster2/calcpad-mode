using System;
using System.Text;
using Calcpad.Highlighter.Snippets;
using Xunit;

namespace Calcpad.Tests
{
    public class SnippetRegistrySmokeTests
    {
        [Fact]
        public void GetAllSnippetsArray_DoesNotThrow()
        {
            try
            {
                var snippets = SnippetRegistry.GetAllSnippetsArray();
                Assert.NotEmpty(snippets);
            }
            catch (Exception ex)
            {
                var sb = new StringBuilder();
                var current = ex;
                int depth = 0;
                while (current != null && depth < 10)
                {
                    sb.AppendLine($"[{depth}] {current.GetType().FullName}: {current.Message}");
                    sb.AppendLine(current.StackTrace);
                    sb.AppendLine();
                    current = current.InnerException;
                    depth++;
                }
                Assert.Fail("SnippetRegistry initialization failed:\n" + sb);
            }
        }
    }
}
