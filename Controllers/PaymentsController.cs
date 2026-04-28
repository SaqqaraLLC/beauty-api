using Beauty.Api.Data;
using Beauty.Api.Models.Payments;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IStripeService _stripe;
    private readonly BeautyDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IStripeService stripe,
        BeautyDbContext db,
        IConfiguration config,
        ILogger<PaymentsController> logger)
    {
        _stripe = stripe;
        _db     = db;
        _config = config;
        _logger = logger;
    }

    // ── POST /api/payments/create-intent ──────────────────────────────────────
    // Creates a Stripe PaymentIntent and returns the client secret for the frontend.

    public record CreateIntentRequest(long AmountCents, string Description, string PayerEmail, long? BookingId = null);

    [HttpPost("create-intent")]
    public async Task<IActionResult> CreateIntent([FromBody] CreateIntentRequest req)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var result = await _stripe.CreatePaymentIntentAsync(
            req.AmountCents, req.Description, req.PayerEmail, req.BookingId, userId);

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Ok(new
        {
            paymentId      = result.PaymentId,
            paymentIntentId = result.PaymentIntentId,
            clientSecret   = result.ClientSecret,
        });
    }

    // ── POST /api/payments/{id}/confirm ───────────────────────────────────────
    // Called after the frontend confirms payment with Stripe.js.

    [HttpPost("{id:long}/confirm")]
    public async Task<IActionResult> ConfirmPayment(long id)
    {
        var payment = await _stripe.GetPaymentAsync(id);
        if (payment == null) return NotFound();

        var result = await _stripe.ConfirmPaymentAsync(payment.WorldpayTransactionId);
        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Ok(new { paymentId = result.PaymentId, status = result.Status });
    }

    // ── GET /api/payments/{id} ────────────────────────────────────────────────

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetPayment(long id)
    {
        var payment = await _stripe.GetPaymentAsync(id);
        if (payment == null) return NotFound();

        return Ok(new
        {
            payment.PaymentId,
            stripePaymentIntentId = payment.WorldpayTransactionId,
            payment.BookingId,
            payment.AmountCents,
            payment.CurrencyCode,
            payment.Status,
            payment.CreatedAt,
            payment.CompletedAt,
            payment.CardLast4,
            payment.CardBrand,
            RefundCount    = payment.Refunds.Count,
            RefundedAmount = payment.Refunds.Sum(r => r.AmountCents)
        });
    }

    // ── POST /api/payments/{id}/refund ────────────────────────────────────────

    public record RefundRequest(long? AmountCents);

    [HttpPost("{id:long}/refund")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RefundPayment(long id, [FromBody] RefundRequest request)
    {
        var result = await _stripe.RefundAsync(id, request.AmountCents);
        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Ok(new
        {
            refundId      = result.RefundId,
            stripeRefundId = result.WorldpayRefundId,
            amountCents   = result.AmountCents
        });
    }

    // ── POST /api/payments/webhook ────────────────────────────────────────────
    // Stripe sends signed events here. Verify signature, then update DB.

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleWebhook()
    {
        var webhookSecret = _config["Stripe:WebhookSecret"];
        if (string.IsNullOrEmpty(webhookSecret))
        {
            _logger.LogWarning("[STRIPE WEBHOOK] Stripe:WebhookSecret not configured — skipping signature check");
            return Ok(new { status = "no_secret" });
        }

        string json;
        using (var reader = new System.IO.StreamReader(Request.Body))
            json = await reader.ReadToEndAsync();

        Request.Headers.TryGetValue("Stripe-Signature", out var sig);

        Event stripeEvent;
        try
        {
            stripeEvent = _stripe.ConstructWebhookEvent(json, sig.ToString(), webhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("[STRIPE WEBHOOK] Signature validation failed: {Msg}", ex.Message);
            return Unauthorized(new { error = "Invalid signature" });
        }

        _logger.LogInformation("[STRIPE WEBHOOK] {EventType} — {EventId}", stripeEvent.Type, stripeEvent.Id);

        try
        {
            switch (stripeEvent.Type)
            {
                case "payment_intent.succeeded":
                {
                    var intent  = stripeEvent.Data.Object as PaymentIntent;
                    var payment = intent == null ? null
                        : await _db.WpPayments.FirstOrDefaultAsync(p => p.WorldpayTransactionId == intent.Id);

                    if (payment != null)
                    {
                        payment.Status      = WpPaymentStatus.Captured;
                        payment.CompletedAt = DateTime.UtcNow;
                        payment.CardLast4   = intent!.LatestCharge?.PaymentMethodDetails?.Card?.Last4;
                        payment.CardBrand   = intent.LatestCharge?.PaymentMethodDetails?.Card?.Brand;
                        Audit(payment.PaymentId, WpPaymentAuditAction.Captured, "Succeeded via webhook");
                        await _db.SaveChangesAsync();
                    }
                    break;
                }
                case "payment_intent.payment_failed":
                {
                    var intent  = stripeEvent.Data.Object as PaymentIntent;
                    var payment = intent == null ? null
                        : await _db.WpPayments.FirstOrDefaultAsync(p => p.WorldpayTransactionId == intent.Id);

                    if (payment != null)
                    {
                        var reason = intent!.LastPaymentError?.Message ?? "Payment failed";
                        payment.Status       = WpPaymentStatus.Failed;
                        payment.ErrorMessage = reason;
                        Audit(payment.PaymentId, WpPaymentAuditAction.Error, $"Failed: {reason}");
                        await _db.SaveChangesAsync();
                    }
                    break;
                }
                case "charge.refunded":
                {
                    var charge  = stripeEvent.Data.Object as Charge;
                    var payment = charge == null ? null
                        : await _db.WpPayments.FirstOrDefaultAsync(p => p.WorldpayTransactionId == charge.PaymentIntentId);

                    if (payment != null)
                    {
                        payment.Status = WpPaymentStatus.Refunded;
                        Audit(payment.PaymentId, WpPaymentAuditAction.Refunded, $"Refunded via webhook — charge {charge!.Id}");
                        await _db.SaveChangesAsync();
                    }
                    break;
                }
                default:
                    _logger.LogInformation("[STRIPE WEBHOOK] Unhandled event type: {EventType}", stripeEvent.Type);
                    break;
            }

            return Ok(new { status = "processed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[STRIPE WEBHOOK] Processing error for event {EventId}", stripeEvent.Id);
            return BadRequest(new { error = "Webhook processing failed" });
        }
    }

    private void Audit(long paymentId, WpPaymentAuditAction action, string details)
        => _db.WpPaymentAuditLogs.Add(new WpPaymentAuditLog
        {
            PaymentId = paymentId,
            Action    = action,
            Details   = details,
            Timestamp = DateTime.UtcNow
        });
}
