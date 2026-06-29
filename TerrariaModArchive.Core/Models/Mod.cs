using System.ComponentModel.DataAnnotations;

namespace TerrariaModArchive.Core.Models;

public class Mod
{
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string SteamId { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ModVersion> Versions { get; set; } = new();
}
