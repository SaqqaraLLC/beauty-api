using Beauty.Api.Data;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Beauty.Api.Controllers;

[EnableRateLimiting("general")]
[ApiController]
[Route("api/streams")]
public class StreamsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly AcsStreamingService _acs;
    private readonly UserManager<Beauty.Api.Models.ApplicationUser> _users;

    public StreamsController(BeautyDbContext db, AcsStreamingService acs,
        UserManager<Beauty.Api.Models.ApplicationUser> users)
    {
        _db = db;
        _acs = acs;
        _users = users;
    }

    private static string[] ParseJson(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }

    // ── GET /api/streams/browse ─────────────────────────────────────────
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

        if (filter?.ToLowerInvariant() == "live")
            query = query.Where(s => s.IsLive);
        else if (filter?.ToLowerInvariant() == "recorded")
            query = query.Where(s => !s.IsLive && s.RecordedAt != null);

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
            streamId     = s.StreamId,
            artistId     = s.ArtistProfileId,
            artistName   = s.ArtistProfile.FullName,
            title        = s.Title,
            thumbnailUrl = s.ThumbnailUrl,
            isLive       = s.IsLive,
            viewerCount  = s.ViewerCount,
            scheduledAt  = s.ScheduledAt,
            recordedAt   = s.RecordedAt,
            tags         = ParseJson(s.TagsJson)
        }));
    }

    // ── GET /api/streams/{id} ───────────────────────────────────────────
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(int id)
    {
        var s = await _db.Streams
            .AsNoTracking()
            .Include(s => s.ArtistProfile)
            .FirstOrDefaultAsync(s => s.StreamId == id && s.IsActive);
        if (s == null) return NotFound();

        return Ok(new
        {
            streamId     = s.StreamId,
            artistId     = s.ArtistProfileId,
            artistName   = s.ArtistProfile.FullName,
            title        = s.Title,
            thumbnailUrl = s.ThumbnailUrl,
            isLive       = s.IsLive,
            viewerCount  = s.ViewerCount,
            scheduledAt  = s.ScheduledAt,
            recordedAt   = s.RecordedAt,
            tags         = ParseJson(s.TagsJson),
            acsRoomId    = s.AcsRoomId
        });
    }

    // ── POST /api/streams/start — Artist starts a live broadcast ────────
    [HttpPost("start")]
    [Authorize]
    public async Task<IActionResult> Start([FromBody] StartStreamRequest req)
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var profile = await _db.ArtistProfiles
            .FirstOrDefaultAsync(a => a.UserId == user.Id);
        if (profile == null)
            return BadRequest(new { message = "No artist profile found for this account." });

        try
        {
            var result = await _acs.StartBroadcastAsync(
                profile.ArtistProfileId,
                user.Id,
                req.Title,
                req.ThumbnailUrl,
                req.Tags ?? []);

            return Ok(new
            {
                streamId    = result.StreamId,
                roomId      = result.RoomId,
                acsToken    = result.AcsToken,
                acsUserId   = result.AcsUserId,
                acsEndpoint = result.AcsEndpoint,
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // ── POST /api/streams/{id}/join — Viewer joins a live broadcast ─────
    [HttpPost("{id:int}/join")]
    [AllowAnonymous]
    public async Task<IActionResult> Join(int id, [FromBody] JoinStreamRequest? req)
    {
        try
        {
            var result = await _acs.JoinBroadcastAsync(id, req?.DisplayName);
            return Ok(new
            {
                streamId    = result.StreamId,
                roomId      = result.RoomId,
                acsToken    = result.AcsToken,
                acsUserId   = result.AcsUserId,
                acsEndpoint = result.AcsEndpoint,
                title       = result.Title,
            });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex)
            { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex)
            { return StatusCode(500, new { message = ex.Message }); }
    }

    // ── POST /api/streams/{id}/end — Artist ends their broadcast ────────
    [HttpPost("{id:int}/end")]
    [Authorize]
    public async Task<IActionResult> End(int id)
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return Unauthorized();

        try
        {
            await _acs.EndBroadcastAsync(id, user.Id);
            return Ok(new { message = "Stream ended." });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex)
            { return StatusCode(500, new { message = ex.Message }); }
    }

    // ── DTOs ────────────────────────────────────────────────────────────
    public record StartStreamRequest(
        string Title,
        string? ThumbnailUrl,
        string[]? Tags);

    public record JoinStreamRequest(string? DisplayName);
}
