using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.Enterprise;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/artists")]
public class ArtistsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public ArtistsController(BeautyDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    // ── DTOs ────────────────────────────────────────────────────────

    private static object MapArtistDto(ArtistProfile a) => new
    {
        artistId = a.ArtistProfileId,
        fullName = a.FullName,
        specialty = a.Specialty,
        bio = a.Bio,
        city = a.City,
        state = a.State,
        profileImageUrl = a.ProfileImageUrl,
        isVerified = a.IsVerified,
        averageRating = a.AverageRating,
        reviewCount = a.ReviewCount,
        bookingCount = a.BookingCount,
        agencyName = a.AgencyName,
        websiteUrl = a.WebsiteUrl,
        specialties = ParseJson(a.SpecialtiesJson),
        hourlyRate = a.HourlyRate
    };

    private static string[] ParseJson(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }

    // ── GET /api/artists ────────────────────────────────────────────

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? specialty,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var query = _db.ArtistProfiles
            .AsNoTracking()
            .Where(a => a.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(a =>
                a.FullName.Contains(search) ||
                (a.Bio != null && a.Bio.Contains(search)) ||
                (a.City != null && a.City.Contains(search)));

        if (!string.IsNullOrWhiteSpace(specialty))
            query = query.Where(a =>
                a.Specialty != null && a.Specialty.Contains(specialty));

        var total = await query.CountAsync();
        var artists = await query
            .OrderByDescending(a => a.IsVerified)
            .ThenByDescending(a => a.AverageRating)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            artists = artists.Select(MapArtistDto),
            total
        });
    }

    // ── GET /api/artists/{id} ───────────────────────────────────────

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(int id)
    {
        var artist = await _db.ArtistProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ArtistProfileId == id && a.IsActive);

        if (artist is null) return NotFound();

        return Ok(MapArtistDto(artist));
    }

    // ── GET /api/artists/{id}/availability ─────────────────────────

    [HttpGet("{id:int}/availability")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAvailability(
        int id,
        [FromQuery] string? from,
        [FromQuery] string? to)
    {
        var artistExists = await _db.ArtistProfiles
            .AsNoTracking()
            .AnyAsync(a => a.ArtistProfileId == id && a.IsActive);
        if (!artistExists) return NotFound();

        var query = _db.AvailabilityBlocks
            .AsNoTracking()
            .Where(b => b.ArtistProfileId == id);

        if (DateOnly.TryParse(from, out var fromDate))
            query = query.Where(b => b.Date >= fromDate);

        if (DateOnly.TryParse(to, out var toDate))
            query = query.Where(b => b.Date <= toDate);

        var blocks = await query.OrderBy(b => b.Date).ToListAsync();

        return Ok(blocks.Select(b => new
        {
            blockId = b.BlockId,
            artistId = b.ArtistProfileId,
            date = b.Date.ToString("yyyy-MM-dd"),
            startTime = b.StartTime?.ToString("HH:mm"),
            endTime = b.EndTime?.ToString("HH:mm"),
            isAvailable = b.IsAvailable,
            note = b.Note
        }));
    }

    // ── POST /api/artists/{id}/availability ─────────────────────────

    public record AvailabilityBlockInput(string Date, bool IsAvailable, string? Note, string? StartTime, string? EndTime);
    public record UpsertAvailabilityReq(List<AvailabilityBlockInput> Blocks);

    [HttpPost("{id:int}/availability")]
    [Authorize]
    public async Task<IActionResult> UpsertAvailability(int id, [FromBody] UpsertAvailabilityReq req)
    {
        var artist = await _db.ArtistProfiles
            .FirstOrDefaultAsync(a => a.ArtistProfileId == id);
        if (artist is null) return NotFound();

        // Verify caller owns this profile
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (artist.UserId != userId && !User.IsInRole("Admin"))
            return Forbid();

        foreach (var input in req.Blocks)
        {
            if (!DateOnly.TryParse(input.Date, out var date)) continue;

            var existing = await _db.AvailabilityBlocks
                .FirstOrDefaultAsync(b => b.ArtistProfileId == id && b.Date == date);

            if (existing is null)
            {
                existing = new AvailabilityBlock
                {
                    ArtistProfileId = id,
                    Date = date
                };
                _db.AvailabilityBlocks.Add(existing);
            }

            existing.IsAvailable = input.IsAvailable;
            existing.Note = input.Note;
            existing.StartTime = TimeOnly.TryParse(input.StartTime, out var st) ? st : null;
            existing.EndTime = TimeOnly.TryParse(input.EndTime, out var et) ? et : null;
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Availability updated." });
    }

    // ── GET /api/artists/{id}/stats ─────────────────────────────────

    [HttpGet("{id:int}/stats")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStats(int id)
    {
        var artist = await _db.ArtistProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ArtistProfileId == id);
        if (artist is null) return NotFound();

        // Pull booking counts from enterprise bookings (by ArtistProfileId via ArtistProfile.UserId)
        // The legacy Booking table uses ArtistId (long) — we aggregate from ArtistProfile fields for now
        // and compute earnings from the legacy bookings table if linked
        var totalBookings = artist.BookingCount;
        var averageRating = artist.AverageRating;
        var totalReviews = artist.ReviewCount;

        // Count reviews by status from review table for accuracy
        var reviewStats = await _db.Reviews
            .AsNoTracking()
            .Where(r => r.SubjectEntityType == "Artist" && r.SubjectEntityId == id && r.Status == "Published")
            .GroupBy(r => 1)
            .Select(g => new { Count = g.Count(), Avg = g.Average(r => (double)r.Rating) })
            .FirstOrDefaultAsync();

        if (reviewStats != null)
        {
            totalReviews = reviewStats.Count;
            averageRating = Math.Round(reviewStats.Avg, 2);
        }

        return Ok(new
        {
            artistId = id,
            totalBookings,
            completedBookings = 0,  // Placeholder: wire to legacy Booking table when ArtistProfileId is linked
            pendingBookings = 0,
            averageRating,
            totalReviews,
            totalEarnings = (decimal)0
        });
    }

    // ── GET /api/artists/{id}/bookings ──────────────────────────────

    [HttpGet("{id:int}/bookings")]
    [Authorize]
    public async Task<IActionResult> GetBookings(int id)
    {
        var artist = await _db.ArtistProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ArtistProfileId == id);
        if (artist is null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (artist.UserId != userId && !User.IsInRole("Admin") && !User.IsInRole("Staff"))
            return Forbid();

        // Return empty for now; bookings tied to legacy ArtistId (long) need a separate mapping step
        return Ok(new { artistId = id, bookings = Array.Empty<object>() });
    }

    // ── POST /api/artists ───────────────────────────────────────────

    public record CreateArtistReq(
        string FullName,
        string? Specialty,
        string? Bio,
        string? City,
        string? State,
        string? Country,
        string? ProfileImageUrl,
        string? AgencyName,
        string? WebsiteUrl,
        List<string>? Specialties,
        decimal? HourlyRate);

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateArtistReq req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Prevent duplicate profile
        var exists = await _db.ArtistProfiles.AnyAsync(a => a.UserId == userId);
        if (exists) return Conflict(new { message = "Artist profile already exists for this user." });

        var profile = new ArtistProfile
        {
            UserId = userId,
            FullName = req.FullName,
            Specialty = req.Specialty,
            Bio = req.Bio,
            City = req.City,
            State = req.State,
            Country = req.Country,
            ProfileImageUrl = req.ProfileImageUrl,
            AgencyName = req.AgencyName,
            WebsiteUrl = req.WebsiteUrl,
            SpecialtiesJson = JsonSerializer.Serialize(req.Specialties ?? []),
            HourlyRate = req.HourlyRate,
            CreatedAt = DateTime.UtcNow
        };

        _db.ArtistProfiles.Add(profile);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = profile.ArtistProfileId }, MapArtistDto(profile));
    }

    // ── PUT /api/artists/{id} ───────────────────────────────────────

    public record UpdateArtistReq(
        string? FullName,
        string? Specialty,
        string? Bio,
        string? City,
        string? State,
        string? Country,
        string? ProfileImageUrl,
        string? AgencyName,
        string? WebsiteUrl,
        List<string>? Specialties,
        decimal? HourlyRate);

    [HttpPut("{id:int}")]
    [Authorize]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateArtistReq req)
    {
        var artist = await _db.ArtistProfiles.FirstOrDefaultAsync(a => a.ArtistProfileId == id);
        if (artist is null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (artist.UserId != userId && !User.IsInRole("Admin"))
            return Forbid();

        if (req.FullName is not null) artist.FullName = req.FullName;
        if (req.Specialty is not null) artist.Specialty = req.Specialty;
        if (req.Bio is not null) artist.Bio = req.Bio;
        if (req.City is not null) artist.City = req.City;
        if (req.State is not null) artist.State = req.State;
        if (req.Country is not null) artist.Country = req.Country;
        if (req.ProfileImageUrl is not null) artist.ProfileImageUrl = req.ProfileImageUrl;
        if (req.AgencyName is not null) artist.AgencyName = req.AgencyName;
        if (req.WebsiteUrl is not null) artist.WebsiteUrl = req.WebsiteUrl;
        if (req.Specialties is not null) artist.SpecialtiesJson = JsonSerializer.Serialize(req.Specialties);
        if (req.HourlyRate is not null) artist.HourlyRate = req.HourlyRate;

        await _db.SaveChangesAsync();
        return Ok(MapArtistDto(artist));
    }
}
