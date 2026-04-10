using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Enterprise;

public class Stream
{
    [Key]
    public int StreamId { get; set; }

    public int ArtistProfileId { get; set; }
    public ArtistProfile ArtistProfile { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(500)]
    public string? ThumbnailUrl { get; set; }

    public bool IsLive { get; set; } = false;

    public int ViewerCount { get; set; } = 0;

    public DateTime? ScheduledAt { get; set; }

    public DateTime? RecordedAt { get; set; }

    /// <summary>JSON array of tag strings, e.g. ["bridal","tutorial"]</summary>
    [Required]
    public string TagsJson { get; set; } = "[]";

    public bool IsActive { get; set; } = true;
}
