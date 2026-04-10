using Beauty.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/streams")]
public class StreamsController : ControllerBase
{
    private readonly BeautyDbContext _db;

    public StreamsController(BeautyDbContext db)
    {
        _db = db;
    }

    private static string[] ParseJson(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }

    // ── GET /api/streams/browse ─────────────────────────────────────

    [HttpGet("browse")]
    [AllowAnonymous]
    public async Task<IActionResult> Browse(
        [FromQuery] string? filter,
        [FromQuery] string? search)
    {
        var query = _db.Streams
            .AsNoTracking()
            .Include(s => s.ArtistProfile)
            .Where(s => s.IsActive);

        // filter=live → only live streams; filter=recorded → non-live only
        if (filter?.ToLowerInvariant() == "live")
            query = query.Where(s => s.IsLive);
        else if (filter?.ToLowerInvariant() == "recorded")
            query = query.Where(s => !s.IsLive);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(s =>
                s.Title.Contains(search) ||
                s.ArtistProfile.FullName.Contains(search));

        var streams = await query
            .OrderByDescending(s => s.IsLive)
            .ThenByDescending(s => s.ViewerCount)
            .ThenByDescending(s => s.ScheduledAt)
            .ToListAsync();

        return Ok(streams.Select(s => new
        {
            streamId = s.StreamId,
            artistId = s.ArtistProfileId,
            artistName = s.ArtistProfile.FullName,
            title = s.Title,
            thumbnailUrl = s.ThumbnailUrl,
            isLive = s.IsLive,
            viewerCount = s.ViewerCount,
            scheduledAt = s.ScheduledAt,
            recordedAt = s.RecordedAt,
            tags = ParseJson(s.TagsJson)
        }));
    }
}
