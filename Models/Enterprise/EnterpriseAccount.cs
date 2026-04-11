using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Enterprise;

public class EnterpriseAccount
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(300)]
    public string LegalName { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = "";

    /// <summary>Pending | Active | Suspended | Terminated</summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Pending";

    /// <summary>Billing tier key — e.g. Starter, Professional, Enterprise</summary>
    [Required]
    [MaxLength(100)]
    public string BillingTier { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ActivatedAt { get; set; }

    public DateTime? SuspendedAt { get; set; }
}
