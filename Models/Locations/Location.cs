using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Beauty.Api.Models.Enterprise;

namespace Beauty.Api.Models.Locations;

public class Location : ISoftDeletable
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EnterpriseAccountId { get; set; }

    [ForeignKey(nameof(EnterpriseAccountId))]
    public EnterpriseAccount EnterpriseAccount { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = "";

    [Required]
    [MaxLength(500)]
    public string Address { get; set; } = "";

    /// <summary>Identity user who owns/manages this location.</summary>
    [MaxLength(450)]
    public string? OwnerUserId { get; set; }

    // ── 100% PURE wholesale account tracking ──────────────────────

    /// <summary>Date admin activated the %PURE wholesale account for this location.
    /// Starts the 60-day first-order clock.</summary>
    public DateTime? PureAccountActivatedAt { get; set; }

    /// <summary>Date the first %PURE order was placed. Stops the countdown.</summary>
    public DateTime? PureFirstOrderPlacedAt { get; set; }

    /// <summary>NotSetup | Active | FirstOrderPlaced | Lapsed</summary>
    [MaxLength(30)]
    public string PureAccountStatus { get; set; } = "NotSetup";

    /// <summary>Days remaining to place first order. Null if not applicable.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int? PureFirstOrderDaysRemaining
    {
        get
        {
            if (PureAccountActivatedAt is null || PureFirstOrderPlacedAt is not null)
                return null;
            var deadline = PureAccountActivatedAt.Value.AddDays(60);
            return (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalDays);
        }
    }

    // ISoftDeletable
    public bool      IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}
