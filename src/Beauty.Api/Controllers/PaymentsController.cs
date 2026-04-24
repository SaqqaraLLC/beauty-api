using Beauty.Api.Data;
using Beauty.Api.Models.Payments;
using Beauty.Api.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IWorldpayService _worldpayService;
    private readonly ILogger<PaymentsController> _logger;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BeautyDbContext _db;

    public PaymentsController(
        IWorldpayService worldpayService,
        ILogger<PaymentsController> logger,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        BeautyDbContext db)
    {
        _worldpayService   = worldpayService;
        _logger            = logger;
        _config            = config;
        _httpClientFactory = httpClientFactory;
        _db                = db;
    }

    // ============================
    // REQUEST MODELS
    // ============================

    // Raw card data collected from the frontend form.
    // PCI note: sandbox only — switch to Authvia web component before production.
    public record ChargeRequest(
        long? BookingId,
        string PayerEmail,
        string? PayerName,
        string? PayerPhone,        // E.164 format, e.g. +14075551234
        string NameOnCard,
        string CardNumber,
        int ExpirationMonth,       // 1–12, no leading zero
        int ExpirationYear,        // 4-digit
        string? StreetAddress,
        string? ZipCode,
        string? CardBrand,         // display only
        long AmountCents,
        string? Description);

    public record RefundRequest(
        string AuthviaCustomerRef);      // needed to identify the customer on Authvia's transaction endpoint

    // ============================
    // CREATE PAYMENT
    // ============================

    /// <summary>
    /// Process a payment charge via Worldpay (Admin/User)
    /// </summary>
    [HttpPost("charge")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Charge([FromBody] ChargeRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var paymentRequest = new PaymentRequest(
            BookingId:        request.BookingId,
            RecipientUserId:  User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            PayerEmail:       request.PayerEmail,
            PayerName:        request.PayerName,
            PayerPhone:       request.PayerPhone,
            NameOnCard:       request.NameOnCard,
            CardNumber:       request.CardNumber,
            ExpirationMonth:  request.ExpirationMonth,
            ExpirationYear:   request.ExpirationYear,
            StreetAddress:    request.StreetAddress,
            ZipCode:          request.ZipCode,
            CardBrand:        request.CardBrand,
            AmountCents:      request.AmountCents,
            CurrencyCode:     "USD",
            Description:      request.Description
        );

        var result = await _worldpayService.ChargeAsync(paymentRequest);

        if (!result.Success)
            return BadRequest(new { error = result.Error, code = result.ResponseCode });

        return Ok(new
        {
            paymentId = result.PaymentId,
            worldpayTransactionId = result.WorldpayTransactionId,
            status = result.Status,
            amount = request.AmountCents,
            message = "Payment processed successfully"
        });
    }

    // ============================
    // LIST PAYMENTS (Admin)
    // ============================

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ListPayments(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.Payments.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<PaymentStatus>(status, ignoreCase: true, out var s))
            query = query.Where(p => p.Status == s);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            query = query.Where(p =>
                p.PayerEmail.Contains(q) ||
                p.WorldpayTransactionId.Contains(q) ||
                (p.Description != null && p.Description.Contains(q)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.PaymentId,
                p.WorldpayTransactionId,
                p.BookingId,
                p.PayerEmail,
                p.AmountCents,
                p.CurrencyCode,
                p.Status,
                p.Description,
                p.CardLast4,
                p.CardBrand,
                p.CreatedAt,
                p.CompletedAt,
                p.ResponseCode
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    // ============================
    // MANUAL PAYMENT (Admin)
    // ============================

    public record ManualPaymentRequest(
        string PayerEmail,
        string? PayerName,
        long AmountCents,
        string? Description,
        long? BookingId,
        string PaymentMethod,   // Cash, Check, BankTransfer, Zelle, Other
        string? ReferenceNumber);

    /// <summary>
    /// Record a payment that was collected outside of Authvia (cash, check, bank transfer, Zelle, etc.)
    /// Does not call Authvia — creates a Payment row directly with status Captured.
    /// </summary>
    [HttpPost("manual")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RecordManualPayment([FromBody] ManualPaymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PayerEmail))
            return BadRequest(new { error = "PayerEmail is required" });
        if (request.AmountCents <= 0)
            return BadRequest(new { error = "AmountCents must be greater than 0" });

        var adminId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var payment = new Payment
        {
            WorldpayTransactionId = $"MANUAL-{request.PaymentMethod.ToUpperInvariant()}-{Guid.NewGuid():N}",
            BookingId             = request.BookingId,
            RecipientUserId       = adminId,
            PayerEmail            = request.PayerEmail,
            AmountCents           = request.AmountCents,
            CurrencyCode          = "USD",
            Status                = PaymentStatus.Captured,
            Description           = request.Description ?? $"{request.PaymentMethod} payment" +
                                    (request.ReferenceNumber != null ? $" · Ref: {request.ReferenceNumber}" : ""),
            CardBrand             = request.PaymentMethod,
            CreatedAt             = DateTime.UtcNow,
            CompletedAt           = DateTime.UtcNow,
            ResponseCode          = "MANUAL"
        };

        _db.Payments.Add(payment);
        _db.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            PaymentId = payment.PaymentId,
            Action    = PaymentAuditAction.Captured,
            Details   = $"Manual payment recorded by admin {adminId} · Method: {request.PaymentMethod}" +
                        (request.ReferenceNumber != null ? $" · Ref: {request.ReferenceNumber}" : ""),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("[PAYMENTS] Manual payment recorded: {PaymentId} by admin {Admin}",
            payment.PaymentId, adminId);

        return Ok(new
        {
            paymentId             = payment.PaymentId,
            worldpayTransactionId = payment.WorldpayTransactionId,
            status                = payment.Status,
            message               = "Manual payment recorded"
        });
    }

    // ============================
    // GET PAYMENT
    // ============================

    /// <summary>
    /// Get payment details by ID
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPayment(long id)
    {
        var payment = await _worldpayService.GetPaymentAsync(id);
        if (payment == null)
            return NotFound();

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
            RefundCount = payment.Refunds.Count,
            RefundedAmount = payment.Refunds.Sum(r => r.AmountCents)
        });
    }

    // ============================
    // REFUND PAYMENT
    // ============================

    /// <summary>
    /// Refund a payment (full or partial)
    /// </summary>
    [HttpPost("{id:long}/refund")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RefundPayment(long id, [FromBody] RefundRequest request)
    {
        var result = await _worldpayService.RefundAsync(id, request.AuthviaCustomerRef);

        if (!result.Success)
            return BadRequest(new { error = result.Error, code = result.ResponseCode });

        return Ok(new
        {
            refundId = result.RefundId,
            worldpayRefundId = result.WorldpayRefundId,
            amountCents = result.AmountCents,
            message = "Refund processed successfully"
        });
    }

    // ============================
    // WEBHOOK
    // ============================

    /// <summary>
    /// Authvia webhook endpoint for payment lifecycle events.
    /// Registered URL: https://api.saqqarallc.com/api/payments/webhook
    ///
    /// All events are persisted to WebhookEvents before processing (audit + replay).
    /// Duplicate event IDs are silently acknowledged (idempotent).
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> HandleWebhook()
    {
        // EnableBuffering so we can read the body for both signature verification and JSON parsing.
        Request.EnableBuffering();
        var body = await new StreamReader(Request.Body, leaveOpen: true).ReadToEndAsync();
        Request.Body.Position = 0;

        // ── 1. Signature verification ────────────────────────────────────────────
        // Authvia sends: X-AUTHVIA-VALUE, X-AUTHVIA-TIMESTAMP, X-AUTHVIA-SIGNATURE
        // Signature = Base64(HMAC-SHA256("{value}.{value.Length}.{timestamp}", secret))
        Request.Headers.TryGetValue("X-AUTHVIA-VALUE",     out var authhviaValueHeader);
        Request.Headers.TryGetValue("X-AUTHVIA-TIMESTAMP", out var authhviaTimestampHeader);
        Request.Headers.TryGetValue("X-AUTHVIA-SIGNATURE", out var authhviaSignatureHeader);

        var authhviaValue     = authhviaValueHeader.ToString();
        var authhviaTimestamp = authhviaTimestampHeader.ToString();
        var authhviaSignature = authhviaSignatureHeader.ToString();

        if (!_worldpayService.ValidateWebhookSignature(
                string.IsNullOrEmpty(authhviaValue)     ? null : authhviaValue,
                string.IsNullOrEmpty(authhviaTimestamp) ? null : authhviaTimestamp,
                string.IsNullOrEmpty(authhviaSignature) ? null : authhviaSignature))
        {
            _logger.LogWarning("[WEBHOOK] Signature validation failed");
            return Unauthorized(new { error = "Invalid signature" });
        }

        // ── 2. Parse payload ─────────────────────────────────────────────────────
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Extract raw event type (Authvia may use "eventType", "event_type", or "type")
            var rawEventType = TryGetStringProperty(root, "eventType", "event_type", "type") ?? "unknown";

            // Extract Authvia's unique event ID for idempotency
            var eventId = TryGetStringProperty(root, "eventId", "event_id", "id") ?? Guid.NewGuid().ToString();

            // Extract transaction ref from common payload shapes:
            //   { data: { id, ref } }  or  { transaction: { id } }  or  { id }
            var txRef = ExtractTransactionRef(root);

            var internalType = NormalizeEventType(rawEventType);

            _logger.LogInformation("[WEBHOOK] Received {RawType} → {InternalType} eventId={EventId} txRef={TxRef}",
                rawEventType, internalType, eventId, txRef);

            // ── 3. Idempotency — skip if already processed ───────────────────────
            var alreadyProcessed = await _db.WebhookEvents
                .AnyAsync(e => e.EventId == eventId);

            if (alreadyProcessed)
            {
                _logger.LogInformation("[WEBHOOK] Duplicate event ignored: {EventId}", eventId);
                return Ok(new { status = "duplicate", eventId });
            }

            // ── 4. Persist raw payload ───────────────────────────────────────────
            var webhookEvent = new WebhookEvent
            {
                EventId           = eventId,
                RawEventType      = rawEventType,
                InternalEventType = internalType,
                TransactionRef    = txRef,
                RawPayload        = body,
                ReceivedAt        = DateTime.UtcNow,
                Processed         = false
            };
            _db.WebhookEvents.Add(webhookEvent);
            await _db.SaveChangesAsync();

            // ── 5. Process event ─────────────────────────────────────────────────
            var notes = await DispatchWebhookEventAsync(internalType, txRef, root);

            webhookEvent.Processed        = true;
            webhookEvent.ProcessingNotes  = notes;
            await _db.SaveChangesAsync();

            return Ok(new { status = "processed", eventId, internalType });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WEBHOOK] Unhandled error processing webhook");
            // Return 200 to prevent Authvia from retrying a payload we already persisted.
            return Ok(new { status = "error", message = "Persisted but processing failed" });
        }
    }

    // ── Event dispatch ────────────────────────────────────────────────────────────

    private async Task<string> DispatchWebhookEventAsync(string internalType, string? txRef, JsonElement root)
    {
        Payment? payment = txRef != null
            ? await _db.Payments.FirstOrDefaultAsync(p => p.WorldpayTransactionId == txRef)
            : null;

        switch (internalType)
        {
            case "transaction.created":
                // Charge submitted — we already created the Payment row during ChargeAsync.
                return "acknowledged";

            case "transaction.authorized":
                if (payment != null && payment.Status == PaymentStatus.Pending)
                {
                    payment.Status = PaymentStatus.Authorized;
                    await _db.SaveChangesAsync();
                    return $"payment {payment.PaymentId} → Authorized";
                }
                return payment == null ? "payment not found" : "no status change";

            case "transaction.captured":
            case "transaction.settled":
                if (payment != null && payment.Status != PaymentStatus.Captured)
                {
                    payment.Status      = PaymentStatus.Captured;
                    payment.CompletedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                    _logger.LogInformation("[WEBHOOK] Payment {Id} marked Captured", payment.PaymentId);
                    return $"payment {payment.PaymentId} → Captured";
                }
                return payment == null ? "payment not found" : "no status change";

            case "transaction.declined":
                if (payment != null && payment.Status == PaymentStatus.Pending)
                {
                    payment.Status = PaymentStatus.Declined;
                    await _db.SaveChangesAsync();
                    return $"payment {payment.PaymentId} → Declined";
                }
                return payment == null ? "payment not found" : "no status change";

            case "transaction.failed":
                if (payment != null)
                {
                    payment.Status       = PaymentStatus.Failed;
                    payment.ErrorMessage = TryGetStringProperty(root, "reason", "message", "error");
                    await _db.SaveChangesAsync();
                    return $"payment {payment.PaymentId} → Failed";
                }
                return "payment not found";

            case "transaction.refunded":
            case "transaction.reversed":
            case "transaction.voided":
                if (payment != null)
                {
                    payment.Status = PaymentStatus.Refunded;
                    await _db.SaveChangesAsync();
                    return $"payment {payment.PaymentId} → Refunded";
                }
                return "payment not found";

            case "transaction.chargeback":
                if (payment != null)
                {
                    // Mark as failed and note the chargeback — payout should be held
                    payment.Status       = PaymentStatus.Failed;
                    payment.ErrorMessage = "Chargeback filed";
                    await _db.SaveChangesAsync();
                    _logger.LogWarning("[WEBHOOK] Chargeback on payment {Id}", payment.PaymentId);
                    return $"payment {payment.PaymentId} → chargeback flagged";
                }
                return "payment not found";

            case "transaction.updated":
                // Status sync — re-map whatever status Authvia reports
                if (payment != null)
                {
                    var rawStatus = TryGetStringProperty(root, "status",
                        "data.status", "transaction.status") ?? "";
                    _logger.LogInformation("[WEBHOOK] transaction.updated payment={Id} rawStatus={S}",
                        payment.PaymentId, rawStatus);
                    return $"payment {payment.PaymentId} status noted: {rawStatus}";
                }
                return "payment not found";

            default:
                _logger.LogWarning("[WEBHOOK] Unhandled internal event type: {Type}", internalType);
                return $"unhandled: {internalType}";
        }
    }

    // ── Translation layer — map Authvia's names to our internal model ─────────────
    // Authvia calls payment requests "conversations":
    //   conversations.create  = new charge submitted
    //   conversations.update  = status changed (captured/declined/refunded/etc.)

    private static string NormalizeEventType(string raw) => raw.ToLowerInvariant() switch
    {
        "transaction.created"     or "conversations.create"
            or "conversation.created"                                    => "transaction.created",
        "transaction.authorized"  or "payment.authorized"
            or "payment_authorized"                                      => "transaction.authorized",
        "transaction.declined"    or "payment.declined"
            or "payment_declined"                                        => "transaction.declined",
        "transaction.captured"    or "payment.captured"
            or "payment_captured" or "payment.charged"                  => "transaction.captured",
        "transaction.settled"     or "payment.settled"                  => "transaction.settled",
        "transaction.failed"      or "payment.failed"
            or "payment_failed"                                          => "transaction.failed",
        "transaction.refunded"    or "payment.refunded"
            or "refund.completed"                                        => "transaction.refunded",
        "transaction.voided"      or "payment.voided"                   => "transaction.voided",
        "transaction.reversed"    or "payment.reversed"
            or "payment.reversal"                                        => "transaction.reversed",
        "transaction.chargeback"  or "dispute.created"                  => "transaction.chargeback",
        "transaction.updated"     or "payment.updated"
            or "transactions.update" or "conversations.update"
            or "conversation.updated"                                    => "transaction.updated",
        _                                                                => raw.ToLowerInvariant()
    };

    // ── Payload helpers ───────────────────────────────────────────────────────────

    private static string? ExtractTransactionRef(JsonElement root)
    {
        // Try data.id / data.ref first (most payment platforms wrap in a data envelope)
        if (root.TryGetProperty("data", out var data))
        {
            var val = TryGetStringProperty(data, "id", "ref", "transactionId", "transaction_id");
            if (val != null) return val;
        }
        // Try transaction.id
        if (root.TryGetProperty("transaction", out var tx))
        {
            var val = TryGetStringProperty(tx, "id", "ref");
            if (val != null) return val;
        }
        // Top-level fallback
        return TryGetStringProperty(root, "transactionId", "transaction_id", "ref");
    }

    private static string? TryGetStringProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    // ============================
    // REGISTER WEBHOOK (one-time setup)
    // ============================

    /// <summary>
    /// Register Saqqara's webhook URL with Authvia (one-time admin setup).
    /// POST https://api.saqqarallc.com/api/payments/register-webhook
    ///
    /// Before calling this, set AUTHVIA_WEBHOOK_TYPE in Azure App Settings to
    /// the correct subscription type value (get it from Authvia support).
    /// On success the response includes a "webhookSecret" field — copy that
    /// value into Azure App Settings as AUTHVIA_WEBHOOK_SECRET.
    /// </summary>
    [HttpPost("register-webhook")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RegisterWebhook()
    {
        try
        {
            var clientId      = _config["AUTHVIA_CLIENT_ID"]    ?? throw new InvalidOperationException("AUTHVIA_CLIENT_ID not configured");
            var secretKey     = _config["AUTHVIA_SECRET_KEY"]   ?? throw new InvalidOperationException("AUTHVIA_SECRET_KEY not configured");
            var webhookType   = _config["AUTHVIA_WEBHOOK_TYPE"] ?? throw new InvalidOperationException(
                "AUTHVIA_WEBHOOK_TYPE not configured. Set it in Azure App Settings to the subscription type confirmed by Authvia support.");
            var isSandbox     = (_config["AUTHVIA_ENVIRONMENT"] ?? "production").Equals("sandbox", StringComparison.OrdinalIgnoreCase);
            var baseUrl       = _config["AUTHVIA_BASE_URL"]?.TrimEnd('/')
                ?? (isSandbox ? "https://sandbox.authvia.com/v3" : "https://api.authvia.com/v3");

            // --- Step 1: Get bearer token (same HMAC-SHA256 algorithm as WorldpayService) ---
            var sigValue  = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var sigInput  = $"{sigValue}.{sigValue.Length}.{timestamp}";

            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
            var signature  = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(sigInput)));

            var tokenBody = JsonSerializer.Serialize(new
            {
                client_id       = clientId,
                audience        = "api.authvia.com/v3",
                timestamp       = timestamp,
                signature       = signature,
                signature_value = sigValue,
                scope           = "subscriptions:create customers.transactions:read"
            });

            var httpClient = _httpClientFactory.CreateClient();
            var tokenResp  = await httpClient.PostAsync($"{baseUrl}/tokens",
                new StringContent(tokenBody, Encoding.UTF8, "application/json"));

            if (!tokenResp.IsSuccessStatusCode)
            {
                var err = await tokenResp.Content.ReadAsStringAsync();
                _logger.LogError("[AUTHVIA] Token request failed: {Status} — {Body}", tokenResp.StatusCode, err);
                return BadRequest(new { error = $"Token request failed: {err}" });
            }

            var tokenJson = await tokenResp.Content.ReadAsStringAsync();
            using var tDoc = JsonDocument.Parse(tokenJson);
            // Authvia returns { "type": "Bearer", "token": "eyJ..." }
            var bearerToken = tDoc.RootElement.TryGetProperty("token", out var t)
                ? t.GetString()
                : tDoc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;

            if (string.IsNullOrEmpty(bearerToken))
                return BadRequest(new { error = "Token response did not contain a bearer token", raw = tokenJson });

            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // --- Step 2: Register subscription ---
            var subBody = JsonSerializer.Serialize(new
            {
                destination = "https://api.saqqarallc.com/api/payments/webhook",
                type        = webhookType,
                disabled    = false
            });

            var subResp = await httpClient.PostAsync($"{baseUrl}/subscriptions",
                new StringContent(subBody, Encoding.UTF8, "application/json"));

            var subJson = await subResp.Content.ReadAsStringAsync();

            if (!subResp.IsSuccessStatusCode)
            {
                _logger.LogError("[AUTHVIA] Subscription registration failed: {Status} — {Body}", subResp.StatusCode, subJson);
                return BadRequest(new { error = $"Subscription registration failed ({subResp.StatusCode})", raw = subJson });
            }

            // --- Step 3: Extract the signing secret from the response ---
            // Authvia may use any of these field names for the webhook signing secret.
            using var subDoc = JsonDocument.Parse(subJson);
            var subRoot      = subDoc.RootElement;

            var webhookSecret = TryGetStringProperty(subRoot,
                "secret", "signingSecret", "signing_secret", "webhook_secret", "webhookSecret", "key");

            var subscriptionId = TryGetStringProperty(subRoot, "id", "subscriptionId", "subscription_id");

            _logger.LogInformation("[AUTHVIA] Webhook registered. id={Id} secretFound={SecretFound}",
                subscriptionId, webhookSecret != null);

            if (webhookSecret == null)
            {
                _logger.LogWarning("[AUTHVIA] No signing secret found in subscription response. Full response: {Body}", subJson);
            }

            return Ok(new
            {
                message        = "Webhook registered successfully",
                subscriptionId = subscriptionId,
                // ⚠️  Copy this value into Azure App Settings as AUTHVIA_WEBHOOK_SECRET
                webhookSecret  = webhookSecret ?? "(not returned — check raw response)",
                nextStep       = webhookSecret != null
                    ? "Copy the webhookSecret value into Azure App Settings as AUTHVIA_WEBHOOK_SECRET, then restart the app."
                    : "No secret found in Authvia's response. Check raw to find the field name and update this endpoint.",
                raw            = subJson
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("[AUTHVIA] Webhook registration error: {Message}", ex.Message);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
