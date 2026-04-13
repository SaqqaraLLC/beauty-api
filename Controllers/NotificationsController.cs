using Beauty.Api.Data;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using Beauty.Api.Models.Enterprise;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public NotificationsController(BeautyDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    // ── GET /api/notifications ──────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var notifications = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync();

        return Ok(notifications.Select(n => new
        {
            notificationId = n.NotificationId.ToString(),
            eventType = n.EventType,
            title = n.Title,
            body = n.Body,
            entityType = n.EntityType,
            entityId = n.EntityId,
            actionUrl = n.ActionUrl,
            createdAt = n.CreatedAt,
            isRead = n.IsRead
        }));
    }

    // ── POST /api/notifications/mark-read ───────────────────────────

    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        return Ok(new { message = "All notifications marked as read." });
    }

    // ── POST /api/notifications/{id}/read ───────────────────────────

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkOneRead(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == id && n.UserId == userId);

        if (notification is null) return NotFound();

        notification.IsRead = true;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Notification marked as read." });
    }

    // ── GET /api/notifications/reminders ───────────────────────────
    // Reminders: 24h ahead, 5-min check-in window, post-service completion + rating

    [HttpGet("reminders")]
    public async Task<IActionResult> GetReminders()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var now      = DateTime.UtcNow;
        var in24h    = now.AddHours(24);
        var reminders = new List<object>();

        // ── Regular bookings ──────────────────────────────────────
        var bookings = await _db.Bookings
            .AsNoTracking()
            .Where(b =>
                b.ArtistUserId.ToString() == userId &&
                b.StartsAt <= in24h &&
                b.EndsAt   >= now.AddHours(-2) &&
                b.Status   == BookingStatus.FullyApproved)
            .OrderBy(b => b.StartsAt)
            .ToListAsync();

        foreach (var b in bookings)
        {
            var minsUntil       = (b.StartsAt - now).TotalMinutes;
            var hasEnded        = now > b.EndsAt;
            var isCheckinWindow = minsUntil >= -5 && minsUntil <= 5;

            if (hasEnded && !b.ServiceCompleted)
            {
                reminders.Add(new { reminderId = $"complete-{b.BookingId}", type = "complete_service",
                    title = "Mark service complete",
                    body  = "Your booking time has ended. Please confirm the service was completed.",
                    bookingId = b.BookingId, startsAt = b.StartsAt, urgent = true,
                    actionUrl = $"/dashboard/artist/bookings/{b.BookingId}" });
            }
            else if (isCheckinWindow && !b.ArtistCheckedIn)
            {
                reminders.Add(new { reminderId = $"checkin-{b.BookingId}", type = "checkin_due",
                    title = "Check in now",
                    body  = "Your booking starts in 5 minutes. Tap to check in.",
                    bookingId = b.BookingId, startsAt = b.StartsAt, urgent = true,
                    actionUrl = $"/dashboard/artist/bookings/{b.BookingId}" });
            }
            else if (minsUntil is > 5 and <= 1440)
            {
                var h = (int)(minsUntil / 60);
                reminders.Add(new { reminderId = $"upcoming-{b.BookingId}", type = "upcoming_booking",
                    title = "Upcoming booking",
                    body  = h < 1 ? $"Your booking starts in {(int)minsUntil} minutes."
                                  : $"You have a booking in {h} hours.",
                    bookingId = b.BookingId, startsAt = b.StartsAt, urgent = false,
                    actionUrl = $"/dashboard/artist/bookings/{b.BookingId}" });
            }
        }

        // ── Company booking slots ─────────────────────────────────
        var slots = await _db.CompanyBookingArtistSlots
            .AsNoTracking()
            .Include(s => s.CompanyBooking)
            .Where(s =>
                s.ArtistUserId == userId &&
                s.CompanyBooking!.EventDate <= in24h &&
                s.CompanyBooking!.EventDate >= now.AddHours(-2) &&
                s.Status == Beauty.Api.Models.Company.SlotStatus.Accepted)
            .OrderBy(s => s.CompanyBooking!.EventDate)
            .ToListAsync();

        foreach (var s in slots)
        {
            var eventDate       = s.CompanyBooking!.EventDate;
            var minsUntil       = (eventDate - now).TotalMinutes;
            var hasEnded        = s.CompanyBooking.EventEndDate.HasValue
                ? now > s.CompanyBooking.EventEndDate
                : now > eventDate.AddHours(3);
            var isCheckinWindow = minsUntil >= -5 && minsUntil <= 5;

            if (hasEnded && !s.ArtistCheckedIn)
            {
                reminders.Add(new { reminderId = $"complete-slot-{s.Id}", type = "complete_service",
                    title = "Mark service complete",
                    body  = $"{s.CompanyBooking.Title} has ended. Please confirm completion.",
                    bookingId = s.CompanyBooking.Id, slotId = s.Id, startsAt = eventDate, urgent = true,
                    actionUrl = "/dashboard/artist/company-requests" });
            }
            else if (isCheckinWindow && !s.ArtistCheckedIn)
            {
                reminders.Add(new { reminderId = $"checkin-slot-{s.Id}", type = "checkin_due",
                    title = "Check in now",
                    body  = $"{s.CompanyBooking.Title} starts in 5 minutes. Tap to check in.",
                    bookingId = s.CompanyBooking.Id, slotId = s.Id, startsAt = eventDate, urgent = true,
                    actionUrl = "/dashboard/artist/company-requests" });
            }
            else if (minsUntil is > 5 and <= 1440)
            {
                var h = (int)(minsUntil / 60);
                reminders.Add(new { reminderId = $"upcoming-slot-{s.Id}", type = "upcoming_booking",
                    title = "Upcoming event",
                    body  = h < 1 ? $"{s.CompanyBooking.Title} starts in {(int)minsUntil} minutes."
                                  : $"{s.CompanyBooking.Title} is in {h} hours.",
                    bookingId = s.CompanyBooking.Id, slotId = s.Id, startsAt = eventDate, urgent = false,
                    actionUrl = "/dashboard/artist/company-requests" });
            }
        }

        return Ok(reminders.OrderByDescending(r => ((dynamic)r).urgent).ThenBy(r => ((dynamic)r).startsAt));
    }

    // ── POST /api/bookings/{id}/checkin ─────────────────────────────

    [HttpPost("/api/bookings/{id:long}/checkin")]
    public async Task<IActionResult> CheckIn(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var booking = await _db.Bookings
            .FirstOrDefaultAsync(b => b.BookingId == id && b.ArtistUserId.ToString() == userId);
        if (booking is null) return NotFound();

        booking.ArtistCheckedIn   = true;
        booking.ArtistCheckedInAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var admins = await _users.GetUsersInRoleAsync("Admin");
        foreach (var admin in admins)
            _db.Notifications.Add(new Notification
            {
                UserId = admin.Id, EventType = "artist_checked_in",
                Title  = "Artist Checked In", Body = $"Artist checked in for booking #{id}.",
                EntityType = "Booking", EntityId = (int?)id,
                ActionUrl = "/dashboard/admin", CreatedAt = DateTime.UtcNow,
            });
        await _db.SaveChangesAsync();

        return Ok(new { checkedInAt = booking.ArtistCheckedInAt });
    }

    // ── POST /api/bookings/{id}/complete ────────────────────────────

    [HttpPost("/api/bookings/{id:long}/complete")]
    public async Task<IActionResult> CompleteService(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var booking = await _db.Bookings
            .FirstOrDefaultAsync(b => b.BookingId == id && b.ArtistUserId.ToString() == userId);
        if (booking is null) return NotFound();

        booking.ServiceCompleted   = true;
        booking.ServiceCompletedAt = DateTime.UtcNow;
        booking.Status             = BookingStatus.Completed;
        await _db.SaveChangesAsync();

        // Notify client to rate
        if (booking.ClientId.HasValue)
        {
            var client = await _users.FindByIdAsync(booking.ClientId.Value.ToString());
            if (client != null)
                _db.Notifications.Add(new Notification
                {
                    UserId = client.Id, EventType = "rate_service",
                    Title  = "How was your service?",
                    Body   = "Your booking is complete. Please take a moment to rate your artist.",
                    EntityType = "Booking", EntityId = (int?)id,
                    ActionUrl  = $"/artists/rate?bookingId={id}", CreatedAt = DateTime.UtcNow,
                });
            await _db.SaveChangesAsync();
        }

        return Ok(new { completedAt = booking.ServiceCompletedAt });
    }

    // ── POST /api/company-bookings/slots/{id}/checkin ───────────────

    [HttpPost("/api/company-bookings/slots/{id:int}/checkin")]
    public async Task<IActionResult> CheckInSlot(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var slot = await _db.CompanyBookingArtistSlots
            .Include(s => s.CompanyBooking)
            .FirstOrDefaultAsync(s => s.Id == id && s.ArtistUserId == userId);
        if (slot is null) return NotFound();

        slot.ArtistCheckedIn   = true;
        slot.ArtistCheckedInAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { checkedInAt = slot.ArtistCheckedInAt });
    }

    // ── GET /api/notifications/stream (SSE) ─────────────────────────

    [HttpGet("stream")]
    public async Task Stream(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.Headers["Connection"] = "keep-alive";

        await Response.Body.FlushAsync(cancellationToken);

        // Send an initial connected event
        var connected = $"event: connected\ndata: {{\"userId\":\"{userId}\"}}\n\n";
        var connBytes = Encoding.UTF8.GetBytes(connected);
        await Response.Body.WriteAsync(connBytes, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);

        // Keep alive with a comment every 30 seconds until disconnect
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

                var keepAlive = Encoding.UTF8.GetBytes(": keepalive\n\n");
                await Response.Body.WriteAsync(keepAlive, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
