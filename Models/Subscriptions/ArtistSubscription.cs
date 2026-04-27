using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Subscriptions;

public class ArtistSubscription
{
    public long Id { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = "";

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trialing;

    public decimal MonthlyAmount { get; set; } = 19.00m;

    public DateTime TrialStartDate { get; set; } = DateTime.UtcNow;

    public DateTime TrialEndDate { get; set; }

    public DateTime? SubscriptionStartDate { get; set; }

    public DateTime? NextBillingDate { get; set; }

    public DateTime? LastBilledDate { get; set; }

    public decimal? LastBilledAmount { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
