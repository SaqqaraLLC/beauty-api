using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Enterprise;

public class EnterpriseAccount : ISoftDeletable
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(300)]
    public string Name { get; set; } = "";

    /// <summary>Pending | Active | Suspended</summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Pending";

    /// <summary>Starter | Growth | Enterprise | Custom</summary>
    [Required]
    [MaxLength(100)]
    public string PlanTier { get; set; } = EnterpriseTier.Starter;

    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

    /// <summary>0 = use tier default</summary>
    public int SeatLimit { get; set; } = 0;

    /// <summary>0 = use tier default</summary>
    public int MaxMonthlyBookings { get; set; } = 0;

    /// <summary>Total contract value for the current period (monthly or yearly amount).</summary>
    public decimal ContractAmount { get; set; } = 0m;

    public DateTime? ContractStartDate  { get; set; }
    public DateTime? ContractRenewalDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ISoftDeletable
    public bool      IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    public ICollection<EnterpriseContractHistory> ContractHistory { get; set; } = new List<EnterpriseContractHistory>();
}
