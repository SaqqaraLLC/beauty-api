using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Enterprise;

public enum RepresentationRequestStatus { Pending, Accepted, Declined }

public class RepresentationRequest
{
    [Key]
    public int RepresentationRequestId { get; set; }

    public int AgentProfileId { get; set; }
    public AgentProfile AgentProfile { get; set; } = null!;

    public int ArtistProfileId { get; set; }
    public ArtistProfile ArtistProfile { get; set; } = null!;

    /// <summary>Identity user who created the request (artist or agent).</summary>
    [Required]
    [MaxLength(450)]
    public string RequestedByUserId { get; set; } = "";

    [MaxLength(1000)]
    public string? Message { get; set; }

    public RepresentationRequestStatus Status { get; set; } = RepresentationRequestStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RespondedAt { get; set; }

    [MaxLength(500)]
    public string? ResponseNote { get; set; }
}
