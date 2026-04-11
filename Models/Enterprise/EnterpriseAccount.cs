using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Enterprise;

public class EnterpriseAccount : ISoftDeletable
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(300)]
    public string Name { get; set; } = "";

    /// <summary>Pending | Active | Suspended</summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Pending";

    [Required]
    [MaxLength(100)]
    public string BillingTier { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ISoftDeletable
    public bool      IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}
