using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Calcpad.Core
{
    internal static class ClientFileDiskCache
    {
        internal const int DiskThresholdBytes = 51_200;
        private const string CacheFileExtension = ".cache";

        internal static bool TryRead(string folder, string guid, out byte[] bytes)
        {
            bytes = null;
            if (folder == null || guid == null)
                return false;

            var path = Path.Combine(folder, guid + CacheFileExtension);
            if (!File.Exists(path))
                return false;

            bytes = File.ReadAllBytes(path);
            try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return true;
        }

        internal static string Write(string folder, string filename, byte[] bytes)
        {
            var cacheKey = GetCacheKey(filename);
            var path = Path.Combine(folder, cacheKey + CacheFileExtension);
            if (File.Exists(path))
            {
                try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }
            }
            else
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(path, bytes);
            }
            return cacheKey;
        }

        private static string GetCacheKey(string filename) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(filename)))[..32];
    }
}
