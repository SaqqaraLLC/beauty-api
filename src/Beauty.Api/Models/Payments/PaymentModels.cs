using System;
using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Payments;

/// <summary>
/// Represents a payment transaction processed through Worldpay.
/// </summary>
public class Payment
{
    public long PaymentId { get; set; }

    [Required, StringLength(100)]
    public string WorldpayTransactionId { get; set; } = string.Empty;

    /// <summary>
    /// The booking this payment is for (null allows for other payment types)
    /// </summary>
    public long? BookingId { get; set; }
    public Booking? Booking { get; set; }

    /// <summary>
    /// Artist or location receiving payment
    /// </summary>
    public string? RecipientUserId { get; set; }

    [Required, StringLength(255)]
    public string PayerEmail { get; set; } = string.Empty;

    /// <summary>Amount in cents (e.g., $10.00 = 1000)</summary>
    public long AmountCents { get; set; }

    [StringLength(3)]
    public string CurrencyCode { get; set; } = "USD";

    /// <summary>Pending, Authorized, Captured, Declined, Refunded, Failed</summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    [StringLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>When the payment was completed</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Error/decline reason from Worldpay</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Worldpay response code</summary>
    [StringLength(50)]
    public string? ResponseCode { get; set; }

    /// <summary>Last 4 digits of card (masked)</summary>
    [StringLength(4)]
    public string? CardLast4 { get; set; }

    [StringLength(50)]
    public string? CardBrand { get; set; } // Visa, Mastercard, etc.

    /// <summary>For future use with Worldpay tokenization</summary>
    [StringLength(255)]
    public string? PaymentTokenId { get; set; }

    public ICollection<PaymentRefund> Refunds { get; set; } = new List<PaymentRefund>();
}

/// <summary>
/// Tracks refunds for payments.
/// </summary>
public class PaymentRefund
{
    public long RefundId { get; set; }

    public long PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;

    /// <summary>Worldpay refund transaction ID</summary>
    [Required, StringLength(100)]
    public string WorldpayRefundId { get; set; } = string.Empty;

    /// <summary>Amount refunded in cents</summary>
    public long AmountCents { get; set; }

    /// <summary>Pending, Completed, Failed</summary>
    public RefundStatus Status { get; set; } = RefundStatus.Pending;

    [StringLength(500)]
    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Audit log for payment operations.
/// </summary>
public class PaymentAuditLog
{
    public long LogId { get; set; }

    public long PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;

    public PaymentAuditAction Action { get; set; }

    [StringLength(500)]
    public string? Details { get; set; }

    public DateTime Timestamp { get; set; }
}

// ============================================
// ENUMS
// ============================================

public enum PaymentStatus
{
    Pending = 1,
    Authorized = 2,
    Captured = 3,
    Declined = 4,
    Refunded = 5,
    Failed = 6,
    Expired = 7
}

public enum RefundStatus
{
    Pending = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

public enum PaymentAuditAction
{
    Created = 1,
    Authorized = 2,
    Captured = 3,
    Declined = 4,
    Refunded = 5,
    Error = 6,
    Reconciled = 7
}
