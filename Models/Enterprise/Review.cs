using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Beauty.Api.Models.Enterprise;

public class Review
{
    [Key]
    public int ReviewId { get; set; }

    public int? BookingId { get; set; }

    [Required]
    [MaxLength(450)]
    public string ReviewerUserId { get; set; } = "";

    [Required]
    [MaxLength(50)]
    public string ReviewerRole { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string ReviewerName { get; set; } = "";

    [MaxLength(500)]
    public string? ReviewerAvatarUrl { get; set; }

    /// <summary>Artist | Client | Company | Agent</summary>
    [Required]
    [MaxLength(50)]
    public string SubjectEntityType { get; set; } = "";

    public int SubjectEntityId { get; set; }

    [Required]
    [MaxLength(200)]
    public string SubjectName { get; set; } = "";

    /// <summary>1–5 star rating</summary>
    public int Rating { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(3000)]
    public string? Body { get; set; }

    public bool IsVerifiedBooking { get; set; } = false;

    /// <summary>Published | Pending | Removed</summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Published";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
