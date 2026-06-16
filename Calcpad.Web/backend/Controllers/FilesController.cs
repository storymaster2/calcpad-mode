using Calcpad.Server.Services.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Calcpad.Server.Controllers
{
    [ApiController]
    [Route("api/files")]
    [Authorize]
    public class FilesController : ControllerBase
    {
        private readonly IFileStorageService? _storage;

        public FilesController(IFileStorageService? storage = null)
        {
            _storage = storage;
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            if (_storage == null) return NotFound(new { error = "Storage is not enabled" });
            var userId = User.FindFirst("userId")?.Value;
            if (userId == null) return Unauthorized();

            var files = await _storage.ListForOwnerAsync(userId, ct);
            return Ok(files.Select(ToDto));
        }

        [HttpPost]
        [RequestSizeLimit(100 * 1024 * 1024)]
        public async Task<IActionResult> Create(
            [FromForm] string displayName,
            [FromForm] string? description,
            [FromForm] string? tags,
            IFormFile file,
            CancellationToken ct)
        {
            if (_storage == null) return NotFound(new { error = "Storage is not enabled" });
            var userId = User.FindFirst("userId")?.Value;
            if (userId == null) return Unauthorized();

            if (file == null || file.Length == 0) return BadRequest(new { error = "File body is required" });
            if (string.IsNullOrWhiteSpace(displayName)) return BadRequest(new { error = "displayName is required" });

            await using var body = file.OpenReadStream();
            var stored = await _storage.CreateAsync(new UploadRequest(
                userId,
                displayName,
                description,
                tags,
                file.ContentType ?? "application/octet-stream",
                body), ct);

            return Created($"/api/files/{stored.Entry.Id}", new
            {
                id = stored.Entry.Id,
                versionId = stored.VersionId,
                sha256 = stored.Sha256,
                stored.Entry.DisplayName,
                stored.Entry.SizeBytes,
                stored.Entry.MimeType
            });
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            if (_storage == null) return NotFound(new { error = "Storage is not enabled" });
            var userId = User.FindFirst("userId")?.Value;
            if (userId == null) return Unauthorized();

            var content = await _storage.GetLatestAsync(id, userId, ct);
            if (content == null) return NotFound();

            Response.Headers["x-version-id"] = content.VersionId;
            if (!string.IsNullOrEmpty(content.Sha256)) Response.Headers["x-content-sha256"] = content.Sha256;
            return File(content.Body, content.ContentType, content.DisplayName);
        }

        [HttpPost("{id:guid}")]
        [RequestSizeLimit(100 * 1024 * 1024)]
        public async Task<IActionResult> Update(Guid id, IFormFile file, CancellationToken ct)
        {
            if (_storage == null) return NotFound(new { error = "Storage is not enabled" });
            var userId = User.FindFirst("userId")?.Value;
            if (userId == null) return Unauthorized();

            if (file == null || file.Length == 0) return BadRequest(new { error = "File body is required" });

            try
            {
                await using var body = file.OpenReadStream();
                var stored = await _storage.UpdateAsync(id, userId, body, file.ContentType, ct);
                return Ok(new
                {
                    id = stored.Entry.Id,
                    versionId = stored.VersionId,
                    sha256 = stored.Sha256,
                    stored.Entry.SizeBytes,
                    stored.Entry.MimeType
                });
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpGet("{id:guid}/versions")]
        public async Task<IActionResult> ListVersions(Guid id, CancellationToken ct)
        {
            if (_storage == null) return NotFound(new { error = "Storage is not enabled" });
            var userId = User.FindFirst("userId")?.Value;
            if (userId == null) return Unauthorized();

            var versions = await _storage.ListVersionsAsync(id, userId, ct);
            return Ok(versions);
        }

        [HttpGet("{id:guid}/versions/{versionId}")]
        public async Task<IActionResult> GetVersion(Guid id, string versionId, CancellationToken ct)
        {
            if (_storage == null) return NotFound(new { error = "Storage is not enabled" });
            var userId = User.FindFirst("userId")?.Value;
            if (userId == null) return Unauthorized();

            var content = await _storage.GetVersionAsync(id, userId, versionId, ct);
            if (content == null) return NotFound();

            Response.Headers["x-version-id"] = content.VersionId;
            if (!string.IsNullOrEmpty(content.Sha256)) Response.Headers["x-content-sha256"] = content.Sha256;
            return File(content.Body, content.ContentType, content.DisplayName);
        }

        [HttpPost("{id:guid}/restore/{versionId}")]
        public async Task<IActionResult> Restore(Guid id, string versionId, CancellationToken ct)
        {
            if (_storage == null) return NotFound(new { error = "Storage is not enabled" });
            var userId = User.FindFirst("userId")?.Value;
            if (userId == null) return Unauthorized();

            var stored = await _storage.RestoreVersionAsync(id, userId, versionId, ct);
            if (stored == null) return NotFound();

            return Ok(new
            {
                id = stored.Entry.Id,
                versionId = stored.VersionId,
                sha256 = stored.Sha256
            });
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            if (_storage == null) return NotFound(new { error = "Storage is not enabled" });
            var userId = User.FindFirst("userId")?.Value;
            if (userId == null) return Unauthorized();

            var deleted = await _storage.SoftDeleteAsync(id, userId, ct);
            return deleted ? NoContent() : NotFound();
        }

        private static object ToDto(Data.FileEntry f) => new
        {
            id = f.Id,
            displayName = f.DisplayName,
            description = f.Description,
            tags = f.Tags,
            mimeType = f.MimeType,
            sizeBytes = f.SizeBytes,
            currentVersionId = f.CurrentVersionId,
            currentSha256 = f.CurrentSha256,
            createdAt = f.CreatedAt,
            updatedAt = f.UpdatedAt
        };
    }
}
