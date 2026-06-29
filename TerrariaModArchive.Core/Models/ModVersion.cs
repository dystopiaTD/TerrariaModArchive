using System.ComponentModel.DataAnnotations;

namespace TerrariaModArchive.Core.Models;

public enum ModSide
{
    Client,
    Server,
    Both,
    NoSync
}

public class ModVersion
{
    public int Id { get; set; }

    public int ModId { get; set; }
    public Mod Mod { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string Version { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string TModLoaderVersion { get; set; } = string.Empty;

    public ModSide Side { get; set; } = ModSide.Both;

    [Required]
    public string FilePath { get; set; } = string.Empty;

    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
}
