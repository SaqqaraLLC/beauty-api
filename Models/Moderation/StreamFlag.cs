using System.ComponentModel.DataAnnotations;
using Beauty.Api.Models.Enterprise;

namespace Beauty.Api.Models.Moderation;

public class StreamFlag
{
    public long Id { get; set; }

    public int StreamId { get; set; }
    public Enterprise.Stream Stream { get; set; } = null!;

    [Required, MaxLength(500)]
    public string Reason { get; set; } = "";

    public double FlagConfidence { get; set; } = 1.0;

    [MaxLength(450)]
    public string? FlaggedByUserId { get; set; }

    public StreamFlagStatus Status { get; set; } = StreamFlagStatus.Flagged;

    [MaxLength(50)]
    public string? Action { get; set; }

    [MaxLength(450)]
    public string? ReviewedByUserId { get; set; }

    [MaxLength(200)]
    public string? ReviewedByName { get; set; }

    public DateTime? ReviewedAt { get; set; }

    [MaxLength(1000)]
    public string? ReviewNotes { get; set; }

    public DateTime FlaggedAt { get; set; } = DateTime.UtcNow;
}

public enum StreamFlagStatus
{
    Flagged  = 0,
    Reviewed = 1,
    Dismissed = 2
}
