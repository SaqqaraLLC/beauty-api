using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Beauty.Api.Models.Catalog;

public enum ProductStatus { Pending, Approved, Declined }

public class Product
{
    [Key]
    public int ProductId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string Brand { get; set; } = "";

    [MaxLength(100)]
    public string Category { get; set; } = "Other";

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(3000)]
    public string? Ingredients { get; set; }

    [MaxLength(100)]
    public string? Sku { get; set; }

    [MaxLength(200)]
    public string? VendorName { get; set; } = "%PURE";

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    /// <summary>Wholesale cost in cents (what Saqqara pays %PURE).</summary>
    public int WholesalePriceCents { get; set; }

    /// <summary>Service-use billing price in cents — 80% markup over wholesale.</summary>
    [NotMapped]
    public int BilledPriceCents => (int)(WholesalePriceCents * 1.8m);

    /// <summary>Retail (take-home) price in cents — 3.5× wholesale to cover shrinkage and margin.</summary>
    [NotMapped]
    public int RetailPriceCents => (int)(WholesalePriceCents * 3.5m);

    /// <summary>Promo client billing price in cents — 60% markup over wholesale.</summary>
    [NotMapped]
    public int PromoBilledPriceCents => (int)(WholesalePriceCents * 1.6m);

    public ProductStatus Status { get; set; } = ProductStatus.Pending;

    public bool IsActive { get; set; } = true;

    [MaxLength(450)]
    public string? SubmittedByUserId { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ApprovedAt { get; set; }
    public DateTime? DeclinedAt { get; set; }

    [MaxLength(1000)]
    public string? DeclineReason { get; set; }

    [Column(TypeName = "double")]
    public double AverageRating { get; set; } = 0.0;

    public int ReviewCount { get; set; } = 0;

    public ICollection<ProductReview> Reviews { get; set; } = new List<ProductReview>();
}
