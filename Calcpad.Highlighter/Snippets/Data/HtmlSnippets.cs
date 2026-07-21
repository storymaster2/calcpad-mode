using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for HTML tags and SVG graphics.
    /// Most HTML snippets are UI-only and excluded from linter.
    /// </summary>
    public static class HtmlSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            // ============================================
            // HTML HEADINGS
            // ============================================
            new SnippetItem
            {
                Insert = "'<h3>§'</h3>",
                Description = "Heading level 3",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<h4>§'</h4>",
                Description = "Heading level 4",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<h5>§'</h5>",
                Description = "Heading level 5",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<h6>§'</h6>",
                Description = "Heading level 6",
                Category = "HTML"
            },

            // ============================================
            // HTML TEXT FORMATTING
            // ============================================
            new SnippetItem
            {
                Insert = "'<p>§'</p>",
                Description = "Paragraph",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<br/>",
                Description = "Line break",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<strong>§'</strong>",
                Description = "Bold / Strong emphasis",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<em>§'</em>",
                Description = "Italic / Emphasis",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<ins>§'</ins>",
                Description = "Inserted / Underlined text",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<del>§'</del>",
                Description = "Deleted / Strikethrough text",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<sub>§'</sub>",
                Description = "Subscript",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<sup>§'</sup>",
                Description = "Superscript",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<span>§'</span>",
                Description = "Generic span",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<span class=\"err\">§'</span>",
                Description = "Error span (red text)",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<span class=\"ok\">§'</span>",
                Description = "Success span (green text)",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<hr/>",
                Description = "Horizontal rule / divider",
                Category = "HTML"
            },

            // ============================================
            // HTML CONTAINERS
            // ============================================
            new SnippetItem
            {
                Insert = "'<div>§'</div>",
                Description = "Generic div container",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<div class=\"fold\">§'<h4>Heading</h4>§'Folded content§'</div>",
                Description = "Foldable/collapsible section",
                Category = "HTML"
            },

            // ============================================
            // HTML LISTS
            // ============================================
            new SnippetItem
            {
                Insert = "'<ul>§'<li>Item 1</li>§'<li>Item 2</li>§'<li>Item 3</li>§'</ul>",
                Description = "Unordered list (bullets)",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<ol>§'<li>Item 1</li>§'<li>Item 2</li>§'<li>Item 3</li>§'</ol>",
                Description = "Ordered list (numbered)",
                Category = "HTML"
            },

            // ============================================
            // HTML TABLES
            // ============================================
            new SnippetItem
            {
                Insert = "§'<table class=\"bordered\">§'<thead>§'<tr><th>col 1</th><th>col 2</th></tr>§'</thead>§'<tbody>§'<tr><td>'11'</td><td>'12'</td></tr>§'<tr><td>'21'</td><td>'22'</td></tr>§'</tbody>§'</table>§",
                Description = "HTML table with header",
                Category = "HTML"
            },

            // ============================================
            // HTML FORMS / INPUT ELEMENTS
            // ============================================
            new SnippetItem
            {
                Insert = "§'<p>Select an option: <select name=\"target1\">§'<option value=\"11;12\">x1; y1</option>§'<option value=\"21;22\">x2; y2</option>§'<option value=\"31;32\">x3; y3</option>§'</select></p>§'...§'<p id=\"target1\"> Values:'x = ? {21}','y = ? {22}'</p>§",
                Description = "Dropdown select input",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "§'<p>Select: §'<input name=\"target2\" type=\"radio\" id=\"opt1\" value=\"1\"/>§'<label for=\"opt1\">option 1</label>§'<input name=\"target2\" type=\"radio\" id=\"opt2\" value=\"2\"/>§'<label for=\"opt2\">option 2</label>§'...§'<p id=\"target2\">Value -'opt = ? {2}'</p>§",
                Description = "Radio button group",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "§'<p><input name=\"target3\" type=\"checkbox\" id=\"chk1\" value=\"3\"/>§'<label for=\"chk1\">Checkbox 1</label></p>§'...§'<p id=\"target3\">Value -'chk = ? {3}'</p>§",
                Description = "Checkbox input",
                Category = "HTML"
            },

            // ============================================
            // HTML COMMENTS - METADATA
            // ============================================
            new SnippetItem
            {
                Insert = "'<!--{\"desc\": \"§\"}-->",
                Description = "Metadata comment with description",
                Label = "Description metadata",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"desc\": \"§\", \"paramTypes\": [§], \"paramDesc\": [§]}-->",
                Description = "Full metadata comment with description, parameter types, and parameter descriptions",
                Label = "Full parameter metadata",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"paramTypes\": [§], \"paramDesc\": [§]}-->",
                Description = "Metadata comment with parameter types and descriptions",
                Label = "Parameter types + descriptions",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"paramTypes\": [§]}-->",
                Description = "Metadata comment with parameter types only",
                Label = "Parameter types only",
                Category = "HTML Comments"
            },

            // ============================================
            // HTML COMMENTS - SETTINGS OVERRIDES
            // ============================================
            new SnippetItem
            {
                Insert = "'<!--{\"settings\": {§}}-->",
                Description = "File settings override block. Keys: decimals, degrees, complex, substitute, formatEquations, vectorGraphics, colorScale, etc.",
                Label = "Settings override",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"settings\": {\"decimals\": §}}-->",
                Description = "Override decimal places in output (0-15)",
                Label = "Settings: decimals",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"settings\": {\"degrees\": 1}}-->",
                Description = "Set angle unit to degrees (0=radians, 1=degrees, 2=gradians)",
                Label = "Settings: degrees",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"settings\": {\"complex\": true}}-->",
                Description = "Enable complex number mode",
                Label = "Settings: complex mode",
                Category = "HTML Comments"
            },

            // ============================================
            // HTML COMMENTS - LINT IGNORE
            // ============================================
            new SnippetItem
            {
                Insert = "'<!--{\"LintIgnore\": [\"§\"]}-->§§'<!--{\"EndLintIgnore\": []}-->",
                Description = "Suppress specific linter diagnostics in a region. Add error codes like CPD-3301.",
                Label = "Lint ignore region (specific codes)",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"LintIgnore\": []}-->§§'<!--{\"EndLintIgnore\": []}-->",
                Description = "Suppress all linter diagnostics in a region",
                Label = "Lint ignore region (all)",
                Category = "HTML Comments"
            },

            // ============================================
            // SVG GRAPHICS - CONTAINER
            // ============================================
            new SnippetItem
            {
                Insert = "#val§#hide§w = 400§h = 400§#show§'<svg viewbox=\"'0' '0' 'w' 'h'\" xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" style=\"font-size:16px; width:'w'px; height:'h'px\">§'<rect x=\"'0'\" y=\"'0'\" width=\"'w'\" height=\"'h'\" style=\"stroke:black; stroke-width:1; fill:WhiteSmoke; fill-opacity:0.2; stroke-opacity:0.1\" />§'<text x=\"'w/2'\" y=\"'h/2'\" text-anchor=\"middle\" fill=\"red\" style=\"font-size:32px;\">Your drawing goes here!</text>§'</svg>§#equ",
                Description = "SVG container template",
                Category = "SVG"
            },

            // ============================================
            // SVG GRAPHICS - SHAPES
            // ============================================
            new SnippetItem
            {
                Insert = "#hide§x1 = 30§y1 = 30§x2 = 380§y2 = 200§#show§'<line x1=\"'x1'\" y1=\"'y1'\" x2=\"'x2'\" y2=\"'y2'\" style=\"stroke:black; stroke-width:2; stroke-opacity:0.8\" />",
                Description = "SVG line",
                Category = "SVG"
            },
            new SnippetItem
            {
                Insert = "#hide§x = 80§y = 60§w = 300§h = 200§#show§'<rect x=\"'x'\" y=\"'y'\" width=\"'w'\" height=\"'h'\" style=\"stroke:black; stroke-width:2; fill:yellow; fill-opacity:0.2; stroke-opacity:0.8\" />",
                Description = "SVG rectangle",
                Category = "SVG"
            },
            new SnippetItem
            {
                Insert = "#hide§cx = 250§cy = 150§r = 70§#show§'<circle cx=\"'cx'\" cy=\"'cy'\" r=\"'r'\" style=\"stroke:black; stroke-width:2; fill:lime; fill-opacity:0.2; stroke-opacity:0.8\" />",
                Description = "SVG circle",
                Category = "SVG"
            },
            new SnippetItem
            {
                Insert = "#hide§cx = 300§cy = 320§rx = 80§ry = 50§#show§'<ellipse cx=\"'cx'\" cy=\"'cy'\" rx=\"'rx'\" ry=\"'ry'\" style=\"stroke:black; stroke-width:2; fill:magenta; fill-opacity:0.1; stroke-opacity:0.8\" />",
                Description = "SVG ellipse",
                Category = "SVG"
            },
            new SnippetItem
            {
                Insert = "#hide§x1 = 20','y1 = 40§x2 = 60','y2 = 350§x3 = 250','y3 = 300§x4 = 360','y4 = 150§#show§'<polyline points=\"'x1','y1' 'x2','y2' 'x3','y3' 'x4','y4'\" style=\"stroke:black; stroke-width:2; fill:none; fill-opacity:0.2; stroke-opacity:0.8\" />",
                Description = "SVG polyline (connected lines)",
                Category = "SVG"
            },
            new SnippetItem
            {
                Insert = "#hide§x1 = 150','y1 = 20§x2 = 10','y2 = 140§x3 = 120','y3 = 360§x4 = 280','y4 = 120§#show§'<polygon points=\"'x1','y1' 'x2','y2' 'x3','y3' 'x4','y4'\" style=\"stroke:black; stroke-width:2; fill:cyan; fill-opacity:0.2; stroke-opacity:0.8\" />",
                Description = "SVG polygon (closed shape)",
                Category = "SVG"
            },
            new SnippetItem
            {
                Insert = "#hide§x = 50§y = 30§#show§'<text x=\"'x'\" y=\"'y'\" text-anchor=\"start\">text1</text>§",
                Description = "SVG text",
                Category = "SVG"
            }
        ];
    }
}
