
using System;
using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models;

public class StreamQuotaOverride
{
    [Key]                     // ← make OverrideId the primary key
    public long OverrideId { get; set; }
    public long ArtistId { get; set; }
    public long RequestedByOwnerId { get; set; }
    public string ApprovalStatus { get; set; } = "pending";
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public string? Reason { get; set; }
}
