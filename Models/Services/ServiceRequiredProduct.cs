using System.ComponentModel.DataAnnotations;
using Beauty.Api.Models.Catalog;

namespace Beauty.Api.Models.Services;

public enum ProductUsageType
{
    /// <summary>Auto-attached to every booking; cost rolled into service price.</summary>
    Required,
    /// <summary>Provider can add as a paid upgrade during booking.</summary>
    Optional,
    /// <summary>Retail recommendation shown to the client after the service.</summary>
    Aftercare,
}

public class ServiceRequiredProduct
{
    public long Id { get; set; }

    public long ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public ProductUsageType UsageType { get; set; } = ProductUsageType.Required;

    /// <summary>Full units included in the kit (1, 2, 3 — never a fraction).</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Override sale price in cents. If null, Product.BilledPriceCents (wholesale × 1.8) is used.
    /// </summary>
    public int? SalePriceCents { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public int SortOrder { get; set; } = 0;

    public bool IsActive { get; set; } = true;
}
