using Beauty.Api.Data;
using Beauty.Api.Models.Gifts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DbStream = Beauty.Api.Models.Enterprise.Stream;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/leaderboard")]
public class LeaderboardController : ControllerBase
{
    private readonly BeautyDbContext _db;
    public LeaderboardController(BeautyDbContext db) => _db = db;

    // ── GET /api/leaderboard ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int top = 10)
    {
        var results = await Task.WhenAll(
            GetTopGifted(top),
            GetBattleChampions(top),
            GetMostStreamed(top),
            GetMostBooked(top),
            GetTopRated(top),
            GetShortStars(top)
        );

        return Ok(new
        {
            TopGifted       = results[0],
            BattleChampions = results[1],
            MostStreamed    = results[2],
            MostBooked      = results[3],
            TopRated        = results[4],
            ShortStars      = results[5],
            GeneratedAt     = DateTime.UtcNow,
        });
    }

    // ── GET /api/leaderboard/gifter-breakdown?artistUserId=&top=20 ───────
    // Who gave the most slabs to a specific artist (all-time)
    [HttpGet("gifter-breakdown")]
    public async Task<IActionResult> GifterBreakdown(
        [FromQuery] string artistUserId,
        [FromQuery] int top = 20)
    {
        if (string.IsNullOrWhiteSpace(artistUserId))
            return BadRequest(new { error = "artistUserId required" });

        var rows = await _db.GiftTransactions
            .AsNoTracking()
            .Where(g => g.RecipientArtistUserId == artistUserId)
            .GroupBy(g => g.SenderId)
            .Select(g => new
            {
                SenderId   = g.Key,
                TotalSlabs = g.Sum(x => x.SlabsSpent),
                GiftCount  = g.Count(),
                LastGiftAt = g.Max(x => x.CreatedAt),
            })
            .OrderByDescending(x => x.TotalSlabs)
            .Take(top)
            .ToListAsync();

        return Ok(rows);
    }

    // ── GET /api/leaderboard/stream-guards/{streamId} ───────────────────
    // Viewers who gave 5 000+ slabs to any artist on this stream in the last hour
    [HttpGet("stream-guards/{streamId:int}")]
    public async Task<IActionResult> StreamGuards(int streamId)
    {
        var since = DateTime.UtcNow.AddHours(-1);

        var guards = await _db.GiftTransactions
            .AsNoTracking()
            .Where(g => g.StreamId == streamId && g.CreatedAt >= since)
            .GroupBy(g => g.SenderId)
            .Select(g => new
            {
                SenderId   = g.Key,
                SlabsGiven = g.Sum(x => x.SlabsSpent),
                GiftCount  = g.Count(),
            })
            .Where(x => x.SlabsGiven >= 5000)
            .OrderByDescending(x => x.SlabsGiven)
            .ToListAsync();

        return Ok(guards);
    }

    // ── Private category builders ────────────────────────────────────────

    private async Task<List<LeaderEntry>> GetTopGifted(int top)
    {
        var rows = await _db.GiftTransactions
            .AsNoTracking()
            .GroupBy(g => g.RecipientArtistUserId)
            .Select(g => new
            {
                UserId     = g.Key,
                TotalSlabs = g.Sum(x => x.SlabsSpent),
                GiftCount  = g.Count(),
            })
            .OrderByDescending(x => x.TotalSlabs)
            .Take(top)
            .ToListAsync();

        var profiles = await ProfileMap(rows.Select(r => r.UserId).ToList());

        return rows.Select((r, i) =>
        {
            profiles.TryGetValue(r.UserId, out var p);
            return new LeaderEntry
            {
                Rank            = i + 1,
                UserId          = r.UserId,
                Name            = p?.FullName ?? "Unknown",
                ProfileImageUrl = p?.ProfileImageUrl,
                Specialty       = p?.Specialty,
                Stat            = r.TotalSlabs,
                StatLabel       = "slabs received",
                SubStat         = r.GiftCount,
                SubStatLabel    = "gifts",
            };
        }).ToList();
    }

    private async Task<List<LeaderEntry>> GetBattleChampions(int top)
    {
        var rows = await _db.ArtistBattles
            .AsNoTracking()
            .Where(b => b.Status == BattleStatus.Completed && b.WinnerUserId != null)
            .GroupBy(b => b.WinnerUserId!)
            .Select(g => new { UserId = g.Key, Wins = g.Count() })
            .OrderByDescending(x => x.Wins)
            .Take(top)
            .ToListAsync();

        var profiles = await ProfileMap(rows.Select(r => r.UserId).ToList());

        return rows.Select((r, i) =>
        {
            profiles.TryGetValue(r.UserId, out var p);
            return new LeaderEntry
            {
                Rank            = i + 1,
                UserId          = r.UserId,
                Name            = p?.FullName ?? "Unknown",
                ProfileImageUrl = p?.ProfileImageUrl,
                Specialty       = p?.Specialty,
                Stat            = r.Wins,
                StatLabel       = "battles won",
            };
        }).ToList();
    }

    private async Task<List<LeaderEntry>> GetMostStreamed(int top)
    {
        var rows = await _db.Set<DbStream>()
            .AsNoTracking()
            .Where(s => s.IsActive)
            .GroupBy(s => s.ArtistProfileId)
            .Select(g => new
            {
                ArtistProfileId = g.Key,
                StreamCount     = g.Count(),
                TotalViewers    = g.Sum(x => x.ViewerCount),
            })
            .OrderByDescending(x => x.StreamCount)
            .Take(top)
            .ToListAsync();

        var profileIds = rows.Select(r => r.ArtistProfileId).ToList();
        var profiles   = await _db.ArtistProfiles
            .AsNoTracking()
            .Where(p => profileIds.Contains(p.ArtistProfileId))
            .ToDictionaryAsync(p => p.ArtistProfileId);

        return rows.Select((r, i) =>
        {
            profiles.TryGetValue(r.ArtistProfileId, out var p);
            return new LeaderEntry
            {
                Rank            = i + 1,
                UserId          = p?.UserId ?? "",
                Name            = p?.FullName ?? "Unknown",
                ProfileImageUrl = p?.ProfileImageUrl,
                Specialty       = p?.Specialty,
                Stat            = r.StreamCount,
                StatLabel       = "streams",
                SubStat         = r.TotalViewers,
                SubStatLabel    = "total viewers",
            };
        }).ToList();
    }

    private async Task<List<LeaderEntry>> GetMostBooked(int top)
    {
        var rows = await _db.ArtistProfiles
            .AsNoTracking()
            .Where(p => p.IsActive && p.BookingCount > 0)
            .OrderByDescending(p => p.BookingCount)
            .Take(top)
            .Select(p => new
            {
                p.UserId,
                p.FullName,
                p.ProfileImageUrl,
                p.Specialty,
                p.BookingCount,
                p.AverageRating,
            })
            .ToListAsync();

        return rows.Select((r, i) => new LeaderEntry
        {
            Rank            = i + 1,
            UserId          = r.UserId,
            Name            = r.FullName,
            ProfileImageUrl = r.ProfileImageUrl,
            Specialty       = r.Specialty,
            Stat            = r.BookingCount,
            StatLabel       = "bookings",
            Rating          = r.AverageRating > 0 ? r.AverageRating : null,
        }).ToList();
    }

    private async Task<List<LeaderEntry>> GetTopRated(int top)
    {
        var rows = await _db.ArtistProfiles
            .AsNoTracking()
            .Where(p => p.IsActive && p.ReviewCount >= 3)
            .OrderByDescending(p => p.AverageRating)
            .ThenByDescending(p => p.ReviewCount)
            .Take(top)
            .Select(p => new
            {
                p.UserId,
                p.FullName,
                p.ProfileImageUrl,
                p.Specialty,
                p.AverageRating,
                p.ReviewCount,
            })
            .ToListAsync();

        return rows.Select((r, i) => new LeaderEntry
        {
            Rank            = i + 1,
            UserId          = r.UserId,
            Name            = r.FullName,
            ProfileImageUrl = r.ProfileImageUrl,
            Specialty       = r.Specialty,
            Stat            = r.ReviewCount,
            StatLabel       = "reviews",
            Rating          = r.AverageRating,
        }).ToList();
    }

    private async Task<List<LeaderEntry>> GetShortStars(int top)
    {
        var rows = await _db.ArtistShorts
            .AsNoTracking()
            .Where(s => s.IsActive)
            .GroupBy(s => s.ArtistUserId)
            .Select(g => new
            {
                UserId     = g.Key,
                TotalViews = g.Sum(x => x.Views),
                TotalLikes = g.Sum(x => x.Likes),
            })
            .OrderByDescending(x => x.TotalViews)
            .Take(top)
            .ToListAsync();

        var profiles = await ProfileMap(rows.Select(r => r.UserId).ToList());

        return rows.Select((r, i) =>
        {
            profiles.TryGetValue(r.UserId, out var p);
            return new LeaderEntry
            {
                Rank            = i + 1,
                UserId          = r.UserId,
                Name            = p?.FullName ?? "Unknown",
                ProfileImageUrl = p?.ProfileImageUrl,
                Specialty       = p?.Specialty,
                Stat            = r.TotalViews,
                StatLabel       = "views",
                SubStat         = r.TotalLikes,
                SubStatLabel    = "likes",
            };
        }).ToList();
    }

    private async Task<Dictionary<string, ArtistProfileSlim>> ProfileMap(List<string> userIds)
    {
        return await _db.ArtistProfiles
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new ArtistProfileSlim
            {
                UserId          = p.UserId,
                FullName        = p.FullName,
                ProfileImageUrl = p.ProfileImageUrl,
                Specialty       = p.Specialty,
            })
            .ToDictionaryAsync(p => p.UserId);
    }
}

public record LeaderEntry
{
    public int     Rank            { get; init; }
    public string  UserId          { get; init; } = "";
    public string  Name            { get; init; } = "";
    public string? ProfileImageUrl { get; init; }
    public string? Specialty       { get; init; }
    public long    Stat            { get; init; }
    public string  StatLabel       { get; init; } = "";
    public long?   SubStat         { get; init; }
    public string? SubStatLabel    { get; init; }
    public double? Rating          { get; init; }
}

internal record ArtistProfileSlim
{
    public string  UserId          { get; init; } = "";
    public string  FullName        { get; init; } = "";
    public string? ProfileImageUrl { get; init; }
    public string? Specialty       { get; init; }
}
