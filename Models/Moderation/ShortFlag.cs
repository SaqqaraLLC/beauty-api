using System.ComponentModel.DataAnnotations;
using Beauty.Api.Models.Gifts;

namespace Beauty.Api.Models.Moderation;

public class ShortFlag
{
    public long Id { get; set; }

    public long ShortId { get; set; }
    public ArtistShort Short { get; set; } = null!;

    [Required, MaxLength(500)]
    public string Reason { get; set; } = "";

    [MaxLength(450)]
    public string? FlaggedByUserId { get; set; }

    public StreamFlagStatus Status { get; set; } = StreamFlagStatus.Flagged;

    [MaxLength(50)]
    public string? Action { get; set; }

    [MaxLength(450)]
    public string? ReviewedByUserId { get; set; }

    [MaxLength(200)]
    public string? ReviewedByName { get; set; }

    public DateTime? ReviewedAt { get; set; }

    [MaxLength(1000)]
    public string? ReviewNotes { get; set; }

    public DateTime FlaggedAt { get; set; } = DateTime.UtcNow;
}
