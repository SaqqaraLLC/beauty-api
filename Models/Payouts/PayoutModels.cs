using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Payouts;

public class PayoutCycle
{
    public long Id { get; set; }

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd   { get; set; }

    public PayoutCycleStatus Status { get; set; } = PayoutCycleStatus.PendingReview;

    /// <summary>Sum of all provider line amounts</summary>
    public long TotalProviderAmountCents { get; set; }

    /// <summary>Invoice revenue minus provider amounts = platform margin</summary>
    public long TotalPlatformAmountCents { get; set; }

    /// <summary>Total invoices collected during the period</summary>
    public long TotalInvoiceAmountCents { get; set; }

    [StringLength(450)]
    public string? ApprovedByUserId { get; set; }

    [StringLength(256)]
    public string? ApprovedByEmail { get; set; }

    public DateTime? ApprovedAt   { get; set; }
    public DateTime? DisbursedAt  { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProviderPayoutLine> Lines { get; set; } = new List<ProviderPayoutLine>();
}

public class ProviderPayoutLine
{
    public long Id { get; set; }

    public long CycleId { get; set; }
    public PayoutCycle Cycle { get; set; } = null!;

    [Required, StringLength(450)]
    public string ProviderUserId { get; set; } = string.Empty;

    [StringLength(256)]
    public string? ProviderEmail { get; set; }

    [StringLength(200)]
    public string? ProviderName { get; set; }

    [StringLength(50)]
    public string ProviderRole { get; set; } = "Artist";

    public long AmountCents { get; set; }

    public PayoutLineStatus Status { get; set; } = PayoutLineStatus.Pending;

    /// <summary>Optional: which booking generated this line</summary>
    public long? BookingId { get; set; }

    /// <summary>Optional: which invoice was paid to generate this line</summary>
    public long? InvoiceId { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}

public enum PayoutCycleStatus
{
    PendingReview = 1,
    Approved      = 2,
    Disbursed     = 3,
    Voided        = 4
}

public enum PayoutLineStatus
{
    Pending   = 1,
    Approved  = 2,
    Disbursed = 3,
    Held      = 4
}
