namespace Beauty.Api.Models.Company;

public enum SlotStatus { Pending, Accepted, Declined }

public class CompanyBookingArtistSlot
{
    public long Id { get; set; }

    public long CompanyBookingId { get; set; }
    public CompanyBooking CompanyBooking { get; set; } = null!;

    public long ArtistId { get; set; }
    public string ArtistUserId { get; set; } = "";
    public string? ArtistName { get; set; }

    public string ServiceRequested { get; set; } = "";
    public int? FeeCents { get; set; }

    public SlotStatus Status { get; set; } = SlotStatus.Pending;
    public DateTime? RespondedAt { get; set; }
    public string? ResponseNote { get; set; }
}
