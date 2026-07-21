using System.Text.Json;
using Calcpad.Core;
using Calcpad.Highlighter.Tokenizer;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Extracts per-file settings overrides from embedded HTML comment blocks and
    /// merges them onto a base <see cref="Settings"/> instance.
    ///
    /// A Calcpad file can contain a block of the form:
    /// <code>
    ///   '&lt;!--{"settings": {"decimals": 4, "degrees": 1}}--&gt;
    /// </code>
    /// The first block whose JSON root object contains a <c>"settings"</c> property
    /// is used. File-level settings take precedence over settings passed by the frontend.
    /// Unknown keys and type-mismatched values are silently ignored.
    /// </summary>
    internal sealed class FileSettingsExtractor
    {
        private static readonly HtmlCommentParser _parser = new();
        private static readonly CalcpadTokenizer _tokenizer = new();

        /// <summary>
        /// Supported JSON keys and their descriptions.
        /// These are the only keys recognised inside the <c>"settings"</c> object.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> ValidKeys =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["decimals"]                 = "Number of decimal places in output (0–15)",
                ["degrees"]                  = "Angle unit: 0=radians, 1=degrees, 2=gradians",
                ["complex"]                  = "Enable complex number mode (bool)",
                ["substitute"]               = "Substitute variable values into expressions (bool)",
                ["formatEquations"]          = "Format equations in output (bool)",
                ["zeroSmallMatrixElements"]  = "Zero out near-zero matrix elements (bool)",
                ["maxOutputCount"]           = "Maximum number of output rows (5–100)",
                ["units"]                    = "Unit system string passed to the math engine",
                ["vectorGraphics"]           = "Render plots as SVG instead of bitmap (bool)",
                ["colorScale"]               = "Plot color scale: None, Gray, Rainbow, Terrain, VioletToYellow, GreenToYellow, Blues, BlueToYellow, BlueToRed, PurpleToYellow",
                ["smoothScale"]              = "Smooth color scale transitions (bool)",
                ["shadows"]                  = "Enable 3-D plot shadows (bool)",
                ["adaptivePlot"]             = "Use adaptive sampling for plots (bool)",
            };

        /// <summary>
        /// Tokenizes <paramref name="content"/>, finds the first
        /// <c>&lt;!--{"settings":{...}}--&gt;</c> block, and returns a new
        /// <see cref="Settings"/> with the overrides merged onto
        /// <paramref name="baseSettings"/>.
        /// Returns <paramref name="baseSettings"/> unchanged when no block is found.
        /// </summary>
        public Settings ApplyFileSettings(string content, Settings baseSettings)
        {
            if (string.IsNullOrEmpty(content))
                return baseSettings;

            var tokens = _tokenizer.Tokenize(content);
            var blocks = _parser.Parse(tokens);

            foreach (var block in blocks)
            {
                if (block.Status != HtmlCommentParseStatus.Success || !block.Data.HasValue)
                    continue;

                if (!block.Data.Value.TryGetProperty("settings", out var settingsElement))
                    continue;

                if (settingsElement.ValueKind != JsonValueKind.Object)
                    continue;

                return ApplyOverrides(baseSettings, settingsElement);
            }

            return baseSettings;
        }

        private static Settings ApplyOverrides(Settings base_, JsonElement overrides)
        {
            var math = base_.Math;
            var plot = base_.Plot;

            var result = new Settings
            {
                Units          = base_.Units,
                ClientFileCache = base_.ClientFileCache,
                SourceFilePath = base_.SourceFilePath,
                EnableUi       = base_.EnableUi,
                UiOverrides    = base_.UiOverrides,
                Math = new MathSettings
                {
                    Decimals                = math.Decimals,
                    Degrees                 = math.Degrees,
                    IsComplex               = math.IsComplex,
                    Substitute              = math.Substitute,
                    FormatEquations         = math.FormatEquations,
                    ZeroSmallMatrixElements = math.ZeroSmallMatrixElements,
                    MaxOutputCount          = math.MaxOutputCount,
                    FormatString            = math.FormatString,
                },
                Plot = new PlotSettings
                {
                    IsAdaptive        = plot.IsAdaptive,
                    ScreenScaleFactor = plot.ScreenScaleFactor,
                    ImagePath         = plot.ImagePath,
                    ImageUri          = plot.ImageUri,
                    VectorGraphics    = plot.VectorGraphics,
                    ColorScale        = plot.ColorScale,
                    SmoothScale       = plot.SmoothScale,
                    Shadows           = plot.Shadows,
                    LightDirection    = plot.LightDirection,
                },
            };

            foreach (var prop in overrides.EnumerateObject())
            {
                switch (prop.Name.ToLowerInvariant())
                {
                    case "decimals":
                        if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out int dec))
                            result.Math.Decimals = dec;
                        break;

                    case "degrees":
                        if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out int deg))
                            result.Math.Degrees = deg;
                        break;

                    case "complex":
                        if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            result.Math.IsComplex = prop.Value.GetBoolean();
                        break;

                    case "substitute":
                        if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            result.Math.Substitute = prop.Value.GetBoolean();
                        break;

                    case "formatequations":
                        if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            result.Math.FormatEquations = prop.Value.GetBoolean();
                        break;

                    case "zerosmallmatrixelements":
                        if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            result.Math.ZeroSmallMatrixElements = prop.Value.GetBoolean();
                        break;

                    case "maxoutputcount":
                        if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out int moc))
                            result.Math.MaxOutputCount = moc;
                        break;

                    case "units":
                        if (prop.Value.ValueKind == JsonValueKind.String)
                            result.Units = prop.Value.GetString() ?? result.Units;
                        break;

                    case "vectorgraphics":
                        if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            result.Plot.VectorGraphics = prop.Value.GetBoolean();
                        break;

                    case "colorscale":
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var raw = prop.Value.GetString();
                            if (System.Enum.TryParse<PlotSettings.ColorScales>(raw, ignoreCase: true, out var cs))
                                result.Plot.ColorScale = cs;
                        }
                        break;

                    case "smoothscale":
                        if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            result.Plot.SmoothScale = prop.Value.GetBoolean();
                        break;

                    case "shadows":
                        if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            result.Plot.Shadows = prop.Value.GetBoolean();
                        break;

                    case "adaptiveplot":
                        if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            result.Plot.IsAdaptive = prop.Value.GetBoolean();
                        break;
                }
            }

            return result;
        }
    }
}
