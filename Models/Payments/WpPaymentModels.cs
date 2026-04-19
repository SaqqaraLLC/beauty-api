using System.ComponentModel.DataAnnotations;
using Beauty.Api.Models;

namespace Beauty.Api.Models.Payments;

public class WpPayment
{
    public long PaymentId { get; set; }

    [Required, StringLength(100)]
    public string WorldpayTransactionId { get; set; } = string.Empty;

    public long? BookingId { get; set; }
    public Booking? Booking { get; set; }

    public string? RecipientUserId { get; set; }

    [Required, StringLength(255)]
    public string PayerEmail { get; set; } = string.Empty;

    public long AmountCents { get; set; }

    [StringLength(3)]
    public string CurrencyCode { get; set; } = "USD";

    public WpPaymentStatus Status { get; set; } = WpPaymentStatus.Pending;

    [StringLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }

    [StringLength(50)]
    public string? ResponseCode { get; set; }

    [StringLength(4)]
    public string? CardLast4 { get; set; }

    [StringLength(50)]
    public string? CardBrand { get; set; }

    [StringLength(255)]
    public string? PaymentTokenId { get; set; }

    public ICollection<WpPaymentRefund> Refunds { get; set; } = new List<WpPaymentRefund>();
    public ICollection<WpPaymentAuditLog> AuditLogs { get; set; } = new List<WpPaymentAuditLog>();
}

public class WpPaymentRefund
{
    public long RefundId { get; set; }

    public long PaymentId { get; set; }
    public WpPayment Payment { get; set; } = null!;

    [Required, StringLength(100)]
    public string WorldpayRefundId { get; set; } = string.Empty;

    public long AmountCents { get; set; }

    public WpRefundStatus Status { get; set; } = WpRefundStatus.Pending;

    [StringLength(500)]
    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class WpPaymentAuditLog
{
    public long LogId { get; set; }

    public long PaymentId { get; set; }
    public WpPayment Payment { get; set; } = null!;

    public WpPaymentAuditAction Action { get; set; }

    [StringLength(500)]
    public string? Details { get; set; }

    public DateTime Timestamp { get; set; }
}

public enum WpPaymentStatus
{
    Pending = 1,
    Authorized = 2,
    Captured = 3,
    Declined = 4,
    Refunded = 5,
    Failed = 6,
    Expired = 7
}

public enum WpRefundStatus
{
    Pending = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

public enum WpPaymentAuditAction
{
    Created = 1,
    Authorized = 2,
    Captured = 3,
    Declined = 4,
    Refunded = 5,
    Error = 6,
    Reconciled = 7
}
