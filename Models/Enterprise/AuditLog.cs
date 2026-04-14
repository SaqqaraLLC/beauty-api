using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Beauty.Api.Models.Enterprise;

/// <summary>
/// Immutable audit trail — append only, never updated or soft-deleted.
/// Records every user action, admin action, and system event.
/// EnterpriseAccountId is optional for system-level events (auth, admin).
/// </summary>
public class AuditLog
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // Nullable — system events (login, admin actions) have no tenant context
    public Guid? EnterpriseAccountId { get; set; }

    [ForeignKey(nameof(EnterpriseAccountId))]
    public EnterpriseAccount? EnterpriseAccount { get; set; }

    /// <summary>Identity user ID of the actor. "system" for automated events.</summary>
    [Required]
    [MaxLength(450)]
    public string ActorUserId { get; set; } = "";

    /// <summary>Human-readable email — denormalised for readability without a join.</summary>
    [MaxLength(256)]
    public string? ActorEmail { get; set; }

    /// <summary>Verb-noun action — e.g. "Auth.Login", "Admin.UserApproved", "Booking.Approved"</summary>
    [Required]
    [MaxLength(200)]
    public string Action { get; set; } = "";

    /// <summary>Entity type + ID — e.g. "Booking/123" or "User/abc-456"</summary>
    [MaxLength(300)]
    public string? TargetEntity { get; set; }

    /// <summary>Free-form JSON or plain text with extra context (before/after state, reason).</summary>
    [MaxLength(2000)]
    public string? Details { get; set; }

    /// <summary>Caller IP address — recorded at event time.</summary>
    [MaxLength(64)]
    public string? IpAddress { get; set; }

    /// <summary>HTTP status code of the response (200, 401, 403, etc.).</summary>
    public int? ResultCode { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
