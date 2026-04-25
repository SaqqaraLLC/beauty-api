using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models;

/// <summary>
/// Optional extras that can be selected alongside a base service at booking time.
/// Examples: aromatherapy, hot stones, nail art upgrade.
/// Non-blocking — a service works fully without any add-ons.
/// </summary>
public class ServiceAddOn
{
    public long Id { get; set; }

    public long ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Additional price on top of the base service price.</summary>
    public decimal Price { get; set; }

    /// <summary>Extra minutes added to the base service duration.</summary>
    public int ExtraMinutes { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; } = 0;
}
