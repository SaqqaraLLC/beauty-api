using Beauty.Api.Models;
using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Gifts;

public class GiftTransaction
{
    public long Id { get; set; }

    [Required]
    public string SenderId { get; set; } = "";
    public ApplicationUser Sender { get; set; } = null!;

    [Required]
    public string RecipientArtistUserId { get; set; } = "";

    public int StreamId { get; set; }
    public Beauty.Api.Models.Enterprise.Stream Stream { get; set; } = null!;

    public int GiftId { get; set; }
    public GiftCatalogItem Gift { get; set; } = null!;

    // Currency spent — either slabs or piece-equivalent
    public int SlabsSpent { get; set; }
    public bool PaidWithPieces { get; set; } = false;

    // Pieces earned back by the gifter (equals SlabsSpent)
    public int PiecesEarned { get; set; }

    // Battle context
    public bool IsBattleGift { get; set; } = false;
    public long? BattleId { get; set; }

    // 25% bonus slab value added to artist earnings during battles
    public int BonusSlabs { get; set; } = 0;

    // Payout tracking
    public bool IncludedInPayout { get; set; } = false;
    public long? PayoutLineId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
