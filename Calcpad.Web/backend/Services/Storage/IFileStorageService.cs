using Calcpad.Server.Data;

namespace Calcpad.Server.Services.Storage
{
    public record StoredFile(FileEntry Entry, string VersionId, string Sha256);

    public record FileVersion(
        string VersionId,
        bool IsLatest,
        bool IsDeleteMarker,
        long SizeBytes,
        DateTime LastModified,
        string? Sha256);

    public record FileContent(
        Stream Body,
        string ContentType,
        long SizeBytes,
        string VersionId,
        string? Sha256,
        string? DisplayName);

    public record UploadRequest(
        string OwnerUserId,
        string DisplayName,
        string? Description,
        string? Tags,
        string MimeType,
        Stream Body);

    public interface IFileStorageService
    {
        Task InitializeAsync(CancellationToken ct = default);

        Task<StoredFile> CreateAsync(UploadRequest request, CancellationToken ct = default);

        Task<StoredFile> UpdateAsync(Guid fileId, string ownerUserId, Stream body, string? mimeType, CancellationToken ct = default);

        Task<FileEntry?> FindAsync(Guid fileId, string ownerUserId, CancellationToken ct = default);

        Task<FileContent?> GetLatestAsync(Guid fileId, string ownerUserId, CancellationToken ct = default);

        Task<FileContent?> GetVersionAsync(Guid fileId, string ownerUserId, string versionId, CancellationToken ct = default);

        Task<IReadOnlyList<FileVersion>> ListVersionsAsync(Guid fileId, string ownerUserId, CancellationToken ct = default);

        Task<StoredFile?> RestoreVersionAsync(Guid fileId, string ownerUserId, string versionId, CancellationToken ct = default);

        Task<bool> SoftDeleteAsync(Guid fileId, string ownerUserId, CancellationToken ct = default);

        Task<IReadOnlyList<FileEntry>> ListForOwnerAsync(string ownerUserId, CancellationToken ct = default);
    }
}
