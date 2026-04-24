using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Products;

public class Product
{
    public long ProductId { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Brand { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Category { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(2000)]
    public string? Ingredients { get; set; }

    [StringLength(100)]
    public string? VendorName { get; set; }

    [StringLength(100)]
    public string? Sku { get; set; }

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    /// <summary>Price the vendor charges Saqqara, in cents</summary>
    public long WholesalePriceCents { get; set; }

    /// <summary>Standard price billed to artist/client (wholesale × 1.8)</summary>
    public long BilledPriceCents { get; set; }

    /// <summary>Promotional price (wholesale × 1.6)</summary>
    public long PromoBilledPriceCents { get; set; }

    public ProductStatus Status { get; set; } = ProductStatus.Pending;

    [StringLength(500)]
    public string? DeclineReason { get; set; }

    /// <summary>Submitted by vendor token owner (vendor name) or "admin"</summary>
    [StringLength(100)]
    public string? SubmittedBy { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }

    [StringLength(255)]
    public string? ReviewedByUserId { get; set; }

    public ICollection<ProductReview> Reviews { get; set; } = new List<ProductReview>();
}

public class ProductReview
{
    public long ReviewId { get; set; }

    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [StringLength(255)]
    public string? ReviewerUserId { get; set; }

    [StringLength(100)]
    public string? ReviewerName { get; set; }

    public int Rating { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public string Recommendation { get; set; } = "Neutral"; // Approve, Decline, Neutral

    public DateTime ReviewedAt { get; set; }
}

public enum ProductStatus
{
    Pending  = 1,
    Approved = 2,
    Declined = 3
}
