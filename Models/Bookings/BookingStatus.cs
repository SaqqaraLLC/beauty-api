namespace Beauty.Api.Models;

// Overall booking workflow state
public enum BookingStatus
{
    PendingApprovals = 0,
    ArtistApproved = 1,
    LocationApproved = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5
}
