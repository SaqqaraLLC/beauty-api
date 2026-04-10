using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Enterprise;

public class FeaturedSlot
{
    [Key]
    public int SlotId { get; set; }

    public int ArtistProfileId { get; set; }
    public ArtistProfile ArtistProfile { get; set; } = null!;

    /// <summary>Featured | Sponsored</summary>
    [Required]
    [MaxLength(50)]
    public string SlotType { get; set; } = "Featured";

    public int DisplayPosition { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public bool IsActive { get; set; } = true;
}
