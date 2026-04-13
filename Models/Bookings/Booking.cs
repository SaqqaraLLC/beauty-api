using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Beauty.Api.Models.Enterprise;

namespace Beauty.Api.Models;

public class Booking
{
    [Key]
    public long BookingId { get; set; }

    // ── Enterprise context ─────────────────────────────────────────
    public Guid? EnterpriseAccountId { get; set; }

    [ForeignKey(nameof(EnterpriseAccountId))]
    public EnterpriseAccount? EnterpriseAccount { get; set; }

    // ── Canonical FK references ────────────────────────────────────
    public long LocationId { get; set; }

    /// <summary>FK to EnterpriseClient.Id (Guid)</summary>
    public Guid? ClientId { get; set; }

    [ForeignKey(nameof(ClientId))]
    public EnterpriseClient? Client { get; set; }

    /// <summary>FK to EnterpriseUser.Id (Guid) — the assigned artist</summary>
    public Guid? ArtistUserId { get; set; }

    [ForeignKey(nameof(ArtistUserId))]
    public EnterpriseUser? ArtistUser { get; set; }

    // ── Legacy references (kept for backward compatibility) ─────────
    public long CustomerId { get; set; }
    public long ArtistId { get; set; }
    public long ServiceId { get; set; }

    // ── Scheduling ─────────────────────────────────────────────────
    public DateTime ScheduledAt { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    // ── Status ─────────────────────────────────────────────────────
    public BookingStatus Status { get; set; } = BookingStatus.Requested;

    // ── Check-in ───────────────────────────────────────────────────
    public bool      ArtistCheckedIn   { get; set; } = false;
    public DateTime? ArtistCheckedInAt { get; set; }

    // ── Service completion ─────────────────────────────────────────
    public bool      ServiceCompleted   { get; set; } = false;
    public DateTime? ServiceCompletedAt { get; set; }

    // ── Reminder flags (prevent duplicate notifications) ───────────
    public bool Reminder24hSent { get; set; } = false;
    public bool Reminder2hSent  { get; set; } = false;

    // ── Approval workflow ──────────────────────────────────────────
    public ApprovalDecision ArtistApproval { get; set; }   = ApprovalDecision.Pending;
    public ApprovalDecision LocationApproval { get; set; } = ApprovalDecision.Pending;

    public DateTime? ArtistApprovedAt   { get; set; }
    public DateTime? LocationApprovedAt { get; set; }

    [MaxLength(450)]
    public string? ArtistApprovedByUserId { get; set; }

    [MaxLength(450)]
    public string? LocationApprovedByUserId { get; set; }

    [MaxLength(1000)]
    public string? RejectionReason { get; set; }

    public DateTime? DirectorApprovedAt { get; set; }

    [MaxLength(450)]
    public string? DirectorApprovedByUserId { get; set; }

    public ApplicationUser? DirectorApprovedByUser { get; set; }

    // ── Payments ───────────────────────────────────────────────────
    public ICollection<Payment> Payments { get; set; } = [];

    [NotMapped]
    public bool CanCustomerCompleteApplication => Status == BookingStatus.FullyApproved;
}
