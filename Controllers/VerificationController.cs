using Azure.Storage.Blobs;
using Beauty.Api.Data;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Identity;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("verification")]
[Authorize]
public sealed class VerificationController : ControllerBase
{
    private readonly BeautyDbContext            _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IConfiguration             _config;
    private readonly ILogger<VerificationController> _log;

    public VerificationController(
        BeautyDbContext db,
        UserManager<ApplicationUser> users,
        IConfiguration config,
        ILogger<VerificationController> log)
    {
        _db     = db;
        _users  = users;
        _config = config;
        _log    = log;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ── GET /verification/status ──────────────────────────────────────────────
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var user = await _users.FindByIdAsync(UserId);
        if (user is null) return NotFound();

        return Ok(new
        {
            status            = user.VerificationStatus.ToString(),
            idVerified        = user.VerificationStatus >= VerificationStatus.IdVerified,
            addressUploaded   = user.AddressDocumentUploaded,
            fullyVerified     = user.VerificationStatus == VerificationStatus.Verified,
            rejectedReason    = user.VerificationRejectedReason,
            verifiedAt        = user.VerifiedAt,
        });
    }

    // ── POST /verification/start-id ───────────────────────────────────────────
    // Creates a Stripe Identity session and returns the client_secret for the SDK.
    [HttpPost("start-id")]
    public async Task<IActionResult> StartId()
    {
        var user = await _users.FindByIdAsync(UserId);
        if (user is null) return NotFound();

        if (user.VerificationStatus >= VerificationStatus.IdVerified)
            return Ok(new { alreadyVerified = true });

        var stripeClient = new Stripe.StripeClient(_config["Stripe:SecretKey"]);
        var svc     = new VerificationSessionService(stripeClient);
        var session = await svc.CreateAsync(new VerificationSessionCreateOptions
        {
            Type = "document",
            Options = new VerificationSessionOptionsOptions
            {
                Document = new VerificationSessionOptionsDocumentOptions
                {
                    RequireIdNumber           = true,
                    RequireLiveCapture        = true,
                    RequireMatchingSelfie     = true,
                    AllowedTypes = ["driving_license", "passport", "id_card"],
                },
            },
            Metadata = new Dictionary<string, string> { ["userId"] = UserId },
        });

        user.StripeVerificationSessionId = session.Id;
        user.VerificationStatus          = VerificationStatus.IdPending;
        await _users.UpdateAsync(user);

        return Ok(new { clientSecret = session.ClientSecret, sessionId = session.Id });
    }

    // ── POST /verification/upload-address ─────────────────────────────────────
    // Accepts a single document file (PDF, JPG, PNG — max 10 MB).
    [HttpPost("upload-address")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadAddress(IFormFile file)
    {
        var user = await _users.FindByIdAsync(UserId);
        if (user is null) return NotFound();

        if (user.VerificationStatus < VerificationStatus.IdVerified)
            return BadRequest(new { code = "ID_NOT_VERIFIED", message = "Complete identity verification first." });

        var allowed = new[] { "application/pdf", "image/jpeg", "image/png" };
        if (!allowed.Contains(file.ContentType))
            return BadRequest(new { code = "INVALID_FILE_TYPE", message = "Upload a PDF, JPG, or PNG." });

        var connStr    = _config["Azure:BlobConnectionString"];
        var blobClient = new BlobContainerClient(connStr, "verification-docs");
        await blobClient.CreateIfNotExistsAsync();

        var blobName = $"{UserId}/address-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{Path.GetExtension(file.FileName)}";
        var blob     = blobClient.GetBlobClient(blobName);

        await using var stream = file.OpenReadStream();
        await blob.UploadAsync(stream, overwrite: true);

        user.AddressDocumentUploaded = true;
        user.AddressDocumentUrl      = blob.Uri.ToString();
        user.VerificationStatus      = VerificationStatus.AddressPending;
        await _users.UpdateAsync(user);

        _log.LogInformation("Address doc uploaded for user {UserId}", UserId);
        return Ok(new { message = "Address document received. Under review — usually within 24 hours." });
    }

    // ── POST /verification/webhook ────────────────────────────────────────────
    // Stripe sends identity.verification_session.verified / requires_input here.
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        var payload   = await new StreamReader(Request.Body).ReadToEndAsync();
        var sigHeader = Request.Headers["Stripe-Signature"].ToString();
        var secret    = _config["Stripe:IdentityWebhookSecret"];

        Stripe.Event stripeEvent;
        try
        {
            stripeEvent = Stripe.EventUtility.ConstructEvent(payload, sigHeader, secret);
        }
        catch (Stripe.StripeException ex)
        {
            _log.LogWarning("Stripe webhook sig failed: {Msg}", ex.Message);
            return BadRequest();
        }

        if (stripeEvent.Data.Object is not VerificationSession session) return Ok();

        var userId = session.Metadata.GetValueOrDefault("userId");
        if (string.IsNullOrEmpty(userId)) return Ok();

        var user = await _users.FindByIdAsync(userId);
        if (user is null) return Ok();

        switch (stripeEvent.Type)
        {
            case "identity.verification_session.verified":
                if (user.VerificationStatus < VerificationStatus.IdVerified)
                {
                    user.VerificationStatus = user.AddressDocumentUploaded
                        ? VerificationStatus.AddressPending
                        : VerificationStatus.IdVerified;
                    await _users.UpdateAsync(user);
                    _log.LogInformation("ID verified for user {UserId}", userId);
                }
                break;

            case "identity.verification_session.requires_input":
                user.VerificationStatus         = VerificationStatus.Rejected;
                user.VerificationRejectedReason = session.LastError?.Reason ?? "Verification failed";
                await _users.UpdateAsync(user);
                _log.LogWarning("ID verification failed for user {UserId}: {Reason}", userId, user.VerificationRejectedReason);
                break;
        }

        return Ok();
    }
}
