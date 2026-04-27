using Beauty.Api.Data;
using Beauty.Api.Models.Gifts;
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

    public BattlesController(BeautyDbContext db) => _db = db;

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ── Artist signs up to be matched for a battle ─────────────────

    [HttpPost("signup")]
    [Authorize(Roles = "Artist")]
    public async Task<IActionResult> Signup([FromBody] BattleSignupRequest req)
    {
        if (req.DurationMinutes != 5 && req.DurationMinutes != 10)
            return BadRequest(new { error = "Duration must be 5 or 10 minutes." });

        // Prevent duplicate signups
        var existing = await _db.BattleSignups
            .FirstOrDefaultAsync(s => s.ArtistUserId == UserId
                                   && s.Status == BattleSignupStatus.Waiting);
        if (existing != null)
            return Conflict(new { error = "You are already in the matchmaking queue." });

        var signup = new BattleSignup
        {
            ArtistUserId             = UserId,
            PreferredDurationMinutes = req.DurationMinutes,
        };
        _db.BattleSignups.Add(signup);
        await _db.SaveChangesAsync();

        // Try to match immediately with another waiting artist
        var match = await _db.BattleSignups
            .FirstOrDefaultAsync(s => s.ArtistUserId != UserId
                                   && s.Status == BattleSignupStatus.Waiting
                                   && s.PreferredDurationMinutes == req.DurationMinutes);

        if (match != null)
        {
            var battle = new ArtistBattle
            {
                Artist1UserId    = match.ArtistUserId,
                Artist2UserId    = UserId,
                DurationMinutes  = req.DurationMinutes,
                Status           = BattleStatus.Pending,
            };
            _db.ArtistBattles.Add(battle);

            match.Status         = BattleSignupStatus.Matched;
            match.MatchedBattleId = battle.Id;
            signup.Status        = BattleSignupStatus.Matched;

            await _db.SaveChangesAsync();

            signup.MatchedBattleId = battle.Id;
            await _db.SaveChangesAsync();

            return Ok(new { matched = true, battleId = battle.Id, message = "Match found! Battle pending start." });
        }

        return Ok(new { matched = false, signupId = signup.Id, message = "In queue — waiting for an opponent." });
    }

    // ── Admin or artist starts a matched battle ────────────────────

    [HttpPost("{id}/start")]
    [Authorize(Roles = "Admin,Staff,Artist")]
    public async Task<IActionResult> Start(long id)
    {
        var battle = await _db.ArtistBattles.FindAsync(id);
        if (battle == null) return NotFound();
        if (battle.Status != BattleStatus.Pending)
            return BadRequest(new { error = "Battle is not in Pending state." });

        // Only the matched artists or admin can start
        if (User.IsInRole("Artist") &&
            battle.Artist1UserId != UserId &&
            battle.Artist2UserId != UserId)
            return Forbid();

        battle.Status    = BattleStatus.Active;
        battle.StartedAt = DateTime.UtcNow;
        battle.EndsAt    = DateTime.UtcNow.AddMinutes(battle.DurationMinutes);

        await _db.SaveChangesAsync();
        return Ok(new { battleId = id, endsAt = battle.EndsAt });
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

        battle.Status = BattleStatus.Completed;
        battle.WinnerUserId = battle.Artist1TotalSlabs >= battle.Artist2TotalSlabs
            ? battle.Artist1UserId
            : battle.Artist2UserId;

        await _db.SaveChangesAsync();
        return Ok(new
        {
            battleId        = id,
            winner          = battle.WinnerUserId,
            artist1Slabs    = battle.Artist1TotalSlabs,
            artist2Slabs    = battle.Artist2TotalSlabs,
        });
    }

    // ── Get active battles ─────────────────────────────────────────

    [HttpGet("active")]
    [AllowAnonymous]
    public async Task<IActionResult> GetActive()
    {
        var now = DateTime.UtcNow;
        var battles = await _db.ArtistBattles
            .Where(b => b.Status == BattleStatus.Active && b.EndsAt > now)
            .Select(b => new
            {
                b.Id,
                b.Artist1UserId,
                b.Artist2UserId,
                b.Artist1TotalSlabs,
                b.Artist2TotalSlabs,
                b.DurationMinutes,
                b.StartedAt,
                b.EndsAt,
            })
            .ToListAsync();

        return Ok(battles);
    }

    // ── Get single battle ──────────────────────────────────────────

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(long id)
    {
        var b = await _db.ArtistBattles.FindAsync(id);
        if (b == null) return NotFound();

        return Ok(new
        {
            b.Id,
            b.Artist1UserId,
            b.Artist2UserId,
            b.Artist1TotalSlabs,
            b.Artist2TotalSlabs,
            b.DurationMinutes,
            b.Status,
            b.StartedAt,
            b.EndsAt,
            b.WinnerUserId,
        });
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
