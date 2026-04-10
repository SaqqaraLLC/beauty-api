using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.Enterprise;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/featured-artists")]
public class FeaturedArtistsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public FeaturedArtistsController(BeautyDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    // ── GET /api/featured-artists ───────────────────────────────────

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        var now = DateTime.UtcNow;

        var slots = await _db.FeaturedSlots
            .AsNoTracking()
            .Include(s => s.ArtistProfile)
            .Where(s => s.IsActive && s.StartsAt <= now && s.EndsAt >= now)
            .OrderBy(s => s.DisplayPosition)
            .ToListAsync();

        return Ok(slots.Select(s => new
        {
            slotId = s.SlotId,
            artistId = s.ArtistProfileId,
            artistName = s.ArtistProfile.FullName,
            artistProfileImageUrl = s.ArtistProfile.ProfileImageUrl,
            artistSpecialty = s.ArtistProfile.Specialty,
            artistRating = s.ArtistProfile.AverageRating,
            isVerified = s.ArtistProfile.IsVerified,
            slotType = s.SlotType,
            displayPosition = s.DisplayPosition,
            startsAt = s.StartsAt,
            endsAt = s.EndsAt
        }));
    }

    // ── POST /api/featured-artists ──────────────────────────────────

    public record CreateSlotReq(int ArtistId, int? SortOrder, string? SlotType);

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateSlotReq req)
    {
        var artist = await _db.ArtistProfiles
            .FirstOrDefaultAsync(a => a.ArtistProfileId == req.ArtistId);
        if (artist is null)
            return NotFound(new { message = "Artist profile not found." });

        var now = DateTime.UtcNow;
        var slot = new FeaturedSlot
        {
            ArtistProfileId = req.ArtistId,
            SlotType = req.SlotType ?? "Featured",
            DisplayPosition = req.SortOrder ?? 0,
            StartsAt = now,
            EndsAt = now.AddDays(30),
            IsActive = true
        };

        _db.FeaturedSlots.Add(slot);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), null, new
        {
            slotId = slot.SlotId,
            artistId = slot.ArtistProfileId,
            artistName = artist.FullName,
            artistProfileImageUrl = artist.ProfileImageUrl,
            artistSpecialty = artist.Specialty,
            artistRating = artist.AverageRating,
            isVerified = artist.IsVerified,
            slotType = slot.SlotType,
            displayPosition = slot.DisplayPosition,
            startsAt = slot.StartsAt,
            endsAt = slot.EndsAt
        });
    }

    // ── POST /api/featured-artists/{id}/remove ──────────────────────

    [HttpPost("{id:int}/remove")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Remove(int id)
    {
        var slot = await _db.FeaturedSlots.FindAsync(id);
        if (slot is null) return NotFound();

        slot.IsActive = false;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Featured slot deactivated.", slotId = id });
    }
}
