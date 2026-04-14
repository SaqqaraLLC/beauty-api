using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.Enterprise;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace Beauty.Api.Controllers;

[EnableRateLimiting("general")]
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

    // ── POST /api/agents/representation-requests ─────────────────────
    // Artist or Agent initiates a representation request

    public record RepresentationRequestReq(int AgentId, int ArtistId, string? Message);

    [HttpPost("representation-requests")]
    [Authorize]
    public async Task<IActionResult> CreateRepresentationRequest(
        [FromBody] RepresentationRequestReq req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var agent = await _db.AgentProfiles.FindAsync(req.AgentId);
        if (agent is null) return BadRequest(new { message = "Agent not found." });

        var artist = await _db.ArtistProfiles.FindAsync(req.ArtistId);
        if (artist is null) return BadRequest(new { message = "Artist not found." });

        // Caller must be the agent or the artist
        var isAgent  = agent.UserId == userId;
        var isArtist = artist.UserId == userId;
        var isAdmin  = User.IsInRole("Admin");
        if (!isAgent && !isArtist && !isAdmin)
            return Forbid();

        // Block duplicate pending requests
        var duplicate = await _db.RepresentationRequests.AnyAsync(r =>
            r.AgentProfileId  == req.AgentId &&
            r.ArtistProfileId == req.ArtistId &&
            r.Status == RepresentationRequestStatus.Pending);
        if (duplicate)
            return Conflict(new { message = "A pending request already exists for this agent-artist pair." });

        // Block if already on roster
        var onRoster = await _db.AgentRosterEntries.AnyAsync(r =>
            r.AgentProfileId  == req.AgentId &&
            r.ArtistProfileId == req.ArtistId &&
            r.Status == "Active");
        if (onRoster)
            return Conflict(new { message = "Artist is already on this agent's roster." });

        var request = new RepresentationRequest
        {
            AgentProfileId    = req.AgentId,
            ArtistProfileId   = req.ArtistId,
            RequestedByUserId = userId,
            Message           = req.Message,
            Status            = RepresentationRequestStatus.Pending,
            CreatedAt         = DateTime.UtcNow
        };

        _db.RepresentationRequests.Add(request);
        await _db.SaveChangesAsync();

        return Ok(new { requestId = request.RepresentationRequestId, status = "Pending" });
    }

    // ── GET /api/agents/representation-requests ──────────────────────
    // Returns requests relevant to the calling user (as agent or artist)

    [HttpGet("representation-requests")]
    [Authorize]
    public async Task<IActionResult> GetRepresentationRequests()
    {
        var userId  = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var isAdmin = User.IsInRole("Admin");

        var agentProfile  = await _db.AgentProfiles.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId);
        var artistProfile = await _db.ArtistProfiles.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId);

        IQueryable<RepresentationRequest> query = _db.RepresentationRequests
            .AsNoTracking()
            .Include(r => r.AgentProfile)
            .Include(r => r.ArtistProfile);

        if (!isAdmin)
        {
            if (agentProfile is null && artistProfile is null)
                return Ok(Array.Empty<object>());

            query = query.Where(r =>
                (agentProfile  != null && r.AgentProfileId  == agentProfile.AgentProfileId) ||
                (artistProfile != null && r.ArtistProfileId == artistProfile.ArtistProfileId));
        }

        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(requests.Select(r => new
        {
            requestId    = r.RepresentationRequestId,
            agentId      = r.AgentProfileId,
            agentName    = r.AgentProfile.FullName,
            agencyName   = r.AgentProfile.AgencyName,
            artistId     = r.ArtistProfileId,
            artistName   = r.ArtistProfile.FullName,
            message      = r.Message,
            status       = r.Status.ToString(),
            createdAt    = r.CreatedAt,
            respondedAt  = r.RespondedAt,
            responseNote = r.ResponseNote
        }));
    }

    // ── POST /api/agents/representation-requests/{id}/accept ─────────

    [HttpPost("representation-requests/{id:int}/accept")]
    [Authorize]
    public async Task<IActionResult> AcceptRepresentationRequest(
        int id, [FromBody] RespondReq? body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var request = await _db.RepresentationRequests
            .Include(r => r.AgentProfile)
            .Include(r => r.ArtistProfile)
            .FirstOrDefaultAsync(r => r.RepresentationRequestId == id);

        if (request is null) return NotFound();
        if (request.Status != RepresentationRequestStatus.Pending)
            return BadRequest(new { message = "Request is no longer pending." });

        // Acceptance must come from the other party (not the one who created the request)
        var isAgent  = request.AgentProfile.UserId  == userId;
        var isArtist = request.ArtistProfile.UserId == userId;
        var isAdmin  = User.IsInRole("Admin");

        if (!isAgent && !isArtist && !isAdmin)
            return Forbid();

        // Prevent the originator from accepting their own request (unless admin)
        if (!isAdmin && request.RequestedByUserId == userId && (isAgent || isArtist))
            return BadRequest(new { message = "The requesting party cannot accept their own request." });

        request.Status       = RepresentationRequestStatus.Accepted;
        request.RespondedAt  = DateTime.UtcNow;
        request.ResponseNote = body?.Note;

        // Create roster entry if not already present
        var alreadyRostered = await _db.AgentRosterEntries.AnyAsync(r =>
            r.AgentProfileId  == request.AgentProfileId &&
            r.ArtistProfileId == request.ArtistProfileId);

        if (!alreadyRostered)
        {
            _db.AgentRosterEntries.Add(new AgentRosterEntry
            {
                AgentProfileId  = request.AgentProfileId,
                ArtistProfileId = request.ArtistProfileId,
                Status          = "Active",
                LinkedAt        = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        return Ok(new { requestId = id, status = "Accepted" });
    }

    // ── POST /api/agents/representation-requests/{id}/decline ────────

    [HttpPost("representation-requests/{id:int}/decline")]
    [Authorize]
    public async Task<IActionResult> DeclineRepresentationRequest(
        int id, [FromBody] RespondReq? body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var request = await _db.RepresentationRequests
            .Include(r => r.AgentProfile)
            .Include(r => r.ArtistProfile)
            .FirstOrDefaultAsync(r => r.RepresentationRequestId == id);

        if (request is null) return NotFound();
        if (request.Status != RepresentationRequestStatus.Pending)
            return BadRequest(new { message = "Request is no longer pending." });

        var isAgent  = request.AgentProfile.UserId  == userId;
        var isArtist = request.ArtistProfile.UserId == userId;
        var isAdmin  = User.IsInRole("Admin");

        if (!isAgent && !isArtist && !isAdmin)
            return Forbid();

        request.Status       = RepresentationRequestStatus.Declined;
        request.RespondedAt  = DateTime.UtcNow;
        request.ResponseNote = body?.Note;

        await _db.SaveChangesAsync();

        return Ok(new { requestId = id, status = "Declined" });
    }

    // ── DTOs ──────────────────────────────────────────────────────────
    public record RespondReq(string? Note);
}
