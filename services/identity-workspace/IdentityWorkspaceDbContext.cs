using Microsoft.EntityFrameworkCore;

namespace Telumera.Services.IdentityWorkspace.Api;

public sealed class IdentityWorkspaceDbContext(DbContextOptions<IdentityWorkspaceDbContext> options)
    : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces");
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.EntraObjectId).IsRequired().HasMaxLength(64);
            entity.Property(u => u.DisplayName).HasMaxLength(200);
            entity.Property(u => u.Email).HasMaxLength(320);
            entity.HasIndex(u => u.EntraObjectId).IsUnique();
        });

        modelBuilder.Entity<Membership>(entity =>
        {
            entity.ToTable("memberships");
            entity.HasKey(m => m.Id);
            entity.HasIndex(m => new { m.UserId, m.WorkspaceId }).IsUnique();
        });
    }
}
