using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Enterprise;

public class AgentRosterEntry
{
    [Key]
    public int RosterId { get; set; }

    public int AgentProfileId { get; set; }
    public AgentProfile AgentProfile { get; set; } = null!;

    public int ArtistProfileId { get; set; }
    public ArtistProfile ArtistProfile { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Active";

    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
}
