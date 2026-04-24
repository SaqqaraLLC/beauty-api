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
// Authvia API (https://api.authvia.com/v3)
//
// Flow for a charge:
//   1. POST /tokens                               → bearer token
//   2. POST /customers                            → create/ensure customer (ref = our userId)
//   3. POST /customers/{ref}/payment-methods      → tokenize card → paymentMethod.id
//   4. POST /customers/{ref}/transactions         → CHARGE with paymentMethod.id → 202 PROCESSING
//
// PCI note: raw card data accepted in sandbox.
// Production: switch to Authvia web component / capture experience.
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
    // Base URL
    // ------------------------------------------------------------------

    private string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(_config["AUTHVIA_BASE_URL"]))
            return _config["AUTHVIA_BASE_URL"]!.TrimEnd('/');

        var isSandbox = (_config["AUTHVIA_ENVIRONMENT"] ?? "production")
            .Equals("sandbox", StringComparison.OrdinalIgnoreCase);

        return isSandbox ? "https://sandbox.authvia.com/v3" : "https://api.authvia.com/v3";
    }

    // ------------------------------------------------------------------
    // Token — Authvia HMAC-SHA256 signed request
    // Key  : UTF-8 bytes of the raw secret string
    // Input: "{sigValue}.{sigValue.Length}.{timestamp}"
    // Output: Base64 (with padding)
    // ------------------------------------------------------------------

    private async Task<string> GetBearerTokenAsync()
    {
        if (_cache.TryGetValue(TOKEN_CACHE_KEY, out string? cached) && !string.IsNullOrEmpty(cached))
            return cached;

        var clientId   = _config["AUTHVIA_CLIENT_ID"]  ?? throw new InvalidOperationException("AUTHVIA_CLIENT_ID not configured");
        var secretKey  = _config["AUTHVIA_SECRET_KEY"] ?? throw new InvalidOperationException("AUTHVIA_SECRET_KEY not configured");
        var expiryMins = int.TryParse(_config["AUTHVIA_TOKEN_EXPIRY_MINUTES"], out var m) ? m : 30;
        var tokenUrl   = !string.IsNullOrEmpty(_config["AUTHVIA_TOKEN_URL"])
            ? _config["AUTHVIA_TOKEN_URL"]!
            : $"{GetBaseUrl()}/tokens";

        var sigValue  = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sigInput  = $"{sigValue}.{sigValue.Length}.{timestamp}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var signature  = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(sigInput)));

        var body = new
        {
            client_id       = clientId,
            audience        = "api.authvia.com/v3",
            timestamp       = timestamp,
            signature       = signature,
            signature_value = sigValue
        };

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

        // Response: { "type": "Bearer", "token": "eyJ..." }
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
    // Step 1 — Ensure customer exists in Authvia
    // POST /customers  { ref, name, addresses: [{type,value}] }
    // ref = our userId (no spaces). 409 = already exists, continue.
    // ------------------------------------------------------------------

    private async Task EnsureCustomerAsync(HttpClient client, string customerRef, string payerEmail, string? payerName, string? phone)
    {
        var addresses = new System.Collections.Generic.List<object>
        {
            new { type = "email", value = payerEmail }
        };

        if (!string.IsNullOrWhiteSpace(phone))
            addresses.Add(new { type = "mobilePhone", value = phone });

        var body = new
        {
            @ref      = customerRef,
            name      = payerName ?? payerEmail,
            addresses = addresses
        };

        var response = await client.PostAsync($"{GetBaseUrl()}/customers", Serialize(body));

        // 201 = created, 409 = already exists — both are fine
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict ||
            response.IsSuccessStatusCode)
        {
            _logger.LogInformation("[AUTHVIA] Customer ensured: {Ref}", customerRef);
            return;
        }

        var err = await response.Content.ReadAsStringAsync();
        _logger.LogError("[AUTHVIA] Customer creation failed: {Status} — {Body}", response.StatusCode, err);
        throw new InvalidOperationException($"Authvia customer creation failed: {response.StatusCode}");
    }

    // ------------------------------------------------------------------
    // Step 2 — Create payment method (tokenize card)
    // POST /customers/{ref}/payment-methods
    // Returns payment method id used in the transaction
    // ------------------------------------------------------------------

    private async Task<string> CreatePaymentMethodAsync(
        HttpClient client,
        string customerRef,
        string nameOnCard,
        string cardNumber,
        int expirationMonth,
        int expirationYear,
        string? streetAddress,
        string? zipCode)
    {
        var body = new
        {
            type            = "CreditCard",
            nameOnCard      = nameOnCard,
            cardNumber      = cardNumber,
            expirationMonth = expirationMonth,
            expirationYear  = expirationYear,
            streetAddress   = streetAddress,
            zipCode         = zipCode
        };

        var url      = $"{GetBaseUrl()}/customers/{Uri.EscapeDataString(customerRef)}/payment-methods";
        var response = await client.PostAsync(url, Serialize(body));

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("[AUTHVIA] Payment method creation failed: {Status} — {Body}", response.StatusCode, err);
            throw new InvalidOperationException($"Authvia payment method creation failed: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var pmId = doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        if (string.IsNullOrEmpty(pmId))
            throw new InvalidOperationException("Authvia payment method response did not include id");

        _logger.LogInformation("[AUTHVIA] Payment method created: {PmId}", pmId);
        return pmId;
    }

    // ------------------------------------------------------------------
    // ChargeAsync — orchestrates all three steps
    // ------------------------------------------------------------------

    public async Task<PaymentResult> ChargeAsync(PaymentRequest request)
    {
        try
        {
            var client      = await GetAuthorizedClientAsync();
            var customerRef = request.RecipientUserId ?? throw new InvalidOperationException("RecipientUserId (customerRef) is required");

            // 1. Ensure customer exists
            await EnsureCustomerAsync(client, customerRef, request.PayerEmail, request.PayerName, request.PayerPhone);

            // 2. Tokenize card → payment method ID
            var pmId = await CreatePaymentMethodAsync(
                client,
                customerRef,
                request.NameOnCard,
                request.CardNumber,
                request.ExpirationMonth,
                request.ExpirationYear,
                request.StreetAddress,
                request.ZipCode);

            // 3. Create transaction
            var txBody = new
            {
                action        = "CHARGE",
                amount        = request.AmountCents,
                paymentMethod = new { id = pmId },
                references    = request.Description != null
                    ? new[] { new { label = "description", value = request.Description } }
                    : null
            };

            var txUrl      = $"{GetBaseUrl()}/customers/{Uri.EscapeDataString(customerRef)}/transactions";
            var txResponse = await client.PostAsync(txUrl, Serialize(txBody));

            if (!txResponse.IsSuccessStatusCode)
            {
                var errBody = await txResponse.Content.ReadAsStringAsync();
                _logger.LogError("[AUTHVIA] Charge failed: {Status} — {Body}", txResponse.StatusCode, errBody);
                return new PaymentResult(false, 0, null, null, "Payment declined by processor", txResponse.StatusCode.ToString());
            }

            var txJson = await txResponse.Content.ReadAsStringAsync();
            using var txDoc     = JsonDocument.Parse(txJson);
            var txRoot          = txDoc.RootElement;
            var transactionId   = txRoot.TryGetProperty("id", out var idProp)         ? idProp.GetString()     : null;
            var rawStatus       = txRoot.TryGetProperty("status", out var statusProp)  ? statusProp.GetString() : "PROCESSING";

            var cardLast4 = request.CardNumber.Length >= 4 ? request.CardNumber[^4..] : null;

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
                CardLast4             = cardLast4,
                CardBrand             = request.CardBrand,
                ResponseCode          = txResponse.StatusCode.ToString(),
                CreatedAt             = DateTime.UtcNow,
                CompletedAt           = null
            };

            _db.Payments.Add(payment);
            _db.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                PaymentId = payment.PaymentId,
                Action    = PaymentAuditAction.Created,
                Details   = $"Charge submitted ${request.AmountCents / 100m:F2}; pmId={pmId}; status={rawStatus}",
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation("[AUTHVIA] Charge submitted: {PaymentId} txId={TxId} status={Status}",
                payment.PaymentId, transactionId, rawStatus);

            return new PaymentResult(true, payment.PaymentId, transactionId, rawStatus, null, txResponse.StatusCode.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError("[AUTHVIA] Charge error: {Message}", ex.Message);
            return new PaymentResult(false, 0, null, null, $"Payment processing failed: {ex.Message}", null);
        }
    }

    // ------------------------------------------------------------------
    // RefundAsync — REVERSAL on transaction
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
                action     = "REVERSAL",
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
            using var doc  = JsonDocument.Parse(json);
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
    // ------------------------------------------------------------------

    public bool ValidateWebhookSignature(string payload, string signature)
    {
        var webhookSecret = _config["AUTHVIA_WEBHOOK_SECRET"];
        if (string.IsNullOrEmpty(webhookSecret))
        {
            _logger.LogWarning("[AUTHVIA] AUTHVIA_WEBHOOK_SECRET not configured — rejecting webhook");
            return false;
        }

        using var hmac  = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var computedHex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

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
            PropertyNamingPolicy           = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition         = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
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
    string? RecipientUserId,   // used as Authvia customer ref (our user ID, no spaces)
    string PayerEmail,
    string? PayerName,
    string? PayerPhone,        // E.164 format e.g. +14075551234
    // Card details — server-side for sandbox; use Authvia web component in production
    string NameOnCard,
    string CardNumber,
    int ExpirationMonth,
    int ExpirationYear,
    string? StreetAddress,
    string? ZipCode,
    string? CardBrand,         // display only
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
