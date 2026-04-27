using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.Gifts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("gifts")]
[EnableRateLimiting("general")]
public class GiftsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public GiftsController(BeautyDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ── Gift catalog (public) ──────────────────────────────────────

    [HttpGet("catalog")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCatalog()
    {
        var gifts = await _db.GiftCatalog
            .Where(g => g.IsActive)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.SlabCost)
            .Select(g => new { g.Id, g.Name, g.Emoji, g.SlabCost })
            .ToListAsync();

        return Ok(gifts);
    }

    // ── Send a gift during a broadcast ────────────────────────────

    [HttpPost("send")]
    [Authorize]
    public async Task<IActionResult> SendGift([FromBody] SendGiftRequest req)
    {
        var gift = await _db.GiftCatalog.FindAsync(req.GiftId);
        if (gift == null || !gift.IsActive)
            return NotFound(new { error = "Gift not found." });

        var stream = await _db.Streams.FindAsync(req.StreamId);
        if (stream == null || !stream.IsLive)
            return BadRequest(new { error = "Stream is not live." });

        var wallet = await WalletController.GetOrCreateWalletAsync(UserId!, _db);

        int slabCost = gift.SlabCost;
        bool paidWithPieces = false;

        if (req.UseSlabs)
        {
            if (wallet.Slabs < slabCost)
                return BadRequest(new { error = $"Not enough slabs. Need {slabCost}, have {wallet.Slabs}." });
            wallet.Slabs -= slabCost;
        }
        else
        {
            // Paying with pieces — 4 pieces = 1 slab
            int piecesNeeded = slabCost * 4;
            if (wallet.Pieces < piecesNeeded)
                return BadRequest(new { error = $"Not enough pieces. Need {piecesNeeded}, have {wallet.Pieces}." });
            wallet.Pieces -= piecesNeeded;
            paidWithPieces = true;
        }

        // Gifter earns pieces equal to slabs spent (encourages gifting)
        wallet.Pieces   += slabCost;
        wallet.UpdatedAt = DateTime.UtcNow;

        // Battle context
        int bonusSlabs = 0;
        ArtistBattle? battle = null;
        if (req.BattleId.HasValue)
        {
            battle = await _db.ArtistBattles
                .FirstOrDefaultAsync(b => b.Id == req.BattleId && b.Status == BattleStatus.Active);

            if (battle != null && DateTime.UtcNow <= battle.EndsAt)
            {
                bonusSlabs = (int)Math.Floor(slabCost * 0.25m);

                if (battle.Artist1UserId == stream.AcsHostUserId || req.TargetArtistUserId == battle.Artist1UserId)
                    battle.Artist1TotalSlabs += slabCost + bonusSlabs;
                else
                    battle.Artist2TotalSlabs += slabCost + bonusSlabs;
            }
            else
            {
                battle = null; // battle expired or not active
            }
        }

        var tx = new GiftTransaction
        {
            SenderId              = UserId!,
            RecipientArtistUserId = req.TargetArtistUserId,
            StreamId              = req.StreamId,
            GiftId                = req.GiftId,
            SlabsSpent            = slabCost,
            PaidWithPieces        = paidWithPieces,
            PiecesEarned          = slabCost,
            IsBattleGift          = battle != null,
            BattleId              = battle?.Id,
            BonusSlabs            = bonusSlabs,
        };

        _db.GiftTransactions.Add(tx);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            success       = true,
            gift          = new { gift.Name, gift.Emoji },
            slabsSpent    = slabCost,
            piecesEarned  = slabCost,
            bonusSlabs,
            wallet        = new { wallet.Slabs, wallet.Pieces },
        });
    }

    // ── Broadcast gift leaderboard ─────────────────────────────────

    [HttpGet("leaderboard/{streamId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLeaderboard(int streamId, [FromQuery] int top = 10)
    {
        top = Math.Clamp(top, 1, 50);

        var leaders = await _db.GiftTransactions
            .Where(g => g.StreamId == streamId)
            .GroupBy(g => g.SenderId)
            .Select(g => new
            {
                userId     = g.Key,
                totalSlabs = g.Sum(x => x.SlabsSpent),
                giftCount  = g.Count(),
            })
            .OrderByDescending(g => g.totalSlabs)
            .Take(top)
            .ToListAsync();

        return Ok(leaders);
    }

    // ── Artist gift earnings summary (for payout inclusion) ────────

    [HttpGet("earnings/{artistUserId}")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> GetArtistEarnings(string artistUserId, [FromQuery] bool unpaidOnly = true)
    {
        var query = _db.GiftTransactions
            .Where(g => g.RecipientArtistUserId == artistUserId);

        if (unpaidOnly)
            query = query.Where(g => !g.IncludedInPayout);

        var summary = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                totalSlabs  = g.Sum(x => x.SlabsSpent + x.BonusSlabs),
                giftCount   = g.Count(),
                pendingPayout = g.Count(x => !x.IncludedInPayout),
            })
            .FirstOrDefaultAsync();

        return Ok(summary ?? new { totalSlabs = 0, giftCount = 0, pendingPayout = 0 });
    }

    public record SendGiftRequest(
        int GiftId,
        int StreamId,
        string TargetArtistUserId,
        bool UseSlabs = true,
        long? BattleId = null);
}
