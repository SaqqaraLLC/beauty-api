using Beauty.Api.Data;
using Beauty.Api.Models.Gifts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("collab")]
[Authorize(Roles = "Artist")]
[EnableRateLimiting("general")]
public class CollabController : ControllerBase
{
    private readonly BeautyDbContext _db;
    public CollabController(BeautyDbContext db) => _db = db;
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ── Host starts a collab on their current live stream ─────────

    [HttpPost("start")]
    public async Task<IActionResult> Start()
    {
        // Find host's current live stream
        var profile = await _db.ArtistProfiles.FirstOrDefaultAsync(p => p.UserId == UserId);
        if (profile == null) return BadRequest(new { error = "Artist profile not found." });

        var stream = await _db.Streams
            .Where(s => s.ArtistProfileId == profile.ArtistProfileId && s.IsLive)
            .OrderByDescending(s => s.StreamId)
            .FirstOrDefaultAsync();
        if (stream == null) return BadRequest(new { error = "You must be live to start a collab." });

        // Check no active collab exists for this stream
        var existing = await _db.CollabSessions
            .FirstOrDefaultAsync(c => c.StreamId == stream.StreamId && c.Status == CollabStatus.Active);
        if (existing != null)
            return Ok(new { collabId = existing.Id, message = "Collab already active." });

        var collab = new CollabSession
        {
            StreamId         = stream.StreamId,
            HostArtistUserId = UserId,
        };
        // Host is automatically a joined participant
        collab.Participants.Add(new CollabParticipant
        {
            ArtistUserId = UserId,
            Status       = CollabInviteStatus.Joined,
            JoinedAt     = DateTime.UtcNow,
        });

        _db.CollabSessions.Add(collab);
        await _db.SaveChangesAsync();

        return Ok(new { collabId = collab.Id, streamId = stream.StreamId, acsRoomId = stream.AcsRoomId });
    }

    // ── Host invites another artist ────────────────────────────────

    [HttpPost("{id}/invite")]
    public async Task<IActionResult> Invite(long id, [FromBody] InviteRequest req)
    {
        var collab = await _db.CollabSessions
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (collab == null || collab.Status != CollabStatus.Active) return NotFound();
        if (collab.HostArtistUserId != UserId) return Forbid();

        // Verify invited artist exists
        var invitedProfile = await _db.ArtistProfiles
            .FirstOrDefaultAsync(p => p.UserId == req.ArtistUserId);
        if (invitedProfile == null) return NotFound(new { error = "Artist not found." });

        // No duplicate invites
        if (collab.Participants.Any(p => p.ArtistUserId == req.ArtistUserId))
            return Conflict(new { error = "Already invited." });

        collab.Participants.Add(new CollabParticipant { ArtistUserId = req.ArtistUserId });
        await _db.SaveChangesAsync();

        return Ok(new { message = $"Invite sent to {invitedProfile.FullName}." });
    }

    // ── Invited artist accepts and joins ───────────────────────────

    [HttpPost("{id}/join")]
    public async Task<IActionResult> Join(long id)
    {
        var collab = await _db.CollabSessions
            .Include(c => c.Participants)
            .Include(c => c.Stream)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (collab == null || collab.Status != CollabStatus.Active) return NotFound();

        var participant = collab.Participants.FirstOrDefault(p => p.ArtistUserId == UserId);
        if (participant == null) return Forbid();
        if (participant.Status == CollabInviteStatus.Joined)
            return Ok(new { acsRoomId = collab.Stream.AcsRoomId, streamId = collab.StreamId });

        participant.Status   = CollabInviteStatus.Joined;
        participant.JoinedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            collabId  = collab.Id,
            streamId  = collab.StreamId,
            acsRoomId = collab.Stream.AcsRoomId,
        });
    }

    // ── Artist declines an invite ──────────────────────────────────

    [HttpPost("{id}/decline")]
    public async Task<IActionResult> Decline(long id)
    {
        var participant = await _db.CollabParticipants
            .FirstOrDefaultAsync(p => p.CollabSessionId == id && p.ArtistUserId == UserId);
        if (participant == null) return NotFound();

        participant.Status = CollabInviteStatus.Declined;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Artist leaves the collab ───────────────────────────────────

    [HttpPost("{id}/leave")]
    public async Task<IActionResult> Leave(long id)
    {
        var collab = await _db.CollabSessions
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (collab == null) return NotFound();

        var participant = collab.Participants.FirstOrDefault(p => p.ArtistUserId == UserId);
        if (participant != null)
            participant.Status = CollabInviteStatus.Left;

        // If host leaves, end the session
        if (collab.HostArtistUserId == UserId)
        {
            collab.Status  = CollabStatus.Ended;
            collab.EndedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Get collab details ─────────────────────────────────────────

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(long id)
    {
        var collab = await _db.CollabSessions
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (collab == null) return NotFound();

        var userIds = collab.Participants
            .Where(p => p.Status == CollabInviteStatus.Joined)
            .Select(p => p.ArtistUserId)
            .ToList();

        var profiles = await _db.ArtistProfiles
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.FullName, p.ProfileImageUrl })
            .ToListAsync();

        return Ok(new
        {
            collab.Id,
            collab.StreamId,
            collab.HostArtistUserId,
            collab.Status,
            collab.CreatedAt,
            artists = profiles,
            participants = collab.Participants.Select(p => new
            {
                p.ArtistUserId,
                p.Status,
                p.InvitedAt,
                p.JoinedAt,
            }),
        });
    }

    // ── My pending collab invites ──────────────────────────────────

    [HttpGet("my-invites")]
    public async Task<IActionResult> MyInvites()
    {
        var invites = await _db.CollabParticipants
            .Include(p => p.CollabSession)
            .ThenInclude(c => c.Stream)
            .Where(p => p.ArtistUserId == UserId
                     && p.Status == CollabInviteStatus.Invited
                     && p.CollabSession.Status == CollabStatus.Active)
            .Select(p => new
            {
                collabId   = p.CollabSessionId,
                streamId   = p.CollabSession.StreamId,
                hostUserId = p.CollabSession.HostArtistUserId,
                invitedAt  = p.InvitedAt,
            })
            .ToListAsync();

        return Ok(invites);
    }

    // ── Get active collab for a stream (public) ────────────────────

    [HttpGet("by-stream/{streamId}")]
    [AllowAnonymous]
    public async Task<IActionResult> ByStream(int streamId)
    {
        var collab = await _db.CollabSessions
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.StreamId == streamId && c.Status == CollabStatus.Active);

        if (collab == null) return Ok(null);

        var userIds = collab.Participants
            .Where(p => p.Status == CollabInviteStatus.Joined)
            .Select(p => p.ArtistUserId)
            .ToList();

        var profiles = await _db.ArtistProfiles
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.FullName, p.ProfileImageUrl })
            .ToListAsync();

        return Ok(new { collab.Id, artists = profiles });
    }

    public record InviteRequest(string ArtistUserId);
}
