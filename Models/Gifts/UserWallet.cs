using Beauty.Api.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Beauty.Api.Models.Gifts;

public class UserWallet
{
    [Key]
    public string UserId { get; set; } = "";

    public ApplicationUser User { get; set; } = null!;

    // Purchased currency — spent on gifts
    public int Slabs { get; set; } = 0;

    // Earned from gifts — 4 pieces = 1 slab; stored as decimal (battles award 1.5× e.g. 7.5)
    public decimal Pieces { get; set; } = 0m;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
