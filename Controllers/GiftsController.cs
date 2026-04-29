using Beauty.Api.Authorization;
using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.Gifts;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("gifts")]
[EnableRateLimiting("general")]
public class GiftsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly GiftBroadcastService _broadcast;

    public GiftsController(BeautyDbContext db, UserManager<ApplicationUser> userManager, GiftBroadcastService broadcast)
    {
        _db        = db;
        _userManager = userManager;
        _broadcast = broadcast;
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
    [RequiresVerification]
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

        // Normal: artist earns slabCost pieces. Battle: 1.5× (e.g. 5 slabs → 7.5 pieces exactly).
        decimal artistPieces = battle != null ? slabCost * 1.5m : slabCost;

        var artistWallet = await WalletController.GetOrCreateWalletAsync(req.TargetArtistUserId, _db);
        artistWallet.Pieces   += artistPieces;
        artistWallet.UpdatedAt = DateTime.UtcNow;

        // Slab-equivalent for payout reporting
        decimal artistSlabs = artistPieces / 4m;

        var tx = new GiftTransaction
        {
            SenderId              = UserId!,
            RecipientArtistUserId = req.TargetArtistUserId,
            StreamId              = req.StreamId,
            GiftId                = req.GiftId,
            SlabsSpent            = slabCost,
            PaidWithPieces        = paidWithPieces,
            PiecesEarned          = artistPieces,
            ArtistSlabs           = artistSlabs,
            IsBattleGift          = battle != null,
            BattleId              = battle?.Id,
            BonusSlabs            = bonusSlabs,
        };

        _db.GiftTransactions.Add(tx);
        await _db.SaveChangesAsync();

        // Push real-time event to all SSE subscribers watching this stream
        _broadcast.Broadcast(req.StreamId, new GiftEvent(
            Emoji:       gift.Emoji,
            GiftName:    gift.Name,
            SenderId:    UserId!,
            IsBattleGift: battle != null
        ));

        return Ok(new
        {
            success    = true,
            gift       = new { gift.Name, gift.Emoji },
            slabsSpent = slabCost,
            bonusSlabs,
            wallet     = new { wallet.Slabs, wallet.Pieces },
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
                artistSlabs   = g.Sum(x => x.ArtistSlabs),
                totalGifted   = g.Sum(x => x.SlabsSpent),
                giftCount     = g.Count(),
                pendingPayout = g.Count(x => !x.IncludedInPayout),
            })
            .FirstOrDefaultAsync();

        return Ok(summary ?? new { artistSlabs = 0m, totalGifted = 0, giftCount = 0, pendingPayout = 0 });
    }

    // ── Real-time SSE feed for a stream ───────────────────────────

    [HttpGet("live/{streamId}")]
    [AllowAnonymous]
    public async Task LiveFeed(int streamId, CancellationToken ct)
    {
        Response.Headers["Content-Type"]  = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        // Send a keepalive comment every 20 s to prevent proxy timeouts
        using var keepaliveTimer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        var keepaliveTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && await keepaliveTimer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await Response.WriteAsync(": keepalive\n\n", ct).ConfigureAwait(false);
                await Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        }, ct);

        await foreach (var evt in _broadcast.SubscribeAsync(streamId, ct))
        {
            var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await Response.WriteAsync($"data: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        await keepaliveTask.ConfigureAwait(false);
    }

    public record SendGiftRequest(
        int GiftId,
        int StreamId,
        string TargetArtistUserId,
        bool UseSlabs = true,
        long? BattleId = null);
}
