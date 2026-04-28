using Beauty.Api.Data;
using Beauty.Api.Models.Payments;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace Beauty.Api.Services;

public interface IStripeService
{
    Task<PaymentIntentResult> CreatePaymentIntentAsync(long amountCents, string description, string payerEmail, long? bookingId = null, string? recipientUserId = null);
    Task<PaymentResult> ConfirmPaymentAsync(string paymentIntentId);
    Task<RefundResult> RefundAsync(long paymentId, long? amountCents = null);
    Task<WpPayment?> GetPaymentAsync(long paymentId);
    Event ConstructWebhookEvent(string json, string signature, string secret);
}

public sealed class StripeService : IStripeService
{
    private readonly BeautyDbContext _db;
    private readonly ILogger<StripeService> _logger;
    private readonly PaymentIntentService _paymentIntents;
    private readonly RefundService _refunds;

    public StripeService(BeautyDbContext db, IConfiguration config, ILogger<StripeService> logger)
    {
        _db     = db;
        _logger = logger;

        var secretKey = config["Stripe:SecretKey"]
            ?? throw new InvalidOperationException("Stripe:SecretKey is not configured");

        var client = new StripeClient(secretKey);
        _paymentIntents = new PaymentIntentService(client);
        _refunds        = new RefundService(client);
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(
        long amountCents, string description, string payerEmail,
        long? bookingId = null, string? recipientUserId = null)
    {
        try
        {
            var intent = await _paymentIntents.CreateAsync(new PaymentIntentCreateOptions
            {
                Amount      = amountCents,
                Currency    = "usd",
                Description = description,
                ReceiptEmail = payerEmail,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
                Metadata = new Dictionary<string, string>
                {
                    ["bookingId"]       = bookingId?.ToString() ?? "",
                    ["recipientUserId"] = recipientUserId ?? "",
                    ["payerEmail"]      = payerEmail,
                }
            });

            var payment = new WpPayment
            {
                WorldpayTransactionId = intent.Id,
                BookingId             = bookingId,
                RecipientUserId       = recipientUserId,
                PayerEmail            = payerEmail,
                AmountCents           = amountCents,
                CurrencyCode          = "USD",
                Description           = description,
                Status                = WpPaymentStatus.Pending,
                CreatedAt             = DateTime.UtcNow,
            };

            _db.WpPayments.Add(payment);
            _db.WpPaymentAuditLogs.Add(new WpPaymentAuditLog
            {
                PaymentId = payment.PaymentId,
                Action    = WpPaymentAuditAction.Created,
                Details   = $"Payment intent created for ${amountCents / 100m:F2}",
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation("[STRIPE] PaymentIntent {IntentId} created — ${Amount:F2}", intent.Id, amountCents / 100m);
            return new PaymentIntentResult(true, payment.PaymentId, intent.Id, intent.ClientSecret, null);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[STRIPE] CreatePaymentIntent error");
            return new PaymentIntentResult(false, 0, null, null, ex.StripeError?.Message ?? ex.Message);
        }
    }

    public async Task<PaymentResult> ConfirmPaymentAsync(string paymentIntentId)
    {
        try
        {
            var intent  = await _paymentIntents.GetAsync(paymentIntentId);
            var payment = await _db.WpPayments.FirstOrDefaultAsync(p => p.WorldpayTransactionId == paymentIntentId);

            if (payment == null)
                return new PaymentResult(false, 0, null, null, "Payment record not found", null);

            payment.Status      = MapStripeStatus(intent.Status);
            payment.CardLast4   = intent.PaymentMethod?.Card?.Last4;
            payment.CardBrand   = intent.PaymentMethod?.Card?.Brand;
            payment.CompletedAt = intent.Status == "succeeded" ? DateTime.UtcNow : null;

            _db.WpPaymentAuditLogs.Add(new WpPaymentAuditLog
            {
                PaymentId = payment.PaymentId,
                Action    = payment.Status == WpPaymentStatus.Captured ? WpPaymentAuditAction.Captured : WpPaymentAuditAction.Error,
                Details   = $"Stripe status: {intent.Status}",
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            return new PaymentResult(intent.Status == "succeeded", payment.PaymentId, paymentIntentId, payment.Status.ToString(), null, null);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[STRIPE] ConfirmPayment error for {IntentId}", paymentIntentId);
            return new PaymentResult(false, 0, null, null, ex.StripeError?.Message ?? ex.Message, null);
        }
    }

    public async Task<RefundResult> RefundAsync(long paymentId, long? amountCents = null)
    {
        try
        {
            var payment = await _db.WpPayments.FindAsync(paymentId);
            if (payment == null)
                return new RefundResult(false, 0, null, 0, "Payment not found", null);

            if (payment.Status != WpPaymentStatus.Captured)
                return new RefundResult(false, 0, null, 0, "Payment cannot be refunded in current status", null);

            var refundAmount = amountCents ?? payment.AmountCents;
            var refund = await _refunds.CreateAsync(new RefundCreateOptions
            {
                PaymentIntent = payment.WorldpayTransactionId,
                Amount        = refundAmount,
            });

            var refundRecord = new WpPaymentRefund
            {
                PaymentId        = paymentId,
                WorldpayRefundId = refund.Id,
                AmountCents      = refundAmount,
                Status           = WpRefundStatus.Completed,
                CreatedAt        = DateTime.UtcNow,
                CompletedAt      = DateTime.UtcNow
            };

            _db.WpPaymentRefunds.Add(refundRecord);

            if (refundAmount >= payment.AmountCents)
                payment.Status = WpPaymentStatus.Refunded;

            _db.WpPaymentAuditLogs.Add(new WpPaymentAuditLog
            {
                PaymentId = paymentId,
                Action    = WpPaymentAuditAction.Refunded,
                Details   = $"Refunded ${refundAmount / 100m:F2} — Stripe refund {refund.Id}",
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation("[STRIPE] Refund {RefundId} processed", refund.Id);
            return new RefundResult(true, refundRecord.RefundId, refund.Id, refundAmount, null, null);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[STRIPE] Refund error for payment {PaymentId}", paymentId);
            return new RefundResult(false, 0, null, 0, ex.StripeError?.Message ?? ex.Message, null);
        }
    }

    public async Task<WpPayment?> GetPaymentAsync(long paymentId)
        => await _db.WpPayments
            .Include(p => p.Refunds)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId);

    public Event ConstructWebhookEvent(string json, string signature, string secret)
        => EventUtility.ConstructEvent(json, signature, secret);

    private static WpPaymentStatus MapStripeStatus(string? status) => status switch
    {
        "succeeded"              => WpPaymentStatus.Captured,
        "requires_capture"       => WpPaymentStatus.Authorized,
        "requires_payment_method"=> WpPaymentStatus.Declined,
        "canceled"               => WpPaymentStatus.Failed,
        _                        => WpPaymentStatus.Pending
    };
}

public record PaymentIntentResult(
    bool    Success,
    long    PaymentId      = 0,
    string? PaymentIntentId = null,
    string? ClientSecret   = null,
    string? Error          = null);
