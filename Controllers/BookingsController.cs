
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

    public BookingsController(BeautyDbContext db, EmailTemplateService tmpl, ITemplateRenderer renderer, IEmailSender sender)
    { _db = db; _tmpl = tmpl; _renderer = renderer; _sender = sender; }

    public record CreateReq(long CustomerId, string CustomerEmail, string CustomerName, long ArtistId, string ArtistName, long ServiceId, string ServiceName, long LocationId, string LocationName, DateTime StartsAtUtc, DateTime EndsAtUtc);
    // inside BookingsController
    
    [HttpPost("test")]
    public IActionResult PostTest() => Created("/api/bookings/test", new { ok = true });


    // ⬇️ absolute route + AllowAnonymous for the test
    [HttpPost("/api/bookings/create")]
    [AllowAnonymous] // TEMPORARY: just to isolate the 404
    public async Task<IActionResult> Create([FromBody] CreateReq req)
    {
        var b = new Booking
        {
            CustomerId = req.CustomerId,
            ArtistId = req.ArtistId,
            ServiceId = req.ServiceId,
            LocationId = req.LocationId,
            StartsAt = req.StartsAtUtc,
            EndsAt = req.EndsAtUtc,

            // If you want two‑party approval, start as Pending:
            Status = BookingStatus.Pending,
            ArtistApproved = false,
            LocationApproved = false
        };

        _db.Bookings.Add(b);
        await _db.SaveChangesAsync();
        return Created($"/api/bookings/{b.BookingId}", b);

        if (!ModelState.IsValid)
            return BadRequest(ModelState);   // <— anything after this inside the same block is unreachable

        // move the remaining logic above the return,
        // or wrap it in an else block:

        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        else
        {
            // the rest of the logic
        }


        // Render email + build ICS
        var subject = "Your Saqqara booking is confirmed";
        var model = new Dictionary<string,string> {
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
        var (fn, bytes, mime) = IcsBuilder.Build(req.CustomerEmail, subject, req.StartsAtUtc, req.EndsAtUtc, req.LocationName, $"{req.ServiceName} with {req.ArtistName} at {req.LocationName}");
        await _sender.SendHtmlWithAttachmentAsync(req.CustomerEmail, subject, html, fn, bytes, mime);


        return Created($"/api/bookings/{b.BookingId}", b);
    }
}

