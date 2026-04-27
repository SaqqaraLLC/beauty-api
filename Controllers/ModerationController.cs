using Beauty.Api.Data;
using Beauty.Api.Models.Moderation;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/moderation")]
[Authorize(Roles = "Admin,Staff")]
public class ModerationController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly IWebhookService _webhook;
    private readonly PowerAutomateSettings _pa;

    public ModerationController(
        BeautyDbContext db,
        IWebhookService webhook,
        IOptions<PowerAutomateSettings> pa)
    {
        _db      = db;
        _webhook = webhook;
        _pa      = pa.Value;
    }

    // ── GET /api/moderation/flags ──────────────────────────────────────
    // ?status=Flagged|Reviewed|Dismissed  (omit for all)

    [HttpGet("flags")]
    public async Task<IActionResult> GetFlags([FromQuery] string? status)
    {
        var query = _db.StreamFlags
            .AsNoTracking()
            .Include(f => f.Stream)
                .ThenInclude(s => s.ArtistProfile)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<StreamFlagStatus>(status, true, out var parsed))
            query = query.Where(f => f.Status == parsed);

        var flags = await query
            .OrderByDescending(f => f.FlaggedAt)
            .ToListAsync();

        return Ok(flags.Select(f => new
        {
            flagId        = f.Id,
            streamId      = f.StreamId,
            artistName    = f.Stream.ArtistProfile?.FullName ?? "Unknown Artist",
            streamTitle   = f.Stream.Title,
            reason        = f.Reason,
            flagConfidence = f.FlagConfidence,
            flaggedAt     = f.FlaggedAt,
            status        = f.Status.ToString(),
            reviewedBy    = f.ReviewedByName,
            reviewedAt    = f.ReviewedAt,
            action        = f.Action,
            reviewNotes   = f.ReviewNotes
        }));
    }

    // ── POST /api/moderation/{flagId}/review ───────────────────────────
    // Body: { action: "approve" | "remove" | "ban", notes?: string }

    [HttpPost("{flagId:long}/review")]
    public async Task<IActionResult> Review(long flagId, [FromBody] FlagReviewReq req)
    {
        var flag = await _db.StreamFlags
            .Include(f => f.Stream)
            .FirstOrDefaultAsync(f => f.Id == flagId);

        if (flag is null) return NotFound();

        var reviewerName = User.FindFirst(ClaimTypes.Name)?.Value
                        ?? User.FindFirst(ClaimTypes.Email)?.Value
                        ?? "Admin";

        flag.Status         = StreamFlagStatus.Reviewed;
        flag.Action         = req.Action.ToLower();
        flag.ReviewedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        flag.ReviewedByName = reviewerName;
        flag.ReviewedAt     = DateTime.UtcNow;
        flag.ReviewNotes    = req.Notes;

        // Apply action to the stream
        if (req.Action.Equals("remove", StringComparison.OrdinalIgnoreCase) ||
            req.Action.Equals("ban",    StringComparison.OrdinalIgnoreCase))
        {
            flag.Stream.IsActive = false;
        }

        // Ban: deactivate the artist profile too
        if (req.Action.Equals("ban", StringComparison.OrdinalIgnoreCase) &&
            flag.Stream.ArtistProfileId > 0)
        {
            var profile = await _db.ArtistProfiles.FindAsync(flag.Stream.ArtistProfileId);
            if (profile is not null)
                profile.IsActive = false;
        }

        await _db.SaveChangesAsync();
        return Ok(new { flagId, action = flag.Action, reviewedAt = flag.ReviewedAt });
    }

    public record FlagReviewReq(string Action, string? Notes);

    // ── POST /api/moderation/flag-stream/{streamId} ────────────────────
    // Any authenticated user can flag a stream

    [HttpPost("flag-stream/{streamId:int}")]
    [Authorize]
    public async Task<IActionResult> FlagStream(int streamId, [FromBody] FlagReq req)
    {
        if (!await _db.Set<Beauty.Api.Models.Enterprise.Stream>().AnyAsync(s => s.StreamId == streamId && s.IsActive))
            return NotFound();

        var flag = new StreamFlag
        {
            StreamId        = streamId,
            Reason          = req.Reason,
            FlagConfidence  = req.Confidence ?? 1.0,
            FlaggedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Status          = StreamFlagStatus.Flagged,
            FlaggedAt       = DateTime.UtcNow
        };

        _db.StreamFlags.Add(flag);
        await _db.SaveChangesAsync();

        if (flag.FlagConfidence >= 0.8)
        {
            _ = _webhook.FireAsync(_pa.ModerationAlertUrl, new
            {
                event_type  = "moderation.high_confidence_flag",
                flag_id     = flag.Id,
                stream_id   = streamId,
                reason      = flag.Reason,
                confidence  = flag.FlagConfidence,
                flagged_at  = flag.FlaggedAt
            });
        }

        return Ok(new { flagId = flag.Id, message = "Stream flagged for review." });
    }

    public record FlagReq(string Reason, double? Confidence);

    // ── GET /api/moderation/summary ────────────────────────────────────

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var counts = await _db.StreamFlags
            .AsNoTracking()
            .GroupBy(f => f.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        return Ok(counts);
    }
}
