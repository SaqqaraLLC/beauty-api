using Beauty.Api.Data;
using Beauty.Api.Domain.Approvals;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Beauty.Api.Models.ApprovalHistory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
    private readonly IBookingApprovalService _approvalService;

    public BookingsController(
        BeautyDbContext db,
        ITemplateRenderer renderer,
        IEmailSender sender,
        IBookingApprovalService approvalService)
    {
        _db = db;
        _renderer = renderer;
        _sender = sender;
        _approvalService = approvalService;
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
    // CREATE BOOKING
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
            Status = BookingStatus.Requested,
            ArtistApproval = ApprovalDecision.Pending,
            LocationApproval = ApprovalDecision.Pending
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        var html = _renderer.Render("booking_confirmation", new Dictionary<string, string>
        {
            ["CustomerName"] = req.CustomerName,
            ["ArtistName"] = req.ArtistName,
            ["ServiceName"] = req.ServiceName,
            ["LocationName"] = req.LocationName,
            ["Date"] = req.StartsAtUtc.ToLocalTime().ToString("MMM d, yyyy"),
            ["StartTime"] = req.StartsAtUtc.ToLocalTime().ToString("h:mm tt"),
            ["EndTime"] = req.EndsAtUtc.ToLocalTime().ToString("h:mm tt"),
            ["BookingLink"] = $"https://saqqarallc.net/dashboard/artist/bookings",
            ["Year"] = DateTime.UtcNow.Year.ToString()
        });

        await _sender.SendHtmlAsync(
            req.CustomerEmail,
            "Your Saqqara booking request was received",
            html);

        return Created($"/api/bookings/{booking.BookingId}", new
        {
            booking.BookingId,
            booking.Status
        });
    }

    // ============================
    // ARTIST APPROVAL
    // ============================
    [HttpPost("{id:long}/approve/artist")]
    [Authorize(Roles = "Artist,Admin")]
    public async Task<IActionResult> ApproveArtist(long id)
    {
        await _approvalService.ApproveAsync(id, ApprovalStage.Artist, User);
        return Ok();
    }

    [HttpPost("{id:long}/reject/artist")]
    [Authorize(Roles = "Artist,Admin")]
    public async Task<IActionResult> RejectArtist(long id, [FromBody] RejectReq req)
    {
        await _approvalService.RejectAsync(id, ApprovalStage.Artist, req.Reason, User);
        return Ok();
    }

    // ============================
    // LOCATION APPROVAL
    // ============================
    [HttpPost("{id:long}/approve/location")]
    [Authorize(Roles = "Location,Admin")]
    public async Task<IActionResult> ApproveLocation(long id)
    {
        await _approvalService.ApproveAsync(id, ApprovalStage.Location, User);
        return Ok();
    }

    [HttpPost("{id:long}/reject/location")]
    [Authorize(Roles = "Location,Admin")]
    public async Task<IActionResult> RejectLocation(long id, [FromBody] RejectReq req)
    {
        await _approvalService.RejectAsync(id, ApprovalStage.Location, req.Reason, User);
        return Ok();
    }

    // ========================
    // ADMIN / PENDING USERS
    // ========================

    [Authorize(
        AuthenticationSchemes = "Bearer,Identity.Application",
        Roles = "Admin"
    )]
    [HttpGet("admin/pending-users")]
    public IActionResult GetPendingUsers()
    {
        var users = _approvalService.GetPendingUsers();
        return Ok(users);
    }


    // ============================
    // DIRECTOR APPROVAL
    // ============================
    [HttpPost("{id:long}/approve/director")]
    [Authorize(Roles = "Director")]
    public async Task<IActionResult> ApproveByDirector(long id)
    {
        await _approvalService.ApproveAsync(id, ApprovalStage.Director, User);
        return Ok();
    }

    // ============================
    // APPROVAL HISTORY
    // ============================
    [HttpGet("{id:long}/approval-history")]
    [Authorize]
    public async Task<IActionResult> GetApprovalHistory(long id)
    {
        var history = await _db.BookingApprovalHistories
            .Where(h => h.BookingId == id)
            .OrderBy(h => h.ActionAt)
            .Select(h => new
            {
                h.Stage,
                h.Action,
                h.ActionAt,
                h.PerformedByEmail,
                h.PerformedByRole,
                h.Comment
            })
            .ToListAsync();

        return Ok(history);
    }

    // ============================
    // STATUS CHECK
    // ============================
    [HttpGet("{id:long}/status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStatus(long id)
    {
        var booking = await _db.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BookingId == id);

        if (booking == null)
            return NotFound();

        return Ok(new
        {
            booking.BookingId,
            booking.Status,
            booking.ArtistApproval,
            booking.LocationApproval,
            booking.ArtistApprovedAt,
            booking.LocationApprovedAt,
            booking.DirectorApprovedAt,
            booking.RejectionReason
        });
    }
}

