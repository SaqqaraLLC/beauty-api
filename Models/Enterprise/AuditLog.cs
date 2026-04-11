using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Beauty.Api.Models.Enterprise;

/// <summary>
/// Immutable audit trail — append only, never updated or soft-deleted.
/// Records every user action, admin action, and system event within a tenant.
/// </summary>
public class AuditLog
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EnterpriseAccountId { get; set; }

    [ForeignKey(nameof(EnterpriseAccountId))]
    public EnterpriseAccount EnterpriseAccount { get; set; } = null!;

    /// <summary>Identity user ID of the person who performed the action</summary>
    [Required]
    [MaxLength(450)]
    public string ActorUserId { get; set; } = "";

    /// <summary>Verb-noun action — e.g. "Booking.Created", "User.Suspended", "Payment.Refunded"</summary>
    [Required]
    [MaxLength(200)]
    public string Action { get; set; } = "";

    /// <summary>Entity type + ID — e.g. "Booking/abc-123" or "EnterpriseUser/xyz-456"</summary>
    [MaxLength(300)]
    public string? TargetEntity { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
