using System;
using System.Text;

namespace Calcpad.Core
{
    [Serializable()]
    public class Settings
    {
        public MathSettings Math { get; set; } = new();
        public PlotSettings Plot { get; set; } = new();
        public string Units { get; set; } = "m";

        public ClientFileCache ClientFileCache { get; set; }

        public string SourceFilePath { get; set; }
    }

    [Serializable()]
    public class ClientFileCache
    {
        public string[] Filenames { get; set; } = Array.Empty<string>();
        public byte[][] Contents { get; set; } = Array.Empty<byte[]>();
        public string[] Errors { get; set; } = Array.Empty<string>();
        public string[] DiskGuids { get; set; } = Array.Empty<string>();

        public string DiskCacheFolder { get; set; }

        [field: NonSerialized]
        public Func<string, byte[]> RefetchDelegate { get; set; }

        private int IndexOf(string filename) =>
            Array.FindIndex(Filenames, f => string.Equals(f, filename, StringComparison.OrdinalIgnoreCase));

        public bool TryGetContent(string filename, out string content)
        {
            content = null;
            if (!TryGetBytes(filename, out var bytes))
                return false;
            content = Encoding.UTF8.GetString(bytes);
            return true;
        }

        public bool TryGetContentMultiKey(string primaryKey, string fallbackKey, out string content) =>
            TryGetContent(primaryKey, out content) ||
            (fallbackKey != null && TryGetContent(fallbackKey, out content));

        public bool TryGetBytes(string filename, out byte[] bytes)
        {
            bytes = null;
            var idx = IndexOf(filename);
            if (idx < 0)
                return false;

            if (Contents[idx] != null)
            {
                bytes = Contents[idx];
                return true;
            }

            if (DiskGuids[idx] != null && DiskCacheFolder != null)
            {
                if (ClientFileDiskCache.TryRead(DiskCacheFolder, DiskGuids[idx], out bytes))
                    return true;

                if (RefetchDelegate != null)
                {
                    try
                    {
                        bytes = RefetchDelegate(filename);
                        if (bytes != null)
                        {
                            DiskGuids[idx] = ClientFileDiskCache.Write(DiskCacheFolder, filename, bytes);
                            return true;
                        }
                    }
                    catch { }
                }
            }

            return false;
        }

        public bool TryGetError(string filename, out string error)
        {
            error = null;
            var idx = IndexOf(filename);
            if (idx < 0)
                return false;
            error = Errors[idx];
            return error != null;
        }

        public bool TryGetErrorMultiKey(string primaryKey, string fallbackKey, out string error) =>
            TryGetError(primaryKey, out error) ||
            (fallbackKey != null && TryGetError(fallbackKey, out error));

        public void AddEntry(string filename, byte[] content, string error)
        {
            byte[] contentEntry;
            string guidEntry;

            if (content != null && content.Length > ClientFileDiskCache.DiskThresholdBytes && DiskCacheFolder != null)
            {
                guidEntry = ClientFileDiskCache.Write(DiskCacheFolder, filename, content);
                contentEntry = null;
            }
            else
            {
                guidEntry = null;
                contentEntry = content;
            }

            Filenames = [.. Filenames, filename];
            Contents = [.. Contents, contentEntry];
            Errors = [.. Errors, error];
            DiskGuids = [.. DiskGuids, guidEntry];
        }
    }

    [Serializable()]
    public class MathSettings
    {
        private int _decimals;
        private int _maxOutputCount;
        public int Decimals
        {
            get => _decimals;
            set
            {
                _decimals = value switch
                {
                    <= 0 => 0,
                    >= 15 => 15,
                    _ => value
                };
            }
        }
        public int Degrees { get; set; }
        public bool IsComplex { get; set; }
        public bool Substitute { get; set; }
        public bool FormatEquations { get; set; }
        public bool ZeroSmallMatrixElements { get; set; }
        public int MaxOutputCount
        {
            get => _maxOutputCount;
            set
            {
                _maxOutputCount = value switch
                {
                    <= 5 => 5,
                    >= 100 => 100,
                    _ => value
                };
            }
        }
        public string FormatString { get; set; }

        public MathSettings()
        {
            Decimals = 2;
            Degrees = 0;
            IsComplex = false;
            Substitute = true;
            FormatEquations = true;
            ZeroSmallMatrixElements = true;
            MaxOutputCount = 20;
        }
    }

    [Serializable()]
    public class PlotSettings
    {
        private bool _shadows;
        public bool IsAdaptive { get; set; }
        public double ScreenScaleFactor { get; set; } = 2.0;
        public string ImagePath { get; set; }
        public string ImageUri { get; set; }
        public bool VectorGraphics { get; set; }
        public ColorScales ColorScale { get; set; }
        public bool SmoothScale { get; set; }
        public bool Shadows
        {
            set => _shadows = value;
            get => _shadows && ColorScale != ColorScales.Gray || ColorScale == ColorScales.None;
        }
        public LightDirections LightDirection { get; set; }

        public enum LightDirections
        {
            North,
            NorthEast,
            East,
            SouthEast,
            South,
            SouthWest,
            West,
            NorthWest
        }

        public enum ColorScales
        {
            None,
            Gray,
            Rainbow,
            Terrain,
            VioletToYellow,
            GreenToYellow,
            Blues,
            BlueToYellow,
            BlueToRed,
            PurpleToYellow,
        }

        public PlotSettings()
        {
            IsAdaptive = true;
            ImagePath = string.Empty;
            ImageUri = string.Empty;
            VectorGraphics = false;
            ColorScale = ColorScales.Rainbow;
            SmoothScale = false;
            Shadows = true;
            LightDirection = LightDirections.NorthWest;
        }
    }
}