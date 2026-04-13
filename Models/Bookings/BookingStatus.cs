namespace Beauty.Api.Models;

// Overall booking workflow state

public enum BookingStatus
{
    Requested = 1,
    ArtistApproved = 2,
    LocationApproved = 3,
    FullyApproved = 4,   // ✅ ADD
    Rejected = 5,
    Revoked = 6,
    Completed = 7
}
