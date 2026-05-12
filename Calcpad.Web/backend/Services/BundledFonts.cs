using System.Reflection;
using System.Text;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Loads font files bundled with the backend (filesystem Fonts/ folder or
    /// embedded resources under <c>Calcpad.Server.Fonts.*</c>) and exposes them
    /// to the rendered page via <c>window.__calcpadFonts</c>. Client-side
    /// scripts (e.g. the DXF render module) read from that dict and only fall
    /// back to a CDN URL when no bundled font is available — i.e. live preview
    /// outside the Calcpad backend.
    ///
    /// Drop a .woff / .woff2 / .ttf / .otf file into the Fonts/ directory and
    /// it gets picked up automatically; the dict is keyed by filename.
    /// </summary>
    internal static class BundledFonts
    {
        private static IReadOnlyDictionary<string, string>? _cachedDataUrls;
        private static string? _cachedScriptTag;
        private static readonly object _lock = new();

        public static IReadOnlyDictionary<string, string> GetDataUrls()
        {
            if (_cachedDataUrls != null) return _cachedDataUrls;
            lock (_lock)
            {
                if (_cachedDataUrls != null) return _cachedDataUrls;

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                LoadFromFilesystem(map);
                LoadFromEmbeddedResources(map);

                FileLogger.LogInfo("Bundled fonts loaded", map.Count == 0 ? "(none)" : string.Join(", ", map.Keys));
                _cachedDataUrls = map;
                return _cachedDataUrls;
            }
        }

        /// <summary>
        /// Returns a <c>&lt;script&gt;</c> tag that defines
        /// <c>window.__calcpadFonts</c> with every bundled font, or an empty
        /// string if none are bundled.
        /// </summary>
        public static string GetInjectionScript()
        {
            if (_cachedScriptTag != null) return _cachedScriptTag;

            var fonts = GetDataUrls();
            if (fonts.Count == 0)
            {
                _cachedScriptTag = string.Empty;
                return _cachedScriptTag;
            }

            var sb = new StringBuilder();
            sb.Append("<script>window.__calcpadFonts={");
            bool first = true;
            foreach (var (fileName, dataUrl) in fonts)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(JsEscape(fileName)).Append("\":\"").Append(JsEscape(dataUrl)).Append('"');
            }
            sb.Append("};</script>");

            _cachedScriptTag = sb.ToString();
            return _cachedScriptTag;
        }

        private static void LoadFromFilesystem(Dictionary<string, string> map)
        {
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
                if (!Directory.Exists(dir)) return;

                foreach (var path in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    var mime = MimeForFont(path);
                    if (mime == null) continue;
                    try
                    {
                        var bytes = File.ReadAllBytes(path);
                        map[Path.GetFileName(path)] = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogWarning($"Failed to load bundled font from {path}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogWarning("Failed to enumerate Fonts/ directory", ex.Message);
            }
        }

        private static void LoadFromEmbeddedResources(Dictionary<string, string> map)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (!name.StartsWith("Calcpad.Server.Fonts.", StringComparison.OrdinalIgnoreCase)) continue;
                    var mime = MimeForFont(name);
                    if (mime == null) continue;

                    // Resource names use '.' as path separator. Recover a usable
                    // "<stem>.<ext>" filename for URL matching.
                    var leaf = name.Substring("Calcpad.Server.Fonts.".Length);
                    var lastDot = leaf.LastIndexOf('.');
                    var fileName = lastDot < 0
                        ? leaf
                        : leaf.Substring(0, lastDot).Replace('.', '_') + leaf.Substring(lastDot);
                    if (map.ContainsKey(fileName)) continue;

                    try
                    {
                        using var stream = assembly.GetManifestResourceStream(name);
                        if (stream == null) continue;
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        map[fileName] = $"data:{mime};base64,{Convert.ToBase64String(ms.ToArray())}";
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogWarning($"Failed to load embedded font resource {name}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogWarning("Failed to enumerate embedded font resources", ex.Message);
            }
        }

        private static string? MimeForFont(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                ".otf" => "font/otf",
                _ => null
            };
        }

        private static string JsEscape(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("</", "<\\/");
    }
}
