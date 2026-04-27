using Beauty.Api.Models;
using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Gifts;

public class ArtistBattle
{
    public long Id { get; set; }

    [Required]
    public string Artist1UserId { get; set; } = "";

    [Required]
    public string Artist2UserId { get; set; } = "";

    public int? Stream1Id { get; set; }
    public int? Stream2Id { get; set; }

    // 5 or 10
    public int DurationMinutes { get; set; } = 5;

    public BattleStatus Status { get; set; } = BattleStatus.Pending;

    public DateTime? StartedAt { get; set; }
    public DateTime? EndsAt { get; set; }

    // Running gift totals (slabs + bonus)
    public int Artist1TotalSlabs { get; set; } = 0;
    public int Artist2TotalSlabs { get; set; } = 0;

    [MaxLength(450)]
    public string? WinnerUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<GiftTransaction> Gifts { get; set; } = new List<GiftTransaction>();
}

public enum BattleStatus
{
    Pending   = 1,
    Active    = 2,
    Completed = 3,
    Cancelled = 4,
}

// Artist signing up to be matched for a battle
public class BattleSignup
{
    public long Id { get; set; }

    [Required]
    public string ArtistUserId { get; set; } = "";

    public int PreferredDurationMinutes { get; set; } = 5;

    public BattleSignupStatus Status { get; set; } = BattleSignupStatus.Waiting;

    public long? MatchedBattleId { get; set; }

    // Composite matchmaking score — tenure + profit + popularity
    public double BattleScore { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum BattleSignupStatus
{
    Waiting = 1,
    Matched = 2,
    Expired = 3,
}
