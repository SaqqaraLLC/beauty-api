using Beauty.Api.Data;
using Beauty.Api.Models.Gifts;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("battles")]
[EnableRateLimiting("general")]
public class BattlesController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly BattleMatchmakingService _matchmaking;

    public BattlesController(BeautyDbContext db, BattleMatchmakingService matchmaking)
    {
        _db          = db;
        _matchmaking = matchmaking;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ── Artist signs up to be matched ─────────────────────────────

    [HttpPost("signup")]
    [Authorize(Roles = "Artist")]
    public async Task<IActionResult> Signup([FromBody] BattleSignupRequest req)
    {
        if (req.DurationMinutes != 5 && req.DurationMinutes != 10)
            return BadRequest(new { error = "Duration must be 5 or 10 minutes." });

        var existing = await _db.BattleSignups
            .FirstOrDefaultAsync(s => s.ArtistUserId == UserId
                                   && s.Status == BattleSignupStatus.Waiting);
        if (existing != null)
            return Conflict(new { error = "You are already in the matchmaking queue." });

        // Calculate composite score: tenure + profit + popularity
        var score = await _matchmaking.CalculateScoreAsync(UserId);

        var signup = new BattleSignup
        {
            ArtistUserId             = UserId,
            PreferredDurationMinutes = req.DurationMinutes,
            BattleScore              = score,
        };
        _db.BattleSignups.Add(signup);
        await _db.SaveChangesAsync();

        // Find closest-scored opponent
        var match = await _matchmaking.FindBestMatchAsync(UserId, req.DurationMinutes, score);

        if (match != null)
        {
            var battle = new ArtistBattle
            {
                Artist1UserId   = match.ArtistUserId,
                Artist2UserId   = UserId,
                DurationMinutes = req.DurationMinutes,
                Status          = BattleStatus.Pending,
            };
            _db.ArtistBattles.Add(battle);

            match.Status          = BattleSignupStatus.Matched;
            match.MatchedBattleId = battle.Id;
            signup.Status         = BattleSignupStatus.Matched;

            await _db.SaveChangesAsync();

            signup.MatchedBattleId = battle.Id;
            await _db.SaveChangesAsync();

            return Ok(new { matched = true, battleId = battle.Id, message = "Match found! Battle pending start." });
        }

        return Ok(new { matched = false, signupId = signup.Id, score, message = "In queue — waiting for an opponent." });
    }

    // ── Artist or admin starts a matched battle ────────────────────

    [HttpPost("{id}/start")]
    [Authorize(Roles = "Admin,Staff,Artist")]
    public async Task<IActionResult> Start(long id)
    {
        var battle = await _db.ArtistBattles.FindAsync(id);
        if (battle == null) return NotFound();
        if (battle.Status != BattleStatus.Pending)
            return BadRequest(new { error = "Battle is not in Pending state." });

        if (User.IsInRole("Artist") &&
            battle.Artist1UserId != UserId &&
            battle.Artist2UserId != UserId)
            return Forbid();

        // Link live streams to the battle
        var profile1 = await _db.ArtistProfiles.FirstOrDefaultAsync(p => p.UserId == battle.Artist1UserId);
        var profile2 = await _db.ArtistProfiles.FirstOrDefaultAsync(p => p.UserId == battle.Artist2UserId);

        if (profile1 != null)
        {
            var stream1 = await _db.Streams
                .Where(s => s.ArtistProfileId == profile1.ArtistProfileId && s.IsLive)
                .OrderByDescending(s => s.StreamId)
                .FirstOrDefaultAsync();
            if (stream1 != null) battle.Stream1Id = stream1.StreamId;
        }

        if (profile2 != null)
        {
            var stream2 = await _db.Streams
                .Where(s => s.ArtistProfileId == profile2.ArtistProfileId && s.IsLive)
                .OrderByDescending(s => s.StreamId)
                .FirstOrDefaultAsync();
            if (stream2 != null) battle.Stream2Id = stream2.StreamId;
        }

        battle.Status    = BattleStatus.Active;
        battle.StartedAt = DateTime.UtcNow;
        battle.EndsAt    = DateTime.UtcNow.AddMinutes(battle.DurationMinutes);

        await _db.SaveChangesAsync();

        return Ok(new
        {
            battleId  = id,
            stream1Id = battle.Stream1Id,
            stream2Id = battle.Stream2Id,
            endsAt    = battle.EndsAt,
        });
    }

    // ── Resolve a completed battle ─────────────────────────────────

    [HttpPost("{id}/resolve")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Resolve(long id)
    {
        var battle = await _db.ArtistBattles.FindAsync(id);
        if (battle == null) return NotFound();
        if (battle.Status != BattleStatus.Active)
            return BadRequest(new { error = "Battle is not active." });

        battle.Status       = BattleStatus.Completed;
        battle.WinnerUserId = battle.Artist1TotalSlabs >= battle.Artist2TotalSlabs
            ? battle.Artist1UserId
            : battle.Artist2UserId;

        await _db.SaveChangesAsync();
        return Ok(new
        {
            battleId     = id,
            winner       = battle.WinnerUserId,
            artist1Slabs = battle.Artist1TotalSlabs,
            artist2Slabs = battle.Artist2TotalSlabs,
        });
    }

    // ── Get active battles ─────────────────────────────────────────

    [HttpGet("active")]
    [AllowAnonymous]
    public async Task<IActionResult> GetActive()
    {
        var now     = DateTime.UtcNow;
        var battles = await _db.ArtistBattles
            .Where(b => b.Status == BattleStatus.Active && b.EndsAt > now)
            .Select(b => new
            {
                b.Id,
                b.Artist1UserId,
                b.Artist2UserId,
                b.Artist1TotalSlabs,
                b.Artist2TotalSlabs,
                b.Stream1Id,
                b.Stream2Id,
                b.DurationMinutes,
                b.StartedAt,
                b.EndsAt,
            })
            .ToListAsync();

        return Ok(battles);
    }

    // ── Get single battle with artist names ────────────────────────

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(long id)
    {
        var b = await _db.ArtistBattles.FindAsync(id);
        if (b == null) return NotFound();

        var profile1 = await _db.ArtistProfiles.FirstOrDefaultAsync(p => p.UserId == b.Artist1UserId);
        var profile2 = await _db.ArtistProfiles.FirstOrDefaultAsync(p => p.UserId == b.Artist2UserId);

        return Ok(new
        {
            b.Id,
            b.Artist1UserId,
            b.Artist2UserId,
            Artist1Name         = profile1?.FullName ?? "Artist 1",
            Artist2Name         = profile2?.FullName ?? "Artist 2",
            Artist1Image        = profile1?.ProfileImageUrl,
            Artist2Image        = profile2?.ProfileImageUrl,
            b.Artist1TotalSlabs,
            b.Artist2TotalSlabs,
            b.Stream1Id,
            b.Stream2Id,
            b.DurationMinutes,
            b.Status,
            b.StartedAt,
            b.EndsAt,
            b.WinnerUserId,
        });
    }

    // ── My current battle/signup status ───────────────────────────

    [HttpGet("my-status")]
    [Authorize(Roles = "Artist")]
    public async Task<IActionResult> MyStatus()
    {
        var signup = await _db.BattleSignups
            .Where(s => s.ArtistUserId == UserId && s.Status == BattleSignupStatus.Waiting)
            .FirstOrDefaultAsync();

        if (signup != null)
            return Ok(new { status = "waiting", signupId = signup.Id, score = signup.BattleScore });

        var active = await _db.ArtistBattles
            .Where(b => (b.Artist1UserId == UserId || b.Artist2UserId == UserId)
                     && (b.Status == BattleStatus.Pending || b.Status == BattleStatus.Active))
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync();

        if (active != null)
            return Ok(new { status = active.Status.ToString().ToLower(), battleId = active.Id, endsAt = active.EndsAt });

        return Ok(new { status = "idle" });
    }

    // ── Cancel a signup ────────────────────────────────────────────

    [HttpDelete("signup")]
    [Authorize(Roles = "Artist")]
    public async Task<IActionResult> CancelSignup()
    {
        var signup = await _db.BattleSignups
            .FirstOrDefaultAsync(s => s.ArtistUserId == UserId
                                   && s.Status == BattleSignupStatus.Waiting);
        if (signup == null) return NotFound();

        signup.Status = BattleSignupStatus.Expired;
        await _db.SaveChangesAsync();
        return Ok();
    }

    public record BattleSignupRequest(int DurationMinutes = 5);
}
