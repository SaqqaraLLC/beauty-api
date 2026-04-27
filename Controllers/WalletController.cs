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

[Authorize]
[ApiController]
[Route("wallet")]
[EnableRateLimiting("general")]
public class WalletController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public WalletController(BeautyDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ── Get wallet balance ─────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetWallet()
    {
        var wallet = await GetOrCreateWalletAsync(UserId);
        return Ok(new
        {
            slabs  = wallet.Slabs,
            pieces = wallet.Pieces,
            pieceSlabEquivalent = Math.Floor(wallet.Pieces / 4m),
        });
    }

    // ── Exchange pieces → slabs (4 pieces = 1 slab) ───────────────

    [HttpPost("exchange-pieces")]
    public async Task<IActionResult> ExchangePieces([FromBody] ExchangeRequest req)
    {
        if (req.Pieces < 4m)
            return BadRequest(new { error = "Minimum exchange is 4 pieces." });

        // Slabs gained = floor(pieces / 4) — always whole slabs
        var slabsGained = (int)Math.Floor(req.Pieces / 4m);
        if (slabsGained < 1)
            return BadRequest(new { error = "Not enough pieces to earn a full slab." });

        var wallet = await GetOrCreateWalletAsync(UserId);
        if (wallet.Pieces < req.Pieces)
            return BadRequest(new { error = "Insufficient pieces." });

        // Deduct exactly the pieces that convert to whole slabs
        decimal piecesUsed = slabsGained * 4m;
        wallet.Pieces   -= piecesUsed;
        wallet.Slabs    += slabsGained;
        wallet.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(new { slabsGained, slabs = wallet.Slabs, pieces = wallet.Pieces });
    }

    // ── Gift sending history ───────────────────────────────────────

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);

        var sent = await _db.GiftTransactions
            .Include(g => g.Gift)
            .Where(g => g.SenderId == UserId)
            .OrderByDescending(g => g.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new
            {
                g.Id,
                gift      = g.Gift.Name,
                emoji     = g.Gift.Emoji,
                g.SlabsSpent,
                g.PiecesEarned,
                g.IsBattleGift,
                g.BonusSlabs,
                g.CreatedAt,
            })
            .ToListAsync();

        return Ok(sent);
    }

    // ── Slab purchase history (for admin refund reference) ─────────

    [HttpGet("purchases")]
    public async Task<IActionResult> GetPurchases()
    {
        var purchases = await _db.SlabPurchases
            .Where(p => p.UserId == UserId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id,
                p.SlabsGranted,
                amountDollars = p.AmountCents / 100m,
                p.Status,
                p.CreatedAt,
            })
            .ToListAsync();

        return Ok(purchases);
    }

    // ── Gift earnings history (artist: gifts received as pieces) ──

    [HttpGet("earnings")]
    public async Task<IActionResult> GetEarnings([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);

        var received = await _db.GiftTransactions
            .Include(g => g.Gift)
            .Where(g => g.RecipientArtistUserId == UserId)
            .OrderByDescending(g => g.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new
            {
                g.Id,
                gift         = g.Gift.Name,
                emoji        = g.Gift.Emoji,
                g.SlabsSpent,
                g.ArtistSlabs,
                piecesEarned = g.PiecesEarned,  // 1× normal, 1.5× during battles
                g.IsBattleGift,
                g.BonusSlabs,
                g.IncludedInPayout,
                g.CreatedAt,
            })
            .ToListAsync();

        var totals = await _db.GiftTransactions
            .Where(g => g.RecipientArtistUserId == UserId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                totalPiecesEarned = g.Sum(x => x.PiecesEarned),
                totalSlabsEquiv   = g.Sum(x => x.ArtistSlabs),
                unpaidCount       = g.Count(x => !x.IncludedInPayout),
            })
            .FirstOrDefaultAsync();

        return Ok(new { totals, history = received });
    }

    // ── Internal helper ────────────────────────────────────────────

    public static async Task<UserWallet> GetOrCreateWalletAsync(string userId, BeautyDbContext db)
    {
        var wallet = await db.UserWallets.FindAsync(userId);
        if (wallet == null)
        {
            wallet = new UserWallet { UserId = userId };
            db.UserWallets.Add(wallet);
            await db.SaveChangesAsync();
        }
        return wallet;
    }

    private Task<UserWallet> GetOrCreateWalletAsync(string userId)
        => GetOrCreateWalletAsync(userId, _db);

    public record ExchangeRequest(decimal Pieces);
}
