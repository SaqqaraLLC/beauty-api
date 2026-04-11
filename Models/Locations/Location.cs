using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Beauty.Api.Models.Enterprise;

namespace Beauty.Api.Models.Locations;

public class Location
{
    [Key]
    public long Id { get; set; }

    // ── Enterprise link ────────────────────────────────────────────
    public Guid? EnterpriseAccountId { get; set; }

    [ForeignKey(nameof(EnterpriseAccountId))]
    public EnterpriseAccount? EnterpriseAccount { get; set; }

    // ── Core fields ────────────────────────────────────────────────
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = "";

    [Required]
    [MaxLength(500)]
    public string Address { get; set; } = "";

    [MaxLength(100)]
    public string City { get; set; } = "";

    [MaxLength(100)]
    public string State { get; set; } = "";

    [MaxLength(20)]
    public string PostalCode { get; set; } = "";

    [MaxLength(20)]
    public string Phone { get; set; } = "";

    /// <summary>IANA timezone identifier — e.g. "America/New_York"</summary>
    [MaxLength(100)]
    public string Timezone { get; set; } = "America/New_York";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
