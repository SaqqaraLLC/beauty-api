using Beauty.Api.Data;
using Beauty.Api.Models.Enterprise;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Beauty.Api.Controllers;

/// <summary>
/// Public read-only API v1.
/// Authentication: JWT Bearer only (no cookie).
/// Rate limit: 30 requests/min per IP.
/// All endpoints are GET-only (no mutations).
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[EnableRateLimiting("public-api")]
public class PublicApiV1Controller : ControllerBase
{
    private readonly BeautyDbContext _db;

    public PublicApiV1Controller(BeautyDbContext db)
    {
        _db = db;
    }

    private static string[] ParseJson(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }

    // ── GET /api/v1/artists ─────────────────────────────────────────
    // List active, verified-first artists. Supports optional filtering.

    [HttpGet("artists")]
    public async Task<IActionResult> GetArtists(
        [FromQuery] string? specialty,
        [FromQuery] string? city,
        [FromQuery] string? state,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        var query = _db.ArtistProfiles
            .AsNoTracking()
            .Where(a => a.IsActive);

        if (!string.IsNullOrWhiteSpace(specialty))
            query = query.Where(a => a.Specialty != null &&
                a.Specialty.Contains(specialty));

        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(a => a.City != null &&
                a.City.Contains(city));

        if (!string.IsNullOrWhiteSpace(state))
            query = query.Where(a => a.State != null &&
                a.State.Contains(state));

        var total = await query.CountAsync();

        var artists = await query
            .OrderByDescending(a => a.IsVerified)
            .ThenByDescending(a => a.AverageRating)
            .ThenByDescending(a => a.BookingCount)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            page,
            pageSize,
            total,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
            data = artists.Select(a => new
            {
                artistId       = a.ArtistProfileId,
                fullName       = a.FullName,
                specialty      = a.Specialty,
                specialties    = ParseJson(a.SpecialtiesJson),
                bio            = a.Bio,
                city           = a.City,
                state          = a.State,
                country        = a.Country,
                profileImageUrl = a.ProfileImageUrl,
                isVerified     = a.IsVerified,
                averageRating  = a.AverageRating,
                reviewCount    = a.ReviewCount,
                bookingCount   = a.BookingCount,
                agencyName     = a.AgencyName,
                websiteUrl     = a.WebsiteUrl,
                hourlyRate     = a.HourlyRate,
                createdAt      = a.CreatedAt
            })
        });
    }

    // ── GET /api/v1/artists/{id} ────────────────────────────────────

    [HttpGet("artists/{id:int}")]
    public async Task<IActionResult> GetArtist(int id)
    {
        var a = await _db.ArtistProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ArtistProfileId == id && x.IsActive);

        if (a is null) return NotFound(new { message = "Artist not found." });

        return Ok(new
        {
            artistId       = a.ArtistProfileId,
            fullName       = a.FullName,
            specialty      = a.Specialty,
            specialties    = ParseJson(a.SpecialtiesJson),
            bio            = a.Bio,
            city           = a.City,
            state          = a.State,
            country        = a.Country,
            profileImageUrl = a.ProfileImageUrl,
            isVerified     = a.IsVerified,
            averageRating  = a.AverageRating,
            reviewCount    = a.ReviewCount,
            bookingCount   = a.BookingCount,
            agencyName     = a.AgencyName,
            websiteUrl     = a.WebsiteUrl,
            hourlyRate     = a.HourlyRate,
            createdAt      = a.CreatedAt
        });
    }

    // ── GET /api/v1/agents ──────────────────────────────────────────

    [HttpGet("agents")]
    public async Task<IActionResult> GetAgents(
        [FromQuery] string? specialty,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        var query = _db.AgentProfiles
            .AsNoTracking()
            .Where(a => a.Status != "Suspended");

        if (!string.IsNullOrWhiteSpace(specialty))
            query = query.Where(a =>
                a.SpecialtiesJson.Contains(specialty));

        var total = await query.CountAsync();

        var agents = await query
            .OrderByDescending(a => a.IsVerified)
            .ThenByDescending(a => a.AverageRating)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            page,
            pageSize,
            total,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
            data = agents.Select(a => new
            {
                agentId     = a.AgentProfileId,
                fullName    = a.FullName,
                agencyName  = a.AgencyName,
                bio         = a.Bio,
                isVerified  = a.IsVerified,
                averageRating = a.AverageRating,
                reviewCount = a.ReviewCount,
                rosterCount = a.RosterCount,
                specialties = ParseJson(a.SpecialtiesJson),
                websiteUrl  = a.WebsiteUrl,
                createdAt   = a.CreatedAt
            })
        });
    }

    // ── GET /api/v1/agents/{id} ─────────────────────────────────────

    [HttpGet("agents/{id:int}")]
    public async Task<IActionResult> GetAgent(int id)
    {
        var a = await _db.AgentProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.AgentProfileId == id && x.Status != "Suspended");

        if (a is null) return NotFound(new { message = "Agent not found." });

        return Ok(new
        {
            agentId     = a.AgentProfileId,
            fullName    = a.FullName,
            agencyName  = a.AgencyName,
            bio         = a.Bio,
            isVerified  = a.IsVerified,
            averageRating = a.AverageRating,
            reviewCount = a.ReviewCount,
            rosterCount = a.RosterCount,
            specialties = ParseJson(a.SpecialtiesJson),
            websiteUrl  = a.WebsiteUrl,
            createdAt   = a.CreatedAt
        });
    }

    // ── GET /api/v1/agents/{id}/roster ──────────────────────────────

    [HttpGet("agents/{id:int}/roster")]
    public async Task<IActionResult> GetAgentRoster(int id)
    {
        var agentExists = await _db.AgentProfiles
            .AsNoTracking()
            .AnyAsync(a => a.AgentProfileId == id && a.Status != "Suspended");

        if (!agentExists) return NotFound(new { message = "Agent not found." });

        var roster = await _db.AgentRosterEntries
            .AsNoTracking()
            .Include(r => r.ArtistProfile)
            .Where(r => r.AgentProfileId == id && r.Status == "Active")
            .OrderBy(r => r.LinkedAt)
            .ToListAsync();

        return Ok(roster.Select(r => new
        {
            artistId        = r.ArtistProfileId,
            artistName      = r.ArtistProfile.FullName,
            artistSpecialty = r.ArtistProfile.Specialty,
            artistCity      = r.ArtistProfile.City,
            artistState     = r.ArtistProfile.State,
            profileImageUrl = r.ArtistProfile.ProfileImageUrl,
            averageRating   = r.ArtistProfile.AverageRating,
            isVerified      = r.ArtistProfile.IsVerified,
            linkedAt        = r.LinkedAt
        }));
    }

    // ── GET /api/v1/artists/{id}/reviews ────────────────────────────

    [HttpGet("artists/{id:int}/reviews")]
    public async Task<IActionResult> GetArtistReviews(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        page     = Math.Max(1, page);

        var artistExists = await _db.ArtistProfiles
            .AsNoTracking()
            .AnyAsync(a => a.ArtistProfileId == id && a.IsActive);

        if (!artistExists) return NotFound(new { message = "Artist not found." });

        var query = _db.Reviews
            .AsNoTracking()
            .Where(r =>
                r.SubjectEntityType == "ArtistProfile" &&
                r.SubjectEntityId   == id &&
                r.Status            == "Published");

        var total = await query.CountAsync();

        var reviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            page,
            pageSize,
            total,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
            data = reviews.Select(r => new
            {
                reviewId      = r.ReviewId,
                reviewerName  = r.ReviewerName,
                reviewerRole  = r.ReviewerRole,
                rating        = r.Rating,
                title         = r.Title,
                body          = r.Body,
                createdAt     = r.CreatedAt
            })
        });
    }
}
