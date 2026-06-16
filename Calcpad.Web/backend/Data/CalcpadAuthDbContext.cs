using Microsoft.EntityFrameworkCore;
using Calcpad.Server.Models.Auth;

namespace Calcpad.Server.Data
{
    public class CalcpadAuthDbContext : DbContext
    {
        public CalcpadAuthDbContext(DbContextOptions<CalcpadAuthDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<FileEntry> Files { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(u => u.Id);
                entity.HasIndex(u => u.Username).IsUnique();
                entity.HasIndex(u => u.Email).IsUnique();
                entity.Property(u => u.Role).HasDefaultValue(UserRole.Contributor);
                entity.Property(u => u.IsActive).HasDefaultValue(true);
            });

            modelBuilder.Entity<FileEntry>(entity =>
            {
                entity.ToTable("Files");
                entity.HasKey(f => f.Id);
                entity.HasIndex(f => f.OwnerUserId);
                entity.HasIndex(f => f.ObjectKey).IsUnique();
            });
        }
    }
}
