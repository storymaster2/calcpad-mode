using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Calcpad.Server.Services
{
    public class DiskCacheCleanupService : IHostedService, IDisposable
    {
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

        public static string CacheFolder { get; } = Path.Combine(AppContext.BaseDirectory, "cache");

        private Timer? _timer;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(CacheFolder);
            _timer = new Timer(Cleanup, null, CleanupInterval, CleanupInterval);
            FileLogger.LogInfo("Disk cache cleanup service started", $"Folder: {CacheFolder}, Interval: {CleanupInterval.TotalHours}h, MaxAge: {MaxAge.TotalHours}h");
            return Task.CompletedTask;
        }

        private void Cleanup(object? state)
        {
            try
            {
                if (!Directory.Exists(CacheFolder))
                    return;

                var cutoff = DateTime.UtcNow - MaxAge;
                var deletedCount = 0;

                foreach (var pattern in new[] { "*.cache", "*.wcache" })
                {
                    foreach (var file in Directory.EnumerateFiles(CacheFolder, pattern))
                    {
                        try
                        {
                            if (File.GetLastWriteTimeUtc(file) < cutoff)
                            {
                                File.Delete(file);
                                deletedCount++;
                            }
                        }
                        catch
                        {
                            // Skip files that can't be deleted (in use, permissions, etc.)
                        }
                    }
                }

                CalcpadService.CleanupExpiredExportSessions();

                if (deletedCount > 0)
                    FileLogger.LogInfo($"Disk cache cleanup removed {deletedCount} expired file(s)");
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Disk cache cleanup failed", ex);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
