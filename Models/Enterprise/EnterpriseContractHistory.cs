using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Enterprise;

public class EnterpriseContractHistory
{
    public long Id { get; set; }

    public Guid EnterpriseAccountId { get; set; }
    public EnterpriseAccount EnterpriseAccount { get; set; } = null!;

    /// <summary>NewContract | Renewal | Upgrade | Downgrade | SeatExpansion | BillingCycleChange</summary>
    [Required, MaxLength(50)]
    public string ChangeType { get; set; } = "";

    [MaxLength(100)]
    public string? PreviousTier { get; set; }

    [Required, MaxLength(100)]
    public string NewTier { get; set; } = "";

    public int PreviousSeatLimit { get; set; }
    public int NewSeatLimit      { get; set; }

    public int PreviousMaxMonthlyBookings { get; set; }
    public int NewMaxMonthlyBookings      { get; set; }

    [MaxLength(20)]
    public string? PreviousBillingCycle { get; set; }

    [Required, MaxLength(20)]
    public string NewBillingCycle { get; set; } = "";

    public decimal? PreviousContractAmount { get; set; }
    public decimal  NewContractAmount      { get; set; }

    public DateTime EffectiveDate { get; set; }
    public DateTime? ExpiryDate   { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [MaxLength(450)]
    public string? ChangedByUserId { get; set; }

    [MaxLength(200)]
    public string? ChangedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
