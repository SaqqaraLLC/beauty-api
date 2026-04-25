using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models;

public class Service
{
    public long Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(1000)]
    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int DurationMinutes { get; set; }

    public bool Active { get; set; } = true;

    // Nullable — existing services work untouched; category is optional discovery aid
    public long? CategoryId { get; set; }
    public ServiceCategory? Category { get; set; }

    public ICollection<ServiceAddOn> AddOns { get; set; } = new List<ServiceAddOn>();
}
