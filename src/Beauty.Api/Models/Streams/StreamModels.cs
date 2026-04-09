using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace Beauty.Api.Models.Streams;

/// <summary>
/// Represents a live or recorded stream from an artist.
/// Publicly visible to all users.
/// </summary>
public class ArtistStream
{
    public long StreamId { get; set; }

    public long ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;

    [Required, StringLength(255)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    /// <summary>Live, Recording, Recorded, Archived, Deleted</summary>
    public StreamStatus Status { get; set; } = StreamStatus.Recording;

    /// <summary>Stream URL or embed code</summary>
    [StringLength(500)]
    public string? StreamUrl { get; set; }

    /// <summary>Thumbnail image URL</summary>
    [StringLength(500)]
    public string? ThumbnailUrl { get; set; }

    /// <summary>Duration in seconds (only set when recording completes)</summary>
    public long? DurationSeconds { get; set; }

    /// <summary>Total views across all viewers</summary>
    public long ViewCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    /// <summary>When stream ended or was archived</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>Category/tags for discovery</summary>
    [StringLength(500)]
    public string? Tags { get; set; }

    /// <summary>Is content flagged as dangerous and under review?</summary>
    public bool IsFlaggedForReview { get; set; }

    /// <summary>Reason for flag (if any)</summary>
    [StringLength(500)]
    public string? FlagReason { get; set; }

    // Navigation
    public ICollection<StreamReview> Reviews { get; set; } = new List<StreamReview>();
    public ICollection<StreamDangerFlag> DangerFlags { get; set; } = new List<StreamDangerFlag>();
    public ICollection<StreamViewer> Viewers { get; set; } = new List<StreamViewer>();
}

/// <summary>
/// Danger detection flags for streams (auto-generated when unsafe content detected).
/// Only flagged streams are saved and reviewed.
/// </summary>
public class StreamDangerFlag
{
    public long FlagId { get; set; }

    public long StreamId { get; set; }
    public ArtistStream Stream { get; set; } = null!;

    /// <summary>Type of danger detected: Inappropriate, Harassment, Illegal, Spam, Other</summary>
    public DangerType DangerType { get; set; }

    /// <summary>Confidence score (0.0 - 1.0) from detection algorithm</summary>
    public decimal ConfidenceScore { get; set; }

    [StringLength(1000)]
    public string? DetectionReason { get; set; }

    /// <summary>Timestamp when danger was detected (for timestamp-based review)</summary>
    public DateTime? TimeStampSeconds { get; set; }

    /// <summary>Pending, Under Review, Approved, Rejected, Actioned</summary>
    public DangerReviewStatus ReviewStatus { get; set; } = DangerReviewStatus.Pending;

    public DateTime FlaggedAt { get; set; }

    /// <summary>Admin who reviewed this flag</summary>
    public string? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    [StringLength(500)]
    public string? ReviewNotes { get; set; }

    /// <summary>Action taken: Deleted, Hidden, Warned, Suspended, None</summary>
    public DangerAction? ActionTaken { get; set; }
}

/// <summary>
/// Admin review record for flagged streams.
/// </summary>
public class StreamReview
{
    public long ReviewId { get; set; }

    public long StreamId { get; set; }
    public ArtistStream Stream { get; set; } = null!;

    public string ReviewedByUserId { get; set; } = string.Empty;

    /// <summary>Approved, Rejected, NeedsAction</summary>
    public StreamReviewDecision Decision { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime ReviewedAt { get; set; }

    /// <summary>Action taken by moderator</summary>
    [StringLength(500)]
    public string? ActionTaken { get; set; }
}

/// <summary>
/// Track stream viewers for analytics (not stored permanently, only aggregated).
/// </summary>
public class StreamViewer
{
    public long ViewerId { get; set; }

    public long StreamId { get; set; }
    public ArtistStream Stream { get; set; } = null!;

    public string? ViewerUserId { get; set; }

    [StringLength(255)]
    public string? ViewerIpAddress { get; set; }

    public DateTime ViewedAt { get; set; }

    /// <summary>Duration watched in seconds</summary>
    public long? WatchedDurationSeconds { get; set; }
}

// ============================================
// ENUMS
// ============================================

public enum StreamStatus
{
    Live = 1,
    Recording = 2,
    Recorded = 3,
    Archived = 4,
    Deleted = 5,
    Hidden = 6
}

public enum DangerType
{
    Inappropriate = 1,
    Harassment = 2,
    Illegal = 3,
    Spam = 4,
    Misinformation = 5,
    Other = 6
}

public enum DangerReviewStatus
{
    Pending = 1,
    UnderReview = 2,
    Approved = 3,
    Rejected = 4,
    Actioned = 5
}

public enum DangerAction
{
    Deleted = 1,
    Hidden = 2,
    Warned = 3,
    Suspended = 4,
    ArtistNotified = 5
}

public enum StreamReviewDecision
{
    Approved = 1,
    Rejected = 2,
    NeedsMoreReview = 3,
    TakeAction = 4
}
