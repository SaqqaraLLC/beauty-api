
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Beauty.Api.Models;

public class Booking
{
    public long BookingId { get; set; }
    public BookingStatus Status { get; set; }

    public long CustomerId { get; set; }
    public long ArtistId { get; set; }
    public long ServiceId { get; set; }
    public long LocationId { get; set; }

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    // Two-party approval decisions
    public ApprovalDecision ArtistApproval { get; set; } = ApprovalDecision.Pending;
    public ApprovalDecision LocationApproval { get; set; } = ApprovalDecision.Pending;

    public DateTime? ArtistApprovedAt { get; set; }
    public DateTime? LocationApprovedAt { get; set; }

    public string? ArtistApprovedByUserId { get; set; }
    public string? LocationApprovedByUserId { get; set; }

    public string? RejectionReason { get; set; }
    public DateTime? DirectorApprovedAt { get; set; }
    public string? DirectorApprovedByUserId { get; set; }
    public ApplicationUser? DirectorApprovedByUser { get; set; }


    // ✅ computed, not stored
    [NotMapped]
    public bool CanCustomerCompleteApplication =>
            Status == BookingStatus.FullyApproved;

}
