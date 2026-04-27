using Beauty.Api.Models;
using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Gifts;

public class SlabPurchase
{
    public long Id { get; set; }

    [Required]
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;

    public int SlabsGranted { get; set; }

    // Amount paid in cents (USD)
    public long AmountCents { get; set; }

    [MaxLength(200)]
    public string? PaymentReference { get; set; }

    public SlabPurchaseStatus Status { get; set; } = SlabPurchaseStatus.Completed;

    [MaxLength(500)]
    public string? RefundReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RefundedAt { get; set; }
}

public enum SlabPurchaseStatus
{
    Completed    = 1,
    Refunded     = 2,
}
