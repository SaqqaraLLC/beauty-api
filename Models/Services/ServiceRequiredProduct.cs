using System.ComponentModel.DataAnnotations;
using Beauty.Api.Models.Catalog;

namespace Beauty.Api.Models.Services;

/// <summary>
/// A full-unit product the customer purchases and keeps as part of a service booking.
/// Charged on top of the service fee. No partial quantities.
/// </summary>
public class ServiceRequiredProduct
{
    public long Id { get; set; }

    public long ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

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
