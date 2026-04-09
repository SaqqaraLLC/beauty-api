using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.Streams;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Beauty.Api.Domain.Streams;

/// <summary>
/// Danger detection service for stream content.
/// Uses keyword matching and pattern detection (can be extended with ML).
/// </summary>
public interface IStreamDangerDetectionService
{
    /// <summary>Analyze stream content for danger flags</summary>
    Task<List<StreamDangerFlag>> AnalyzeStreamAsync(long streamId, string content);

    /// <summary>Auto-flag stream if dangerous content detected</summary>
    Task<bool> FlagStreamIfDangerousAsync(long streamId, string? content, decimal confidenceThreshold = 0.7m);

    /// <summary>Get pending review flags</summary>
    Task<List<StreamDangerFlag>> GetPendingFlagsAsync(int limit = 50);

    /// <summary>Review and action a flag</summary>
    Task ReviewFlagAsync(long flagId, StreamReviewDecision decision, string reviewNotes, string userId);
}

public sealed class StreamDangerDetectionService : IStreamDangerDetectionService
{
    private readonly BeautyDbContext _db;
    private readonly ILogger<StreamDangerDetectionService> _logger;

    // Keywords that trigger danger detection
    private static readonly Dictionary<DangerType, string[]> DangerKeywords = new()
    {
        {
            DangerType.Inappropriate,
            new[]
            {
                "nsfw", "explicit", "nude", "adult", "sexual", "xxx",
                "porn", "sex", "escort", "prostitution"
            }
        },
        {
            DangerType.Harassment,
            new[] { "hate", "abuse", "bully", "harass", "threaten", "rape", "kill" }
        },
        {
            DangerType.Illegal,
            new[]
            {
                "drug", "cocaine", "meth", "heroin", "steal", "robbery",
                "gun", "weapon", "bomb", "copyright violation"
            }
        },
        {
            DangerType.Spam,
            new[] { "click here", "buy now", "limited offer", "crypto", "bitcoin", "urgent" }
        },
        {
            DangerType.Misinformation,
            new[]
            {
                "fake news", "hoax", "conspiracy", "scam", "pyramid scheme"
            }
        }
    };

    public StreamDangerDetectionService(
        BeautyDbContext db,
        ILogger<StreamDangerDetectionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<StreamDangerFlag>> AnalyzeStreamAsync(long streamId, string content)
    {
        var flags = new List<StreamDangerFlag>();

        if (string.IsNullOrEmpty(content))
            return flags;

        var lowerContent = content.ToLower();

        // Check each danger type
        foreach (var (dangerType, keywords) in DangerKeywords)
        {
            var matchCount = keywords.Count(kw => lowerContent.Contains(kw));
            
            if (matchCount > 0)
            {
                var confidenceScore = Math.Min(1.0m, (matchCount * 0.3m)); // 30% per keyword match

                var flag = new StreamDangerFlag
                {
                    StreamId = streamId,
                    DangerType = dangerType,
                    ConfidenceScore = confidenceScore,
                    DetectionReason = $"Detected {matchCount} keyword(s) for {dangerType}",
                    ReviewStatus = DangerReviewStatus.Pending,
                    FlaggedAt = DateTime.UtcNow
                };

                flags.Add(flag);
            }
        }

        // Pattern detection: Spam-like repetition
        if (ParseRepetitionPattern(lowerContent))
        {
            flags.Add(new StreamDangerFlag
            {
                StreamId = streamId,
                DangerType = DangerType.Spam,
                ConfidenceScore = 0.6m,
                DetectionReason = "Detected repetitive/spam-like pattern",
                ReviewStatus = DangerReviewStatus.Pending,
                FlaggedAt = DateTime.UtcNow
            });
        }

        return flags;
    }

    public async Task<bool> FlagStreamIfDangerousAsync(long streamId, string? content, decimal confidenceThreshold = 0.7m)
    {
        if (string.IsNullOrEmpty(content))
            return false;

        var flags = await AnalyzeStreamAsync(streamId, content);

        if (flags.Count == 0)
            return false;

        // Filter by confidence threshold
        var highConfidenceFlags = flags.Where(f => f.ConfidenceScore >= confidenceThreshold).ToList();

        if (highConfidenceFlags.Count == 0)
            return false;

        // Add flags to database
        _db.StreamDangerFlags.AddRange(highConfidenceFlags);

        // Mark stream as flagged
        var stream = await _db.ArtistStreams.FindAsync(streamId);
        if (stream != null)
        {
            stream.IsFlaggedForReview = true;
            stream.FlagReason = string.Join(", ", highConfidenceFlags.Select(f => f.DangerType.ToString()).Distinct());
        }

        await _db.SaveChangesAsync();

        _logger.LogWarning($"[DANGER] Stream {streamId} flagged with {highConfidenceFlags.Count} flags: {stream?.FlagReason}");

        return true;
    }

    public async Task<List<StreamDangerFlag>> GetPendingFlagsAsync(int limit = 50)
    {
        return await _db.StreamDangerFlags
            .Where(f => f.ReviewStatus == DangerReviewStatus.Pending)
            .Include(f => f.Stream)
            .OrderByDescending(f => f.ConfidenceScore)
            .Take(limit)
            .ToListAsync();
    }

    public async Task ReviewFlagAsync(
        long flagId,
        StreamReviewDecision decision,
        string reviewNotes,
        string userId)
    {
        var flag = await _db.StreamDangerFlags
            .Include(f => f.Stream)
            .FirstOrDefaultAsync(f => f.FlagId == flagId)
            ?? throw new InvalidOperationException($"Flag {flagId} not found");

        flag.ReviewStatus = DangerReviewStatus.Actioned;
        flag.ReviewedByUserId = userId;
        flag.ReviewedAt = DateTime.UtcNow;
        flag.ReviewNotes = reviewNotes;

        // Determine action
        if (decision == StreamReviewDecision.Rejected)
        {
            flag.ActionTaken = DangerAction.Hidden;
            flag.Stream.Status = StreamStatus.Hidden;
        }
        else if (decision == StreamReviewDecision.TakeAction)
        {
            flag.ActionTaken = DangerAction.Deleted;
            flag.Stream.Status = StreamStatus.Deleted;
        }
        else if (decision == StreamReviewDecision.Approved)
        {
            // Clear flag if approved
            flag.Stream.IsFlaggedForReview = false;
            flag.Stream.FlagReason = null;
            flag.ActionTaken = DangerAction.ArtistNotified;
        }

        // Log review
        _db.StreamReviews.Add(new StreamReview
        {
            StreamId = flag.StreamId,
            ReviewedByUserId = userId,
            Decision = decision,
            Notes = reviewNotes,
            ReviewedAt = DateTime.UtcNow,
            ActionTaken = flag.ActionTaken?.ToString()
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation($"[MODERATION] Flag {flagId} reviewed: {decision} by {userId}");
    }

    private bool ParseRepetitionPattern(string content)
    {
        // Detect if same word/phrase repeated excessively
        var words = content.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (words.Length < 10)
            return false;

        // Count word occurrences
        var wordCounts = words
            .GroupBy(w => w)
            .ToDictionary(g => g.Key, g => g.Count());

        // If any single word appears more than 30% of the time, it's suspicious
        var maxCount = wordCounts.Max(kv => kv.Value);
        var repetitionRatio = (decimal)maxCount / words.Length;

        return repetitionRatio > 0.3m;
    }
}
