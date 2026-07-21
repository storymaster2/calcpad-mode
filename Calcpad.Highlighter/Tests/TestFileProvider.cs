using System;
using System.Collections.Generic;
using System.IO;

namespace Calcpad.Highlighter.Tests
{
    /// <summary>
    /// Provides test file content for linter testing.
    /// Maps include/read filenames to their content.
    /// </summary>
    public class TestFileProvider
    {
        private readonly string _samplesPath;

        public TestFileProvider(string samplesPath)
        {
            _samplesPath = samplesPath;
        }

        /// <summary>
        /// Gets the dictionary of include files for use with ContentResolver.
        /// Loads all .cpd files from the Samples folder.
        /// </summary>
        public Dictionary<string, string> GetIncludeFiles()
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.GetFiles(_samplesPath, "*.cpd", SearchOption.AllDirectories))
            {
                var filename = Path.GetFileName(file);
                files[filename] = File.ReadAllText(file);
            }

            return files;
        }

        /// <summary>
        /// Gets a specific test file content by name.
        /// </summary>
        public string GetFileContent(string filename)
        {
            var filePath = Path.Combine(_samplesPath, filename);
            return File.ReadAllText(filePath);
        }
    }
}
