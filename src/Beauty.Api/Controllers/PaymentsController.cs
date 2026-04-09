using Beauty.Api.Models.Payments;
using Beauty.Api.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IWorldpayService _worldpayService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(IWorldpayService worldpayService, ILogger<PaymentsController> logger)
    {
        _worldpayService = worldpayService;
        _logger = logger;
    }

    // ============================
    // REQUEST MODELS
    // ============================

    public record ChargeRequest(
        long? BookingId,
        string PayerEmail,
        string PayerName,
        string CardNumber,
        string CardExpiry,
        string CardCvc,
        string CardholderName,
        string CardBrand,
        long AmountCents,
        string Description);

    public record RefundRequest(
        long? AmountCents);

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
            BookingId: request.BookingId,
            RecipientUserId: User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            PayerEmail: request.PayerEmail,
            PayerName: request.PayerName,
            CardNumber: request.CardNumber,
            CardExpiry: request.CardExpiry,
            CardCvc: request.CardCvc,
            CardholderName: request.CardholderName,
            CardBrand: request.CardBrand,
            AmountCents: request.AmountCents,
            CurrencyCode: "USD",
            Description: request.Description
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
        var result = await _worldpayService.RefundAsync(id, request.AmountCents);

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
    /// Worldpay webhook endpoint for payment updates
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> HandleWebhook()
    {
        try
        {
            // Read raw body for signature validation
            Request.Body.Position = 0;
            var body = await new StreamReader(Request.Body).ReadToEndAsync();

            // Get signature from headers
            if (!Request.Headers.TryGetValue("X-Worldpay-Signature", out var signatureValue))
                return Unauthorized(new { error = "Missing signature" });

            // Validate signature
            if (!_worldpayService.ValidateWebhookSignature(body, signatureValue.ToString()))
                return Unauthorized(new { error = "Invalid signature" });

            _logger.LogInformation($"[WEBHOOK] Worldpay webhook received: {body.Substring(0, Math.Min(100, body.Length))}");

            // Parse and handle webhook
            // In production, you would process different event types here
            // e.g., PAYMENT_AUTHORIZED, PAYMENT_CAPTURED, REFUND_COMPLETED etc.

            return Ok(new { status = "received" });
        }
        catch (Exception ex)
        {
            _logger.LogError($"[WEBHOOK] Error processing Worldpay webhook: {ex.Message}");
            return BadRequest(new { error = "Webhook processing failed" });
        }
    }
}
