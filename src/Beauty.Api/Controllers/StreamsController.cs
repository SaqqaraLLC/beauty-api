using Beauty.Api.Data;
using Beauty.Api.Domain.Streams;
using Beauty.Api.Models;
using Beauty.Api.Models.Streams;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/artists")]
[AllowAnonymous] // Artist profiles are PUBLIC
public class ArtistProfileController : ControllerBase
{
    private readonly IArtistStreamService _streamService;
    private readonly BeautyDbContext _db;
    private readonly ILogger<ArtistProfileController> _logger;

    public ArtistProfileController(
        IArtistStreamService streamService,
        BeautyDbContext db,
        ILogger<ArtistProfileController> logger)
    {
        _streamService = streamService;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Get all active artists (directory listing)
    /// Supports search and sorting by name, streams, or views
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllArtists(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        [FromQuery] string? search = null,
        [FromQuery] string sortBy = "name")
    {
        var artists = await _streamService.GetAllArtistsAsync(page, pageSize, search, sortBy);
        return Ok(artists);
    }

    /// <summary>
    /// Get public artist profile (viewable by all: clients, artists, employees, locations)
    /// </summary>
    [HttpGet("{artistId:long}/profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetArtistProfile(long artistId)
    {
        var profile = await _streamService.GetArtistProfileAsync(artistId);
        if (profile == null)
            return NotFound(new { error = "Artist not found" });

        return Ok(profile);
    }

    /// <summary>
    /// Get artist's streams (public, paginated)
    /// </summary>
    [HttpGet("{artistId:long}/streams")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetArtistStreams(
        long artistId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var streams = await _streamService.GetArtistStreamsAsync(artistId, page, pageSize);
        return Ok(new
        {
            artistId,
            page,
            pageSize,
            streams
        });
    }

    /// <summary>
    /// Artist dashboard stats — booking counts, pending approvals
    /// </summary>
    [HttpGet("{artistId:long}/stats")]
    public async Task<IActionResult> GetArtistStats(long artistId)
    {
        var now = DateTime.UtcNow;

        var totalBookings    = await _db.Bookings.CountAsync(b => b.ArtistId == artistId);
        var upcomingBookings = await _db.Bookings.CountAsync(b => b.ArtistId == artistId
            && b.StartsAt > now
            && b.Status == BookingStatus.FullyApproved);
        var pendingApprovals = await _db.Bookings.CountAsync(b => b.ArtistId == artistId
            && b.ArtistApproval == ApprovalDecision.Pending);

        return Ok(new
        {
            totalBookings,
            upcomingBookings,
            pendingApprovals,
            averageRating = 0.0,
            reviewCount   = 0,
            isVerified    = false,
        });
    }

    /// <summary>
    /// Artist's own bookings — enriched with customer, service, location names
    /// </summary>
    [HttpGet("{artistId:long}/bookings")]
    [Authorize]
    public async Task<IActionResult> GetArtistBookings(long artistId, [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        var bookings = await _db.Bookings
            .AsNoTracking()
            .Where(b => b.ArtistId == artistId)
            .OrderByDescending(b => b.StartsAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var customerIds = bookings.Select(b => b.CustomerId).Distinct().ToList();
        var serviceIds  = bookings.Select(b => b.ServiceId).Distinct().ToList();
        var locationIds = bookings.Select(b => b.LocationId).Distinct().ToList();

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.FullName);

        var services = await _db.Services.AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        var locations = await _db.Locations.AsNoTracking()
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name);

        var result = bookings.Select(b => new
        {
            b.BookingId,
            b.CustomerId,
            b.ArtistId,
            b.ServiceId,
            b.LocationId,
            b.StartsAt,
            b.EndsAt,
            Status          = b.Status.ToString(),
            ArtistApproval  = b.ArtistApproval.ToString(),
            LocationApproval = b.LocationApproval.ToString(),
            b.RejectionReason,
            CustomerName  = customers.GetValueOrDefault(b.CustomerId),
            ServiceName   = services.GetValueOrDefault(b.ServiceId),
            LocationName  = locations.GetValueOrDefault(b.LocationId),
        });

        return Ok(result);
    }

    // -----------------------------------------------
    // Travel preferences
    // -----------------------------------------------

    public record TravelRequest(
        bool? TravelNationwide,   // true = anywhere in the US; false/null = use MaxMiles
        int?  MaxMiles);          // 1–5000; required when TravelNationwide is false/null

    /// <summary>
    /// Get artist's travel preferences (public)
    /// </summary>
    [HttpGet("{artistId:long}/travel")]
    public async Task<IActionResult> GetTravel(long artistId)
    {
        var artist = await _db.Artists.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ArtistId == artistId);

        if (artist == null)
            return NotFound(new { error = "Artist not found" });

        return Ok(new
        {
            travelNationwide = artist.TravelNationwide,
            travelMaxMiles   = artist.TravelMaxMiles
        });
    }

    /// <summary>
    /// Update artist's travel preferences (artist only)
    /// </summary>
    [HttpPut("{artistId:long}/travel")]
    [Authorize(Roles = "Artist")]
    public async Task<IActionResult> UpdateTravel(long artistId, [FromBody] TravelRequest request)
    {
        if (request.TravelNationwide != true && (request.MaxMiles == null || request.MaxMiles < 1 || request.MaxMiles > 5000))
            return BadRequest(new { error = "Provide MaxMiles between 1 and 5000, or set TravelNationwide to true." });

        var artist = await _db.Artists.FirstOrDefaultAsync(a => a.ArtistId == artistId);
        if (artist == null)
            return NotFound(new { error = "Artist not found" });

        artist.TravelNationwide = request.TravelNationwide ?? false;
        artist.TravelMaxMiles   = request.TravelNationwide == true ? null : request.MaxMiles;

        await _db.SaveChangesAsync();

        _logger.LogInformation("[TRAVEL] Artist {ArtistId} updated travel: nationwide={N} maxMiles={M}",
            artistId, artist.TravelNationwide, artist.TravelMaxMiles);

        return Ok(new
        {
            travelNationwide = artist.TravelNationwide,
            travelMaxMiles   = artist.TravelMaxMiles
        });
    }

    /// <summary>
    /// Get stream details (public view)
    /// Excludes flagged/deleted streams unless user is admin
    /// </summary>
    [HttpGet("streams/{streamId:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStream(long streamId)
    {
        var stream = await _streamService.GetStreamAsync(streamId);
        if (stream == null)
            return NotFound();

        // Record view (public tracking for analytics)
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        
        _ = _streamService.RecordViewAsync(streamId, userId, ipAddress);

        return Ok(stream);
    }
}

/// <summary>
/// Streams management controller (artist operations)
/// </summary>
[ApiController]
[Route("api/streams")]
[Authorize]
public class StreamsController : ControllerBase
{
    private readonly IArtistStreamService _streamService;
    private readonly IStreamDangerDetectionService _dangerDetection;
    private readonly ILogger<StreamsController> _logger;

    public StreamsController(
        IArtistStreamService streamService,
        IStreamDangerDetectionService dangerDetection,
        ILogger<StreamsController> logger)
    {
        _streamService = streamService;
        _dangerDetection = dangerDetection;
        _logger = logger;
    }

    public record CreateStreamRequest(
        long ArtistId,
        string Title,
        string? Description,
        string? Tags);

    public record UpdateStreamRequest(
        string Title,
        string? Description);

    /// <summary>
    /// Public stream browse — live streams first, then most recent recorded
    /// </summary>
    [HttpGet("browse")]
    [AllowAnonymous]
    public async Task<IActionResult> BrowseStreams([FromQuery] int page = 1, [FromQuery] int pageSize = 24)
    {
        var streams = await _streamService.BrowseStreamsAsync(page, pageSize);
        return Ok(streams);
    }

    /// <summary>
    /// Create a new stream (artist only)
    /// </summary>
    [HttpPost("create")]
    [Authorize(Roles = "Artist")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateStream([FromBody] CreateStreamRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var stream = await _streamService.CreateStreamAsync(
            request.ArtistId,
            request.Title,
            request.Description,
            request.Tags);

        return Created($"/api/artists/streams/{stream.StreamId}", new
        {
            stream.StreamId,
            stream.Title,
            stream.Status,
            stream.CreatedAt
        });
    }

    /// <summary>
    /// Update stream metadata (artist only)
    /// </summary>
    [HttpPut("{streamId:long}")]
    [Authorize(Roles = "Artist")]
    public async Task<IActionResult> UpdateStream(
        long streamId,
        [FromBody] UpdateStreamRequest request,
        [FromQuery] long artistId)
    {
        try
        {
            var stream = await _streamService.UpdateStreamAsync(streamId, request.Title, request.Description, artistId);
            return Ok(new { stream.StreamId, stream.Title, stream.Description });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// End a stream (artist only)
    /// </summary>
    [HttpPost("{streamId:long}/end")]
    [Authorize(Roles = "Artist")]
    public async Task<IActionResult> EndStream(long streamId, [FromQuery] long artistId)
    {
        try
        {
            var stream = await _streamService.EndStreamAsync(streamId, artistId);
            return Ok(new
            {
                stream.StreamId,
                stream.Status,
                stream.EndedAt,
                stream.DurationSeconds
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Analyze stream content for danger (admin tool)
    /// </summary>
    [HttpPost("{streamId:long}/analyze")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AnalyzeStream(long streamId, [FromBody] AnalyzeRequest request)
    {
        var flags = await _dangerDetection.AnalyzeStreamAsync(streamId, request.Content);
        return Ok(new
        {
            streamId,
            flagCount = flags.Count,
            flags = flags.Select(f => new
            {
                f.DangerType,
                f.ConfidenceScore,
                f.DetectionReason
            })
        });
    }

    public record AnalyzeRequest(string Content);
}

/// <summary>
/// Moderation controller (admin operations)
/// </summary>
[ApiController]
[Route("api/moderation")]
[Authorize(Roles = "Admin")]
public class ModerationController : ControllerBase
{
    private readonly IStreamDangerDetectionService _dangerDetection;
    private readonly IArtistStreamService _streamService;
    private readonly ILogger<ModerationController> _logger;

    public ModerationController(
        IStreamDangerDetectionService dangerDetection,
        IArtistStreamService streamService,
        ILogger<ModerationController> logger)
    {
        _dangerDetection = dangerDetection;
        _streamService = streamService;
        _logger = logger;
    }

    /// <summary>
    /// Get flagged streams pending review
    /// </summary>
    [HttpGet("streams/flagged")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFlaggedStreams([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var streams = await _streamService.GetFlaggedStreamsAsync(page, pageSize);
        return Ok(new
        {
            page,
            pageSize,
            totalCount = streams.Count,
            streams
        });
    }

    /// <summary>
    /// Get danger flags pending review
    /// </summary>
    [HttpGet("flags/pending")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingFlags([FromQuery] int limit = 50)
    {
        var flags = await _dangerDetection.GetPendingFlagsAsync(limit);
        return Ok(new
        {
            count = flags.Count,
            flags = flags.Select(f => new
            {
                f.FlagId,
                f.StreamId,
                f.DangerType,
                f.ConfidenceScore,
                f.DetectionReason,
                f.FlaggedAt
            })
        });
    }

    public record ReviewFlagRequest(
        StreamReviewDecision Decision,
        string ReviewNotes);

    /// <summary>
    /// Review a danger flag and take action
    /// </summary>
    [HttpPost("flags/{flagId:long}/review")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReviewFlag(
        long flagId,
        [FromBody] ReviewFlagRequest request)
    {
        try
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? throw new InvalidOperationException("User not authenticated");

            await _dangerDetection.ReviewFlagAsync(flagId, request.Decision, request.ReviewNotes, userId);

            return Ok(new
            {
                flagId,
                decision = request.Decision.ToString(),
                message = "Flag reviewed and action taken"
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
