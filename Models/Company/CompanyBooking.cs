namespace Beauty.Api.Models.Company;

public enum CompanyBookingStatus
{
    Draft,
    Submitted,
    PartiallyAccepted,
    FullyAccepted,
    Completed,
    Cancelled,
    Rejected
}

public class CompanyBooking
{
    public long Id { get; set; }

    public long CompanyId { get; set; }
    public CompanyProfile Company { get; set; } = null!;

    // Submitted by this platform user
    public string SubmittedByUserId { get; set; } = "";

    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime EventDate { get; set; }
    public DateTime? EventEndDate { get; set; }
    public string Location { get; set; } = "";
    public bool NdaRequired { get; set; } = false;
    public string? PackageLabel { get; set; }
    public decimal? PackageDiscountPercent { get; set; }

    public CompanyBookingStatus Status { get; set; } = CompanyBookingStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Contract
    public string? ContractUrl { get; set; }
    public DateTime? ContractGeneratedAt { get; set; }

    public ICollection<CompanyBookingArtistSlot> ArtistSlots { get; set; } = new List<CompanyBookingArtistSlot>();
}
