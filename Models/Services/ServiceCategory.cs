using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models;

public class ServiceCategory
{
    public long Id { get; set; }

    /// <summary>Stable machine key used for filtering — e.g. "massage", "nails".</summary>
    [Required, MaxLength(50)]
    public string Key { get; set; } = "";

    /// <summary>Human-readable label — e.g. "Massage Therapy", "Nail Services".</summary>
    [Required, MaxLength(100)]
    public string DisplayName { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public ICollection<Service> Services { get; set; } = new List<Service>();
}
