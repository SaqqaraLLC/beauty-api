using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Beauty.Api.Models.Enterprise;

public class AuditLog
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // ── Enterprise context ─────────────────────────────────────────
    [Required]
    public Guid EnterpriseAccountId { get; set; }

    [ForeignKey(nameof(EnterpriseAccountId))]
    public EnterpriseAccount EnterpriseAccount { get; set; } = null!;

    // ── Actor ──────────────────────────────────────────────────────
    /// <summary>Identity user ID of the person who performed the action</summary>
    [Required]
    [MaxLength(450)]
    public string ActorUserId { get; set; } = "";

    // ── Action ─────────────────────────────────────────────────────
    /// <summary>e.g. "Booking.Created", "User.Suspended", "Payment.Captured"</summary>
    [Required]
    [MaxLength(200)]
    public string Action { get; set; } = "";

    /// <summary>Entity type that was affected — e.g. "Booking", "EnterpriseUser"</summary>
    [Required]
    [MaxLength(100)]
    public string TargetType { get; set; } = "";

    /// <summary>String representation of the target entity's primary key</summary>
    [MaxLength(200)]
    public string? TargetId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
