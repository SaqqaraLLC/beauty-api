using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Catalog;

public class PromoCode
{
    public int PromoCodeId { get; set; }

    /// <summary>The code clients enter at booking, e.g. "SAQQARA10".</summary>
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = "";

    [MaxLength(300)]
    public string? Description { get; set; }

    /// <summary>
    /// Product billing multiplier when this code is applied.
    /// Standard = 1.8 (80% markup). Promo = 1.6 (60% markup).
    /// </summary>
    public decimal ProductMarkupMultiplier { get; set; } = 1.6m;

    /// <summary>Null = unlimited uses.</summary>
    public int? MaxUses { get; set; }

    public int UsedCount { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True if code can still be used right now.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsValid =>
        IsActive &&
        (ValidFrom  == null || ValidFrom  <= DateTime.UtcNow) &&
        (ValidUntil == null || ValidUntil >= DateTime.UtcNow) &&
        (MaxUses    == null || UsedCount  <  MaxUses);
}
