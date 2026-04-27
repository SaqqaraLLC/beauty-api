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

    // Earned back from gifting — 4 pieces = 1 slab purchasing power
    public int Pieces { get; set; } = 0;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
