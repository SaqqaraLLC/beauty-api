using Beauty.Api.Data;
using Beauty.Api.Models.Payments;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
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

    private readonly IConfiguration _config;

    public PaymentsController(
        IWorldpayService worldpayService,
        BeautyDbContext db,
        IConfiguration config,
        ILogger<PaymentsController> logger)
    {
        _worldpayService = worldpayService;
        _db     = db;
        _config = config;
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

    // ── POST /api/payments/register-webhook ───────────────────────────────────
    // Admin: fetches a fresh Authvia token then registers the webhook subscription.
    // Avoids the 30-minute expiry problem — token is acquired and used immediately.

    [HttpPost("register-webhook")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RegisterWebhook()
    {
        try
        {
            var clientId   = _config["AUTHVIA_CLIENT_ID"]  ?? throw new InvalidOperationException("AUTHVIA_CLIENT_ID not configured");
            var secretKey  = _config["AUTHVIA_SECRET_KEY"] ?? throw new InvalidOperationException("AUTHVIA_SECRET_KEY not configured");
            var accountId  = _config["AUTHVIA_ACCOUNT_ID"] ?? "6e777b79-b8e4-4dd3-b888-2401f6a7ea64";
            var baseUrl    = (_config["AUTHVIA_BASE_URL"] ?? "https://api.authvia.com/v3").TrimEnd('/');
            var tokenUrl   = _config["AUTHVIA_TOKEN_URL"] ?? $"{baseUrl}/tokens";
            var webhookSecret = _config["AUTHVIA_WEBHOOK_SECRET"] ?? throw new InvalidOperationException("AUTHVIA_WEBHOOK_SECRET not configured");
            var destination   = $"{Request.Scheme}://{Request.Host}/api/payments/webhook";

            // Step 1 — get a fresh bearer token
            var sigValue  = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var sigInput  = $"{sigValue}.{sigValue.Length}.{timestamp}";

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
            var signature  = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(sigInput)));

            using var http = new HttpClient();

            var tokenBody = JsonSerializer.Serialize(new
            {
                client_id       = clientId,
                audience        = "api.authvia.com/v3",
                timestamp       = timestamp,
                signature       = signature,
                signature_value = sigValue
            });

            var tokenResp = await http.PostAsync(tokenUrl,
                new StringContent(tokenBody, Encoding.UTF8, "application/json"));

            if (!tokenResp.IsSuccessStatusCode)
            {
                var err = await tokenResp.Content.ReadAsStringAsync();
                _logger.LogError("[AUTHVIA] Token request failed: {Status} {Body}", tokenResp.StatusCode, err);
                return StatusCode(502, new { error = "Failed to obtain Authvia token", detail = err });
            }

            using var tokenDoc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync());
            var bearerToken = tokenDoc.RootElement.GetProperty("token").GetString()
                ?? throw new InvalidOperationException("Authvia token response missing token field");

            // Step 2 — register subscription using the fresh token
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearerToken}");
            http.DefaultRequestHeaders.Add("accept", "application/json");

            var subBody = JsonSerializer.Serialize(new
            {
                type        = "transactions.update",
                secret      = webhookSecret,
                destination = destination
            });

            var subResp = await http.PostAsync(
                $"{baseUrl}/accounts/{accountId}/subscriptions",
                new StringContent(subBody, Encoding.UTF8, "application/json"));

            var subRaw = await subResp.Content.ReadAsStringAsync();

            if (!subResp.IsSuccessStatusCode)
            {
                _logger.LogError("[AUTHVIA] Subscription registration failed: {Status} {Body}", subResp.StatusCode, subRaw);
                return StatusCode(502, new { error = "Subscription registration failed", detail = subRaw });
            }

            _logger.LogInformation("[AUTHVIA] Webhook registered: {Destination}", destination);
            return Ok(new { registered = true, destination, type = "transactions.update", response = subRaw });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AUTHVIA] register-webhook error");
            return StatusCode(500, new { error = ex.Message });
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
        // Authvia official event types (Mallikarjuna, support ticket, Apr 2026)
        "TRANSACTIONS.UPDATE"   => "PAYMENT.CHARGED",
        "TRANSACTIONS.CREATE"   => "PAYMENT.AUTHORIZED",
        // Earlier variants kept as fallback
        "TRANSACTION.UPDATE"    => "PAYMENT.CHARGED",
        "TRANSACTION.CREATE"    => "PAYMENT.AUTHORIZED",
        "CONVERSATIONS.CREATE"  => "PAYMENT.AUTHORIZED",
        "CONVERSATIONS.UPDATE"  => "PAYMENT.CHARGED",
        "CONVERSATION.CREATED"  => "PAYMENT.AUTHORIZED",
        "CONVERSATION.UPDATED"  => "PAYMENT.CHARGED",
        _ => raw?.ToUpperInvariant()
    };
}
