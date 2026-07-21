using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Calcpad.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Calcpad.Server.Services.Storage
{
    public class S3FileStorageService : IFileStorageService
    {
        private const string MetaSha256 = "x-amz-meta-sha256";
        private const string MetaContentType = "x-amz-meta-content-type";
        private const string MetaDisplayName = "x-amz-meta-display-name";
        private const string MetaOwner = "x-amz-meta-owner";

        private readonly IAmazonS3 _s3;
        private readonly CalcpadAuthDbContext _db;
        private readonly S3Options _options;

        public S3FileStorageService(IAmazonS3 s3, CalcpadAuthDbContext db, IOptions<S3Options> options)
        {
            _s3 = s3;
            _db = db;
            _options = options.Value;
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (!_options.EnsureBucketVersioning) return;

            try
            {
                var current = await _s3.GetBucketVersioningAsync(new GetBucketVersioningRequest
                {
                    BucketName = _options.BucketName
                }, ct);

                if (current.VersioningConfig?.Status != VersionStatus.Enabled)
                {
                    await _s3.PutBucketVersioningAsync(new PutBucketVersioningRequest
                    {
                        BucketName = _options.BucketName,
                        VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
                    }, ct);
                }
            }
            catch (AmazonS3Exception ex)
            {
                FileLogger.LogError($"Failed to ensure versioning on bucket '{_options.BucketName}'", ex);
            }
        }

        public async Task<StoredFile> CreateAsync(UploadRequest request, CancellationToken ct = default)
        {
            var entry = new FileEntry
            {
                Id = Guid.NewGuid(),
                OwnerUserId = request.OwnerUserId,
                DisplayName = request.DisplayName,
                Description = request.Description,
                Tags = request.Tags,
                MimeType = request.MimeType,
            };
            entry.ObjectKey = BuildObjectKey(entry.OwnerUserId, entry.Id);

            var (versionId, sha256, size) = await PutObjectAsync(entry.ObjectKey, request.Body, request.MimeType, request.DisplayName, request.OwnerUserId, ct);

            entry.SizeBytes = size;
            entry.CurrentVersionId = versionId;
            entry.CurrentSha256 = sha256;
            entry.UpdatedAt = DateTime.UtcNow;

            _db.Files.Add(entry);
            await _db.SaveChangesAsync(ct);

            return new StoredFile(entry, versionId, sha256);
        }

        public async Task<StoredFile> UpdateAsync(Guid fileId, string ownerUserId, Stream body, string? mimeType, CancellationToken ct = default)
        {
            var entry = await _db.Files.FirstOrDefaultAsync(f => f.Id == fileId && f.OwnerUserId == ownerUserId && f.DeletedAt == null, ct)
                ?? throw new KeyNotFoundException($"File {fileId} not found for user");

            var contentType = mimeType ?? entry.MimeType;
            var (versionId, sha256, size) = await PutObjectAsync(entry.ObjectKey, body, contentType, entry.DisplayName, entry.OwnerUserId, ct);

            entry.MimeType = contentType;
            entry.SizeBytes = size;
            entry.CurrentVersionId = versionId;
            entry.CurrentSha256 = sha256;
            entry.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return new StoredFile(entry, versionId, sha256);
        }

        public async Task<FileEntry?> FindAsync(Guid fileId, string ownerUserId, CancellationToken ct = default)
        {
            return await _db.Files.FirstOrDefaultAsync(f => f.Id == fileId && f.OwnerUserId == ownerUserId && f.DeletedAt == null, ct);
        }

        public async Task<FileContent?> GetLatestAsync(Guid fileId, string ownerUserId, CancellationToken ct = default)
        {
            var entry = await FindAsync(fileId, ownerUserId, ct);
            if (entry == null) return null;
            return await FetchAsync(entry, versionId: null, ct);
        }

        public async Task<FileContent?> GetVersionAsync(Guid fileId, string ownerUserId, string versionId, CancellationToken ct = default)
        {
            var entry = await FindAsync(fileId, ownerUserId, ct);
            if (entry == null) return null;
            return await FetchAsync(entry, versionId, ct);
        }

        public async Task<IReadOnlyList<FileVersion>> ListVersionsAsync(Guid fileId, string ownerUserId, CancellationToken ct = default)
        {
            var entry = await FindAsync(fileId, ownerUserId, ct);
            if (entry == null) return Array.Empty<FileVersion>();

            var response = await _s3.ListVersionsAsync(new ListVersionsRequest
            {
                BucketName = _options.BucketName,
                Prefix = entry.ObjectKey
            }, ct);

            var versions = new List<FileVersion>();

            foreach (var v in response.Versions)
            {
                if (v.Key != entry.ObjectKey) continue;
                string? sha = null;
                long size = v.Size;
                if (!v.IsDeleteMarker)
                {
                    var head = await SafeHeadAsync(entry.ObjectKey, v.VersionId, ct);
                    if (head != null)
                    {
                        sha = head.Metadata["sha256"];
                        size = head.ContentLength;
                    }
                }
                versions.Add(new FileVersion(
                    v.VersionId,
                    v.IsLatest,
                    v.IsDeleteMarker,
                    size,
                    v.LastModified,
                    sha));
            }

            return versions
                .OrderByDescending(x => x.LastModified)
                .ToList();
        }

        public async Task<StoredFile?> RestoreVersionAsync(Guid fileId, string ownerUserId, string versionId, CancellationToken ct = default)
        {
            var entry = await FindAsync(fileId, ownerUserId, ct);
            if (entry == null) return null;

            var copy = await _s3.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = _options.BucketName,
                SourceKey = entry.ObjectKey,
                SourceVersionId = versionId,
                DestinationBucket = _options.BucketName,
                DestinationKey = entry.ObjectKey,
                MetadataDirective = S3MetadataDirective.COPY
            }, ct);

            var head = await SafeHeadAsync(entry.ObjectKey, copy.VersionId, ct);
            entry.CurrentVersionId = copy.VersionId;
            entry.CurrentSha256 = head?.Metadata["sha256"];
            entry.SizeBytes = head?.ContentLength ?? entry.SizeBytes;
            entry.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new StoredFile(entry, copy.VersionId, entry.CurrentSha256 ?? "");
        }

        public async Task<bool> SoftDeleteAsync(Guid fileId, string ownerUserId, CancellationToken ct = default)
        {
            var entry = await FindAsync(fileId, ownerUserId, ct);
            if (entry == null) return false;

            await _s3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _options.BucketName,
                Key = entry.ObjectKey
            }, ct);

            entry.DeletedAt = DateTime.UtcNow;
            entry.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<IReadOnlyList<FileEntry>> ListForOwnerAsync(string ownerUserId, CancellationToken ct = default)
        {
            return await _db.Files
                .Where(f => f.OwnerUserId == ownerUserId && f.DeletedAt == null)
                .OrderByDescending(f => f.UpdatedAt)
                .ToListAsync(ct);
        }

        private static string BuildObjectKey(string ownerUserId, Guid fileId)
            => $"users/{ownerUserId}/files/{fileId:N}";

        private async Task<(string versionId, string sha256, long size)> PutObjectAsync(
            string key, Stream body, string contentType, string displayName, string ownerUserId, CancellationToken ct)
        {
            using var buffered = new MemoryStream();
            await body.CopyToAsync(buffered, ct);
            buffered.Position = 0;

            var sha = await ComputeSha256Async(buffered, ct);
            buffered.Position = 0;

            var put = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key,
                InputStream = buffered,
                ContentType = contentType,
                AutoCloseStream = false,
                DisablePayloadSigning = true
            };
            put.Metadata.Add("sha256", sha);
            put.Metadata.Add("content-type", contentType);
            put.Metadata.Add("display-name", SanitizeMetadata(displayName));
            put.Metadata.Add("owner", ownerUserId);

            var response = await _s3.PutObjectAsync(put, ct);
            return (response.VersionId ?? "", sha, buffered.Length);
        }

        private async Task<FileContent?> FetchAsync(FileEntry entry, string? versionId, CancellationToken ct)
        {
            try
            {
                var get = new GetObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = entry.ObjectKey
                };
                if (!string.IsNullOrEmpty(versionId)) get.VersionId = versionId;

                var response = await _s3.GetObjectAsync(get, ct);
                var body = new MemoryStream();
                await response.ResponseStream.CopyToAsync(body, ct);
                body.Position = 0;

                return new FileContent(
                    body,
                    response.Headers.ContentType ?? entry.MimeType,
                    response.ContentLength,
                    response.VersionId ?? versionId ?? "",
                    response.Metadata["sha256"],
                    response.Metadata["display-name"] ?? entry.DisplayName);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        private async Task<GetObjectMetadataResponse?> SafeHeadAsync(string key, string versionId, CancellationToken ct)
        {
            try
            {
                return await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _options.BucketName,
                    Key = key,
                    VersionId = versionId
                }, ct);
            }
            catch (AmazonS3Exception)
            {
                return null;
            }
        }

        private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken ct)
        {
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string SanitizeMetadata(string value)
        {
            // S3 user-metadata header values must be ASCII; strip non-ASCII to avoid signing issues.
            var span = value.AsSpan();
            Span<char> buffer = stackalloc char[span.Length];
            int j = 0;
            for (int i = 0; i < span.Length; i++)
            {
                var c = span[i];
                if (c >= 0x20 && c < 0x7F) buffer[j++] = c;
            }
            return j == 0 ? "file" : new string(buffer[..j]);
        }
    }
}
