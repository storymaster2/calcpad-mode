using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for special backend setting variables.
    /// These are not commands but configuration variables that affect plotting and numerical behavior.
    /// </summary>
    public static class SettingSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            // ============================================
            // PLOTTING SETTINGS
            // ============================================
            new SnippetItem
            {
                Insert = "PlotHeight = §",
                Description = "Height of plot area in pixels",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotWidth = §",
                Description = "Width of plot area in pixels",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotSVG = §",
                Description = "Draw plots in SVG (1) or PNG (0) format",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotAdaptive = §",
                Description = "Use adaptive mesh (1) or uniform (0)",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotStep = §",
                Description = "Mesh size for map plotting",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotPalette = §",
                Description = "Color palette number (0-9) for surface plots",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotShadows = §",
                Description = "Draw surface plots with shadows",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotSmooth = §",
                Description = "Smooth gradient (1) or isobands (0) for surface plots",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotLightDir = §",
                Description = "Direction to light source (0-7) clockwise",
                Category = "Plotting",
                KeywordType = "Setting"
            },

            // ============================================
            // NUMERICAL METHOD SETTINGS
            // ============================================
            new SnippetItem
            {
                Insert = "Precision = §",
                Description = "Relative precision for numerical methods (10^-2 to 10^-16)",
                Category = "Numerical Methods",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "Tol = §",
                Description = "Target tolerance for iterative PCG solver",
                Category = "Numerical Methods",
                KeywordType = "Setting"
            }
        ];
    }
}
