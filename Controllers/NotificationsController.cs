using Beauty.Api.Data;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

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
