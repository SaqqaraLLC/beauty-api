using Beauty.Api.Data;
using Beauty.Api.Models.Payments;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beauty.Api.Services;

public interface IWorldpayService
{
    Task<PaymentResult> ChargeAsync(PaymentRequest request);
    Task<RefundResult> RefundAsync(long paymentId, long? amountCents = null);
    Task<WpPayment?> GetPaymentAsync(long paymentId);
    bool ValidateWebhookSignature(string? authhviaValue, string? timestamp, string? signature);
}

public sealed class WorldpayService : IWorldpayService
{
    private readonly BeautyDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<WorldpayService> _logger;

    private const string WorldpayApiUrl = "https://api.worldpay.com/v1";

    public WorldpayService(
        BeautyDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<WorldpayService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<PaymentResult> ChargeAsync(PaymentRequest request)
    {
        try
        {
            var apiKey = _config["Worldpay:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("Worldpay API key not configured");

            var client = CreateAuthorizedClient(apiKey);

            var payload = new
            {
                transactionReference = Guid.NewGuid().ToString("N")[..32],
                merchant = new { entity = _config["Worldpay:MerchantCode"] },
                instruction = new
                {
                    narrative = new { line1 = request.Description ?? "Saqqara Service Payment" },
                    value = new { currency = request.CurrencyCode, amount = request.AmountCents },
                    paymentInstrument = new
                    {
                        cardOnFile = new
                        {
                            cardNumber      = request.CardNumber,
                            expiryDate      = request.CardExpiry,
                            cvc             = request.CardCvc,
                            cardHolderName  = request.CardholderName
                        }
                    },
                    risk = new { customerIpAddress = request.CustomerIpAddress }
                },
                customer = new { email = request.PayerEmail, name = request.PayerName }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{WorldpayApiUrl}/payments", content);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogError("[WORLDPAY] Charge failed: {Status} — {Body}", response.StatusCode, err);
                return new PaymentResult(false, 0, null, null, "Payment declined by processor", response.StatusCode.ToString());
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var transactionId  = root.GetProperty("id").GetString();
            var worldpayStatus = root.GetProperty("lastEvent").GetString();

            var payment = new WpPayment
            {
                WorldpayTransactionId = transactionId ?? Guid.NewGuid().ToString(),
                BookingId             = request.BookingId,
                RecipientUserId       = request.RecipientUserId,
                PayerEmail            = request.PayerEmail,
                AmountCents           = request.AmountCents,
                CurrencyCode          = request.CurrencyCode,
                Description           = request.Description,
                Status                = MapWorldpayStatus(worldpayStatus),
                CardLast4             = request.CardNumber?.Length >= 4
                                            ? request.CardNumber[^4..]
                                            : null,
                CardBrand             = request.CardBrand,
                ResponseCode          = response.StatusCode.ToString(),
                CreatedAt             = DateTime.UtcNow,
                CompletedAt           = worldpayStatus == "CHARGED" ? DateTime.UtcNow : null
            };

            _db.WpPayments.Add(payment);
            _db.WpPaymentAuditLogs.Add(new WpPaymentAuditLog
            {
                PaymentId = payment.PaymentId,
                Action    = WpPaymentAuditAction.Created,
                Details   = $"Payment created for ${request.AmountCents / 100m:F2}",
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation("[WORLDPAY] Payment {PaymentId} — {Status}", payment.PaymentId, payment.Status);
            return new PaymentResult(true, payment.PaymentId, transactionId, payment.Status.ToString(), null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WORLDPAY] Charge error");
            return new PaymentResult(false, 0, null, null, $"Payment processing failed: {ex.Message}", null);
        }
    }

    public async Task<RefundResult> RefundAsync(long paymentId, long? amountCents = null)
    {
        try
        {
            var payment = await _db.WpPayments.FindAsync(paymentId);
            if (payment == null)
                return new RefundResult(false, 0, null, 0, "Payment not found", null);

            if (payment.Status != WpPaymentStatus.Captured && payment.Status != WpPaymentStatus.Authorized)
                return new RefundResult(false, 0, null, 0, "Payment cannot be refunded in current status", null);

            var apiKey = _config["Worldpay:ApiKey"];
            var client = CreateAuthorizedClient(apiKey!);
            var refundAmount = amountCents ?? payment.AmountCents;

            var payload  = new { reference = $"REFUND-{Guid.NewGuid():N}"[..26] };
            var content  = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(
                $"{WorldpayApiUrl}/payments/{payment.WorldpayTransactionId}/refunds", content);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogError("[WORLDPAY] Refund failed: {Status} — {Body}", response.StatusCode, err);
                return new RefundResult(false, 0, null, 0, "Refund declined by processor", response.StatusCode.ToString());
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var refundId = doc.RootElement.GetProperty("id").GetString();

            var refund = new WpPaymentRefund
            {
                PaymentId        = paymentId,
                WorldpayRefundId = refundId ?? Guid.NewGuid().ToString(),
                AmountCents      = refundAmount,
                Status           = WpRefundStatus.Completed,
                CreatedAt        = DateTime.UtcNow,
                CompletedAt      = DateTime.UtcNow
            };

            _db.WpPaymentRefunds.Add(refund);

            if (refundAmount >= payment.AmountCents)
                payment.Status = WpPaymentStatus.Refunded;

            _db.WpPaymentAuditLogs.Add(new WpPaymentAuditLog
            {
                PaymentId = paymentId,
                Action    = WpPaymentAuditAction.Refunded,
                Details   = $"Refunded ${refundAmount / 100m:F2}",
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation("[WORLDPAY] Refund {RefundId} processed", refund.RefundId);
            return new RefundResult(true, refund.RefundId, refundId, refundAmount, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WORLDPAY] Refund error");
            return new RefundResult(false, 0, null, 0, $"Refund processing failed: {ex.Message}", null);
        }
    }

    public async Task<WpPayment?> GetPaymentAsync(long paymentId)
        => await _db.WpPayments
            .Include(p => p.Refunds)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId);

    public bool ValidateWebhookSignature(string? authhviaValue, string? timestamp, string? signature)
    {
        var secret = _config["AUTHVIA_WEBHOOK_SECRET"];

        // No secret configured — allow through (e.g. Authvia's registration ping)
        if (string.IsNullOrEmpty(secret)) return true;

        // If secret is configured but headers are absent, reject
        if (string.IsNullOrEmpty(authhviaValue) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature))
            return false;

        // Authvia signature: HMAC-SHA256("{value}.{value.Length}.{timestamp}") → Base64
        var message = $"{authhviaValue}.{authhviaValue.Length}.{timestamp}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
        return computed.Equals(signature, StringComparison.Ordinal);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private HttpClient CreateAuthorizedClient(string apiKey)
    {
        var client = _httpClientFactory.CreateClient("worldpay");
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{apiKey}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        return client;
    }

    private static WpPaymentStatus MapWorldpayStatus(string? status) => status switch
    {
        "AUTHORIZED" => WpPaymentStatus.Authorized,
        "CHARGED"    => WpPaymentStatus.Captured,
        "DECLINED"   => WpPaymentStatus.Declined,
        "FAILED"     => WpPaymentStatus.Failed,
        "REFUNDED"   => WpPaymentStatus.Refunded,
        _            => WpPaymentStatus.Pending
    };
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

public record PaymentRequest(
    long?   BookingId,
    string? RecipientUserId,
    string  PayerEmail,
    string  PayerName,
    string  CardNumber,
    string  CardExpiry,
    string  CardCvc,
    string  CardholderName,
    string? CardBrand,
    long    AmountCents,
    string  CurrencyCode           = "USD",
    string? Description            = null,
    string? CustomerIpAddress      = null);

public record PaymentResult(
    bool    Success,
    long    PaymentId              = 0,
    string? WorldpayTransactionId  = null,
    string? Status                 = null,
    string? Error                  = null,
    string? ResponseCode           = null);

public record RefundResult(
    bool    Success,
    long    RefundId               = 0,
    string? WorldpayRefundId       = null,
    long    AmountCents            = 0,
    string? Error                  = null,
    string? ResponseCode           = null);
