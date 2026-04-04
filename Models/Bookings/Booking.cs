
using System;
using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models;

public class Booking
{
    public long BookingId { get; set; }

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

    public BookingStatus Status { get; set; } = BookingStatus.PendingApprovals;

    public bool CanCustomerCompleteApplication =>
        Status == BookingStatus.Approved;

    public void RecalculateStatus()
    {
        if (ArtistApproval == ApprovalDecision.Rejected ||
            LocationApproval == ApprovalDecision.Rejected)
        {
            Status = BookingStatus.Rejected;
            return;
        }

        if (ArtistApproval == ApprovalDecision.Approved &&
            LocationApproval == ApprovalDecision.Approved)
        {
            Status = BookingStatus.Approved;
            return;
        }

        if (ArtistApproval == ApprovalDecision.Approved &&
            LocationApproval == ApprovalDecision.Pending)
        {
            Status = BookingStatus.ArtistApproved;
            return;
        }

        if (ArtistApproval == ApprovalDecision.Pending &&
            LocationApproval == ApprovalDecision.Approved)
        {
            Status = BookingStatus.LocationApproved;
            return;
        }

        Status = BookingStatus.PendingApprovals;
    }
}
