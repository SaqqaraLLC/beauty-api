using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Beauty.Api.Models.Enterprise;

public class ArtistProfile
{
    [Key]
    public int ArtistProfileId { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string FullName { get; set; } = "";

    [MaxLength(100)]
    public string? Specialty { get; set; }

    [MaxLength(2000)]
    public string? Bio { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? State { get; set; }

    [MaxLength(100)]
    public string? Country { get; set; }

    [MaxLength(500)]
    public string? ProfileImageUrl { get; set; }

    public bool IsVerified { get; set; } = false;

    [Column(TypeName = "double")]
    public double AverageRating { get; set; } = 0.0;

    public int ReviewCount { get; set; } = 0;

    public int BookingCount { get; set; } = 0;

    [MaxLength(200)]
    public string? AgencyName { get; set; }

    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    /// <summary>JSON array of specialty strings, e.g. ["Bridal","Editorial"]</summary>
    [Required]
    public string SpecialtiesJson { get; set; } = "[]";

    [Column(TypeName = "decimal(10,2)")]
    public decimal? HourlyRate { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
