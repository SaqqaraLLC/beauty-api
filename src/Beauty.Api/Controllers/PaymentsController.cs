using Beauty.Api.Models.Payments;
using Beauty.Api.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public PaymentsController(
        IWorldpayService worldpayService,
        ILogger<PaymentsController> logger,
        IConfiguration config,
        IHttpClientFactory httpClientFactory)
    {
        _worldpayService   = worldpayService;
        _logger            = logger;
        _config            = config;
        _httpClientFactory = httpClientFactory;
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
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> HandleWebhook()
    {
        try
        {
            // EnableBuffering allows us to read the body twice (once for signature, once for JSON)
            Request.EnableBuffering();
            var body = await new StreamReader(Request.Body, leaveOpen: true).ReadToEndAsync();
            Request.Body.Position = 0;

            // Authvia sends the signature in X-Authvia-Signature; fall back to X-Worldpay-Signature
            // until the exact header name is confirmed in Authvia developer docs
            var signatureHeader = Request.Headers.TryGetValue("X-Authvia-Signature", out var authviaSig)
                ? authviaSig.ToString()
                : Request.Headers.TryGetValue("X-Worldpay-Signature", out var wpSig)
                    ? wpSig.ToString()
                    : null;

            if (string.IsNullOrEmpty(signatureHeader))
            {
                _logger.LogWarning("[WEBHOOK] Incoming request missing signature header");
                return Unauthorized(new { error = "Missing signature" });
            }

            if (!_worldpayService.ValidateWebhookSignature(body, signatureHeader))
            {
                _logger.LogWarning("[WEBHOOK] Signature validation failed");
                return Unauthorized(new { error = "Invalid signature" });
            }

            using var doc  = System.Text.Json.JsonDocument.Parse(body);
            var root       = doc.RootElement;
            var eventType  = root.TryGetProperty("eventType", out var et) ? et.GetString() : "UNKNOWN";

            _logger.LogInformation("[WEBHOOK] Authvia event received: {EventType}", eventType);

            switch (eventType?.ToUpperInvariant())
            {
                case "PAYMENT.AUTHORIZED":
                case "PAYMENT_AUTHORIZED":
                    // Payment authorized — update status in DB when ready
                    break;

                case "PAYMENT.CAPTURED":
                case "PAYMENT_CAPTURED":
                case "PAYMENT.CHARGED":
                    // Payment captured — mark booking as paid
                    break;

                case "PAYMENT.FAILED":
                case "PAYMENT_FAILED":
                case "PAYMENT.DECLINED":
                    // Payment failed — notify booking system
                    break;

                case "PAYMENT.REFUNDED":
                case "REFUND.COMPLETED":
                    // Refund confirmed — update refund record
                    break;

                case "DISPUTE.CREATED":
                    // Chargeback opened — flag payment, hold payout
                    break;

                case "DISPUTE.RESOLVED":
                    // Chargeback resolved — release or void payout accordingly
                    break;

                case "PAYOUT.PROCESSED":
                    // Artist payout sent
                    break;

                case "PAYOUT.FAILED":
                    // Artist payout failed — alert
                    break;

                default:
                    _logger.LogWarning("[WEBHOOK] Unhandled event type: {EventType}", eventType);
                    break;
            }

            // Always return 200 — Authvia will retry on non-2xx
            return Ok(new { status = "received", eventType });
        }
        catch (Exception ex)
        {
            _logger.LogError("[WEBHOOK] Error processing webhook: {Message}", ex.Message);
            return BadRequest(new { error = "Webhook processing failed" });
        }
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

    private static string? TryGetStringProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }
}
