using Beauty.Api.Data;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/locations")]
[Authorize]
public class LocationsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public LocationsController(BeautyDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    private static object MapLocation(Models.Locations.Location loc) => new
    {
        id                        = loc.Id,
        name                      = loc.Name,
        address                   = loc.Address,
        ownerUserId               = loc.OwnerUserId,
        pureAccountStatus         = loc.PureAccountStatus,
        pureAccountActivatedAt    = loc.PureAccountActivatedAt,
        pureFirstOrderPlacedAt    = loc.PureFirstOrderPlacedAt,
        pureFirstOrderDaysRemaining = loc.PureFirstOrderDaysRemaining,
        pureFirstOrderDeadline    = loc.PureAccountActivatedAt.HasValue
            ? loc.PureAccountActivatedAt.Value.AddDays(60)
            : (DateTime?)null
    };

    // ── GET /api/locations/my ───────────────────────────────────────
    // Location user fetches their own location record

    [HttpGet("my")]
    public async Task<IActionResult> GetMine()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var location = await _db.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.OwnerUserId == userId && !l.IsDeleted);

        if (location is null)
            return NotFound(new { message = "No location found for this account." });

        return Ok(MapLocation(location));
    }

    // ── GET /api/locations ──────────────────────────────────────────
    // Admin: list all locations

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? pureStatus)
    {
        var query = _db.Locations
            .AsNoTracking()
            .Where(l => !l.IsDeleted);

        if (!string.IsNullOrWhiteSpace(pureStatus))
            query = query.Where(l => l.PureAccountStatus == pureStatus);

        var locations = await query
            .OrderBy(l => l.Name)
            .ToListAsync();

        return Ok(locations.Select(MapLocation));
    }

    // ── GET /api/locations/{id} ─────────────────────────────────────

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var location = await _db.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted);

        if (location is null) return NotFound();

        return Ok(MapLocation(location));
    }

    // ── POST /api/locations/{id}/pure/activate ──────────────────────
    // Admin: mark %PURE wholesale account as activated — starts 60-day clock

    [HttpPost("{id:guid}/pure/activate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PureActivate(Guid id)
    {
        var location = await _db.Locations
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted);

        if (location is null) return NotFound();

        location.PureAccountActivatedAt = DateTime.UtcNow;
        location.PureAccountStatus      = "Active";

        await _db.SaveChangesAsync();

        return Ok(new
        {
            id                     = location.Id,
            pureAccountStatus      = location.PureAccountStatus,
            pureAccountActivatedAt = location.PureAccountActivatedAt,
            deadline               = location.PureAccountActivatedAt!.Value.AddDays(60),
            daysRemaining          = 60
        });
    }

    // ── POST /api/locations/{id}/pure/first-order ───────────────────
    // Admin: mark first %PURE order as placed — stops the countdown

    [HttpPost("{id:guid}/pure/first-order")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PureFirstOrder(Guid id)
    {
        var location = await _db.Locations
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted);

        if (location is null) return NotFound();

        if (location.PureAccountActivatedAt is null)
            return BadRequest(new { message = "Account has not been activated yet." });

        location.PureFirstOrderPlacedAt = DateTime.UtcNow;
        location.PureAccountStatus      = "FirstOrderPlaced";

        await _db.SaveChangesAsync();

        return Ok(new
        {
            id                     = location.Id,
            pureAccountStatus      = location.PureAccountStatus,
            pureFirstOrderPlacedAt = location.PureFirstOrderPlacedAt
        });
    }

    // ── POST /api/locations/{id}/pure/lapse ─────────────────────────
    // Admin: mark account as lapsed (60-day window missed)

    [HttpPost("{id:guid}/pure/lapse")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PureLapse(Guid id)
    {
        var location = await _db.Locations
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted);

        if (location is null) return NotFound();

        location.PureAccountStatus = "Lapsed";
        await _db.SaveChangesAsync();

        return Ok(new { id = location.Id, pureAccountStatus = "Lapsed" });
    }
}
