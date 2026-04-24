using Beauty.Api.Data;
using Beauty.Api.Models.Payments;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IWorldpayService _worldpayService;
    private readonly BeautyDbContext _db;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IWorldpayService worldpayService,
        BeautyDbContext db,
        ILogger<PaymentsController> logger)
    {
        _worldpayService = worldpayService;
        _db = db;
        _logger = logger;
    }

    // ── DTOs ───────────────────────────────────────────────────────────────────

    public record ChargeRequest(
        long?  BookingId,
        string PayerEmail,
        string PayerName,
        string CardNumber,
        string CardExpiry,
        string CardCvc,
        string CardholderName,
        string CardBrand,
        long   AmountCents,
        string Description);

    public record RefundRequest(long? AmountCents);

    // ── POST /api/payments/charge ──────────────────────────────────────────────

    [HttpPost("charge")]
    public async Task<IActionResult> Charge([FromBody] ChargeRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var result = await _worldpayService.ChargeAsync(new PaymentRequest(
            BookingId:          request.BookingId,
            RecipientUserId:    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            PayerEmail:         request.PayerEmail,
            PayerName:          request.PayerName,
            CardNumber:         request.CardNumber,
            CardExpiry:         request.CardExpiry,
            CardCvc:            request.CardCvc,
            CardholderName:     request.CardholderName,
            CardBrand:          request.CardBrand,
            AmountCents:        request.AmountCents,
            CurrencyCode:       "USD",
            Description:        request.Description
        ));

        if (!result.Success)
            return BadRequest(new { error = result.Error, code = result.ResponseCode });

        return Ok(new
        {
            paymentId             = result.PaymentId,
            worldpayTransactionId = result.WorldpayTransactionId,
            status                = result.Status,
            amountCents           = request.AmountCents
        });
    }

    // ── GET /api/payments/{id} ─────────────────────────────────────────────────

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetPayment(long id)
    {
        var payment = await _worldpayService.GetPaymentAsync(id);
        if (payment == null) return NotFound();

        return Ok(new
        {
            payment.PaymentId,
            payment.WorldpayTransactionId,
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

    // ── POST /api/payments/{id}/refund ─────────────────────────────────────────

    [HttpPost("{id:long}/refund")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RefundPayment(long id, [FromBody] RefundRequest request)
    {
        var result = await _worldpayService.RefundAsync(id, request.AmountCents);

        if (!result.Success)
            return BadRequest(new { error = result.Error, code = result.ResponseCode });

        return Ok(new
        {
            refundId         = result.RefundId,
            worldpayRefundId = result.WorldpayRefundId,
            amountCents      = result.AmountCents
        });
    }

    // ── POST /api/payments/webhook ─────────────────────────────────────────────

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleWebhook()
    {
        Request.EnableBuffering();

        string body;
        using (var reader = new StreamReader(Request.Body, leaveOpen: true))
            body = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        Request.Headers.TryGetValue("X-AUTHVIA-VALUE",     out var authhviaValue);
        Request.Headers.TryGetValue("X-AUTHVIA-TIMESTAMP", out var authhviaTimestamp);
        Request.Headers.TryGetValue("X-AUTHVIA-SIGNATURE", out var authhviaSignature);

        if (!_worldpayService.ValidateWebhookSignature(
                authhviaValue.ToString(),
                authhviaTimestamp.ToString(),
                authhviaSignature.ToString()))
            return Unauthorized(new { error = "Invalid signature" });

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var rawType       = root.TryGetProperty("type",  out var t) ? t.GetString() : null;
            var eventType     = NormalizeEventType(rawType);
            var transactionId = root.TryGetProperty("id",    out var i) ? i.GetString()
                              : root.TryGetProperty("conversationId", out var ci) ? ci.GetString()
                              : null;

            if (string.IsNullOrEmpty(eventType) || string.IsNullOrEmpty(transactionId))
            {
                _logger.LogWarning("[WEBHOOK] Missing type or id");
                return Ok(new { status = "ignored" });
            }

            _logger.LogInformation("[WEBHOOK] {EventType} for {TransactionId}", eventType, transactionId);

            switch (eventType)
            {
                case "PAYMENT.AUTHORIZED":
                {
                    var payment = await _db.WpPayments.FirstOrDefaultAsync(p => p.WorldpayTransactionId == transactionId);
                    if (payment != null)
                    {
                        payment.Status = WpPaymentStatus.Authorized;
                        _db.WpPaymentAuditLogs.Add(AuditEntry(payment.PaymentId, WpPaymentAuditAction.Authorized, "Authorized via webhook"));
                        await _db.SaveChangesAsync();
                    }
                    break;
                }
                case "PAYMENT.CHARGED":
                {
                    var payment = await _db.WpPayments.FirstOrDefaultAsync(p => p.WorldpayTransactionId == transactionId);
                    if (payment != null)
                    {
                        payment.Status      = WpPaymentStatus.Captured;
                        payment.CompletedAt = DateTime.UtcNow;
                        _db.WpPaymentAuditLogs.Add(AuditEntry(payment.PaymentId, WpPaymentAuditAction.Captured, "Captured via webhook"));
                        await _db.SaveChangesAsync();
                    }
                    break;
                }
                case "PAYMENT.DECLINED":
                {
                    var payment = await _db.WpPayments.FirstOrDefaultAsync(p => p.WorldpayTransactionId == transactionId);
                    if (payment != null)
                    {
                        var reason = root.TryGetProperty("declineReason", out var r) ? r.GetString() : "Declined";
                        payment.Status       = WpPaymentStatus.Declined;
                        payment.ErrorMessage = reason;
                        _db.WpPaymentAuditLogs.Add(AuditEntry(payment.PaymentId, WpPaymentAuditAction.Declined, $"Declined: {reason}"));
                        await _db.SaveChangesAsync();
                    }
                    break;
                }
                case "PAYMENT.FAILED":
                {
                    var payment = await _db.WpPayments.FirstOrDefaultAsync(p => p.WorldpayTransactionId == transactionId);
                    if (payment != null)
                    {
                        payment.Status = WpPaymentStatus.Failed;
                        _db.WpPaymentAuditLogs.Add(AuditEntry(payment.PaymentId, WpPaymentAuditAction.Error, "Payment failed via webhook"));
                        await _db.SaveChangesAsync();
                    }
                    break;
                }
                case "REFUND.COMPLETED":
                {
                    var refundId = root.TryGetProperty("refundId", out var r) ? r.GetString() : null;
                    if (!string.IsNullOrEmpty(refundId))
                    {
                        var refund = await _db.WpPaymentRefunds.FirstOrDefaultAsync(r => r.WorldpayRefundId == refundId);
                        if (refund != null)
                        {
                            refund.Status      = WpRefundStatus.Completed;
                            refund.CompletedAt = DateTime.UtcNow;
                            _db.WpPaymentAuditLogs.Add(AuditEntry(refund.PaymentId, WpPaymentAuditAction.Refunded, $"Refund {refundId} completed"));
                            await _db.SaveChangesAsync();
                        }
                    }
                    break;
                }
                case "REFUND.FAILED":
                {
                    var refundId = root.TryGetProperty("refundId", out var r) ? r.GetString() : null;
                    if (!string.IsNullOrEmpty(refundId))
                    {
                        var refund = await _db.WpPaymentRefunds.FirstOrDefaultAsync(r => r.WorldpayRefundId == refundId);
                        if (refund != null)
                        {
                            refund.Status = WpRefundStatus.Failed;
                            _db.WpPaymentAuditLogs.Add(AuditEntry(refund.PaymentId, WpPaymentAuditAction.Error, $"Refund {refundId} failed"));
                            await _db.SaveChangesAsync();
                        }
                    }
                    break;
                }
                default:
                    _logger.LogInformation("[WEBHOOK] Unhandled event: {EventType}", eventType);
                    break;
            }

            return Ok(new { status = "processed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WEBHOOK] Processing error");
            return BadRequest(new { error = "Webhook processing failed" });
        }
    }

    private static WpPaymentAuditLog AuditEntry(long paymentId, WpPaymentAuditAction action, string details) =>
        new() { PaymentId = paymentId, Action = action, Details = details, Timestamp = DateTime.UtcNow };

    private static string? NormalizeEventType(string? raw) => raw?.ToUpperInvariant() switch
    {
        "PAYMENT.AUTHORIZED"    => "PAYMENT.AUTHORIZED",
        "PAYMENT.CHARGED"       => "PAYMENT.CHARGED",
        "PAYMENT.DECLINED"      => "PAYMENT.DECLINED",
        "PAYMENT.FAILED"        => "PAYMENT.FAILED",
        "REFUND.COMPLETED"      => "REFUND.COMPLETED",
        "REFUND.FAILED"         => "REFUND.FAILED",
        // Authvia confirmed event types (Craig, Apr 2026)
        "TRANSACTION.UPDATE"    => "PAYMENT.CHARGED",
        "TRANSACTION.CREATE"    => "PAYMENT.AUTHORIZED",
        // Earlier alias variants kept for safety
        "CONVERSATIONS.CREATE"  => "PAYMENT.AUTHORIZED",
        "CONVERSATIONS.UPDATE"  => "PAYMENT.CHARGED",
        "CONVERSATION.CREATED"  => "PAYMENT.AUTHORIZED",
        "CONVERSATION.UPDATED"  => "PAYMENT.CHARGED",
        _ => raw?.ToUpperInvariant()
    };
}
