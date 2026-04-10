using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Beauty.Api.Models.Enterprise;

public class AgentProfile
{
    [Key]
    public int AgentProfileId { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string FullName { get; set; } = "";

    [MaxLength(200)]
    public string? AgencyName { get; set; }

    [MaxLength(2000)]
    public string? Bio { get; set; }

    public bool IsVerified { get; set; } = false;

    [Column(TypeName = "double")]
    public double AverageRating { get; set; } = 0.0;

    public int ReviewCount { get; set; } = 0;

    public int RosterCount { get; set; } = 0;

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Pending";

    /// <summary>JSON array of specialty strings</summary>
    [Required]
    public string SpecialtiesJson { get; set; } = "[]";

    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
