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

    // ── GET /api/moderation/flags?status=&type= ───────────────────────────
    // type = "stream" | "short" | omit for both

    [HttpGet("flags")]
    public async Task<IActionResult> GetFlags(
        [FromQuery] string? status,
        [FromQuery] string? type)
    {
        StreamFlagStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<StreamFlagStatus>(status, true, out var s))
            parsed = s;

        var results = new List<object>();

        if (type is null || type.Equals("stream", StringComparison.OrdinalIgnoreCase))
        {
            var q = _db.StreamFlags
                .AsNoTracking()
                .Include(f => f.Stream).ThenInclude(s => s.ArtistProfile)
                .AsQueryable();
            if (parsed.HasValue) q = q.Where(f => f.Status == parsed.Value);

            var flags = await q.OrderByDescending(f => f.FlaggedAt).ToListAsync();
            results.AddRange(flags.Select(f => (object)new
            {
                flagId        = f.Id,
                flagType      = "stream",
                contentId     = f.StreamId,
                artistName    = f.Stream.ArtistProfile?.FullName ?? "Unknown Artist",
                contentTitle  = f.Stream.Title,
                reason        = f.Reason,
                flaggedAt     = f.FlaggedAt,
                status        = f.Status.ToString(),
                reviewedBy    = f.ReviewedByName,
                reviewedAt    = f.ReviewedAt,
                action        = f.Action,
                reviewNotes   = f.ReviewNotes
            }));
        }

        if (type is null || type.Equals("short", StringComparison.OrdinalIgnoreCase))
        {
            var q = _db.ShortFlags
                .AsNoTracking()
                .Include(f => f.Short)
                .AsQueryable();
            if (parsed.HasValue) q = q.Where(f => f.Status == parsed.Value);

            var flags = await q.OrderByDescending(f => f.FlaggedAt).ToListAsync();
            results.AddRange(flags.Select(f => (object)new
            {
                flagId        = f.Id,
                flagType      = "short",
                contentId     = f.ShortId,
                artistName    = f.Short.ArtistName,
                contentTitle  = f.Short.Title,
                reason        = f.Reason,
                flaggedAt     = f.FlaggedAt,
                status        = f.Status.ToString(),
                reviewedBy    = f.ReviewedByName,
                reviewedAt    = f.ReviewedAt,
                action        = f.Action,
                reviewNotes   = f.ReviewNotes
            }));
        }

        return Ok(results.OrderByDescending(r => ((dynamic)r).flaggedAt));
    }

    // ── POST /api/moderation/stream/{flagId}/review ───────────────────────

    [HttpPost("stream/{flagId:long}/review")]
    public async Task<IActionResult> ReviewStream(long flagId, [FromBody] FlagReviewReq req)
    {
        var flag = await _db.StreamFlags
            .Include(f => f.Stream)
            .FirstOrDefaultAsync(f => f.Id == flagId);
        if (flag is null) return NotFound();

        ApplyReview(flag, req);

        if (req.Action.Equals("remove", StringComparison.OrdinalIgnoreCase) ||
            req.Action.Equals("ban",    StringComparison.OrdinalIgnoreCase))
            flag.Stream.IsActive = false;

        if (req.Action.Equals("ban", StringComparison.OrdinalIgnoreCase) &&
            flag.Stream.ArtistProfileId > 0)
        {
            var profile = await _db.ArtistProfiles.FindAsync(flag.Stream.ArtistProfileId);
            if (profile is not null) profile.IsActive = false;
        }

        await _db.SaveChangesAsync();
        return Ok(new { flagId, action = flag.Action, reviewedAt = flag.ReviewedAt });
    }

    // ── POST /api/moderation/short/{flagId}/review ────────────────────────

    [HttpPost("short/{flagId:long}/review")]
    public async Task<IActionResult> ReviewShort(long flagId, [FromBody] FlagReviewReq req)
    {
        var flag = await _db.ShortFlags
            .Include(f => f.Short)
            .FirstOrDefaultAsync(f => f.Id == flagId);
        if (flag is null) return NotFound();

        ApplyReview(flag, req);

        if (req.Action.Equals("remove", StringComparison.OrdinalIgnoreCase) ||
            req.Action.Equals("ban",    StringComparison.OrdinalIgnoreCase))
            flag.Short.IsActive = false;

        await _db.SaveChangesAsync();
        return Ok(new { flagId, action = flag.Action, reviewedAt = flag.ReviewedAt });
    }

    // ── POST /api/moderation/flag-stream/{streamId} ───────────────────────

    [HttpPost("flag-stream/{streamId:int}")]
    [Authorize]
    public async Task<IActionResult> FlagStream(int streamId, [FromBody] FlagReq req)
    {
        if (!await _db.Set<Beauty.Api.Models.Enterprise.Stream>()
                .AnyAsync(s => s.StreamId == streamId && s.IsActive))
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

        FireAlertIfNeeded("stream", flag.Id, streamId, flag.Reason, flag.FlagConfidence);
        return Ok(new { flagId = flag.Id, message = "Reported. Our team will review shortly." });
    }

    // ── POST /api/moderation/flag-short/{shortId} ────────────────────────

    [HttpPost("flag-short/{shortId:long}")]
    [Authorize]
    public async Task<IActionResult> FlagShort(long shortId, [FromBody] FlagReq req)
    {
        if (!await _db.ArtistShorts.AnyAsync(s => s.Id == shortId && s.IsActive))
            return NotFound();

        var flag = new ShortFlag
        {
            ShortId         = shortId,
            Reason          = req.Reason,
            FlaggedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Status          = StreamFlagStatus.Flagged,
            FlaggedAt       = DateTime.UtcNow
        };
        _db.ShortFlags.Add(flag);
        await _db.SaveChangesAsync();

        FireAlertIfNeeded("short", flag.Id, (int)shortId, flag.Reason, 1.0);
        return Ok(new { flagId = flag.Id, message = "Reported. Our team will review shortly." });
    }

    // ── POST /api/moderation/block ────────────────────────────────────────

    [HttpPost("block")]
    [Authorize]
    public async Task<IActionResult> BlockUser([FromBody] BlockReq req)
    {
        var blockerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        if (blockerId == req.UserId) return BadRequest(new { error = "Cannot block yourself." });

        var exists = await _db.UserBlocks
            .AnyAsync(b => b.BlockerUserId == blockerId && b.BlockedUserId == req.UserId);
        if (exists) return Ok(new { message = "Already blocked." });

        _db.UserBlocks.Add(new UserBlock
        {
            BlockerUserId      = blockerId,
            BlockedUserId      = req.UserId,
            BlockedDisplayName = req.DisplayName,
            CreatedAt          = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return Ok(new { message = "User blocked." });
    }

    // ── DELETE /api/moderation/block/{userId} ─────────────────────────────

    [HttpDelete("block/{userId}")]
    [Authorize]
    public async Task<IActionResult> UnblockUser(string userId)
    {
        var blockerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var block = await _db.UserBlocks
            .FirstOrDefaultAsync(b => b.BlockerUserId == blockerId && b.BlockedUserId == userId);
        if (block is null) return Ok(new { message = "Not blocked." });

        _db.UserBlocks.Remove(block);
        await _db.SaveChangesAsync();
        return Ok(new { message = "User unblocked." });
    }

    // ── GET /api/moderation/blocked ───────────────────────────────────────

    [HttpGet("blocked")]
    [Authorize]
    public async Task<IActionResult> GetBlocked()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var blocks = await _db.UserBlocks
            .AsNoTracking()
            .Where(b => b.BlockerUserId == userId)
            .Select(b => new { b.BlockedUserId, b.BlockedDisplayName, b.CreatedAt })
            .ToListAsync();
        return Ok(blocks);
    }

    // ── GET /api/moderation/summary ───────────────────────────────────────

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var streamCounts = await _db.StreamFlags
            .AsNoTracking()
            .GroupBy(f => f.Status)
            .Select(g => new { Type = "stream", Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        var shortCounts = await _db.ShortFlags
            .AsNoTracking()
            .GroupBy(f => f.Status)
            .Select(g => new { Type = "short", Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        return Ok(new { streams = streamCounts, shorts = shortCounts });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void ApplyReview(dynamic flag, FlagReviewReq req)
    {
        flag.Status           = StreamFlagStatus.Reviewed;
        flag.Action           = req.Action.ToLower();
        flag.ReviewedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        flag.ReviewedByName   = User.FindFirst(ClaimTypes.Name)?.Value
                             ?? User.FindFirst(ClaimTypes.Email)?.Value
                             ?? "Admin";
        flag.ReviewedAt       = DateTime.UtcNow;
        flag.ReviewNotes      = req.Notes;
    }

    private void FireAlertIfNeeded(string type, long flagId, int contentId, string reason, double confidence)
    {
        if (string.IsNullOrEmpty(_pa.ModerationAlertUrl)) return;
        _ = _webhook.FireAsync(_pa.ModerationAlertUrl, new
        {
            event_type  = "moderation.flag",
            flag_type   = type,
            flag_id     = flagId,
            content_id  = contentId,
            reason,
            confidence,
            flagged_at  = DateTime.UtcNow
        });
    }

    public record FlagReviewReq(string Action, string? Notes);
    public record FlagReq(string Reason, double? Confidence);
    public record BlockReq(string UserId, string? DisplayName);
}
