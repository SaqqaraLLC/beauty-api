using Beauty.Api.Data;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly EmailTemplateService _tmpl;
    private readonly ITemplateRenderer _renderer;
    private readonly IEmailSender _sender;

    public BookingsController(
        BeautyDbContext db,
        EmailTemplateService tmpl,
        ITemplateRenderer renderer,
        IEmailSender sender)
    {
        _db = db; _tmpl = tmpl; _renderer = renderer; _sender = sender;
    }

    public record CreateReq(
        long CustomerId, string CustomerEmail, string CustomerName,
        long ArtistId, string ArtistName,
        long ServiceId, string ServiceName,
        long LocationId, string LocationName,
        DateTime StartsAtUtc, DateTime EndsAtUtc);

    [HttpPost("test")]
    public IActionResult PostTest() => Created("/api/bookings/test", new { ok = true });

    [HttpPost("create")]                // relative route → /api/bookings/create
    [AllowAnonymous]                    // remove when you secure it
    public async Task<IActionResult> Create([FromBody] CreateReq req)
    {
        // With [ApiController], this check is optional (automatic 400 on invalid model state).
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var b = new Booking
        {
            CustomerId = req.CustomerId,
            ArtistId = req.ArtistId,
            ServiceId = req.ServiceId,
            LocationId = req.LocationId,
            StartsAt = req.StartsAtUtc,
            EndsAt = req.EndsAtUtc,

            // Two‑party approval defaults
            Status = BookingStatus.Pending,
            ArtistApproved = false,
            LocationApproved = false
        };

        _db.Bookings.Add(b);
        await _db.SaveChangesAsync();

        // Compose and send the email (and ICS) *before* returning
        var subject = "Your Saqqara booking is confirmed";
        var model = new Dictionary<string, string>
        {
            ["CustomerName"] = req.CustomerName,
            ["ArtistName"] = req.ArtistName,
            ["ServiceName"] = req.ServiceName,
            ["Date"] = req.StartsAtUtc.ToLocalTime().ToString("MMM d, yyyy"),
            ["StartTime"] = req.StartsAtUtc.ToLocalTime().ToString("h:mm tt"),
            ["EndTime"] = req.EndsAtUtc.ToLocalTime().ToString("h:mm tt"),
            ["LocationName"] = req.LocationName,
            ["BookingLink"] = $"https://app.saqqarallc.com/bookings/{b.BookingId}",
            ["Year"] = DateTime.UtcNow.Year.ToString()
        };

        var html = _renderer.Render("booking_confirmation", model);

        var (fn, bytes, mime) = IcsBuilder.Build(
            req.CustomerEmail,
            subject,
            req.StartsAtUtc,
            req.EndsAtUtc,
            req.LocationName,
            $"{req.ServiceName} with {req.ArtistName} at {req.LocationName}");

        await _sender.SendHtmlWithAttachmentAsync(req.CustomerEmail, subject, html, fn, bytes, mime);

        // Single, final return
        return Created($"/api/bookings/{b.BookingId}", b);
    }
}

