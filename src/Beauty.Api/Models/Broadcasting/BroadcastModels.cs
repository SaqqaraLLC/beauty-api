using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Broadcasting;

/// <summary>
/// Represents a broadcast campaign message sent to users/locations.
/// Supports email and future SMS, push notifications.
/// </summary>
public class BroadcastCampaign
{
    public long CampaignId { get; set; }

    [Required, StringLength(255)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required, StringLength(255)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    /// <summary>Email, SMS, Push</summary>
    public BroadcastChannelType Channel { get; set; } = BroadcastChannelType.Email;

    /// <summary>Draft, Scheduled, Sent, Failed</summary>
    public BroadcastStatus Status { get; set; } = BroadcastStatus.Draft;

    /// <summary>Admin who created the campaign</summary>
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>When to send (null = send immediately)</summary>
    public DateTime? ScheduledFor { get; set; }

    /// <summary>When the campaign was actually sent</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>Track segmentation rules</summary>
    public long? SegmentId { get; set; }
    public BroadcastSegment? Segment { get; set; }

    // Navigation
    public ICollection<BroadcastRecipient> Recipients { get; set; } = new List<BroadcastRecipient>();
    public ICollection<BroadcastAuditLog> AuditLogs { get; set; } = new List<BroadcastAuditLog>();
}

/// <summary>
/// Defines audience segmentation for a broadcast.
/// E.g., "all artists", "locations in NY", "users who booked in last 30 days"
/// </summary>
public class BroadcastSegment
{
    public long SegmentId { get; set; }

    [Required, StringLength(255)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>Target by role: Admin, Artist, Client, Location</summary>
    public BroadcastTargetRole? TargetRole { get; set; }

    /// <summary>Specific location IDs (null = all locations)</summary>
    public string? LocationIds { get; set; } // JSON: [1, 2, 3] or null

    /// <summary>Specific artist IDs (null = all artists)</summary>
    public string? ArtistIds { get; set; }

    /// <summary>Filter by booking activity (e.g., "booked last 30 days")</summary>
    public int? BookedWithinDays { get; set; }

    /// <summary>Include inactive users?</summary>
    public bool IncludeInactive { get; set; } = false;

    public DateTime CreatedAt { get; set; }

    // Denormalized recipient count for UI
    public int EstimatedRecipientCount { get; set; }

    public ICollection<BroadcastCampaign> Campaigns { get; set; } = new List<BroadcastCampaign>();
}

/// <summary>
/// Tracks delivery status for each recipient in a broadcast.
/// </summary>
public class BroadcastRecipient
{
    public long RecipientId { get; set; }

    public long CampaignId { get; set; }
    public BroadcastCampaign Campaign { get; set; } = null!;

    /// <summary>User or Location email address</summary>
    [StringLength(255)]
    public string RecipientEmail { get; set; } = string.Empty;

    /// <summary>User ID or Location ID</summary>
    public string? RecipientUserId { get; set; }

    /// <summary>Pending, Sent, Failed, Bounced, Opened, Clicked</summary>
    public BroadcastDeliveryStatus Status { get; set; } = BroadcastDeliveryStatus.Pending;

    public DateTime CreatedAt { get; set; }

    /// <summary>When email was actually sent</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>Error message if failed</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Track if recipient opened email (requires pixel tracking)</summary>
    public DateTime? OpenedAt { get; set; }

    /// <summary>Track if recipient clicked link</summary>
    public DateTime? ClickedAt { get; set; }
}

/// <summary>
/// Audit log for broadcast operations (create, schedule, send, cancel).
/// </summary>
public class BroadcastAuditLog
{
    public long LogId { get; set; }

    public long CampaignId { get; set; }
    public BroadcastCampaign Campaign { get; set; } = null!;

    public string AdminUserId { get; set; } = string.Empty;

    public BroadcastAuditAction Action { get; set; }

    [StringLength(500)]
    public string? Details { get; set; }

    public DateTime Timestamp { get; set; }
}

// ============================================
// ENUMS
// ============================================

public enum BroadcastChannelType
{
    Email = 1,
    SMS = 2,
    Push = 3,
    InApp = 4
}

public enum BroadcastStatus
{
    Draft = 1,
    Scheduled = 2,
    Sending = 3,
    Sent = 4,
    Failed = 5,
    Cancelled = 6
}

public enum BroadcastDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
    Bounced = 4,
    Opened = 5,
    Clicked = 6
}

public enum BroadcastTargetRole
{
    Admin = 1,
    Artist = 2,
    Client = 3,
    Location = 4,
    All = 5
}

public enum BroadcastAuditAction
{
    Created = 1,
    Scheduled = 2,
    Sent = 3,
    Cancelled = 4,
    Updated = 5,
    Deleted = 6
}
