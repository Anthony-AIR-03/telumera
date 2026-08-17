using Microsoft.EntityFrameworkCore;

namespace Telumera.Services.IdentityWorkspace.Api;

public sealed class IdentityWorkspaceDbContext(DbContextOptions<IdentityWorkspaceDbContext> options)
    : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces");
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Name).IsRequired().HasMaxLength(200);
        });
    }
}
