using Beauty.Api.Data;
using Beauty.Api.Models.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Controllers;

[EnableRateLimiting("general")]
[ApiController]
[Route("api/promo-codes")]
public class PromoCodesController : ControllerBase
{
    private readonly BeautyDbContext _db;

    public PromoCodesController(BeautyDbContext db) => _db = db;

    // ── GET /api/promo-codes/validate/{code} ─────────────────────────
    // Called from booking UI — returns pricing impact without consuming the code

    [HttpGet("validate/{code}")]
    [Authorize]
    public async Task<IActionResult> Validate(string code)
    {
        var promo = await _db.PromoCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == code.Trim().ToUpper() && p.IsActive);

        if (promo is null || !promo.IsValid)
            return NotFound(new { valid = false, message = "Promo code is invalid or expired." });

        return Ok(new
        {
            valid                   = true,
            code                    = promo.Code,
            description             = promo.Description,
            productMarkupMultiplier = promo.ProductMarkupMultiplier,
            standardMultiplier      = 1.8m,
            savingsPercent          = Math.Round((1.8m - promo.ProductMarkupMultiplier) / 1.8m * 100, 1),
            validUntil              = promo.ValidUntil
        });
    }

    // ── GET /api/promo-codes ─────────────────────────────────────────
    // Admin: list all codes

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll()
    {
        var codes = await _db.PromoCodes
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return Ok(codes.Select(p => new
        {
            promoCodeId             = p.PromoCodeId,
            code                    = p.Code,
            description             = p.Description,
            productMarkupMultiplier = p.ProductMarkupMultiplier,
            maxUses                 = p.MaxUses,
            usedCount               = p.UsedCount,
            isActive                = p.IsActive,
            isValid                 = p.IsValid,
            validFrom               = p.ValidFrom,
            validUntil              = p.ValidUntil,
            createdAt               = p.CreatedAt
        }));
    }

    // ── POST /api/promo-codes ────────────────────────────────────────
    // Admin: create a new promo code

    public record CreatePromoCodeReq(
        string Code,
        string? Description,
        decimal ProductMarkupMultiplier,
        int? MaxUses,
        DateTime? ValidFrom,
        DateTime? ValidUntil);

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreatePromoCodeReq req)
    {
        var normalized = req.Code.Trim().ToUpper();

        var exists = await _db.PromoCodes.AnyAsync(p => p.Code == normalized);
        if (exists)
            return Conflict(new { message = $"Promo code '{normalized}' already exists." });

        if (req.ProductMarkupMultiplier <= 0 || req.ProductMarkupMultiplier > 1.8m)
            return BadRequest(new { message = "Multiplier must be between 0 and 1.8." });

        var promo = new PromoCode
        {
            Code                    = normalized,
            Description             = req.Description,
            ProductMarkupMultiplier = req.ProductMarkupMultiplier,
            MaxUses                 = req.MaxUses,
            ValidFrom               = req.ValidFrom,
            ValidUntil              = req.ValidUntil,
            IsActive                = true,
            CreatedAt               = DateTime.UtcNow
        };

        _db.PromoCodes.Add(promo);
        await _db.SaveChangesAsync();

        return Ok(new { promoCodeId = promo.PromoCodeId, code = promo.Code });
    }

    // ── POST /api/promo-codes/{id}/deactivate ────────────────────────

    [HttpPost("{id:int}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var promo = await _db.PromoCodes.FindAsync(id);
        if (promo is null) return NotFound();

        promo.IsActive = false;
        await _db.SaveChangesAsync();

        return Ok(new { promoCodeId = id, isActive = false });
    }
}
