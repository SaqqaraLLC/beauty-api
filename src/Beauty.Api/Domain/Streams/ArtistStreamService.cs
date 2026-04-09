using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.Streams;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Beauty.Api.Domain.Streams;

/// <summary>
/// Service for managing artist streams (public views, creating, etc).
/// </summary>
public interface IArtistStreamService
{
    /// <summary>Get all active artists (directory listing)</summary>
    Task<List<ArtistDto>> GetAllArtistsAsync(int page = 1, int pageSize = 12, string? search = null, string sortBy = "name");

    /// <summary>Get public artist profile (viewable by all)</summary>
    Task<ArtistProfileDto?> GetArtistProfileAsync(long artistId);

    /// <summary>Get artist's streams (sorted by recent)</summary>
    Task<List<StreamDto>> GetArtistStreamsAsync(long artistId, int page = 1, int pageSize = 20);

    /// <summary>Get single stream details</summary>
    Task<StreamDto?> GetStreamAsync(long streamId);

    /// <summary>Create new stream</summary>
    Task<ArtistStream> CreateStreamAsync(long artistId, string title, string? description, string? tags);

    /// <summary>Update stream (artist only)</summary>
    Task<ArtistStream> UpdateStreamAsync(long streamId, string title, string? description, long artistId);

    /// <summary>End/archive a stream</summary>
    Task<ArtistStream> EndStreamAsync(long streamId, long artistId);

    /// <summary>Record stream view</summary>
    Task RecordViewAsync(long streamId, string? userId, string? ipAddress);

    /// <summary>Get streams flagged for review (admin only)</summary>
    Task<List<StreamDto>> GetFlaggedStreamsAsync(int page = 1, int pageSize = 20);

    /// <summary>Public browse — live first, then most recent recorded, excludes flagged/deleted/hidden</summary>
    Task<List<StreamBrowseDto>> BrowseStreamsAsync(int page = 1, int pageSize = 24);
}

public sealed class ArtistStreamService : IArtistStreamService
{
    private readonly BeautyDbContext _db;
    private readonly IStreamDangerDetectionService _dangerDetection;
    private readonly ILogger<ArtistStreamService> _logger;

    public ArtistStreamService(
        BeautyDbContext db,
        IStreamDangerDetectionService dangerDetection,
        ILogger<ArtistStreamService> logger)
    {
        _db = db;
        _dangerDetection = dangerDetection;
        _logger = logger;
    }

    public async Task<List<ArtistDto>> GetAllArtistsAsync(int page = 1, int pageSize = 12, string? search = null, string sortBy = "name")
    {
        var query = _db.Artists
            .AsNoTracking()
            .Where(a => a.Active);

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(a => a.FullName.ToLower().Contains(searchLower) || a.Specialty.ToLower().Contains(searchLower));
        }

        // Apply sorting
        query = sortBy.ToLower() switch
        {
            "streams" => query.OrderByDescending(a => _db.ArtistStreams.Count(s => s.ArtistId == a.ArtistId && s.Status != StreamStatus.Deleted)),
            "views" => query.OrderByDescending(a => _db.ArtistStreams.Where(s => s.ArtistId == a.ArtistId && s.Status != StreamStatus.Deleted).Sum(s => s.ViewCount)),
            _ => query.OrderBy(a => a.FullName) // default to name
        };

        var artists = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Map to DTOs with stream/view counts
        var result = new List<ArtistDto>();
        foreach (var artist in artists)
        {
            var totalStreams = await _db.ArtistStreams.CountAsync(s => s.ArtistId == artist.ArtistId && s.Status != StreamStatus.Deleted);
            var totalViews = await _db.ArtistStreams
                .Where(s => s.ArtistId == artist.ArtistId && s.Status != StreamStatus.Deleted)
                .SumAsync(s => s.ViewCount);

            result.Add(new ArtistDto(
                artist.ArtistId,
                artist.FullName,
                artist.Specialty,
                artist.Bio,
                artist.ProfileImageUrl,
                totalStreams,
                totalViews));
        }

        return result;
    }

    public async Task<ArtistProfileDto?> GetArtistProfileAsync(long artistId)
    {
        var artist = await _db.Artists
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ArtistId == artistId);

        if (artist == null)
            return null;

        // Get artist's streams
        var streams = await _db.ArtistStreams
            .AsNoTracking()
            .Where(s => s.ArtistId == artistId && s.Status != StreamStatus.Deleted)
            .OrderByDescending(s => s.CreatedAt)
            .Take(5)
            .Select(s => new StreamDto(
                s.StreamId,
                s.ArtistId,
                s.Title,
                s.Description,
                s.Status.ToString(),
                s.StreamUrl,
                s.ThumbnailUrl,
                s.ViewCount,
                s.CreatedAt,
                s.EndedAt,
                s.DurationSeconds,
                s.Tags,
                s.IsFlaggedForReview,
                s.FlagReason
            ))
            .ToListAsync();

        var totalStreams = await _db.ArtistStreams
            .CountAsync(s => s.ArtistId == artistId && s.Status != StreamStatus.Deleted);
        var totalViews = await _db.ArtistStreams
            .Where(s => s.ArtistId == artistId && s.Status != StreamStatus.Deleted)
            .SumAsync(s => s.ViewCount);

        return new ArtistProfileDto(
            artist.ArtistId,
            artist.FullName,
            artist.Specialty,
            artist.Bio,
            artist.ProfileImageUrl,
            streams,
            totalStreams,
            totalViews
        );
    }

    public async Task<List<StreamDto>> GetArtistStreamsAsync(long artistId, int page = 1, int pageSize = 20)
    {
        return await _db.ArtistStreams
            .AsNoTracking()
            .Where(s => s.ArtistId == artistId && s.Status != StreamStatus.Deleted)
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StreamDto(
                s.StreamId,
                s.ArtistId,
                s.Title,
                s.Description,
                s.Status.ToString(),
                s.StreamUrl,
                s.ThumbnailUrl,
                s.ViewCount,
                s.CreatedAt,
                s.EndedAt,
                s.DurationSeconds,
                s.Tags,
                s.IsFlaggedForReview,
                s.FlagReason
            ))
            .ToListAsync();
    }

    public async Task<StreamDto?> GetStreamAsync(long streamId)
    {
        var stream = await _db.ArtistStreams
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StreamId == streamId);

        if (stream == null || stream.Status == StreamStatus.Deleted)
            return null;

        return new StreamDto(
            stream.StreamId,
            stream.ArtistId,
            stream.Title,
            stream.Description,
            stream.Status.ToString(),
            stream.StreamUrl,
            stream.ThumbnailUrl,
            stream.ViewCount,
            stream.CreatedAt,
            stream.EndedAt,
            stream.DurationSeconds,
            stream.Tags,
            stream.IsFlaggedForReview,
            stream.FlagReason
        );
    }

    public async Task<ArtistStream> CreateStreamAsync(long artistId, string title, string? description, string? tags)
    {
        var stream = new ArtistStream
        {
            ArtistId = artistId,
            Title = title,
            Description = description,
            Tags = tags,
            Status = StreamStatus.Recording,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow
        };

        _db.ArtistStreams.Add(stream);
        await _db.SaveChangesAsync();

        _logger.LogInformation($"[STREAM] New stream created: {stream.StreamId} by artist {artistId}");

        return stream;
    }

    public async Task<ArtistStream> UpdateStreamAsync(long streamId, string title, string? description, long artistId)
    {
        var stream = await _db.ArtistStreams.FirstOrDefaultAsync(s => s.StreamId == streamId)
            ?? throw new InvalidOperationException($"Stream {streamId} not found");

        if (stream.ArtistId != artistId)
            throw new UnauthorizedAccessException("Can only update your own streams");

        stream.Title = title;
        stream.Description = description;

        await _db.SaveChangesAsync();

        return stream;
    }

    public async Task<ArtistStream> EndStreamAsync(long streamId, long artistId)
    {
        var stream = await _db.ArtistStreams.FirstOrDefaultAsync(s => s.StreamId == streamId)
            ?? throw new InvalidOperationException($"Stream {streamId} not found");

        if (stream.ArtistId != artistId)
            throw new UnauthorizedAccessException("Can only end your own streams");

        if (stream.Status != StreamStatus.Recording && stream.Status != StreamStatus.Live)
            throw new InvalidOperationException($"Cannot end stream in {stream.Status} status");

        stream.Status = StreamStatus.Recorded;
        stream.EndedAt = DateTime.UtcNow;

        if (stream.StartedAt.HasValue)
        {
            stream.DurationSeconds = (long)(stream.EndedAt.Value - stream.StartedAt.Value).TotalSeconds;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation($"[STREAM] Stream ended: {streamId}");

        return stream;
    }

    public async Task RecordViewAsync(long streamId, string? userId, string? ipAddress)
    {
        var stream = await _db.ArtistStreams.FindAsync(streamId);
        if (stream == null)
            return;

        // Increment view count
        stream.ViewCount++;

        // Log viewer (for analytics)
        _db.StreamViewers.Add(new StreamViewer
        {
            StreamId = streamId,
            ViewerUserId = userId,
            ViewerIpAddress = ipAddress,
            ViewedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }

    public async Task<List<StreamBrowseDto>> BrowseStreamsAsync(int page = 1, int pageSize = 24)
    {
        var visibleStatuses = new[] { StreamStatus.Live, StreamStatus.Recording, StreamStatus.Recorded, StreamStatus.Archived };

        return await _db.ArtistStreams
            .AsNoTracking()
            .Include(s => s.Artist)
            .Where(s => visibleStatuses.Contains(s.Status) && !s.IsFlaggedForReview)
            .OrderBy(s => s.Status == StreamStatus.Live ? 0 : 1)
            .ThenByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StreamBrowseDto(
                s.StreamId,
                s.ArtistId,
                s.Artist.FullName,
                s.Artist.ProfileImageUrl,
                s.Title,
                s.Description,
                s.Status.ToString(),
                s.ViewCount,
                s.StartedAt,
                s.EndedAt,
                s.ThumbnailUrl,
                s.Tags
            ))
            .ToListAsync();
    }

    public async Task<List<StreamDto>> GetFlaggedStreamsAsync(int page = 1, int pageSize = 20)
    {
        return await _db.ArtistStreams
            .AsNoTracking()
            .Where(s => s.IsFlaggedForReview)
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StreamDto(
                s.StreamId,
                s.ArtistId,
                s.Title,
                s.Status.ToString(),
                null, // Description
                null, // StreamUrl
                null, // ThumbnailUrl
                0, // ViewCount
                s.CreatedAt,
                null, // EndedAt
                null, // DurationSeconds
                null, // Tags
                s.IsFlaggedForReview,
                s.FlagReason
            ))
            .ToListAsync();
    }
}

// ============================================
// DTOs
// ============================================

public record ArtistDto(
    long ArtistId,
    string FullName,
    string? Specialty,
    string? Bio,
    string? ProfileImageUrl,
    long TotalStreams,
    long TotalViews);

public record ArtistProfileDto(
    long ArtistId,
    string FullName,
    string? Specialty,
    string? Bio,
    string? ProfileImageUrl,
    List<StreamDto> RecentStreams,
    long TotalStreams,
    long TotalViews);

public record StreamBrowseDto(
    long StreamId,
    long ArtistId,
    string ArtistName,
    string? ArtistImageUrl,
    string Title,
    string? Description,
    string Status,
    long ViewCount,
    DateTime? StartedAt,
    DateTime? EndedAt,
    string? ThumbnailUrl,
    string? Tags);

public record StreamDto(
    long StreamId,
    long ArtistId = 0,
    string Title = "",
    string? Description = null,
    string Status = "",
    string? StreamUrl = null,
    string? ThumbnailUrl = null,
    long ViewCount = 0,
    DateTime CreatedAt = default,
    DateTime? EndedAt = null,
    long? DurationSeconds = null,
    string? Tags = null,
    bool IsFlaggedForReview = false,
    string? FlagReason = null);
