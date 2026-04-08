using Microsoft.Graph.Models;

namespace Beauty.Api.Models.ApprovalHistory;

public class BookingApprovalHistory
{
    public long Id { get; set; }

    public long BookingId { get; set; }
    public Booking Booking { get; set; }

    // Artist, Location, Director
    public ApprovalStage Stage { get; set; }

    // Approved, Rejected, Revoked
    public ApprovalAction Action { get; set; }

    public DateTime ActionAt { get; set; }

    // Who did it
    public string PerformedByUserId { get; set; }
    public string PerformedByEmail { get; set; }
    public string PerformedByRole { get; set; }

    // Optional but powerful
    public string? Comment { get; set; }
    public string? IpAddress { get; set; }
}
