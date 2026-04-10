using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Enterprise;

public class AvailabilityBlock
{
    [Key]
    public int BlockId { get; set; }

    public int ArtistProfileId { get; set; }
    public ArtistProfile ArtistProfile { get; set; } = null!;

    public DateOnly Date { get; set; }

    public TimeOnly? StartTime { get; set; }

    public TimeOnly? EndTime { get; set; }

    public bool IsAvailable { get; set; } = true;

    [MaxLength(500)]
    public string? Note { get; set; }
}
