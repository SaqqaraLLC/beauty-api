using Microsoft.Graph.Models;

namespace Beauty.Api.Models.ApprovalHistory;

public class BookingApprovalHistory
{
    public long Id { get; set; }

    public long BookingId { get; set; }
    public Booking Booking { get; set; } = null!;

    // Artist, Location, Director
    public ApprovalStage Stage { get; set; }

    // Approved, Rejected, Revoked
    public ApprovalAction Action { get; set; }

    public DateTime ActionAt { get; set; }

    // Who did it
    public required string PerformedByUserId { get; set; }
    public required string PerformedByEmail { get; set; }
    public required string PerformedByRole { get; set; }

    // Optional but powerful
    public string? Comment { get; set; }
    public string? IpAddress { get; set; }
}
