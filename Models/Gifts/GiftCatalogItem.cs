using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Gifts;

public class GiftCatalogItem
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(10)]
    public string Emoji { get; set; } = "";

    public int SlabCost { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
