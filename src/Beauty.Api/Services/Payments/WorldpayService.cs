using Beauty.Api.Data;
using Beauty.Api.Models.Payments;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Beauty.Api.Services.Payments;

/// <summary>
/// Worldpay payment processing service.
/// Handles authorization, capture, and refunds.
/// </summary>
public interface IWorldpayService
{
    /// <summary>Create and process a payment</summary>
    Task<PaymentResult> ChargeAsync(PaymentRequest request);

    /// <summary>Refund a completed payment</summary>
    Task<RefundResult> RefundAsync(long paymentId, long? amountCents = null);

    /// <summary>Check payment status</summary>
    Task<Payment?> GetPaymentAsync(long paymentId);

    /// <summary>Validate Worldpay webhook signature</summary>
    bool ValidateWebhookSignature(string payload, string signature);
}

public sealed class WorldpayService : IWorldpayService
{
    private readonly BeautyDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<WorldpayService> _logger;

    private const string WORLDPAY_API_URL = "https://api.worldpay.com/v1";

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
            var client = _httpClientFactory.CreateClient();
            var apiKey = _config["Worldpay:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("Worldpay API key not configured");

            // Build request for Worldpay
            var payload = new
            {
                transactionReference = Guid.NewGuid().ToString().Substring(0, 32),
                merchant = new
                {
                    entity = _config["Worldpay:MerchantCode"]
                },
                instruction = new
                {
                    narrative = new
                    {
                        line1 = request.Description ?? "Saqqara Service Payment"
                    },
                    value = new
                    {
                        currency = request.CurrencyCode,
                        amount = request.AmountCents
                    },
                    paymentInstrument = new
                    {
                        cardOnFile = new
                        {
                            cardNumber = request.CardNumber,
                            expiryDate = request.CardExpiry,
                            cvc = request.CardCvc,
                            cardHolderName = request.CardholderName
                        }
                    },
                    risk = new
                    {
                        customerIpAddress = request.CustomerIpAddress
                    }
                },
                customer = new
                {
                    email = request.PayerEmail,
                    name = request.PayerName
                }
            };

            var payloadJson = JsonSerializer.Serialize(payload);
            var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            // Add Basic Auth header
            var authHeader = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{apiKey}"));
            client.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);

            var response = await client.PostAsync($"{WORLDPAY_API_URL}/payments", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"[WORLDPAY] Payment failed: {response.StatusCode} - {errorContent}");
                
                return new PaymentResult(
                    false,
                    0,
                    null,
                    null,
                    "Payment declined by processor",
                    response.StatusCode.ToString()
                );
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            var transactionId = root.GetProperty("id").GetString();
            var paymentStatus = root.GetProperty("lastEvent").GetString();

            // Create payment record
            var payment = new Payment
            {
                WorldpayTransactionId = transactionId ?? Guid.NewGuid().ToString(),
                BookingId = request.BookingId,
                RecipientUserId = request.RecipientUserId,
                PayerEmail = request.PayerEmail,
                AmountCents = request.AmountCents,
                CurrencyCode = request.CurrencyCode,
                Description = request.Description,
                Status = MapWorldpayStatus(paymentStatus),
                CardLast4 = request.CardNumber?.Substring(Math.Max(0, request.CardNumber.Length - 4)),
                CardBrand = request.CardBrand,
                ResponseCode = response.StatusCode.ToString(),
                CreatedAt = DateTime.UtcNow,
                CompletedAt = paymentStatus == "CHARGED" ? DateTime.UtcNow : null
            };

            _db.Payments.Add(payment);

            // Log audit
            _db.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                PaymentId = payment.PaymentId,
                Action = PaymentAuditAction.Created,
                Details = $"Payment created for ${request.AmountCents / 100m:F2}",
                Timestamp = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            _logger.LogInformation($"[WORLDPAY] Payment processed: {payment.PaymentId} - {payment.Status}");

            return new PaymentResult(
                true,
                payment.PaymentId,
                transactionId,
                payment.Status.ToString(),
                null,
                null
            );
        }
        catch (Exception ex)
        {
            _logger.LogError($"[WORLDPAY] Payment error: {ex.Message}");
            return new PaymentResult(
                false,
                0,
                null,
                null,
                $"Payment processing failed: {ex.Message}",
                null
            );
        }
    }

    public async Task<RefundResult> RefundAsync(long paymentId, long? amountCents = null)
    {
        try
        {
            var payment = await _db.Payments.FindAsync(paymentId);
            if (payment == null)
                return new RefundResult(false, 0, null, 0, "Payment not found", null);

            if (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.Authorized)
                return new RefundResult(false, 0, null, 0, "Payment cannot be refunded in current status", null);

            var client = _httpClientFactory.CreateClient();
            var apiKey = _config["Worldpay:ApiKey"];

            var refundAmount = amountCents ?? payment.AmountCents;

            var payload = new
            {
                reference = $"REFUND-{Guid.NewGuid().ToString().Substring(0, 20)}"
            };

            var payloadJson = JsonSerializer.Serialize(payload);
            var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            var authHeader = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{apiKey}"));
            client.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);

            var response = await client.PostAsync(
                $"{WORLDPAY_API_URL}/payments/{payment.WorldpayTransactionId}/refunds",
                content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"[WORLDPAY] Refund failed: {response.StatusCode} - {errorContent}");
                return new RefundResult(
                    false,
                    0,
                    null,
                    0,
                    "Refund declined by processor",
                    response.StatusCode.ToString()
                );
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            var refundId = root.GetProperty("id").GetString();

            // Record refund
            var refund = new PaymentRefund
            {
                PaymentId = paymentId,
                WorldpayRefundId = refundId ?? Guid.NewGuid().ToString(),
                AmountCents = refundAmount,
                Status = RefundStatus.Completed,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };

            _db.PaymentRefunds.Add(refund);

            // Update payment status if full refund
            if (refundAmount >= payment.AmountCents)
            {
                payment.Status = PaymentStatus.Refunded;
            }

            _db.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                PaymentId = paymentId,
                Action = PaymentAuditAction.Refunded,
                Details = $"Refunded ${refundAmount / 100m:F2}",
                Timestamp = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            _logger.LogInformation($"[WORLDPAY] Refund processed: {refund.RefundId}");

            return new RefundResult(
                true,
                refund.RefundId,
                refundId,
                refundAmount,
                null,
                null
            );
        }
        catch (Exception ex)
        {
            _logger.LogError($"[WORLDPAY] Refund error: {ex.Message}");
            return new RefundResult(
                false,
                0,
                null,
                0,
                $"Refund processing failed: {ex.Message}",
                null
            );
        }
    }

    public async Task<Payment?> GetPaymentAsync(long paymentId)
    {
        return await _db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId);
    }

    public bool ValidateWebhookSignature(string payload, string signature)
    {
        var webhookSecret = _config["Worldpay:WebhookSecret"];
        if (string.IsNullOrEmpty(webhookSecret))
            return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var computedSignature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));

        return computedSignature.Equals(signature, StringComparison.Ordinal);
    }

    private PaymentStatus MapWorldpayStatus(string? worldpayStatus)
    {
        return worldpayStatus switch
        {
            "AUTHORIZED" => PaymentStatus.Authorized,
            "CHARGED" => PaymentStatus.Captured,
            "DECLINED" => PaymentStatus.Declined,
            "FAILED" => PaymentStatus.Failed,
            "REFUNDED" => PaymentStatus.Refunded,
            _ => PaymentStatus.Pending
        };
    }
}

// ============================================
// DTOs
// ============================================

public record PaymentRequest(
    long? BookingId,
    string? RecipientUserId,
    string PayerEmail,
    string PayerName,
    string CardNumber,
    string CardExpiry, // MM/YY
    string CardCvc,
    string CardholderName,
    string? CardBrand,
    long AmountCents,
    string CurrencyCode = "USD",
    string? Description = null,
    string? CustomerIpAddress = null);

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
