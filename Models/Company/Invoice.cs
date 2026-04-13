namespace Beauty.Api.Models.Company;

public enum InvoiceStatus { Draft, Sent, Paid, Overdue, Void }

public class Invoice
{
    public long Id { get; set; }
    public string InvoiceNumber { get; set; } = "";   // e.g. INV-000042

    // Linked to company booking
    public long? CompanyBookingId { get; set; }
    public CompanyBooking? CompanyBooking { get; set; }

    // Billed to
    public long CompanyId { get; set; }
    public CompanyProfile Company { get; set; } = null!;

    public string IssuedByUserId { get; set; } = "";

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime DueAt { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public string? Notes { get; set; }

    // PDF blob name
    public string? PdfUrl { get; set; }
    public DateTime? PdfGeneratedAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public ICollection<InvoiceLineItem> LineItems { get; set; } = new List<InvoiceLineItem>();
}

public class InvoiceLineItem
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    public string Description { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public int UnitPriceCents { get; set; }     // price per unit in cents
    public decimal? DiscountPercent { get; set; }

    public int TotalCents =>
        (int)(Quantity * UnitPriceCents * (1 - (DiscountPercent ?? 0) / 100m));
}
