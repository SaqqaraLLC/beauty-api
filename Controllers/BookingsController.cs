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
    private readonly ITemplateRenderer _renderer;
    private readonly IEmailSender _sender;

    public BookingsController(
        BeautyDbContext db,
        ITemplateRenderer renderer,
        IEmailSender sender)
    {
        _db = db;
        _renderer = renderer;
        _sender = sender;
    }

    // ============================
    // REQUEST MODELS
    // ============================

    public record CreateReq(
        long CustomerId, string CustomerEmail, string CustomerName,
        long ArtistId, string ArtistName,
        long ServiceId, string ServiceName,
        long LocationId, string LocationName,
        DateTime StartsAtUtc, DateTime EndsAtUtc);

    public record RejectReq(string Reason);

    // ============================
    // CREATE BOOKING (PUBLIC)
    // ============================
    [HttpPost("create")]
    [AllowAnonymous]
    public async Task<IActionResult> Create([FromBody] CreateReq req)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var booking = new Booking
        {
            CustomerId = req.CustomerId,
            ArtistId = req.ArtistId,
            ServiceId = req.ServiceId,
            LocationId = req.LocationId,
            StartsAt = req.StartsAtUtc,
            EndsAt = req.EndsAtUtc,
            Status = BookingStatus.PendingApprovals,
            ArtistApproval = ApprovalDecision.Pending,
            LocationApproval = ApprovalDecision.Pending
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        // Render confirmation email
        var model = new Dictionary<string, string>
        {
            ["CustomerName"] = req.CustomerName,
            ["ArtistName"] = req.ArtistName,
            ["ServiceName"] = req.ServiceName,
            ["Date"] = req.StartsAtUtc.ToLocalTime().ToString("MMM d, yyyy"),
            ["StartTime"] = req.StartsAtUtc.ToLocalTime().ToString("h:mm tt"),
            ["EndTime"] = req.EndsAtUtc.ToLocalTime().ToString("h:mm tt"),
            ["LocationName"] = req.LocationName,
            ["BookingLink"] = $"https://app.saqqarallc.com/bookings/{booking.BookingId}",
            ["Year"] = DateTime.UtcNow.Year.ToString()
        };

        var html = _renderer.Render("booking_confirmation", model);

        await _sender.SendHtmlAsync(
            req.CustomerEmail,
            "Your Saqqara booking request was received",
            html
        );

        return Created($"/api/bookings/{booking.BookingId}", booking);
    }

    // ============================
    // ARTIST APPROVAL
    // ============================
    [HttpPost("{id:long}/approve/artist")]
    [Authorize(Roles = "Artist")]
    public async Task<IActionResult> ApproveArtist(long id)
    {
        var booking = await _db.Bookings.FindAsync(id);
        if (booking == null)
            return NotFound();

        if (booking.ArtistApproval != ApprovalDecision.Pending)
            return BadRequest("Artist has already acted on this booking.");

        booking.ArtistApproval = ApprovalDecision.Approved;
        booking.ArtistApprovedAt = DateTime.UtcNow;
        booking.ArtistApprovedByUserId = User.Identity?.Name;

        booking.RecalculateStatus();
        await _db.SaveChangesAsync();

        return Ok(new
        {
            booking.BookingId,
            booking.Status,
            booking.ArtistApproval,
            booking.CanCustomerCompleteApplication
        });
    }

    [HttpPost("{id:long}/reject/artist")]
    [Authorize(Roles = "Artist")]
    public async Task<IActionResult> RejectArtist(long id, [FromBody] RejectReq req)
    {
        var booking = await _db.Bookings.FindAsync(id);
        if (booking == null)
            return NotFound();

        booking.ArtistApproval = ApprovalDecision.Rejected;
        booking.RejectionReason = req.Reason;
        booking.RecalculateStatus();

        await _db.SaveChangesAsync();
        return Ok();
    }

    // ============================
    // LOCATION APPROVAL
    // ============================
    [HttpPost("{id:long}/approve/location")]
    [Authorize(Roles = "Location")]
    public async Task<IActionResult> ApproveLocation(long id)
    {
        var booking = await _db.Bookings.FindAsync(id);
        if (booking == null)
            return NotFound();

        if (booking.LocationApproval != ApprovalDecision.Pending)
            return BadRequest("Location has already acted on this booking.");

        booking.LocationApproval = ApprovalDecision.Approved;
        booking.LocationApprovedAt = DateTime.UtcNow;
        booking.LocationApprovedByUserId = User.Identity?.Name;

        booking.RecalculateStatus();
        await _db.SaveChangesAsync();

        return Ok(new
        {
            booking.BookingId,
            booking.Status,
            booking.LocationApproval,
            booking.CanCustomerCompleteApplication
        });
    }

    [HttpPost("{id:long}/reject/location")]
    [Authorize(Roles = "Location")]
    public async Task<IActionResult> RejectLocation(long id, [FromBody] RejectReq req)
    {
        var booking = await _db.Bookings.FindAsync(id);
        if (booking == null)
            return NotFound();

        booking.LocationApproval = ApprovalDecision.Rejected;
        booking.RejectionReason = req.Reason;
        booking.RecalculateStatus();

        await _db.SaveChangesAsync();
        return Ok();
    }

    // ============================
    // TEMPLATE PREVIEW (DEV ONLY)
    // ============================
    [HttpGet("templates/{name}")]
    [Authorize(Roles = "Admin")]
    public IActionResult PreviewTemplate(string name)
    {
        var html = _renderer.Render(name, new Dictionary<string, string>
        {
            ["CustomerName"] = "Preview User",
            ["ArtistName"] = "Preview Artist",
            ["ServiceName"] = "Preview Service",
            ["Date"] = DateTime.Today.ToShortDateString(),
            ["StartTime"] = "10:00 AM",
            ["EndTime"] = "11:00 AM",
            ["LocationName"] = "Preview Location",
            ["BookingLink"] = "#",
            ["Year"] = DateTime.UtcNow.Year.ToString()
        });

        return Content(html, "text/html");
    }

    // ============================
    // STATUS CHECK (PUBLIC)
    // ============================
    [HttpGet("{id:long}/status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStatus(long id)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(x => x.BookingId == id);
        if (booking == null)
            return NotFound();

        return Ok(new
        {
            booking.BookingId,
            booking.Status,
            booking.ArtistApproval,
            booking.LocationApproval,
            booking.CanCustomerCompleteApplication,
            booking.ArtistApprovedAt,
            booking.LocationApprovedAt,
            booking.RejectionReason
        });
    }
}

