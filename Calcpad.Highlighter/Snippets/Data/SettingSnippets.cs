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
                Insert = "PlotHeight = 400",
                Description = "Height of plot area in pixels",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotWidth = 800",
                Description = "Width of plot area in pixels",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotSVG = 1",
                Description = "Draw plots in SVG (1) or PNG (0) format",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotAdaptive = 1",
                Description = "Use adaptive mesh (1) or uniform (0)",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotStep = 0",
                Description = "Mesh size for map plotting",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotPalette = 0",
                Description = "Color palette number (0-9) for surface plots",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotShadows = 1",
                Description = "Draw surface plots with shadows",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotSmooth = 1",
                Description = "Smooth gradient (1) or isobands (0) for surface plots",
                Category = "Plotting",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "PlotLightDir = 0",
                Description = "Direction to light source (0-7) clockwise",
                Category = "Plotting",
                KeywordType = "Setting"
            },

            // ============================================
            // NUMERICAL METHOD SETTINGS
            // ============================================
            new SnippetItem
            {
                Insert = "Precision = 1e-12",
                Description = "Relative precision for numerical methods (10^-2 to 10^-16)",
                Category = "Numerical Methods",
                KeywordType = "Setting"
            },
            new SnippetItem
            {
                Insert = "Tol = 1e-8",
                Description = "Target tolerance for iterative PCG solver",
                Category = "Numerical Methods",
                KeywordType = "Setting"
            }
        ];
    }
}
