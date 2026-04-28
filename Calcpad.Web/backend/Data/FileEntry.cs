using System.ComponentModel.DataAnnotations;

namespace Calcpad.Server.Data
{
    public class FileEntry
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string OwnerUserId { get; set; } = "";

        [Required]
        public string ObjectKey { get; set; } = "";

        [Required]
        public string DisplayName { get; set; } = "";

        public string? Description { get; set; }

        public string? Tags { get; set; }

        public string MimeType { get; set; } = "application/octet-stream";

        public long SizeBytes { get; set; }

        public string? CurrentVersionId { get; set; }

        public string? CurrentSha256 { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? DeletedAt { get; set; }
    }
}
