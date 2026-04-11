using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Beauty.Api.Models.Enterprise;

public class Payment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // ── Enterprise context ─────────────────────────────────────────
    [Required]
    public Guid EnterpriseAccountId { get; set; }

    [ForeignKey(nameof(EnterpriseAccountId))]
    public EnterpriseAccount EnterpriseAccount { get; set; } = null!;

    // ── Booking link ───────────────────────────────────────────────
    public long? BookingId { get; set; }

    [ForeignKey(nameof(BookingId))]
    public Beauty.Api.Models.Booking? Booking { get; set; }

    // ── Financial ──────────────────────────────────────────────────
    /// <summary>Amount in cents (USD)</summary>
    public long Amount { get; set; }

    /// <summary>Pending | Authorized | Captured | Failed | Refunded | Voided</summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Pending";

    /// <summary>Processor transaction reference (e.g. Worldpay transaction ID)</summary>
    [MaxLength(200)]
    public string? ProcessorReference { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }
}
