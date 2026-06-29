using Microsoft.EntityFrameworkCore;
using TerrariaModArchive.Core.Models;

namespace TerrariaModArchive.Core.Data;

public class ArchiveDbContext : DbContext
{
    public ArchiveDbContext(DbContextOptions<ArchiveDbContext> options) : base(options)
    {
    }

    public DbSet<Mod> Mods { get; set; } = null!;
    public DbSet<ModVersion> ModVersions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Mod>()
            .HasIndex(m => m.SteamId)
            .IsUnique();

        modelBuilder.Entity<ModVersion>()
            .HasIndex(mv => new { mv.ModId, mv.Version, mv.TModLoaderVersion })
            .IsUnique();
    }
}
