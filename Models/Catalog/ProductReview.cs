using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Catalog;

public class ProductReview
{
    [Key]
    public int ReviewId { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [Required]
    [MaxLength(450)]
    public string ReviewerUserId { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string ReviewerName { get; set; } = "";

    [MaxLength(50)]
    public string ReviewerRole { get; set; } = "Staff";

    /// <summary>1–5</summary>
    public int Rating { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    /// <summary>Approve | Decline | Neutral</summary>
    [MaxLength(20)]
    public string Recommendation { get; set; } = "Neutral";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
