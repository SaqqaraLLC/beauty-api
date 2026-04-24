using Beauty.Api.Data;
using Beauty.Api.Models.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Beauty.Api.Services.Payments;

// ============================================================
// Authvia API constants (base: https://api.authvia.com/v3)
//   Token:       POST /tokens
//   Merchant:    POST /merchants
//   Customer:    POST /customers
//   Transaction: POST /customers/{ref}/transactions
//   Webhook:     POST /subscriptions
// ============================================================

public interface IWorldpayService
{
    Task<PaymentResult> ChargeAsync(PaymentRequest request);
    Task<RefundResult> RefundAsync(long paymentId, string authviaCustomerRef);
    Task<Payment?> GetPaymentAsync(long paymentId);
    bool ValidateWebhookSignature(string payload, string signature);
}

public sealed class WorldpayService : IWorldpayService
{
    private readonly BeautyDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<WorldpayService> _logger;
    private readonly IMemoryCache _cache;

    private const string TOKEN_CACHE_KEY = "authvia_bearer_token";
    private const int TOKEN_EXPIRY_BUFFER_SECONDS = 60;

    public WorldpayService(
        BeautyDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<WorldpayService> logger,
        IMemoryCache cache)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
        _cache = cache;
    }

    // ------------------------------------------------------------------
    // Authvia token — custom HMAC-SHA256 signed request (not OAuth2)
    //
    // Endpoint: POST https://api.authvia.com/v3/tokens
    //
    // Signature algorithm (server-side only):
    //   1. Generate random signature_value (≥32 chars; 64 recommended)
    //   2. timestamp = current UTC epoch seconds
    //   3. input = "{signature_value}.{signature_value.Length}.{timestamp}"
    //   4. signature = HMAC-SHA256(input, AUTHVIA_SECRET_KEY) → lowercase hex
    // ------------------------------------------------------------------

    private string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(_config["AUTHVIA_BASE_URL"]))
            return _config["AUTHVIA_BASE_URL"]!.TrimEnd('/');

        var isSandbox = (_config["AUTHVIA_ENVIRONMENT"] ?? "production")
            .Equals("sandbox", StringComparison.OrdinalIgnoreCase);

        return isSandbox
            ? "https://sandbox.authvia.com/v3"
            : "https://api.authvia.com/v3";
    }

    private async Task<string> GetBearerTokenAsync()
    {
        if (_cache.TryGetValue(TOKEN_CACHE_KEY, out string? cached) && !string.IsNullOrEmpty(cached))
            return cached;

        var clientId  = _config["AUTHVIA_CLIENT_ID"]  ?? throw new InvalidOperationException("AUTHVIA_CLIENT_ID not configured");
        var secretKey = _config["AUTHVIA_SECRET_KEY"] ?? throw new InvalidOperationException("AUTHVIA_SECRET_KEY not configured");
        var expiryMins = int.TryParse(_config["AUTHVIA_TOKEN_EXPIRY_MINUTES"], out var m) ? m : 30;

        // 64-character random string (Authvia recommends ≥64 chars)
        var signatureValue = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var timestamp      = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sigInput       = $"{signatureValue}.{signatureValue.Length}.{timestamp}";

        // Key = UTF-8 bytes of the raw secret string; output = Base64 (confirmed working)
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var signature  = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(sigInput)));

        var body = new
        {
            client_id       = clientId,
            audience        = "api.authvia.com/v3",
            timestamp       = timestamp,
            signature       = signature,
            signature_value = signatureValue
        };

        // Use explicitly configured token URL if present; otherwise derive from base URL
        var tokenUrl = !string.IsNullOrEmpty(_config["AUTHVIA_TOKEN_URL"])
            ? _config["AUTHVIA_TOKEN_URL"]!
            : $"{GetBaseUrl()}/tokens";

        var client   = _httpClientFactory.CreateClient();
        var response = await client.PostAsync(tokenUrl, Serialize(body));

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("[AUTHVIA] Token request failed: {Status} — {Error}", response.StatusCode, err);
            throw new InvalidOperationException($"Authvia token request failed: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root      = doc.RootElement;

        // Authvia returns { "type": "Bearer", "token": "eyJ..." }
        var token = root.TryGetProperty("token", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Authvia token response did not include token field");

        var expirySec = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : expiryMins * 60;
        var cacheTtl  = TimeSpan.FromSeconds(expirySec - TOKEN_EXPIRY_BUFFER_SECONDS);
        _cache.Set(TOKEN_CACHE_KEY, token, cacheTtl);

        _logger.LogInformation("[AUTHVIA] Token acquired; cached for {Seconds}s", cacheTtl.TotalSeconds);
        return token;
    }

    private async Task<HttpClient> GetAuthorizedClientAsync()
    {
        var token  = await GetBearerTokenAsync();
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    // ------------------------------------------------------------------
    // ChargeAsync
    //
    // Endpoint: POST /customers/{ref}/transactions
    //   ref     = AuthviaCustomerRef (our customer's Authvia ID)
    //   action  = "CHARGE"
    //   amount  = cents (int32)
    //   paymentMethod.id = tokenized payment method ID (from Authvia, never raw card data)
    //
    // Response: 202 Accepted — status is "PROCESSING" (async)
    // Final result delivered via webhook event
    // ------------------------------------------------------------------

    public async Task<PaymentResult> ChargeAsync(PaymentRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.AuthviaCustomerRef))
                return new PaymentResult(false, 0, null, null, "AuthviaCustomerRef is required", null);

            if (string.IsNullOrEmpty(request.AuthviaPaymentMethodId))
                return new PaymentResult(false, 0, null, null, "AuthviaPaymentMethodId is required — card must be tokenized via Authvia before charging", null);

            var client = await GetAuthorizedClientAsync();

            var body = new
            {
                action        = "CHARGE",
                amount        = request.AmountCents,
                paymentMethod = new { id = request.AuthviaPaymentMethodId },
                references    = request.Description != null
                    ? new[] { new { label = "description", value = request.Description } }
                    : null
            };

            var url      = $"{GetBaseUrl()}/customers/{Uri.EscapeDataString(request.AuthviaCustomerRef)}/transactions";
            var response = await client.PostAsync(url, Serialize(body));

            // 202 = Accepted/Processing; 200 = immediate success
            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("[AUTHVIA] Charge failed: {Status} — {Body}", response.StatusCode, errBody);
                return new PaymentResult(false, 0, null, null, "Payment declined by processor", response.StatusCode.ToString());
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc       = JsonDocument.Parse(json);
            var root            = doc.RootElement;
            var transactionId   = root.TryGetProperty("id", out var idProp)         ? idProp.GetString()     : null;
            var rawStatus       = root.TryGetProperty("status", out var statusProp)  ? statusProp.GetString() : "PROCESSING";

            var payment = new Payment
            {
                WorldpayTransactionId = transactionId ?? Guid.NewGuid().ToString(),
                BookingId             = request.BookingId,
                RecipientUserId       = request.RecipientUserId,
                PayerEmail            = request.PayerEmail,
                AmountCents           = request.AmountCents,
                CurrencyCode          = request.CurrencyCode,
                Description           = request.Description,
                Status                = MapAuthviaStatus(rawStatus),
                CardLast4             = request.CardLast4,
                CardBrand             = request.CardBrand,
                ResponseCode          = response.StatusCode.ToString(),
                CreatedAt             = DateTime.UtcNow,
                CompletedAt           = null  // set by webhook when PROCESSING → final state
            };

            _db.Payments.Add(payment);
            _db.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                PaymentId = payment.PaymentId,
                Action    = PaymentAuditAction.Created,
                Details   = $"Charge submitted for ${request.AmountCents / 100m:F2}; status={rawStatus}",
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation("[AUTHVIA] Transaction submitted: {PaymentId} — {Status}", payment.PaymentId, rawStatus);

            return new PaymentResult(true, payment.PaymentId, transactionId, rawStatus, null, response.StatusCode.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError("[AUTHVIA] Charge error: {Message}", ex.Message);
            return new PaymentResult(false, 0, null, null, $"Payment processing failed: {ex.Message}", null);
        }
    }

    // ------------------------------------------------------------------
    // RefundAsync
    //
    // Authvia uses action = "REVERSAL" on the transactions endpoint
    // Endpoint: POST /customers/{ref}/transactions
    //   action = "REVERSAL"
    // ------------------------------------------------------------------

    public async Task<RefundResult> RefundAsync(long paymentId, string authviaCustomerRef)
    {
        try
        {
            var payment = await _db.Payments.FindAsync(paymentId);
            if (payment == null)
                return new RefundResult(false, 0, null, 0, "Payment not found", null);

            if (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.Authorized)
                return new RefundResult(false, 0, null, 0, "Payment cannot be refunded in current status", null);

            if (string.IsNullOrEmpty(authviaCustomerRef))
                return new RefundResult(false, 0, null, 0, "AuthviaCustomerRef is required for refund", null);

            var client = await GetAuthorizedClientAsync();

            var body = new
            {
                action = "REVERSAL",
                references = new[] { new { label = "originalTransactionId", value = payment.WorldpayTransactionId } }
            };

            var url      = $"{GetBaseUrl()}/customers/{Uri.EscapeDataString(authviaCustomerRef)}/transactions";
            var response = await client.PostAsync(url, Serialize(body));

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("[AUTHVIA] Reversal failed: {Status} — {Body}", response.StatusCode, errBody);
                return new RefundResult(false, 0, null, 0, "Refund declined by processor", response.StatusCode.ToString());
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var reversalId = doc.RootElement.TryGetProperty("id", out var rid) ? rid.GetString() : null;

            var refund = new PaymentRefund
            {
                PaymentId        = paymentId,
                WorldpayRefundId = reversalId ?? Guid.NewGuid().ToString(),
                AmountCents      = payment.AmountCents,
                Status           = RefundStatus.Completed,
                CreatedAt        = DateTime.UtcNow,
                CompletedAt      = DateTime.UtcNow
            };

            _db.PaymentRefunds.Add(refund);
            payment.Status = PaymentStatus.Refunded;

            _db.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                PaymentId = paymentId,
                Action    = PaymentAuditAction.Refunded,
                Details   = $"Reversal submitted for transaction {payment.WorldpayTransactionId}",
                Timestamp = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("[AUTHVIA] Reversal submitted: {RefundId}", refund.RefundId);

            return new RefundResult(true, refund.RefundId, reversalId, payment.AmountCents, null, response.StatusCode.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError("[AUTHVIA] Refund error: {Message}", ex.Message);
            return new RefundResult(false, 0, null, 0, $"Refund processing failed: {ex.Message}", null);
        }
    }

    // ------------------------------------------------------------------
    // GetPaymentAsync
    // ------------------------------------------------------------------

    public async Task<Payment?> GetPaymentAsync(long paymentId)
    {
        return await _db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId);
    }

    // ------------------------------------------------------------------
    // Webhook signature validation
    // Add AUTHVIA_WEBHOOK_SECRET to Azure env vars after registering the
    // webhook subscription at POST /subscriptions
    // ------------------------------------------------------------------

    public bool ValidateWebhookSignature(string payload, string signature)
    {
        var webhookSecret = _config["AUTHVIA_WEBHOOK_SECRET"];
        if (string.IsNullOrEmpty(webhookSecret))
        {
            _logger.LogWarning("[AUTHVIA] AUTHVIA_WEBHOOK_SECRET not configured — rejecting webhook");
            return false;
        }

        using var hmac   = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var computedHex  = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        var incoming = signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signature[7..]
            : signature;

        return computedHex.Equals(incoming.ToLowerInvariant(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static StringContent Serialize(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static PaymentStatus MapAuthviaStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "AUTHORIZED"  => PaymentStatus.Authorized,
        "CAPTURED"    => PaymentStatus.Captured,
        "CHARGED"     => PaymentStatus.Captured,
        "PROCESSING"  => PaymentStatus.Pending,
        "DECLINED"    => PaymentStatus.Declined,
        "FAILED"      => PaymentStatus.Failed,
        "REFUNDED"    => PaymentStatus.Refunded,
        "REVERSED"    => PaymentStatus.Refunded,
        "VOIDED"      => PaymentStatus.Failed,
        _             => PaymentStatus.Pending
    };
}

// ============================================================
// DTOs
// ============================================================

public record PaymentRequest(
    long? BookingId,
    string? RecipientUserId,
    string PayerEmail,
    string? PayerName,
    // Authvia tokenized identifiers — raw card data never touches our server
    string AuthviaCustomerRef,       // our customer's ref in Authvia (e.g. our user ID)
    string AuthviaPaymentMethodId,   // tokenized payment method ID from Authvia
    string? CardLast4,               // display only, from Authvia tokenization response
    string? CardBrand,               // display only
    long AmountCents,
    string CurrencyCode = "USD",
    string? Description = null);

public record PaymentResult(
    bool Success,
    long PaymentId = 0,
    string? WorldpayTransactionId = null,
    string? Status = null,
    string? Error = null,
    string? ResponseCode = null);

public record RefundResult(
    bool Success,
    long RefundId = 0,
    string? WorldpayRefundId = null,
    long AmountCents = 0,
    string? Error = null,
    string? ResponseCode = null);
