using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Beauty.Api.Models.Enterprise;

public class EnterpriseUser
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // ── Enterprise link ────────────────────────────────────────────
    [Required]
    public Guid EnterpriseAccountId { get; set; }

    [ForeignKey(nameof(EnterpriseAccountId))]
    public EnterpriseAccount EnterpriseAccount { get; set; } = null!;

    // ── Identity ───────────────────────────────────────────────────
    [Required]
    [MaxLength(256)]
    public string Email { get; set; } = "";

    /// <summary>FK to EnterpriseRole; null until role is assigned</summary>
    public int? RoleId { get; set; }

    [ForeignKey(nameof(RoleId))]
    public EnterpriseRole? Role { get; set; }

    /// <summary>Invited | Active | Suspended | Offboarded</summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Invited";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }
}
