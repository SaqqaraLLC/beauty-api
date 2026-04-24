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

    // Card data is never sent to our server.
    // The frontend tokenizes the card via Authvia and sends back the resulting IDs.
    public record ChargeRequest(
        long? BookingId,
        string PayerEmail,
        string AuthviaCustomerRef,       // customer's ref in Authvia (our user ID used at customer creation)
        string AuthviaPaymentMethodId,   // tokenized payment method ID from Authvia
        string? CardLast4,               // display only
        string? CardBrand,               // display only
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
            BookingId:               request.BookingId,
            RecipientUserId:         User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            PayerEmail:              request.PayerEmail,
            PayerName:               null,
            AuthviaCustomerRef:      request.AuthviaCustomerRef,
            AuthviaPaymentMethodId:  request.AuthviaPaymentMethodId,
            CardLast4:               request.CardLast4,
            CardBrand:               request.CardBrand,
            AmountCents:             request.AmountCents,
            CurrencyCode:            "USD",
            Description:             request.Description
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
    /// Register Saqqara's webhook URL with Authvia.
    /// Call this once from Postman after deploying.
    /// POST https://api.saqqarallc.com/api/payments/register-webhook
    /// Scope required on token: subscriptions:create + {resource}:read
    /// </summary>
    [HttpPost("register-webhook")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RegisterWebhook()
    {
        try
        {
            // Re-use the service token via WorldpayService internals is not accessible here,
            // so we call the token endpoint directly in this one-off admin action.
            var clientId  = _config["AUTHVIA_CLIENT_ID"]  ?? throw new InvalidOperationException("AUTHVIA_CLIENT_ID not configured");
            var secretKey = _config["AUTHVIA_SECRET_KEY"] ?? throw new InvalidOperationException("AUTHVIA_SECRET_KEY not configured");
            var isSandbox = (_config["AUTHVIA_ENVIRONMENT"] ?? "production").Equals("sandbox", StringComparison.OrdinalIgnoreCase);
            var baseUrl   = _config["AUTHVIA_BASE_URL"]?.TrimEnd('/')
                ?? (isSandbox ? "https://sandbox.authvia.com/v3" : "https://api.authvia.com/v3");

            var signatureValue = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var timestamp      = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var sigInput       = $"{signatureValue}.{signatureValue.Length}.{timestamp}";

            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
            var signature  = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(sigInput)));

            var tokenBody = JsonSerializer.Serialize(new
            {
                client_id       = clientId,
                audience        = "api.authvia.com/v3",
                timestamp       = timestamp,
                signature       = signature,
                signature_value = signatureValue,
                scope           = "subscriptions:create customers.transactions:read"
            });

            var httpClient   = _httpClientFactory.CreateClient();
            var tokenResp    = await httpClient.PostAsync($"{baseUrl}/tokens",
                new StringContent(tokenBody, Encoding.UTF8, "application/json"));

            if (!tokenResp.IsSuccessStatusCode)
            {
                var err = await tokenResp.Content.ReadAsStringAsync();
                return BadRequest(new { error = $"Token request failed: {err}" });
            }

            var tokenJson   = await tokenResp.Content.ReadAsStringAsync();
            using var tDoc  = JsonDocument.Parse(tokenJson);
            var bearerToken = tDoc.RootElement.GetProperty("access_token").GetString();

            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var subscriptionBody = JsonSerializer.Serialize(new
            {
                destination = "https://api.saqqarallc.com/api/payments/webhook",
                type        = "customers.transactions:update",
                disabled    = false
            });

            var subResp = await httpClient.PostAsync($"{baseUrl}/subscriptions",
                new StringContent(subscriptionBody, Encoding.UTF8, "application/json"));

            var subJson = await subResp.Content.ReadAsStringAsync();

            if (!subResp.IsSuccessStatusCode)
                return BadRequest(new { error = $"Subscription registration failed: {subJson}" });

            _logger.LogInformation("[AUTHVIA] Webhook subscription registered: {Response}", subJson);
            return Ok(new { message = "Webhook registered successfully", response = subJson });
        }
        catch (Exception ex)
        {
            _logger.LogError("[AUTHVIA] Webhook registration error: {Message}", ex.Message);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
