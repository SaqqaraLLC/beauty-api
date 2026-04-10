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
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public AgentsController(BeautyDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    // ── DTOs ────────────────────────────────────────────────────────

    private static string[] ParseJson(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }

    private static object MapAgentDto(AgentProfile a) => new
    {
        agentId = a.AgentProfileId,
        fullName = a.FullName,
        agencyName = a.AgencyName,
        bio = a.Bio,
        isVerified = a.IsVerified,
        averageRating = a.AverageRating,
        reviewCount = a.ReviewCount,
        rosterCount = a.RosterCount,
        status = a.Status,
        createdAt = a.CreatedAt,
        specialties = ParseJson(a.SpecialtiesJson),
        websiteUrl = a.WebsiteUrl
    };

    // ── GET /api/agents ─────────────────────────────────────────────

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        var agents = await _db.AgentProfiles
            .AsNoTracking()
            .Where(a => a.Status != "Suspended")
            .OrderByDescending(a => a.IsVerified)
            .ThenByDescending(a => a.AverageRating)
            .ToListAsync();

        return Ok(agents.Select(MapAgentDto));
    }

    // ── GET /api/agents/{id} ────────────────────────────────────────

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(int id)
    {
        var agent = await _db.AgentProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AgentProfileId == id);

        if (agent is null) return NotFound();

        return Ok(MapAgentDto(agent));
    }

    // ── GET /api/agents/{id}/roster ─────────────────────────────────

    [HttpGet("{id:int}/roster")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRoster(int id)
    {
        var agentExists = await _db.AgentProfiles
            .AsNoTracking()
            .AnyAsync(a => a.AgentProfileId == id);
        if (!agentExists) return NotFound();

        var roster = await _db.AgentRosterEntries
            .AsNoTracking()
            .Include(r => r.ArtistProfile)
            .Where(r => r.AgentProfileId == id && r.Status == "Active")
            .OrderBy(r => r.LinkedAt)
            .ToListAsync();

        return Ok(roster.Select(r => new
        {
            rosterId = r.RosterId,
            agentId = r.AgentProfileId,
            artistId = r.ArtistProfileId,
            artistName = r.ArtistProfile.FullName,
            artistSpecialty = r.ArtistProfile.Specialty,
            artistProfileImageUrl = r.ArtistProfile.ProfileImageUrl,
            artistRating = r.ArtistProfile.AverageRating,
            status = r.Status,
            linkedAt = r.LinkedAt
        }));
    }

    // ── POST /api/agents ────────────────────────────────────────────

    public record CreateAgentReq(
        string FullName,
        string? AgencyName,
        string? Bio,
        List<string>? Specialties,
        string? WebsiteUrl);

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateAgentReq req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var exists = await _db.AgentProfiles.AnyAsync(a => a.UserId == userId);
        if (exists) return Conflict(new { message = "Agent profile already exists for this user." });

        var profile = new AgentProfile
        {
            UserId = userId,
            FullName = req.FullName,
            AgencyName = req.AgencyName,
            Bio = req.Bio,
            SpecialtiesJson = JsonSerializer.Serialize(req.Specialties ?? []),
            WebsiteUrl = req.WebsiteUrl,
            CreatedAt = DateTime.UtcNow
        };

        _db.AgentProfiles.Add(profile);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = profile.AgentProfileId }, MapAgentDto(profile));
    }
}
